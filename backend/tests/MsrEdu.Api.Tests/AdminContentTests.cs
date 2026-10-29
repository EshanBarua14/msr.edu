using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using static MsrEdu.Api.Endpoints.AdminContentEndpoints;

namespace MsrEdu.Api.Tests;

public class AdminContentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record Created(Guid Id);

    private static string Email() => $"{Guid.NewGuid():N}@x.test";
    private static string Slug() => $"c-{Guid.NewGuid():N}"[..20];

    private static CourseRequest Course(string slug, string status = "draft", string? cover = null) =>
        new(slug, "Course", "sum", "desc", cover, null, status, false, 0);

    private static LessonRequest Lesson(string slug, Guid? module = null, string status = "draft", string? video = null) =>
        new(module, slug, "Lesson", null, "text", 60, false, status, "body", video);

    private static QuestionRequest Question(string prompt = "2+2?") =>
        new(prompt, "because", 1, [new(null, "4", true), new(null, "5", false)]);

    private Task<HttpClient> Editor() => factory.SignedIn(Email(), RoleKeys.ContentManager);

    private static async Task<T> Ok<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Only_content_roles_can_author()
    {
        var learner = await factory.SignedIn(Email());
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/admin/courses")).StatusCode);
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync("/api/admin/courses", Course(Slug()))).StatusCode);
        var owner = await factory.SignedIn(Email(), RoleKeys.OwnerAdmin);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/admin/courses")).StatusCode);
    }

    [Fact]
    public async Task Course_lifecycle_with_modules_lessons_and_order()
    {
        var client = await Editor();
        var slug = Slug();
        var course = await Ok<Created>(await client.PostAsJsonAsync("/api/admin/courses", Course(slug)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/courses", Course(slug))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/courses", Course("Bad Slug"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/courses", Course(Slug(), cover: "javascript:alert(1)"))).StatusCode);

        var m1 = await Ok<ModuleDto>(await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/modules", new ModuleRequest("One")));
        var m2 = await Ok<ModuleDto>(await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/modules", new ModuleRequest("Two")));
        var a = await Ok<LessonEditor>(await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/lessons", Lesson("a", m1.Id)));
        var b = await Ok<LessonEditor>(await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/lessons", Lesson("b", m1.Id)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/lessons", Lesson("a"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/lessons", Lesson("c", video: "http://x.test/v.mp4"))).StatusCode);

        var order = new OrderRequest([m2.Id, m1.Id], [new(b.Id, m2.Id), new(a.Id, m1.Id)]);
        var editor = await Ok<CourseEditor>(await client.PutAsJsonAsync($"/api/admin/courses/{course.Id}/order", order));
        Assert.Equal([m2.Id, m1.Id], editor.Modules.Select(m => m.Id));
        Assert.Equal((b.Id, m2.Id), (editor.Lessons[0].Id, editor.Lessons[0].ModuleId!.Value));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/admin/courses/{course.Id}/order", new OrderRequest([m1.Id], [new(a.Id, null)]))).StatusCode);

        // Publishing makes the course and lesson visible in the public catalogue.
        await Ok<CourseEditor>(await client.PutAsJsonAsync($"/api/admin/courses/{course.Id}", Course(slug, "published")));
        await Ok<LessonEditor>(await client.PutAsJsonAsync($"/api/admin/lessons/{a.Id}", Lesson("a", m1.Id, "published") with { IsPreview = true }));
        var anon = factory.Client();
        var publicCourse = await anon.GetFromJsonAsync<Endpoints.CatalogEndpoints.CourseDetail>($"/api/courses/{slug}");
        Assert.Equal(a.Id, Assert.Single(publicCourse!.Modules.SelectMany(m => m.Lessons)).Id);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/admin/lessons/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/lessons/{b.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/admin/courses/{course.Id}")).StatusCode);
    }

    [Fact]
    public async Task Draft_ai_questions_need_review_and_answered_questions_are_protected()
    {
        var client = await Editor();
        var course = await Ok<Created>(await client.PostAsJsonAsync("/api/admin/courses", Course(Slug(), "published")));
        var quiz = await Ok<Created>(await client.PostAsJsonAsync($"/api/admin/courses/{course.Id}/quizzes",
            new QuizRequest("Quiz", null, null, 50, null, null, true, "published")));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/quizzes/{quiz.Id}/questions",
            Question() with { Options = [new(null, "only", true)] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/quizzes/{quiz.Id}/questions",
            Question() with { Options = [new(null, "a", false), new(null, "b", false)] })).StatusCode);
        var editor = await Ok<QuizEditor>(await client.PostAsJsonAsync($"/api/admin/quizzes/{quiz.Id}/questions", Question()));
        var manual = Assert.Single(editor.Questions);
        Assert.Equal("approved", manual.ReviewStatus);

        Guid aiId;
        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ai = new Question { QuizId = quiz.Id, Prompt = "AI?", AiGenerated = true, Position = 1,
                Options = [new() { Label = "yes", IsCorrect = true }, new() { Label = "no", Position = 1 }] };
            db.Questions.Add(ai);
            await db.SaveChangesAsync();
            aiId = ai.Id;
        }

        var learner = await factory.SignedIn(Email());
        var visible = await learner.GetFromJsonAsync<Endpoints.LearningEndpoints.QuizDetail>($"/api/quizzes/{quiz.Id}");
        Assert.Single(visible!.Questions);

        editor = await Ok<QuizEditor>(await client.PostAsJsonAsync($"/api/admin/questions/{aiId}/review", new ReviewRequest(true)));
        Assert.All(editor.Questions, q => Assert.Equal("approved", q.ReviewStatus));
        visible = await learner.GetFromJsonAsync<Endpoints.LearningEndpoints.QuizDetail>($"/api/quizzes/{quiz.Id}");
        Assert.Equal(2, visible!.Questions.Count);

        // Once answered, a question's correct option can't be changed and it can't be deleted.
        var started = await Ok<Endpoints.LearningEndpoints.StartedAttempt>(await learner.PostAsync($"/api/quizzes/{quiz.Id}/attempts", null));
        await learner.PostAsJsonAsync($"/api/quiz-attempts/{started.AttemptId}/submit", new Endpoints.LearningEndpoints.SubmitRequest([]));

        var flipped = new QuestionRequest(manual.Prompt, null, 1, manual.Options.Select(o => o with { IsCorrect = !o.IsCorrect }).ToList());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/admin/questions/{manual.Id}", flipped)).StatusCode);
        var reworded = new QuestionRequest("What is 2 + 2?", "Count it.", 1, manual.Options);
        editor = await Ok<QuizEditor>(await client.PutAsJsonAsync($"/api/admin/questions/{manual.Id}", reworded));
        Assert.Equal("What is 2 + 2?", editor.Questions[0].Prompt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/admin/questions/{manual.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/admin/quizzes/{quiz.Id}")).StatusCode);
    }

    [Fact]
    public async Task Quiz_lesson_and_module_must_belong_to_the_same_course()
    {
        var client = await Editor();
        var c1 = await Ok<Created>(await client.PostAsJsonAsync("/api/admin/courses", Course(Slug())));
        var c2 = await Ok<Created>(await client.PostAsJsonAsync("/api/admin/courses", Course(Slug())));
        var other = await Ok<ModuleDto>(await client.PostAsJsonAsync($"/api/admin/courses/{c2.Id}/modules", new ModuleRequest("x")));
        var otherLesson = await Ok<LessonEditor>(await client.PostAsJsonAsync($"/api/admin/courses/{c2.Id}/lessons", Lesson("x")));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/courses/{c1.Id}/lessons", Lesson("y", other.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/courses/{c1.Id}/quizzes",
            new QuizRequest("Q", null, otherLesson.Id, 50, null, null, false, "draft"))).StatusCode);
    }
}
