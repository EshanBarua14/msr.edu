using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Endpoints;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.ResourceEndpoints;

namespace MsrEdu.Api.Tests;

/// <summary>In-memory stand-in for Google Drive: folder id -> files, file id -> content.</summary>
public sealed class FakeDrive : IDriveClient
{
    public Dictionary<string, List<DriveFile>> Folders { get; } = [];
    public bool IsConfigured => true;
    public string? AccountEmail => "sync@test.iam.gserviceaccount.com";

    public Task<IReadOnlyList<DriveFile>> ListFolderAsync(string folderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DriveFile>>(Folders.GetValueOrDefault(folderId) ?? []);

    public Task DownloadAsync(string fileId, string mimeType, Stream target, CancellationToken ct = default) =>
        target.WriteAsync(Encoding.UTF8.GetBytes($"content of {fileId} as {mimeType}"), ct).AsTask();
}

public sealed class DriveApiFactory : ApiFactory
{
    public FakeDrive Drive { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IDriveClient>(Drive));
    }
}

public class DriveTests(DriveApiFactory factory) : IClassFixture<DriveApiFactory>
{
    private const string Folder = "folder_ABCDEFGHIJ";
    private static string Email() => $"{Guid.NewGuid():N}@x.test";

    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1AbC_def-GHIjkl?usp=sharing", "1AbC_def-GHIjkl")]
    [InlineData("https://drive.google.com/open?id=1AbC_def-GHIjkl", "1AbC_def-GHIjkl")]
    [InlineData("1AbC_def-GHIjkl", "1AbC_def-GHIjkl")]
    [InlineData("x' or 1=1", null)]
    [InlineData("", null)]
    public void Folder_links_and_ids_are_parsed_strictly(string input, string? expected) =>
        Assert.Equal(expected, ParseFolderId(input));

    [Fact]
    public async Task Sync_creates_drafts_refreshes_and_archives_and_downloads_check_access()
    {
        Course course;
        Lesson preview, paid;
        await using (var scope = factory.Scope())
            (course, preview, paid) = await new Seed(scope.ServiceProvider.GetRequiredService<AppDbContext>()).CourseAsync($"d-{Guid.NewGuid():N}");

        factory.Drive.Folders[Folder] =
        [
            new("f1", "Notes.pdf", "application/pdf", 100, DateTimeOffset.UtcNow, "m1"),
            new("f2", "Slides", "application/vnd.google-apps.presentation", null, DateTimeOffset.UtcNow, null),
            new("f3", "Sub folder", "application/vnd.google-apps.folder", null, null, null),
        ];
        var editor = await factory.SignedIn(Email(), RoleKeys.ContentManager);
        var res = await editor.PostAsJsonAsync($"/api/admin/courses/{course.Id}/drive/sync", new SyncRequest($"https://drive.google.com/drive/folders/{Folder}"));
        Assert.Equal(new DriveSyncService.Result(2, 0, 0, 1), await res.Content.ReadFromJsonAsync<DriveSyncService.Result>());

        var list = await editor.GetFromJsonAsync<CourseResources>($"/api/admin/courses/{course.Id}/resources");
        Assert.Equal(Folder, list!.DriveFolderId);
        Assert.All(list.Resources, r => Assert.Equal("draft", r.Status));
        var notes = list.Resources.Single(r => r.Name == "Notes.pdf");
        Assert.Equal("application/pdf", list.Resources.Single(r => r.Name == "Slides").MimeType);

        // Drafts are invisible; publishing on a paid lesson needs access, on a preview lesson it doesn't.
        var learner = await factory.SignedIn(Email());
        Assert.Equal(HttpStatusCode.NotFound, (await learner.GetAsync($"/api/resources/{notes.Id}/download")).StatusCode);
        await editor.PutAsJsonAsync($"/api/admin/resources/{notes.Id}", new ResourceUpdate("Notes.pdf", paid.Id, false, "published"));
        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/resources/{notes.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().GetAsync($"/api/resources/{notes.Id}/download")).StatusCode);
        Assert.Empty((await learner.GetFromJsonAsync<List<LearnerResource>>($"/api/lessons/{paid.Id}/resources"))!);

        await editor.PutAsJsonAsync($"/api/admin/resources/{notes.Id}", new ResourceUpdate("Notes.pdf", preview.Id, false, "published"));
        var download = await factory.Client().GetAsync($"/api/resources/{notes.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("content of f1 as application/pdf", await download.Content.ReadAsStringAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        var lessonFiles = await learner.GetStringAsync($"/api/lessons/{preview.Id}/resources");
        Assert.Contains("Notes.pdf", lessonFiles);
        Assert.DoesNotContain("f1", lessonFiles); // Drive ids never reach learners

        // Rename in Drive refreshes; removal archives (never deletes).
        factory.Drive.Folders[Folder] = [new("f1", "Notes v2.pdf", "application/pdf", 120, DateTimeOffset.UtcNow.AddMinutes(1), "m2")];
        res = await editor.PostAsJsonAsync($"/api/admin/courses/{course.Id}/drive/sync", new SyncRequest(Folder));
        Assert.Equal(new DriveSyncService.Result(0, 1, 1, 0), await res.Content.ReadFromJsonAsync<DriveSyncService.Result>());
        list = await editor.GetFromJsonAsync<CourseResources>($"/api/admin/courses/{course.Id}/resources");
        Assert.Equal("published", list!.Resources.Single(r => r.Id == notes.Id).Status);
        Assert.Equal("archived", list.Resources.Single(r => r.Name == "Slides").Status);
    }

    [Fact]
    public async Task Sync_needs_content_role_and_a_valid_folder()
    {
        var learner = await factory.SignedIn(Email());
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/admin/drive/status")).StatusCode);
        var editor = await factory.SignedIn(Email(), RoleKeys.ContentManager);
        var status = await editor.GetFromJsonAsync<DriveStatus>("/api/admin/drive/status");
        Assert.True(status!.Configured);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await editor.PostAsJsonAsync($"/api/admin/courses/{Guid.NewGuid()}/drive/sync", new SyncRequest("not a folder!"))).StatusCode);
    }
}
