using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Endpoints;

/// <summary>
/// Course, lesson and quiz authoring for content managers. Published items are archived rather than deleted
/// so learner progress and quiz history are never lost; hard delete is only allowed while nothing depends on them.
/// </summary>
public static partial class AdminContentEndpoints
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public record CategoryDto(Guid Id, string Slug, string Name, string? Description, int Position);
    public record CategoryRequest(string Slug, string Name, string? Description, int Position);

    public record CourseRow(Guid Id, string Slug, string Title, string Status, bool IsFree, string? Category,
        int Lessons, int Quizzes, int PendingReview, DateTimeOffset UpdatedAt);
    public record CourseRequest(string Slug, string Title, string? Summary, string? Description, string? CoverUrl,
        Guid? CategoryId, string Status, bool IsFree, int Position);
    public record ModuleDto(Guid Id, string Title, int Position);
    public record LessonRow(Guid Id, Guid? ModuleId, string Slug, string Title, string Kind, int? DurationSeconds,
        bool IsPreview, string Status, int Position);
    public record QuizRow(Guid Id, string Title, Guid? LessonId, string Status, bool IsPreview, int Questions, int PendingReview);
    public record CourseEditor(Guid Id, string Slug, string Title, string? Summary, string? Description, string? CoverUrl,
        Guid? CategoryId, string Status, bool IsFree, int Position,
        IReadOnlyList<ModuleDto> Modules, IReadOnlyList<LessonRow> Lessons, IReadOnlyList<QuizRow> Quizzes);

    public record ModuleRequest(string Title);
    public record OrderRequest(IReadOnlyList<Guid> Modules, IReadOnlyList<LessonPlacement> Lessons);
    public record LessonPlacement(Guid Id, Guid? ModuleId);

    public record LessonRequest(Guid? ModuleId, string Slug, string Title, string? Summary, string Kind, int? DurationSeconds,
        bool IsPreview, string Status, string? Body, string? VideoRef);
    public record LessonEditor(Guid Id, Guid CourseId, Guid? ModuleId, string Slug, string Title, string? Summary, string Kind,
        int? DurationSeconds, bool IsPreview, string Status, int Position, string? Body, string? VideoRef);

    public record QuizRequest(string Title, string? Description, Guid? LessonId, int PassScorePct, int? TimeLimitSeconds,
        int? MaxAttempts, bool IsPreview, string Status);
    public record OptionDto(Guid? Id, string Label, bool IsCorrect);
    public record QuestionDto(Guid Id, string Prompt, string? Explanation, decimal Marks, int Position, bool AiGenerated,
        string ReviewStatus, DateTimeOffset? ReviewedAt, IReadOnlyList<OptionDto> Options);
    public record QuizEditor(Guid Id, Guid CourseId, string CourseTitle, string Title, string? Description, Guid? LessonId,
        int PassScorePct, int? TimeLimitSeconds, int? MaxAttempts, bool IsPreview, string Status, int Attempts,
        IReadOnlyList<QuestionDto> Questions);
    public record QuestionRequest(string Prompt, string? Explanation, decimal Marks, IReadOnlyList<OptionDto> Options);
    public record ReviewRequest(bool Approve);
    public record AiStatus(bool Configured);
    public record DraftRequest(Guid? LessonId, int Count);

    public static void MapAdminContentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization(Policies.Content);

        // ------------------------------------------------------------ categories
        g.MapGet("/categories", async (AppDbContext db) =>
            await db.Categories.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Slug, c.Name, c.Description, c.Position)).ToListAsync());

        g.MapPost("/categories", async (CategoryRequest req, AppDbContext db) =>
        {
            if (Invalid(req.Slug, req.Name) is { } bad) return bad;
            var c = new Category { Slug = req.Slug, Name = req.Name.Trim(), Description = req.Description, Position = req.Position };
            db.Categories.Add(c);
            return await Save(db, () => Results.Ok(new CategoryDto(c.Id, c.Slug, c.Name, c.Description, c.Position)));
        });

        g.MapPut("/categories/{id:guid}", async (Guid id, CategoryRequest req, AppDbContext db) =>
        {
            if (Invalid(req.Slug, req.Name) is { } bad) return bad;
            var c = await db.Categories.FindAsync(id);
            if (c is null) return Results.NotFound();
            (c.Slug, c.Name, c.Description, c.Position) = (req.Slug, req.Name.Trim(), req.Description, req.Position);
            return await Save(db, () => Results.Ok(new CategoryDto(c.Id, c.Slug, c.Name, c.Description, c.Position)));
        });

        // ------------------------------------------------------------ courses
        g.MapGet("/courses", async (AppDbContext db) =>
            await db.Courses.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Title)
                .Select(c => new CourseRow(c.Id, c.Slug, c.Title, Label(c.Status), c.IsFree, c.Category!.Name,
                    db.Lessons.Count(l => l.CourseId == c.Id),
                    db.Quizzes.Count(q => q.CourseId == c.Id),
                    db.Questions.Count(x => x.ReviewStatus == QuestionReviewStatus.Draft && db.Quizzes.Any(q => q.Id == x.QuizId && q.CourseId == c.Id)),
                    c.UpdatedAt))
                .ToListAsync());

        g.MapPost("/courses", async (CourseRequest req, AppDbContext db, ICurrentUser user) =>
        {
            if (InvalidCourse(req, out var status) is { } bad) return bad;
            var c = new Course { Slug = req.Slug, Title = req.Title.Trim(), CreatedBy = user.Id };
            Apply(c, req, status);
            db.Courses.Add(c);
            return await Save(db, () => Results.Ok(new { c.Id }));
        });

        g.MapGet("/courses/{id:guid}", async (Guid id, AppDbContext db) =>
            await CourseEditorAsync(db, id) is { } dto ? Results.Ok(dto) : Results.NotFound());

        g.MapPut("/courses/{id:guid}", async (Guid id, CourseRequest req, AppDbContext db) =>
        {
            if (InvalidCourse(req, out var status) is { } bad) return bad;
            var c = await db.Courses.FindAsync(id);
            if (c is null) return Results.NotFound();
            c.Slug = req.Slug;
            c.Title = req.Title.Trim();
            Apply(c, req, status);
            return await Save(db, async () => Results.Ok(await CourseEditorAsync(db, id)));
        });

        g.MapDelete("/courses/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var c = await db.Courses.FindAsync(id);
            if (c is null) return Results.NotFound();
            var inUse = c.PublishedAt is not null
                || await db.Enrollments.AnyAsync(e => e.CourseId == id)
                || await db.Products.AnyAsync(p => p.CourseId == id);
            if (inUse) return Conflict("This course has been published, sold or started. Archive it instead.");
            db.Courses.Remove(c);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ------------------------------------------------------------ modules and ordering
        g.MapPost("/courses/{id:guid}/modules", async (Guid id, ModuleRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Bad("Title is required.");
            if (!await db.Courses.AnyAsync(c => c.Id == id)) return Results.NotFound();
            var position = await db.Modules.Where(m => m.CourseId == id).MaxAsync(m => (int?)m.Position) ?? -1;
            var m = new Module { CourseId = id, Title = req.Title.Trim(), Position = position + 1 };
            db.Modules.Add(m);
            await db.SaveChangesAsync();
            return Results.Ok(new ModuleDto(m.Id, m.Title, m.Position));
        });

        g.MapPut("/modules/{id:guid}", async (Guid id, ModuleRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Bad("Title is required.");
            var m = await db.Modules.FindAsync(id);
            if (m is null) return Results.NotFound();
            m.Title = req.Title.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(new ModuleDto(m.Id, m.Title, m.Position));
        });

        // Lessons in a deleted module are kept and become ungrouped.
        g.MapDelete("/modules/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var m = await db.Modules.FindAsync(id);
            if (m is null) return Results.NotFound();
            db.Modules.Remove(m);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPut("/courses/{id:guid}/order", async (Guid id, OrderRequest req, AppDbContext db) =>
        {
            var modules = await db.Modules.Where(m => m.CourseId == id).ToListAsync();
            var lessons = await db.Lessons.Where(l => l.CourseId == id).ToListAsync();
            var moduleIds = modules.Select(m => m.Id).ToHashSet();
            if (!req.Modules.ToHashSet().SetEquals(moduleIds) || !req.Lessons.Select(l => l.Id).ToHashSet().SetEquals(lessons.Select(l => l.Id))
                || req.Lessons.Any(l => l.ModuleId is { } mid && !moduleIds.Contains(mid)))
                return Bad("The order must list every module and lesson of this course exactly once.");

            for (var i = 0; i < req.Modules.Count; i++) modules.Single(m => m.Id == req.Modules[i]).Position = i;
            for (var i = 0; i < req.Lessons.Count; i++)
            {
                var l = lessons.Single(x => x.Id == req.Lessons[i].Id);
                (l.ModuleId, l.Position) = (req.Lessons[i].ModuleId, i);
            }
            await db.SaveChangesAsync();
            return Results.Ok(await CourseEditorAsync(db, id));
        });

        // ------------------------------------------------------------ lessons
        g.MapPost("/courses/{id:guid}/lessons", async (Guid id, LessonRequest req, AppDbContext db) =>
        {
            if (!await db.Courses.AnyAsync(c => c.Id == id)) return Results.NotFound();
            var lesson = new Lesson { CourseId = id, Slug = req.Slug, Title = req.Title };
            lesson.Position = (await db.Lessons.Where(l => l.CourseId == id).MaxAsync(l => (int?)l.Position) ?? -1) + 1;
            if (await ApplyLesson(db, lesson, req) is { } bad) return bad;
            db.Lessons.Add(lesson);
            return await Save(db, () => Results.Ok(LessonEditorDto(lesson)));
        });

        g.MapGet("/lessons/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Lessons.AsNoTracking().Include(l => l.Content).FirstOrDefaultAsync(l => l.Id == id) is { } l
                ? Results.Ok(LessonEditorDto(l)) : Results.NotFound());

        g.MapPut("/lessons/{id:guid}", async (Guid id, LessonRequest req, AppDbContext db) =>
        {
            var lesson = await db.Lessons.Include(l => l.Content).FirstOrDefaultAsync(l => l.Id == id);
            if (lesson is null) return Results.NotFound();
            if (await ApplyLesson(db, lesson, req) is { } bad) return bad;
            return await Save(db, () => Results.Ok(LessonEditorDto(lesson)));
        });

        g.MapDelete("/lessons/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var lesson = await db.Lessons.FindAsync(id);
            if (lesson is null) return Results.NotFound();
            if (lesson.Status != PublishStatus.Draft || await db.LessonProgress.AnyAsync(p => p.LessonId == id))
                return Conflict("Only unused draft lessons can be deleted. Archive it instead.");
            db.Lessons.Remove(lesson);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ------------------------------------------------------------ quizzes
        g.MapGet("/quizzes", async (AppDbContext db) =>
            await db.Quizzes.AsNoTracking().OrderBy(q => q.Course!.Title).ThenBy(q => q.CreatedAt)
                .Select(q => new
                {
                    q.Id, q.Title, CourseId = q.CourseId, CourseTitle = q.Course!.Title, Status = Label(q.Status),
                    Questions = q.Questions.Count,
                    PendingReview = q.Questions.Count(x => x.ReviewStatus == QuestionReviewStatus.Draft),
                })
                .ToListAsync());

        g.MapPost("/courses/{id:guid}/quizzes", async (Guid id, QuizRequest req, AppDbContext db) =>
        {
            if (!await db.Courses.AnyAsync(c => c.Id == id)) return Results.NotFound();
            var quiz = new Quiz { CourseId = id, Title = req.Title };
            if (await ApplyQuiz(db, quiz, req) is { } bad) return bad;
            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            return Results.Ok(new { quiz.Id });
        });

        g.MapGet("/quizzes/{id:guid}", async (Guid id, AppDbContext db) =>
            await QuizEditorAsync(db, id) is { } dto ? Results.Ok(dto) : Results.NotFound());

        g.MapPut("/quizzes/{id:guid}", async (Guid id, QuizRequest req, AppDbContext db) =>
        {
            var quiz = await db.Quizzes.FindAsync(id);
            if (quiz is null) return Results.NotFound();
            if (await ApplyQuiz(db, quiz, req) is { } bad) return bad;
            await db.SaveChangesAsync();
            return Results.Ok(await QuizEditorAsync(db, id));
        });

        g.MapDelete("/quizzes/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var quiz = await db.Quizzes.FindAsync(id);
            if (quiz is null) return Results.NotFound();
            if (await db.QuizAttempts.AnyAsync(a => a.QuizId == id))
                return Conflict("Learners have attempted this quiz. Archive it instead.");
            db.Quizzes.Remove(quiz);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // A question written by a person is approved on save; AI drafts arrive through the generator as drafts.
        g.MapPost("/quizzes/{id:guid}/questions", async (Guid id, QuestionRequest req, AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            if (!await db.Quizzes.AnyAsync(q => q.Id == id)) return Results.NotFound();
            if (InvalidQuestion(req) is { } bad) return bad;
            var position = await db.Questions.Where(q => q.QuizId == id).MaxAsync(q => (int?)q.Position) ?? -1;
            var question = new Question
            {
                QuizId = id, Prompt = req.Prompt.Trim(), Position = position + 1,
                ReviewStatus = QuestionReviewStatus.Approved, ReviewedBy = user.Id, ReviewedAt = clock.GetUtcNow(),
            };
            ApplyQuestion(question, req);
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            return Results.Ok(await QuizEditorAsync(db, id));
        });

        g.MapPut("/questions/{id:guid}", async (Guid id, QuestionRequest req, AppDbContext db) =>
        {
            if (InvalidQuestion(req) is { } bad) return bad;
            var question = await db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == id);
            if (question is null) return Results.NotFound();
            // Changing what counts as correct would silently re-mean past attempts.
            if (await db.QuizAnswers.AnyAsync(a => a.QuestionId == id) && OptionsChanged(question, req))
                return Conflict("Learners have answered this question, so its options can't change. Reject it and add a new one.");
            question.Prompt = req.Prompt.Trim();
            ApplyQuestion(question, req);
            await db.SaveChangesAsync();
            return Results.Ok(await QuizEditorAsync(db, question.QuizId));
        });

        g.MapPost("/questions/{id:guid}/review", async (Guid id, ReviewRequest req, AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            var question = await db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == id);
            if (question is null) return Results.NotFound();
            if (req.Approve && InvalidQuestion(new(question.Prompt, question.Explanation, question.Marks,
                    question.Options.Select(o => new OptionDto(o.Id, o.Label, o.IsCorrect)).ToList())) is { } bad)
                return bad;
            question.ReviewStatus = req.Approve ? QuestionReviewStatus.Approved : QuestionReviewStatus.Rejected;
            question.ReviewedBy = user.Id;
            question.ReviewedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
            return Results.Ok(await QuizEditorAsync(db, question.QuizId));
        });

        g.MapGet("/ai/status", (Services.IQuestionGenerator generator) => new AiStatus(generator.IsConfigured));

        // AI drafts land as unapproved questions; learners never see them until a person approves each one.
        g.MapPost("/quizzes/{id:guid}/ai-drafts", async (Guid id, DraftRequest req, AppDbContext db,
            Services.IQuestionGenerator generator, Services.QuestionDraftService drafts, CancellationToken ct) =>
        {
            if (!generator.IsConfigured) return Results.Problem("AI drafting is not configured on the server.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (req.Count is < 1 or > 10) return Bad("Ask for between 1 and 10 questions.");
            var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);
            if (quiz is null) return Results.NotFound();
            if (req.LessonId is { } lid && !await db.Lessons.AnyAsync(l => l.Id == lid && l.CourseId == quiz.CourseId, ct))
                return Bad("That lesson belongs to another course.");
            try
            {
                await drafts.DraftAsync(id, req.LessonId, req.Count, ct);
            }
            catch (Services.AiDraftException e) { return Bad(e.Message); }
            catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
            {
                return Results.Problem("The server's AI key was rejected. Check Ai:AnthropicApiKey.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Anthropic.Exceptions.AnthropicRateLimitException)
            {
                return Results.Problem("The AI service is busy. Try again in a minute.", statusCode: StatusCodes.Status429TooManyRequests);
            }
            catch (Anthropic.Exceptions.AnthropicException)
            {
                return Results.Problem("The AI service didn't return usable questions. Try again shortly.", statusCode: StatusCodes.Status502BadGateway);
            }
            return Results.Ok(await QuizEditorAsync(db, id));
        });

        g.MapDelete("/questions/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var question = await db.Questions.FindAsync(id);
            if (question is null) return Results.NotFound();
            if (await db.QuizAnswers.AnyAsync(a => a.QuestionId == id))
                return Conflict("Learners have answered this question. Reject it instead.");
            db.Questions.Remove(question);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static string Label<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static bool TryParse<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);

    private static IResult Bad(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
    private static IResult Conflict(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);

    private static IResult? Invalid(string slug, string title) =>
        string.IsNullOrWhiteSpace(title) ? Bad("Title is required.")
        : slug is null || slug.Length > 100 || !SlugPattern().IsMatch(slug) ? Bad("Slug must be lowercase letters, numbers and single hyphens.")
        : null;

    private static IResult? BadStatus(string value, out PublishStatus status) =>
        TryParse(value, out status) ? null : Bad("Status must be draft, published or archived.");

    private static IResult? InvalidCourse(CourseRequest req, out PublishStatus status)
    {
        status = default;
        return Invalid(req.Slug, req.Title)
            ?? BadStatus(req.Status, out status)
            ?? (SafeUrl(req.CoverUrl) ? null : Bad("Cover image must be an https URL."));
    }

    private static IResult? InvalidQuestion(QuestionRequest req) =>
        string.IsNullOrWhiteSpace(req.Prompt) ? Bad("Question text is required.")
        : req.Marks <= 0 ? Bad("Marks must be more than zero.")
        : req.Options is null || req.Options.Count(o => !string.IsNullOrWhiteSpace(o.Label)) < 2 ? Bad("Add at least two options.")
        : !req.Options.Any(o => o.IsCorrect && !string.IsNullOrWhiteSpace(o.Label)) ? Bad("Mark at least one option as correct.")
        : null;

    /// <summary>Unsafe schemes are refused so stored links can't run script when rendered.</summary>
    private static bool SafeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) || (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps);

    private static void Apply(Course c, CourseRequest req, PublishStatus status)
    {
        c.Summary = req.Summary;
        c.Description = req.Description;
        c.CoverUrl = NullIfBlank(req.CoverUrl);
        c.CategoryId = req.CategoryId;
        c.IsFree = req.IsFree;
        c.Position = req.Position;
        c.Status = status;
        if (status == PublishStatus.Published) c.PublishedAt ??= DateTimeOffset.UtcNow;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<IResult?> ApplyLesson(AppDbContext db, Lesson lesson, LessonRequest req)
    {
        if (Invalid(req.Slug, req.Title) is { } bad) return bad;
        if (BadStatus(req.Status, out var status) is { } badStatus) return badStatus;
        if (!TryParse<LessonKind>(req.Kind, out var kind)) return Bad("Unknown lesson type.");
        if (req.DurationSeconds < 0) return Bad("Duration can't be negative.");
        if (!SafeUrl(req.VideoRef)) return Bad("Video link must be an https URL.");
        if (req.ModuleId is { } mid && !await db.Modules.AnyAsync(m => m.Id == mid && m.CourseId == lesson.CourseId))
            return Bad("That module belongs to another course.");

        (lesson.Slug, lesson.Title, lesson.Summary, lesson.Kind, lesson.DurationSeconds, lesson.IsPreview, lesson.Status, lesson.ModuleId) =
            (req.Slug, req.Title.Trim(), req.Summary, kind, req.DurationSeconds, req.IsPreview, status, req.ModuleId);
        lesson.Content ??= new LessonContent { LessonId = lesson.Id };
        lesson.Content.Body = req.Body;
        lesson.Content.VideoRef = NullIfBlank(req.VideoRef);
        lesson.Content.UpdatedAt = DateTimeOffset.UtcNow;
        return null;
    }

    private static async Task<IResult?> ApplyQuiz(AppDbContext db, Quiz quiz, QuizRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return Bad("Title is required.");
        if (BadStatus(req.Status, out var status) is { } badStatus) return badStatus;
        if (req.PassScorePct is < 0 or > 100) return Bad("Pass mark must be between 0 and 100.");
        if (req.TimeLimitSeconds is <= 0 || req.MaxAttempts is <= 0) return Bad("Time limit and attempts must be positive when set.");
        if (req.LessonId is { } lid && !await db.Lessons.AnyAsync(l => l.Id == lid && l.CourseId == quiz.CourseId))
            return Bad("That lesson belongs to another course.");
        (quiz.Title, quiz.Description, quiz.LessonId, quiz.PassScorePct, quiz.TimeLimitSeconds, quiz.MaxAttempts, quiz.IsPreview, quiz.Status) =
            (req.Title.Trim(), req.Description, req.LessonId, (short)req.PassScorePct, req.TimeLimitSeconds, req.MaxAttempts, req.IsPreview, status);
        return null;
    }

    private static void ApplyQuestion(Question q, QuestionRequest req)
    {
        q.Explanation = NullIfBlank(req.Explanation);
        q.Marks = req.Marks;
        var wanted = req.Options.Where(o => !string.IsNullOrWhiteSpace(o.Label)).ToList();
        q.Options.RemoveAll(o => wanted.All(w => w.Id != o.Id));
        for (var i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            var option = q.Options.FirstOrDefault(o => o.Id == w.Id);
            if (option is null) q.Options.Add(option = new QuestionOption { QuestionId = q.Id, Label = w.Label.Trim() });
            (option.Label, option.IsCorrect, option.Position) = (w.Label.Trim(), w.IsCorrect, i);
        }
    }

    private static bool OptionsChanged(Question q, QuestionRequest req)
    {
        var wanted = req.Options.Where(o => !string.IsNullOrWhiteSpace(o.Label)).ToList();
        return wanted.Count != q.Options.Count
            || wanted.Any(w => q.Options.FirstOrDefault(o => o.Id == w.Id) is not { } o || o.IsCorrect != w.IsCorrect);
    }

    private static async Task<IResult> Save(AppDbContext db, Func<IResult> ok) => await Save(db, () => Task.FromResult(ok()));

    private static async Task<IResult> Save(AppDbContext db, Func<Task<IResult>> ok)
    {
        try
        {
            await db.SaveChangesAsync();
            return await ok();
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Conflict("That slug is already in use.");
        }
    }

    private static LessonEditor LessonEditorDto(Lesson l) => new(l.Id, l.CourseId, l.ModuleId, l.Slug, l.Title, l.Summary, Label(l.Kind),
        l.DurationSeconds, l.IsPreview, Label(l.Status), l.Position, l.Content?.Body, l.Content?.VideoRef);

    private static async Task<CourseEditor?> CourseEditorAsync(AppDbContext db, Guid id)
    {
        var c = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;
        var modules = await db.Modules.AsNoTracking().Where(m => m.CourseId == id).OrderBy(m => m.Position)
            .Select(m => new ModuleDto(m.Id, m.Title, m.Position)).ToListAsync();
        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == id).OrderBy(l => l.Position).ToListAsync();
        var quizzes = await db.Quizzes.AsNoTracking().Where(q => q.CourseId == id).OrderBy(q => q.CreatedAt)
            .Select(q => new QuizRow(q.Id, q.Title, q.LessonId, Label(q.Status), q.IsPreview, q.Questions.Count,
                q.Questions.Count(x => x.ReviewStatus == QuestionReviewStatus.Draft)))
            .ToListAsync();
        return new CourseEditor(c.Id, c.Slug, c.Title, c.Summary, c.Description, c.CoverUrl, c.CategoryId, Label(c.Status), c.IsFree, c.Position,
            modules,
            lessons.Select(l => new LessonRow(l.Id, l.ModuleId, l.Slug, l.Title, Label(l.Kind), l.DurationSeconds, l.IsPreview, Label(l.Status), l.Position)).ToList(),
            quizzes);
    }

    private static async Task<QuizEditor?> QuizEditorAsync(AppDbContext db, Guid id)
    {
        var q = await db.Quizzes.AsNoTracking().Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == id);
        if (q is null) return null;
        var questions = await db.Questions.AsNoTracking().Include(x => x.Options).Where(x => x.QuizId == id)
            .OrderBy(x => x.Position).ToListAsync();
        return new QuizEditor(q.Id, q.CourseId, q.Course!.Title, q.Title, q.Description, q.LessonId, q.PassScorePct, q.TimeLimitSeconds,
            q.MaxAttempts, q.IsPreview, Label(q.Status), await db.QuizAttempts.CountAsync(a => a.QuizId == id),
            questions.Select(x => new QuestionDto(x.Id, x.Prompt, x.Explanation, x.Marks, x.Position, x.AiGenerated, Label(x.ReviewStatus),
                x.ReviewedAt, x.Options.OrderBy(o => o.Position).Select(o => new OptionDto(o.Id, o.Label, o.IsCorrect)).ToList())).ToList());
    }
}
