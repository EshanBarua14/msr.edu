using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Services;

public class QuizException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Quiz attempts are graded on the server only. Correct answers and explanations never leave the API
/// before an attempt is submitted. Only approved questions count (AI drafts stay out until reviewed).
/// </summary>
public class QuizService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Late submissions inside this window still count, to absorb network delay.</summary>
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

    public record Answer(Guid QuestionId, Guid? OptionId);

    public static IQueryable<Question> GradedQuestions(AppDbContext db, Guid quizId) =>
        db.Questions.Where(q => q.QuizId == quizId && q.ReviewStatus == QuestionReviewStatus.Approved);

    public static DateTimeOffset? Deadline(QuizAttempt a, Quiz q) =>
        q.TimeLimitSeconds is { } s ? a.StartedAt.AddSeconds(s) : null;

    /// <summary>Resumes the open attempt if it is still within time, otherwise starts a new one.</summary>
    public async Task<(QuizAttempt Attempt, Quiz Quiz)> StartAsync(Guid userId, Guid quizId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialise starts per user and quiz so the attempt limit can't be raced.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock(hashtextextended({userId.ToString() + quizId}, 0))", ct);

        var quiz = await db.Quizzes.AsNoTracking().SingleAsync(q => q.Id == quizId, ct);
        var now = clock.GetUtcNow();
        var attempts = await db.QuizAttempts.Where(a => a.QuizId == quizId && a.UserId == userId).ToListAsync(ct);

        var open = attempts.FirstOrDefault(a => a.SubmittedAt is null);
        if (open is not null)
        {
            if (Deadline(open, quiz) is not { } deadline || now <= deadline + SubmitGrace)
            {
                await tx.CommitAsync(ct);
                return (open, quiz);
            }
            // Expired without submitting: closes with no answers.
            await GradeAsync(open, quiz, [], ct);
        }

        if (quiz.MaxAttempts is { } max && attempts.Count >= max)
            throw new QuizException("No attempts left for this quiz.", StatusCodes.Status409Conflict);

        var attempt = new QuizAttempt { QuizId = quizId, UserId = userId, StartedAt = now };
        db.QuizAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (attempt, quiz);
    }

    public async Task<QuizAttempt> SubmitAsync(Guid userId, Guid attemptId, IReadOnlyList<Answer> answers, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var attempt = await db.QuizAttempts
            .FromSqlInterpolated($"select * from quiz_attempts where id = {attemptId} for update")
            .SingleOrDefaultAsync(ct);
        if (attempt is null || attempt.UserId != userId) throw new QuizException("Attempt not found.", StatusCodes.Status404NotFound);
        if (attempt.SubmittedAt is not null) throw new QuizException("This attempt was already submitted.", StatusCodes.Status409Conflict);

        var quiz = await db.Quizzes.AsNoTracking().SingleAsync(q => q.Id == attempt.QuizId, ct);
        var late = Deadline(attempt, quiz) is { } deadline && clock.GetUtcNow() > deadline + SubmitGrace;
        await GradeAsync(attempt, quiz, late ? [] : answers, ct);
        await tx.CommitAsync(ct);
        return attempt;
    }

    private async Task GradeAsync(QuizAttempt attempt, Quiz quiz, IReadOnlyList<Answer> answers, CancellationToken ct)
    {
        var questions = await GradedQuestions(db, quiz.Id).Include(q => q.Options).AsNoTracking().ToListAsync(ct);
        var chosen = answers.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.Last().OptionId);

        decimal score = 0, max = 0;
        foreach (var q in questions)
        {
            max += q.Marks;
            // An option id from another question is treated as no answer.
            var option = chosen.GetValueOrDefault(q.Id) is { } oid ? q.Options.FirstOrDefault(o => o.Id == oid) : null;
            var correct = option?.IsCorrect == true;
            if (correct) score += q.Marks;
            db.QuizAnswers.Add(new QuizAnswer { AttemptId = attempt.Id, QuestionId = q.Id, OptionId = option?.Id, IsCorrect = correct });
        }

        attempt.SubmittedAt = clock.GetUtcNow();
        attempt.Score = score;
        attempt.MaxScore = max;
        attempt.Passed = max > 0 && score * 100 >= quiz.PassScorePct * max;
        await db.SaveChangesAsync(ct);
    }
}
