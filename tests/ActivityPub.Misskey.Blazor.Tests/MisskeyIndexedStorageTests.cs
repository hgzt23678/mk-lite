using ActivityPub.Misskey.Blazor.BrowserInterop;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyIndexedStorageTests
{
    [Fact]
    public void InterfaceSeparatesAccountStorageFromOrdinaryBrowserStorage()
    {
        Assert.True(typeof(IMisskeyIndexedStorage).IsAssignableTo(typeof(IAsyncDisposable)));
        Assert.NotNull(typeof(IMisskeyIndexedStorage).GetMethod(nameof(IMisskeyIndexedStorage.GetAsync)));
        Assert.NotNull(typeof(IMisskeyIndexedStorage).GetMethod(nameof(IMisskeyIndexedStorage.SetAsync)));
        Assert.NotNull(typeof(IMisskeyIndexedStorage).GetMethod(nameof(IMisskeyIndexedStorage.DeleteAsync)));
    }

    [Fact]
    public void IndexedStorageUsesTypedJsObjectReferenceAndDoesNotExposeTokenLoggingHooks()
    {
        Assert.DoesNotContain(
            typeof(MisskeyIndexedStorage).GetMethods().Select(method => method.Name),
            name => name.Contains("Log", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(typeof(IJSObjectReference), typeof(MisskeyIndexedStorage).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Select(field => field.FieldType));
    }
}
