using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.AdminContentEndpoints;

namespace MsrEdu.Api.Tests;

/// <summary>Answers like the Messages API and records what was sent.</summary>
public sealed class StubAnthropic(string text, string stopReason = "end_turn") : HttpMessageHandler
{
    public JsonNode? Body { get; private set; }
    public HttpRequestMessage? Request { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
        var reply = new JsonObject
        {
            ["id"] = "msg_test", ["type"] = "message", ["role"] = "assistant", ["model"] = "claude-opus-5-5",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["stop_reason"] = stopReason, ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 10 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}

public class ClaudeQuestionGeneratorTests
{
    private const string Drafts = """
        {"questions":[
          {"prompt":"What is 2+2?","explanation":"Add them.","options":[{"label":"4","is_correct":true},{"label":"5","is_correct":false}]},
          {"prompt":"Two right answers","explanation":"x","options":[{"label":"a","is_correct":true},{"label":"b","is_correct":true}]},
          {"prompt":"   ","explanation":"x","options":[{"label":"a","is_correct":true},{"label":"b","is_correct":false}]}
        ]}
        """;

    private static ClaudeQuestionGenerator Generator(StubAnthropic stub) =>
        new(Options.Create(new AiOptions { AnthropicApiKey = "test-key" }), new HttpClient(stub));

    [Fact]
    public async Task Sends_structured_output_request_and_keeps_only_valid_drafts()
    {
        var stub = new StubAnthropic(Drafts);
        var drafts = await Generator(stub).DraftAsync("Lesson: 2+2=4.", 3);

        var d = Assert.Single(drafts);
        Assert.Equal("What is 2+2?", d.Prompt);
        Assert.True(d.Options[0].IsCorrect);

        var body = stub.Body!;
        Assert.Equal("claude-opus-5-5", (string?)body["model"]);
        Assert.Equal("default", (string?)body["fallbacks"]);
        Assert.Equal("json_schema", (string?)body["output_config"]!["format"]!["type"]);
        Assert.Equal("medium", (string?)body["output_config"]!["effort"]);
        Assert.Contains("Lesson: 2+2=4.", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Contains("server-side-fallback-2026-07-01", string.Join(",", stub.Request!.Headers.GetValues("anthropic-beta")));
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task Refusals_and_cut_off_output_become_clear_errors(string stopReason)
    {
        await Assert.ThrowsAsync<AiDraftException>(() => Generator(new StubAnthropic("{}", stopReason)).DraftAsync("text", 2));
    }

    [Fact]
    public void Malformed_json_becomes_a_clear_error() =>
        Assert.Throws<AiDraftException>(() => ClaudeQuestionGenerator.Parse("{\"nope\":1}"));
}

public sealed class FakeGenerator : IQuestionGenerator
{
    public string? LastSource { get; private set; }
    public bool IsConfigured => true;

    public Task<IReadOnlyList<DraftQuestion>> DraftAsync(string sourceText, int count, CancellationToken ct = default)
    {
        LastSource = sourceText;
        return Task.FromResult<IReadOnlyList<DraftQuestion>>(Enumerable.Range(1, count)
            .Select(i => new DraftQuestion($"Q{i}?", "because", [new("yes", true), new("no", false)])).ToList());
    }
}

public sealed class AiApiFactory : ApiFactory
{
    public FakeGenerator Generator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IQuestionGenerator>(Generator));
    }
}

public class AiDraftEndpointTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    private static string Email() => $"{Guid.NewGuid():N}@x.test";

    [Fact]
    public async Task Drafts_are_saved_for_review_and_hidden_from_learners()
    {
        Quiz quiz;
        Lesson preview;
        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (var course, preview, _) = await new Seed(db).CourseAsync($"ai-{Guid.NewGuid():N}", free: true);
            quiz = new Quiz { CourseId = course.Id, Title = "Q", Status = PublishStatus.Published };
            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
        }

        var editor = await factory.SignedIn(Email(), RoleKeys.ContentManager);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/admin/quizzes/{quiz.Id}/ai-drafts", new DraftRequest(null, 50))).StatusCode);

        var res = await editor.PostAsJsonAsync($"/api/admin/quizzes/{quiz.Id}/ai-drafts", new DraftRequest(preview.Id, 3));
        var editorView = await res.Content.ReadFromJsonAsync<QuizEditor>();
        Assert.Equal(3, editorView!.Questions.Count);
        Assert.All(editorView.Questions, q => Assert.True(q.AiGenerated && q.ReviewStatus == "draft"));
        Assert.Contains("free body", factory.Generator.LastSource);
        Assert.DoesNotContain("paid body", factory.Generator.LastSource);

        var learner = await factory.SignedIn(Email());
        var visible = await learner.GetFromJsonAsync<Endpoints.LearningEndpoints.QuizDetail>($"/api/quizzes/{quiz.Id}");
        Assert.Empty(visible!.Questions);

        Assert.Equal(HttpStatusCode.Forbidden, (await learner.PostAsJsonAsync($"/api/admin/quizzes/{quiz.Id}/ai-drafts", new DraftRequest(null, 1))).StatusCode);
    }
}

public class AiNotConfiguredTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Status_reports_not_configured_and_drafting_is_unavailable()
    {
        var editor = await factory.SignedIn($"{Guid.NewGuid():N}@x.test", RoleKeys.ContentManager);
        Assert.False((await editor.GetFromJsonAsync<AiStatus>("/api/admin/ai/status"))!.Configured);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await editor.PostAsJsonAsync($"/api/admin/quizzes/{Guid.NewGuid()}/ai-drafts", new DraftRequest(null, 1))).StatusCode);
    }
}
