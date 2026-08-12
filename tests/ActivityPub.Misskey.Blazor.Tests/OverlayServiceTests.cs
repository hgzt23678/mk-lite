using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class OverlayServiceTests
{
    [Fact]
    public void PopupMenuPreservesThePinnedOptionsAndClosedCallback()
    {
        MisskeyOverlayService service = new();
        Func<Task> closed = () => Task.CompletedTask;

        Guid id = service.ShowPopupMenu(
            default,
            [new MisskeyMenuItem(MisskeyMenuItemKind.Action, "Action")],
            openedViaKeyboard: true,
            matchSourceWidth: false,
            align: "center",
            width: 288,
            closed: closed);

        MisskeyOverlayEntry entry = Assert.Single(service.Entries);
        Assert.Equal(id, entry.Id);
        Assert.True(entry.OpenedViaKeyboard);
        Assert.Equal("center", entry.PopupAlign);
        Assert.Equal(288, entry.PopupWidth);
        Assert.Same(closed, entry.PopupClosed);
    }

    [Fact]
    public async Task ContextMenuPreservesItsCoordinatesItemsAndClosedCallback()
    {
        MisskeyOverlayService service = new();
        Func<Task> closed = () => Task.CompletedTask;
        IReadOnlyList<MisskeyMenuItem> items = [new(MisskeyMenuItemKind.Action, "Edit")];

        Guid id = service.ShowContextMenu(120.5, 240.25, items, closed);

        MisskeyContextMenuEntry entry = Assert.Single(service.ContextMenus);
        Assert.Equal(id, entry.Id);
        Assert.Equal(120.5, entry.X);
        Assert.Equal(240.25, entry.Y);
        Assert.Equal(items, entry.Items);
        Assert.Same(closed, entry.Closed);

        service.Close(id);
        Assert.Empty(service.ContextMenus);
        Assert.False(await service.RequestCloseTopAsync());
    }

    [Fact]
    public void LaunchPadPreservesItsSourceItemsAndRejectsNonActionableRows()
    {
        MisskeyOverlayService service = new();
        Func<Task> closed = () => Task.CompletedTask;
        IReadOnlyList<MisskeyMenuItem> items =
        [
            MisskeyMenuItem.Link("Timeline", "fas fa-home", "/"),
            new(MisskeyMenuItemKind.Action, "Reload", "fas fa-redo-alt", Action: () => Task.CompletedTask)
        ];

        Guid id = service.ShowLaunchPad(default, items, closed);

        MisskeyOverlayEntry entry = Assert.Single(service.Entries);
        Assert.Equal(id, entry.Id);
        Assert.Equal(MisskeyOverlayKind.LaunchPad, entry.Kind);
        Assert.Equal(items, entry.LaunchPad?.Items);
        Assert.Same(closed, entry.LaunchPad?.Closed);
        Assert.Throws<ArgumentException>(() => service.ShowLaunchPad(
            default,
            [new(MisskeyMenuItemKind.Divider)]));
    }

    [Fact]
    public void PostFormPreservesTheExactInitialTextWithoutCreatingDuplicateOverlays()
    {
        MisskeyOverlayService service = new();
        var options = new MisskeyPostFormOptions("I $[jelly ❤] #Misskey", Instant: true);

        Guid first = service.ShowPostForm(options);
        Guid repeated = service.ShowPostForm();

        Assert.Equal(first, repeated);
        MisskeyOverlayEntry entry = Assert.Single(service.Entries);
        Assert.Equal(options, entry.PostForm);
    }

    [Fact]
    public async Task EscapeRequestsTheTopmostRegisteredLeaveTransition()
    {
        MisskeyOverlayService service = new();
        Guid menu = service.ShowPopupMenu(default, []);
        Guid picker = service.ShowEmojiPicker(default, _ => Task.CompletedTask, asReactionPicker: true);
        List<Guid> closing = [];
        service.RegisterCloseHandler(menu, () =>
        {
            closing.Add(menu);
            return Task.CompletedTask;
        });
        service.RegisterCloseHandler(picker, () =>
        {
            closing.Add(picker);
            return Task.CompletedTask;
        });

        bool handled = await service.RequestCloseTopAsync();

        Assert.True(handled);
        Assert.Equal([picker], closing);
        Assert.Equal(2, service.Entries.Count + service.EmojiPickers.Count);
    }

    [Fact]
    public async Task EscapeClosesAnEntryIfRenderingDisconnectsBeforeRegistration()
    {
        MisskeyOverlayService service = new();
        service.ShowVisibilityPicker(
            default,
            Visibility.Public,
            currentLocalOnly: false,
            (_, _) => Task.CompletedTask);

        bool handled = await service.RequestCloseTopAsync();

        Assert.True(handled);
        Assert.Empty(service.VisibilityPickers);
        Assert.False(await service.RequestCloseTopAsync());
    }

    [Fact]
    public async Task RegistrationSuccessAlertRemainsAboveTheClosingSignupDialog()
    {
        MisskeyOverlayService service = new();
        Guid signup = service.ShowSignUp();
        Guid alert = service.ShowAlert(new MisskeyAlertOptions(
            "success",
            "Almost there",
            "Confirmation sent to alice@example.test",
            "Almost there",
            "Got it"));

        Assert.Equal([MisskeyOverlayKind.SignUp, MisskeyOverlayKind.Alert], service.Entries.Select(entry => entry.Kind));

        service.Close(signup);
        MisskeyOverlayEntry remaining = Assert.Single(service.Entries);
        Assert.Equal(alert, remaining.Id);
        Assert.Equal("alice@example.test", remaining.Alert?.Text?.Split(' ').Last());

        Assert.True(await service.RequestCloseTopAsync());
        Assert.Empty(service.Entries);
    }

    [Fact]
    public void AlertRejectsUnknownTypesAndUnboundedText()
    {
        MisskeyOverlayService service = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ShowAlert(new MisskeyAlertOptions(
            "custom",
            null,
            null,
            "Notice",
            "OK")));
        Assert.Throws<ArgumentException>(() => service.ShowAlert(new MisskeyAlertOptions(
            "info",
            null,
            new string('a', 4_097),
            "Notice",
            "OK")));
    }

    [Fact]
    public async Task GenericDialogIsOwnedByTheOverlayStackAndUsesItsRegisteredLeaveTransition()
    {
        MisskeyOverlayService service = new();
        var options = new MisskeyDialogOptions(
            "question",
            "Visibility",
            "Choose who can see this note",
            Select: new MisskeyDialogSelect(
            [
                MkFormSelectItem.Option("public", "Public"),
                MkFormSelectItem.Option("home", "Home")
            ],
            "home"));
        Guid id = service.ShowDialog(options);
        int closeRequests = 0;
        service.RegisterCloseHandler(id, () =>
        {
            closeRequests++;
            service.Close(id);
            return Task.CompletedTask;
        });

        MisskeyOverlayEntry entry = Assert.Single(service.Entries);
        Assert.Equal(MisskeyOverlayKind.Dialog, entry.Kind);
        Assert.Same(options, entry.Dialog);

        Assert.True(await service.RequestCloseTopAsync());
        Assert.Equal(1, closeRequests);
        Assert.Empty(service.Entries);
    }

    [Fact]
    public void UserPreviewKeepsOneEntryAndRejectsStaleShowAndHideCallbacks()
    {
        MisskeyOverlayService service = new();
        Guid first = service.ShowUserPreview("host-1", "source-1", "alice-id", generation: 1);
        Guid second = service.ShowUserPreview("host-1", "source-2", "bob-id", generation: 2);

        Assert.NotEqual(first, second);
        MisskeyUserPreviewEntry current = Assert.Single(service.UserPreviews);
        Assert.Equal("bob-id", current.Query);
        Assert.Equal(2, current.Generation);

        Assert.Equal(Guid.Empty, service.ShowUserPreview("host-1", "source-1", "alice-id", generation: 1));
        Assert.False(service.HideUserPreview("host-1", "source-1", generation: 1));
        Assert.True(Assert.Single(service.UserPreviews).Showing);
        Assert.True(service.HideUserPreview("host-1", "source-2", generation: 2));
        Assert.False(Assert.Single(service.UserPreviews).Showing);
        Assert.Equal(second, service.ShowUserPreview("host-1", "source-2", "bob-id", generation: 2));
        Assert.True(Assert.Single(service.UserPreviews).Showing);
    }
}
