using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Services;

namespace MsrEdu.Api.Endpoints;

public static class CatalogEndpoints
{
    public record CourseSummary(Guid Id, string Slug, string Title, string? Summary, string? CoverUrl, bool IsFree, string? Category);
    public record LessonOutline(Guid Id, string Slug, string Title, string Kind, int? DurationSeconds, bool IsPreview, bool Locked);
    public record ModuleOutline(Guid? Id, string Title, IReadOnlyList<LessonOutline> Lessons);
    public record CourseDetail(CourseSummary Course, string? Description, bool HasAccess, IReadOnlyList<ModuleOutline> Modules);
    public record LessonContentResponse(Guid LessonId, string Title, string? Body, string? VideoRef);

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api");

        g.MapGet("/settings/public", async (AppDbContext db) =>
            await db.AppSettings.AsNoTracking().Where(s => s.IsPublic)
                .ToDictionaryAsync(s => s.Key, s => s.Value.RootElement));

        g.MapGet("/courses", async (AppDbContext db) =>
            await db.Courses.AsNoTracking()
                .Where(c => c.Status == PublishStatus.Published)
                .OrderBy(c => c.Position).ThenBy(c => c.Title)
                .Select(c => new CourseSummary(c.Id, c.Slug, c.Title, c.Summary, c.CoverUrl, c.IsFree, c.Category!.Name))
                .ToListAsync());

        // Syllabus is public for published courses; each lesson says whether the viewer can open it.
        g.MapGet("/courses/{slug}", async (string slug, AppDbContext db, ICurrentUser user, AccessService access) =>
        {
            var course = await db.Courses.AsNoTracking().Include(c => c.Category)
                .FirstOrDefaultAsync(c => c.Slug == slug && (c.Status == PublishStatus.Published || user.IsStaff));
            if (course is null) return Results.NotFound();

            var hasAccess = user.IsStaff
                || (user.IsAuthenticated && course.IsFree)
                || await access.HasCourseAccessAsync(user.Id, course.Id);
            var modules = await db.Modules.AsNoTracking().Where(m => m.CourseId == course.Id).OrderBy(m => m.Position).ToListAsync();
            var lessons = await db.Lessons.AsNoTracking()
                .Where(l => l.CourseId == course.Id && (l.Status == PublishStatus.Published || user.IsStaff))
                .OrderBy(l => l.Position).ToListAsync();

            LessonOutline Outline(Data.Entities.Lesson l) => new(l.Id, l.Slug, l.Title,
                l.Kind.ToString().ToLowerInvariant(), l.DurationSeconds, l.IsPreview, Locked: !(hasAccess || l.IsPreview));

            var outline = modules
                .Select(m => new ModuleOutline(m.Id, m.Title, lessons.Where(l => l.ModuleId == m.Id).Select(Outline).ToList()))
                .ToList();
            var loose = lessons.Where(l => l.ModuleId is null || modules.All(m => m.Id != l.ModuleId)).Select(Outline).ToList();
            if (loose.Count > 0) outline.Insert(0, new ModuleOutline(null, "Lessons", loose));

            return Results.Ok(new CourseDetail(
                new CourseSummary(course.Id, course.Slug, course.Title, course.Summary, course.CoverUrl, course.IsFree, course.Category?.Name),
                course.Description, hasAccess, outline));
        });

        // Protected lesson body. 404 (not 403) for hidden items so drafts are not revealed.
        g.MapGet("/lessons/{id:guid}/content", async (Guid id, AppDbContext db, ICurrentUser user, AccessService access) =>
        {
            var lesson = await db.Lessons.AsNoTracking().Include(l => l.Content).FirstOrDefaultAsync(l => l.Id == id);
            if (lesson is null) return Results.NotFound();
            if (!await access.CanViewLessonAsync(user.Id, user.IsStaff, id))
                return user.IsAuthenticated ? Results.StatusCode(StatusCodes.Status402PaymentRequired) : Results.Unauthorized();
            return Results.Ok(new LessonContentResponse(lesson.Id, lesson.Title, lesson.Content?.Body, lesson.Content?.VideoRef));
        });
    }
}
