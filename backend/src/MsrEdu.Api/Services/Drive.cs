using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Services;

/// <summary>
/// Google Drive access through a service account. Share each course folder with the account's email (Viewer).
/// The key is a secret: set GoogleDrive:ServiceAccountJson in user secrets or an environment variable.
/// </summary>
public class DriveOptions
{
    public string? ServiceAccountJson { get; set; }
}

public record DriveFile(string Id, string Name, string MimeType, long? Size, DateTimeOffset? ModifiedAt, string? Md5);

public interface IDriveClient
{
    bool IsConfigured { get; }
    string? AccountEmail { get; }
    Task<IReadOnlyList<DriveFile>> ListFolderAsync(string folderId, CancellationToken ct = default);
    /// <summary>Writes the file to <paramref name="target"/>; Google Docs/Sheets/Slides are exported as PDF.</summary>
    Task DownloadAsync(string fileId, string mimeType, Stream target, CancellationToken ct = default);
}

public sealed class GoogleDriveClient : IDriveClient, IDisposable
{
    public const string GoogleAppsPrefix = "application/vnd.google-apps.";
    private static readonly HashSet<string> Exportable =
        [GoogleAppsPrefix + "document", GoogleAppsPrefix + "spreadsheet", GoogleAppsPrefix + "presentation"];

    private readonly DriveService? _service;

    public GoogleDriveClient(IOptions<DriveOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ServiceAccountJson)) return;
        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(options.Value.ServiceAccountJson);
        AccountEmail = credential.Id;
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential.ToGoogleCredential().CreateScoped(DriveService.Scope.DriveReadonly),
            ApplicationName = "MSR.edu",
        });
    }

    public bool IsConfigured => _service is not null;
    public string? AccountEmail { get; }

    /// <summary>Whether a file can be offered to learners (folders and Google forms, maps etc. can't).</summary>
    public static bool IsSupported(string mimeType) => !mimeType.StartsWith(GoogleAppsPrefix) || Exportable.Contains(mimeType);

    public static string DeliveredMimeType(string mimeType) => Exportable.Contains(mimeType) ? "application/pdf" : mimeType;

    public async Task<IReadOnlyList<DriveFile>> ListFolderAsync(string folderId, CancellationToken ct = default)
    {
        var service = _service ?? throw new InvalidOperationException("Google Drive is not configured.");
        var files = new List<DriveFile>();
        string? page = null;
        do
        {
            var req = service.Files.List();
            req.Q = $"'{folderId.Replace("'", "")}' in parents and trashed = false";
            req.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, md5Checksum)";
            req.PageSize = 200;
            req.PageToken = page;
            req.SupportsAllDrives = true;
            req.IncludeItemsFromAllDrives = true;
            var res = await req.ExecuteAsync(ct);
            files.AddRange(res.Files.Select(f => new DriveFile(f.Id, f.Name, f.MimeType, f.Size, f.ModifiedTimeDateTimeOffset, f.Md5Checksum)));
            page = res.NextPageToken;
        } while (page is not null);
        return files;
    }

    public async Task DownloadAsync(string fileId, string mimeType, Stream target, CancellationToken ct = default)
    {
        var service = _service ?? throw new InvalidOperationException("Google Drive is not configured.");
        if (Exportable.Contains(mimeType))
            await service.Files.Export(fileId, "application/pdf").DownloadAsync(target, ct);
        else
        {
            var req = service.Files.Get(fileId);
            req.SupportsAllDrives = true;
            await req.DownloadAsync(target, ct);
        }
    }

    public void Dispose() => _service?.Dispose();
}

/// <summary>
/// Sync and publish are separate: sync creates or refreshes draft resources from a Drive folder; a content
/// manager publishes them. Files removed from Drive are archived, never deleted.
/// </summary>
public class DriveSyncService(AppDbContext db, IDriveClient drive, TimeProvider clock)
{
    public record Result(int Added, int Updated, int Archived, int Skipped);

    public async Task<Result> SyncCourseAsync(Guid courseId, string folderId, CancellationToken ct = default)
    {
        var files = await drive.ListFolderAsync(folderId, ct);
        var now = clock.GetUtcNow();
        var existing = await db.Resources.Include(r => r.Source)
            .Where(r => r.CourseId == courseId && r.Source != null).ToListAsync(ct);
        var byDriveId = existing.ToDictionary(r => r.Source!.DriveFileId);

        int added = 0, updated = 0, skipped = 0;
        var seen = new HashSet<string>();
        foreach (var f in files)
        {
            if (!GoogleDriveClient.IsSupported(f.MimeType)) { skipped++; continue; }
            seen.Add(f.Id);
            if (!byDriveId.TryGetValue(f.Id, out var r))
            {
                // A file already linked to another course stays with that course.
                if (await db.ResourceSources.AnyAsync(s => s.DriveFileId == f.Id, ct)) { skipped++; continue; }
                r = new Resource { CourseId = courseId, Name = f.Name, Source = new ResourceSource { DriveFileId = f.Id } };
                db.Resources.Add(r);
                added++;
            }
            else if (r.Source!.DriveModifiedAt != f.ModifiedAt || r.Name != f.Name)
                updated++;

            r.Name = f.Name;
            r.MimeType = GoogleDriveClient.DeliveredMimeType(f.MimeType);
            r.SizeBytes = f.Size;
            r.SyncedAt = now;
            r.Source!.DriveModifiedAt = f.ModifiedAt;
            r.Source.Checksum = f.Md5;
            r.Source.DriveMimeType = f.MimeType;
        }

        var archived = 0;
        foreach (var gone in existing.Where(r => !seen.Contains(r.Source!.DriveFileId) && r.Status != PublishStatus.Archived))
        {
            gone.Status = PublishStatus.Archived;
            archived++;
        }

        var course = await db.Courses.SingleAsync(c => c.Id == courseId, ct);
        course.DriveFolderId = folderId;
        course.DriveSyncedAt = now;
        await db.SaveChangesAsync(ct);
        return new Result(added, updated, archived, skipped);
    }
}
