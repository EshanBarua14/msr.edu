using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Endpoints;
using static MsrEdu.Api.Endpoints.LearningEndpoints;

namespace MsrEdu.Api.Tests;

public class LearningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Email() => $"{Guid.NewGuid():N}@x.test";
    private static string Slug() => $"c-{Guid.NewGuid():N}";

    private async Task<(Course Course, Lesson Preview, Lesson Paid)> CourseAsync(bool free = false)
    {
        await using var scope = factory.Scope();
        return await new Seed(scope.ServiceProvider.GetRequiredService<AppDbContext>()).CourseAsync(Slug(), free: free);
    }

    /// <summary>Two approved questions (1 mark each) and one AI draft that must not be shown or graded.</summary>
    private async Task<(Quiz Quiz, Question Q1, Question Q2)> QuizAsync(Course course, bool preview = false, int? maxAttempts = null, int? timeLimit = null)
    {
        await using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quiz = new Quiz { CourseId = course.Id, Title = "Check", Status = PublishStatus.Published, IsPreview = preview, PassScorePct = 50, MaxAttempts = maxAttempts, TimeLimitSeconds = timeLimit };
        Question Q(string prompt, int pos, QuestionReviewStatus status = QuestionReviewStatus.Approved) => new()
        {
            QuizId = quiz.Id, Prompt = prompt, Position = pos, ReviewStatus = status, Explanation = $"why {prompt}",
            Options = [new() { Label = "right", IsCorrect = true }, new() { Label = "wrong", Position = 1 }],
        };
        var q1 = Q("one", 0);
        var q2 = Q("two", 1);
        quiz.Questions.AddRange([q1, q2, Q("draft", 2, QuestionReviewStatus.Draft)]);
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();
        return (quiz, q1, q2);
    }

    private static Guid Right(Question q) => q.Options.Single(o => o.IsCorrect).Id;
    private static Guid Wrong(Question q) => q.Options.Single(o => !o.IsCorrect).Id;

    [Fact]
    public async Task Progress_is_recorded_and_only_moves_forward()
    {
        var (course, preview, _) = await CourseAsync();
        var client = await factory.SignedIn(Email());

        await client.PostAsJsonAsync($"/api/lessons/{preview.Id}/progress", new ProgressRequest(60, false));
        var res = await client.PostAsJsonAsync($"/api/lessons/{preview.Id}/progress", new ProgressRequest(20, false));
        Assert.Equal(60, (await res.Content.ReadFromJsonAsync<ProgressResponse>())!.ProgressPct);

        res = await client.PostAsJsonAsync($"/api/lessons/{preview.Id}/progress", new ProgressRequest(0, true));
        Assert.True((await res.Content.ReadFromJsonAsync<ProgressResponse>())!.Completed);

        var me = await client.GetFromJsonAsync<MyLearning>("/api/me/learning");
        var row = Assert.Single(me!.Courses);
        Assert.Equal(course.Id, row.CourseId);
        Assert.Equal((2, 1), (row.TotalLessons, row.CompletedLessons));
        Assert.Equal(preview.Id, row.LastLesson!.Id);
        Assert.False(row.HasAccess);

        var detail = await client.GetFromJsonAsync<CatalogEndpoints.CourseDetail>($"/api/courses/{course.Slug}");
        Assert.Equal([preview.Id], detail!.CompletedLessonIds);
    }

    [Fact]
    public async Task Progress_on_a_locked_lesson_is_refused()
    {
        var (_, _, paid) = await CourseAsync();
        var client = await factory.SignedIn(Email());
        var res = await client.PostAsJsonAsync($"/api/lessons/{paid.Id}/progress", new ProgressRequest(50, false));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client().PostAsJsonAsync($"/api/lessons/{paid.Id}/progress", new ProgressRequest(50, false))).StatusCode);
    }

    [Fact]
    public async Task Quiz_hides_answers_and_drafts_until_graded_on_the_server()
    {
        var (course, _, _) = await CourseAsync(free: true);
        var (quiz, q1, q2) = await QuizAsync(course);
        var client = await factory.SignedIn(Email());

        var raw = await client.GetStringAsync($"/api/quizzes/{quiz.Id}");
        Assert.DoesNotContain("isCorrect", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("why one", raw);
        Assert.DoesNotContain("draft", raw);
        Assert.Equal(2, JsonDocument.Parse(raw).RootElement.GetProperty("questions").GetArrayLength());

        var outline = await client.GetFromJsonAsync<CatalogEndpoints.CourseDetail>($"/api/courses/{course.Slug}");
        Assert.Equal(2, Assert.Single(outline!.Quizzes).QuestionCount);

        var started = await (await client.PostAsync($"/api/quizzes/{quiz.Id}/attempts", null)).Content.ReadFromJsonAsync<StartedAttempt>();
        var res = await client.PostAsJsonAsync($"/api/quiz-attempts/{started!.AttemptId}/submit",
            new SubmitRequest([new(q1.Id, Right(q1)), new(q2.Id, Right(q1))])); // q2 answered with q1's option: no answer
        var result = await res.Content.ReadFromJsonAsync<AttemptResult>();
        Assert.Equal((1m, 2m, 50, true), (result!.Score, result.MaxScore, result.ScorePct, result.Passed));
        Assert.Equal("why one", result.Answers[0].Explanation);
        Assert.Null(result.Answers[1].ChosenOptionId);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/quiz-attempts/{started.AttemptId}/submit", new SubmitRequest([]))).StatusCode);
        var stranger = await factory.SignedIn(Email());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/quiz-attempts/{started.AttemptId}")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<AttemptSummary>>("/api/me/quiz-attempts"))!);
    }

    [Fact]
    public async Task Paid_quiz_needs_access_and_preview_quiz_does_not()
    {
        var (course, _, _) = await CourseAsync();
        var (paid, _, _) = await QuizAsync(course);
        var (preview, _, _) = await QuizAsync(course, preview: true);
        var client = await factory.SignedIn(Email());

        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.GetAsync($"/api/quizzes/{paid.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsync($"/api/quizzes/{paid.Id}/attempts", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/quizzes/{preview.Id}")).StatusCode);
    }

    [Fact]
    public async Task Attempt_limit_is_enforced_and_open_attempt_is_resumed()
    {
        var (course, _, _) = await CourseAsync(free: true);
        var (quiz, q1, _) = await QuizAsync(course, maxAttempts: 1);
        var client = await factory.SignedIn(Email());

        var a = await (await client.PostAsync($"/api/quizzes/{quiz.Id}/attempts", null)).Content.ReadFromJsonAsync<StartedAttempt>();
        var b = await (await client.PostAsync($"/api/quizzes/{quiz.Id}/attempts", null)).Content.ReadFromJsonAsync<StartedAttempt>();
        Assert.Equal(a!.AttemptId, b!.AttemptId);

        await client.PostAsJsonAsync($"/api/quiz-attempts/{a.AttemptId}/submit", new SubmitRequest([new(q1.Id, Wrong(q1))]));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/quizzes/{quiz.Id}/attempts", null)).StatusCode);
    }

    [Fact]
    public async Task Late_submission_scores_zero()
    {
        var (course, _, _) = await CourseAsync(free: true);
        var (quiz, q1, q2) = await QuizAsync(course, timeLimit: 60);
        var client = await factory.SignedIn(Email());
        var started = await (await client.PostAsync($"/api/quizzes/{quiz.Id}/attempts", null)).Content.ReadFromJsonAsync<StartedAttempt>();

        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var attempt = await db.QuizAttempts.FindAsync(started!.AttemptId);
            attempt!.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var res = await client.PostAsJsonAsync($"/api/quiz-attempts/{started!.AttemptId}/submit",
            new SubmitRequest([new(q1.Id, Right(q1)), new(q2.Id, Right(q2))]));
        var result = await res.Content.ReadFromJsonAsync<AttemptResult>();
        Assert.Equal((0m, false), (result!.Score, result.Passed));
    }
}
