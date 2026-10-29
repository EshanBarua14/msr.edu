using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Services;

namespace MsrEdu.Api.Endpoints;

/// <summary>Learner progress and quizzes. Every write is for the signed-in user only.</summary>
public static class LearningEndpoints
{
    public record ProgressRequest(int ProgressPct, bool Completed);
    public record ProgressResponse(Guid LessonId, int ProgressPct, bool Completed);

    public record LastLesson(Guid Id, string Title);
    public record CourseProgress(
        Guid CourseId, string Slug, string Title, string? CoverUrl, int TotalLessons, int CompletedLessons,
        LastLesson? LastLesson, DateTimeOffset LastActivityAt, bool HasAccess);
    public record AttemptSummary(
        Guid Id, Guid QuizId, string QuizTitle, string CourseSlug, DateTimeOffset? SubmittedAt, decimal? Score, decimal? MaxScore, bool? Passed);
    public record MyLearning(IReadOnlyList<CourseProgress> Courses, IReadOnlyList<AttemptSummary> RecentAttempts);

    public record QuizOption(Guid Id, string Label);
    public record QuizQuestion(Guid Id, string Prompt, decimal Marks, IReadOnlyList<QuizOption> Options);
    public record QuizDetail(
        Guid Id, string Title, string? Description, string CourseSlug, string CourseTitle, int PassScorePct, int? TimeLimitSeconds,
        int? MaxAttempts, int AttemptsUsed, IReadOnlyList<QuizQuestion> Questions);
    public record StartedAttempt(Guid AttemptId, DateTimeOffset StartedAt, DateTimeOffset? Deadline);
    public record SubmitRequest(IReadOnlyList<QuizService.Answer> Answers);
    public record AnswerReview(Guid QuestionId, string Prompt, Guid? ChosenOptionId, IReadOnlyList<Guid> CorrectOptionIds, bool IsCorrect, string? Explanation);
    public record AttemptResult(
        Guid Id, Guid QuizId, string QuizTitle, string CourseSlug, DateTimeOffset SubmittedAt, decimal Score, decimal MaxScore,
        int ScorePct, bool Passed, IReadOnlyList<AnswerReview> Answers);

    public static void MapLearningEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();

