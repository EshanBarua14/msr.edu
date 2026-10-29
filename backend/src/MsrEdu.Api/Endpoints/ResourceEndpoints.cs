using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Services;

namespace MsrEdu.Api.Endpoints;

/// <summary>
/// Course resources synced from Google Drive. Learners download through the API, which checks access on every
/// request; Drive file ids and links are never sent to the browser.
/// </summary>
public static partial class ResourceEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9_-]{10,100}$")]
    private static partial Regex DriveIdPattern();

    [GeneratedRegex(@"/folders/([A-Za-z0-9_-]+)|[?&]id=([A-Za-z0-9_-]+)")]
    private static partial Regex DriveFolderUrl();

    public record DriveStatus(bool Configured, string? AccountEmail);
    public record SyncRequest(string Folder);
    public record AdminResource(Guid Id, string Name, string? MimeType, long? SizeBytes, Guid? LessonId, bool IsPreview,
        string Status, DateTimeOffset? SyncedAt, DateTimeOffset? DriveModifiedAt);
    public record CourseResources(string? DriveFolderId, DateTimeOffset? DriveSyncedAt, IReadOnlyList<AdminResource> Resources);
    public record ResourceUpdate(string Name, Guid? LessonId, bool IsPreview, string Status);
    public record LearnerResource(Guid Id, string Name, string? MimeType, long? SizeBytes);

    /// <summary>Accepts a bare folder id or a Drive folder link.</summary>
    public static string? ParseFolderId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        if (DriveFolderUrl().Match(input) is { Success: true } m) input = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return DriveIdPattern().IsMatch(input) ? input : null;
    }

    public static void MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Content);

        admin.MapGet("/drive/status", (IDriveClient drive) => new DriveStatus(drive.IsConfigured, drive.AccountEmail));

        admin.MapGet("/courses/{id:guid}/resources", async (Guid id, AppDbContext db) =>
        {
            var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (course is null) return Results.NotFound();
            var resources = await db.Resources.AsNoTracking().Include(r => r.Source).Where(r => r.CourseId == id)
                .OrderBy(r => r.Name).ToListAsync();
            return Results.Ok(new CourseResources(course.DriveFolderId, course.DriveSyncedAt, resources.Select(ToAdmin).ToList()));
        });

        admin.MapPost("/courses/{id:guid}/drive/sync", async (Guid id, SyncRequest req, AppDbContext db, IDriveClient drive, DriveSyncService sync) =>
        {
            if (!drive.IsConfigured) return Results.Problem("Google Drive is not configured on the server.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (ParseFolderId(req.Folder) is not { } folderId) return Results.Problem("Paste a Google Drive folder link or id.", statusCode: StatusCodes.Status400BadRequest);
            if (!await db.Courses.AnyAsync(c => c.Id == id)) return Results.NotFound();
            try
            {
                return Results.Ok(await sync.SyncCourseAsync(id, folderId));
            }
            catch (Google.GoogleApiException e) when (e.HttpStatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
            {
                return Results.Problem($"Drive could not open that folder. Share it with {drive.AccountEmail} as a Viewer.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        admin.MapPut("/resources/{id:guid}", async (Guid id, ResourceUpdate req, AppDbContext db) =>
        {
            var r = await db.Resources.Include(x => x.Source).FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);
            if (!Enum.TryParse<PublishStatus>(req.Status, true, out var status) || !Enum.IsDefined(status))
                return Results.Problem("Status must be draft, published or archived.", statusCode: StatusCodes.Status400BadRequest);
            if (req.LessonId is { } lid && !await db.Lessons.AnyAsync(l => l.Id == lid && l.CourseId == r.CourseId))
                return Results.Problem("That lesson belongs to another course.", statusCode: StatusCodes.Status400BadRequest);
            (r.Name, r.LessonId, r.IsPreview, r.Status) = (req.Name.Trim(), req.LessonId, req.IsPreview, status);
            await db.SaveChangesAsync();
            return Results.Ok(ToAdmin(r));
        });

        // ------------------------------------------------------------ learners
        app.MapGet("/api/lessons/{id:guid}/resources", async (Guid id, AppDbContext db, ICurrentUser user, AccessService access) =>
        {
            var courseId = await db.Lessons.Where(l => l.Id == id).Select(l => (Guid?)l.CourseId).FirstOrDefaultAsync();
            if (courseId is null) return Results.NotFound();
            var candidates = await db.Resources.AsNoTracking()
                .Where(r => r.LessonId == id && (r.Status == PublishStatus.Published || user.IsStaff))
                .OrderBy(r => r.Name).ToListAsync();
            var visible = new List<LearnerResource>();
            foreach (var r in candidates)
                if (await access.CanViewResourceAsync(user.Id, user.IsStaff, r.Id))
                    visible.Add(new LearnerResource(r.Id, r.Name, r.MimeType, r.SizeBytes));
            return Results.Ok(visible);
        });

        app.MapGet("/api/resources/{id:guid}/download", async (Guid id, HttpContext http, AppDbContext db, ICurrentUser user, AccessService access, IDriveClient drive) =>
        {
            var r = await db.Resources.AsNoTracking().Include(x => x.Source).FirstOrDefaultAsync(x => x.Id == id);
            if (r?.Source is null) return Results.NotFound();
            if (!await access.CanViewResourceAsync(user.Id, user.IsStaff, id))
                return r.Status != PublishStatus.Published ? Results.NotFound()
                    : user.IsAuthenticated ? Results.StatusCode(StatusCodes.Status402PaymentRequired) : Results.Unauthorized();
            if (!drive.IsConfigured) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var mime = r.MimeType ?? "application/octet-stream";
            var fileName = r.MimeType == "application/pdf" && !r.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? r.Name + ".pdf" : r.Name;
            http.Response.ContentType = mime;
            http.Response.Headers[HeaderNames.ContentDisposition] =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName }.ToString();
            http.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await drive.DownloadAsync(r.Source.DriveFileId, r.Source.DriveMimeType ?? mime, http.Response.Body, http.RequestAborted);
            return Results.Empty;
        });
    }

    private static AdminResource ToAdmin(Data.Entities.Resource r) => new(r.Id, r.Name, r.MimeType, r.SizeBytes, r.LessonId, r.IsPreview,
        r.Status.ToString().ToLowerInvariant(), r.SyncedAt, r.Source?.DriveModifiedAt);
}
