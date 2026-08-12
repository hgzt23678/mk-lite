using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FollowPageTests : BunitContext
{
    [Fact]
    public void ReadsQueryTargetAndShowsTheV12ConfirmationDialog()
    {
        var users = new RecordingUsers();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IUserPreviewPresentationService>(users);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new FollowLocalizer());

        using IRenderedComponent<FollowPage> page = Render<FollowPage>(parameters => parameters
            .Add(component => component.Acct, "alice-id"));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("alice-id", users.LastQuery);
            Assert.Equal("confirming", page.Find(".mk-follow-page").GetAttribute("data-follow-state"));
            MisskeyOverlayEntry dialog = Assert.Single(overlays.Entries);
            Assert.Equal(MisskeyOverlayKind.Dialog, dialog.Kind);
            Assert.Equal("Follow Alice?", dialog.Dialog!.Text);
        });
    }

    [Fact]
    public async Task ConfirmationInvokesTheDurableFollowCommandOnce()
    {
        var users = new RecordingUsers();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IUserPreviewPresentationService>(users);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new FollowLocalizer());

        using IRenderedComponent<FollowPage> page = Render<FollowPage>(parameters => parameters
            .Add(component => component.Acct, "alice-id"));
        page.WaitForAssertion(() => Assert.Single(overlays.Entries));

        MisskeyOverlayEntry dialog = Assert.Single(overlays.Entries);
        await dialog.Dialog!.Done!(new MisskeyDialogResult(Canceled: false));

        Assert.Equal(1, users.FollowCalls);
        page.WaitForAssertion(() => Assert.Equal("completed", page.Find(".mk-follow-page").GetAttribute("data-follow-state")));
    }

    private sealed class RecordingUsers : IUserPreviewPresentationService
    {
        public string? LastQuery { get; private set; }
        public int FollowCalls { get; private set; }

        public Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(CreateUser());
        }

        public Task<UserPreviewViewModel> FollowAsync(
            UserPreviewViewModel user,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            FollowCalls++;
            return Task.FromResult(user with { IsFollowing = true });
        }

        public Task<UserPreviewViewModel> UnfollowAsync(
            UserPreviewViewModel user,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(user);

        private static UserPreviewViewModel CreateUser() => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "alice-id",
            new NoteAuthorViewModel("alice-id", "alice", "alice@example.test", "Alice", string.Empty, false),
            "Description",
            null,
            1,
            2,
            3,
            false,
            true,
            false,
            false,
            false);
    }

    private sealed class FollowLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public bool TrySelectLocale(string? locale) => true;
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "confirm" => "Confirm",
            "followConfirm" => "Follow Alice?",
            "followed" => "Followed",
            "error" => "Error",
            "success" => "Success",
            "ok" => "OK",
            "somethingHappened" => "Something happened",
            _ => key
        };
    }
}