        g.MapPost("/lessons/{id:guid}/progress", async (Guid id, ProgressRequest req, AppDbContext db, ICurrentUser user, AccessService access, TimeProvider clock) =>
        {
            var courseId = await db.Lessons.Where(l => l.Id == id).Select(l => (Guid?)l.CourseId).FirstOrDefaultAsync();
            if (courseId is null || !await access.CanViewLessonAsync(user.Id, user.IsStaff, id)) return Results.NotFound();

            var pct = req.Completed ? 100 : Math.Clamp(req.ProgressPct, 0, 100);
            var now = clock.GetUtcNow();
            DateTimeOffset? completedAt = req.Completed ? now : null;
            // Progress only moves forward; completion is sticky. Upserts are safe under concurrent calls.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into lesson_progress (user_id, lesson_id, progress_pct, completed_at, updated_at)
                values ({user.Id}, {id}, {(short)pct}, {completedAt}, {now})
                on conflict (user_id, lesson_id) do update set
                  progress_pct = greatest(lesson_progress.progress_pct, excluded.progress_pct),
                  completed_at = coalesce(lesson_progress.completed_at, excluded.completed_at),
                  updated_at = excluded.updated_at
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into enrollments (user_id, course_id, started_at, last_lesson_id, last_activity_at)
                values ({user.Id}, {courseId}, {now}, {id}, {now})
                on conflict (user_id, course_id) do update set
                  last_lesson_id = excluded.last_lesson_id, last_activity_at = excluded.last_activity_at
                """);

            var saved = await db.LessonProgress.AsNoTracking().SingleAsync(p => p.UserId == user.Id && p.LessonId == id);
            return Results.Ok(new ProgressResponse(id, saved.ProgressPct, saved.CompletedAt is not null));
        });

        g.MapGet("/me/learning", async (AppDbContext db, ICurrentUser user, AccessService access) =>
        {
            var userId = user.Id!.Value;
            var rows = await (
                from e in db.Enrollments
                join c in db.Courses on e.CourseId equals c.Id
                where e.UserId == userId && (c.Status == PublishStatus.Published || user.IsStaff)
                orderby e.LastActivityAt descending
                select new
                {
                    c.Id, c.Slug, c.Title, c.CoverUrl, c.IsFree, e.LastActivityAt,
                    Last = db.Lessons.Where(l => l.Id == e.LastLessonId).Select(l => new LastLesson(l.Id, l.Title)).FirstOrDefault(),
                    Total = db.Lessons.Count(l => l.CourseId == c.Id && l.Status == PublishStatus.Published),
                    Completed = db.LessonProgress.Count(p => p.UserId == userId && p.CompletedAt != null
                        && db.Lessons.Any(l => l.Id == p.LessonId && l.CourseId == c.Id && l.Status == PublishStatus.Published)),
                }).AsNoTracking().ToListAsync();

            var courses = new List<CourseProgress>(rows.Count);
            foreach (var r in rows)
            {
                var hasAccess = user.IsStaff || r.IsFree || await access.HasCourseAccessAsync(userId, r.Id);
                courses.Add(new CourseProgress(r.Id, r.Slug, r.Title, r.CoverUrl, r.Total, r.Completed, r.Last, r.LastActivityAt, hasAccess));
            }
            return new MyLearning(courses, await Attempts(db, userId).Take(5).ToListAsync());
        });

        g.MapGet("/me/quiz-attempts", async (AppDbContext db, ICurrentUser user) =>
            await Attempts(db, user.Id!.Value).ToListAsync());

        g.MapGet("/quizzes/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser user, AccessService access) =>
        {
            if (!await access.CanViewQuizAsync(user.Id, user.IsStaff, id)) return await Denied(db, id, user);
            var quiz = await db.Quizzes.AsNoTracking().Include(q => q.Course).SingleAsync(q => q.Id == id);
            var questions = await QuizService.GradedQuestions(db, id).AsNoTracking().OrderBy(q => q.Position)
                .Select(q => new QuizQuestion(q.Id, q.Prompt, q.Marks,
                    q.Options.OrderBy(o => o.Position).Select(o => new QuizOption(o.Id, o.Label)).ToList()))
                .ToListAsync();
            var used = await db.QuizAttempts.CountAsync(a => a.QuizId == id && a.UserId == user.Id);
            return Results.Ok(new QuizDetail(quiz.Id, quiz.Title, quiz.Description, quiz.Course!.Slug, quiz.Course.Title,
                quiz.PassScorePct, quiz.TimeLimitSeconds, quiz.MaxAttempts, used, questions));
        });

        g.MapPost("/quizzes/{id:guid}/attempts", async (Guid id, AppDbContext db, ICurrentUser user, AccessService access, QuizService quizzes) =>
        {
            if (!await access.CanViewQuizAsync(user.Id, user.IsStaff, id)) return await Denied(db, id, user);
            try
            {
                var (attempt, quiz) = await quizzes.StartAsync(user.Id!.Value, id);
                return Results.Ok(new StartedAttempt(attempt.Id, attempt.StartedAt, QuizService.Deadline(attempt, quiz)));
            }
            catch (QuizException e) { return Results.Problem(e.Message, statusCode: e.StatusCode); }
        });

        g.MapPost("/quiz-attempts/{id:guid}/submit", async (Guid id, SubmitRequest req, AppDbContext db, ICurrentUser user, QuizService quizzes) =>
        {
            try
            {
                var attempt = await quizzes.SubmitAsync(user.Id!.Value, id, req.Answers ?? []);
                return Results.Ok(await Result(db, attempt.Id));
            }
            catch (QuizException e) { return Results.Problem(e.Message, statusCode: e.StatusCode); }
        });

        // Correct answers and explanations are only revealed for the learner's own submitted attempts.
        g.MapGet("/quiz-attempts/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser user) =>
        {
            var owner = await db.QuizAttempts.Where(a => a.Id == id && a.SubmittedAt != null).Select(a => (Guid?)a.UserId).FirstOrDefaultAsync();
            if (owner is null || (owner != user.Id && !user.IsStaff)) return Results.NotFound();
            return Results.Ok(await Result(db, id));
        });
    }

    private static IQueryable<AttemptSummary> Attempts(AppDbContext db, Guid userId) =>
        from a in db.QuizAttempts
        join q in db.Quizzes on a.QuizId equals q.Id
        where a.UserId == userId && a.SubmittedAt != null
        orderby a.SubmittedAt descending
        select new AttemptSummary(a.Id, q.Id, q.Title, q.Course!.Slug, a.SubmittedAt, a.Score, a.MaxScore, a.Passed);

    private static async Task<AttemptResult> Result(AppDbContext db, Guid attemptId)
    {
        var a = await db.QuizAttempts.AsNoTracking().Include(x => x.Answers).SingleAsync(x => x.Id == attemptId);
        var quiz = await db.Quizzes.AsNoTracking().Include(q => q.Course).SingleAsync(q => q.Id == a.QuizId);
        var answered = a.Answers.Select(x => x.QuestionId).ToList();
        var questions = await db.Questions.AsNoTracking().Include(q => q.Options)
            .Where(q => answered.Contains(q.Id)).OrderBy(q => q.Position).ToListAsync();

        var reviews = questions.Select(q =>
        {
            var ans = a.Answers.Single(x => x.QuestionId == q.Id);
            return new AnswerReview(q.Id, q.Prompt, ans.OptionId,
                q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList(), ans.IsCorrect == true, q.Explanation);
        }).ToList();
        var max = a.MaxScore ?? 0;
        var score = a.Score ?? 0;
        return new AttemptResult(a.Id, quiz.Id, quiz.Title, quiz.Course!.Slug, a.SubmittedAt!.Value, score, max,
            max > 0 ? (int)Math.Floor(score * 100 / max) : 0, a.Passed == true, reviews);
    }

    /// <summary>404 for hidden quizzes; otherwise sign in (401) or purchase (402).</summary>
    private static async Task<IResult> Denied(AppDbContext db, Guid quizId, ICurrentUser user)
    {
        var visible = await db.Quizzes.AnyAsync(q => q.Id == quizId && q.Status == PublishStatus.Published
            && db.Courses.Any(c => c.Id == q.CourseId && c.Status == PublishStatus.Published));
        if (!visible) return Results.NotFound();
        return user.IsAuthenticated ? Results.StatusCode(StatusCodes.Status402PaymentRequired) : Results.Unauthorized();
    }
}
