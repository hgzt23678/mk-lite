using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class DriveCompatibilityTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task DriveLifecycleUploadsListsUpdatesDeletesFilesAndFolders()
    {
        using HttpClient anonymous = CreateApiClient();
        using HttpClient user = AuthorizedClient("fixture-alice");

        using HttpResponseMessage anonymousList = await anonymous.PostAsJsonAsync("/api/drive/files", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        using HttpResponseMessage folder = await user.PostAsJsonAsync(
            "/api/drive/folders/create",
            new { name = "parity-folder" });
        Assert.True(
            folder.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)folder.StatusCode}: " + await folder.Content.ReadAsStringAsync());
        using JsonDocument folderDocument = JsonDocument.Parse(await folder.Content.ReadAsStringAsync());
        string folderId = folderDocument.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("parity-folder", folderDocument.RootElement.GetProperty("name").GetString());

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(folderId), "folderId");
        multipart.Add(new StringContent("false"), "isSensitive");
        multipart.Add(new StringContent("picture.png"), "name");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fixture-image-bytes"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(fileContent, "file", "picture.png");
        using HttpResponseMessage created = await user.PostAsync("/api/drive/files/create", multipart);
        Assert.True(
            created.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)created.StatusCode}: " + await created.Content.ReadAsStringAsync());
        using JsonDocument createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        string fileId = createdDocument.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("picture.png", createdDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal("image/png", createdDocument.RootElement.GetProperty("type").GetString());
        Assert.Equal(19, createdDocument.RootElement.GetProperty("size").GetInt64());
        Assert.Equal(folderId, createdDocument.RootElement.GetProperty("folderId").GetString());
        Assert.StartsWith("/media/", createdDocument.RootElement.GetProperty("url").GetString());

        using HttpResponseMessage listed = await user.PostAsJsonAsync(
            "/api/drive/files",
            new { folderId, limit = 10 });
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        string listedBody = await listed.Content.ReadAsStringAsync();
        using JsonDocument listedDocument = JsonDocument.Parse(listedBody);
        Assert.True(
            listedDocument.RootElement.GetArrayLength() == 1,
            $"Expected one drive file but got {listedDocument.RootElement.GetArrayLength()}: {listedBody}");

        using HttpResponseMessage usage = await user.PostAsJsonAsync("/api/drive", new { });
        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        using JsonDocument usageDocument = JsonDocument.Parse(await usage.Content.ReadAsStringAsync());
        Assert.True(usageDocument.RootElement.GetProperty("usage").GetInt64() >= 20);
        Assert.True(usageDocument.RootElement.GetProperty("capacity").GetInt64() > 0);

        using HttpResponseMessage updated = await user.PostAsJsonAsync(
            "/api/drive/files/update",
            new { fileId, name = "renamed.png" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using JsonDocument updatedDocument = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal("renamed.png", updatedDocument.RootElement.GetProperty("name").GetString());

        using HttpResponseMessage deleted = await user.PostAsJsonAsync(
            "/api/drive/files/delete",
            new { fileId });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using HttpResponseMessage missing = await user.PostAsJsonAsync(
            "/api/drive/files/delete",
            new { fileId });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using HttpResponseMessage folderDeleted = await user.PostAsJsonAsync(
            "/api/drive/folders/delete",
            new { folderId });
        Assert.Equal(HttpStatusCode.OK, folderDeleted.StatusCode);
    }

    private HttpClient CreateApiClient() => fixture.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://local.example", UriKind.Absolute)
    });

    private HttpClient AuthorizedClient(string principal)
    {
        HttpClient client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://local.example", UriKind.Absolute)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", principal);
        return client;
    }
}
