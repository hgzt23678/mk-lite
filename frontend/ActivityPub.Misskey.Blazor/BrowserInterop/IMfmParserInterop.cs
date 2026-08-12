using System.Text.Json;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.BrowserInterop;

public interface IMfmParserInterop : IAsyncDisposable
{
    ValueTask<IReadOnlyList<MfmNode>> ParseAsync(string text, bool plain, CancellationToken cancellationToken);
}

public sealed record MfmNode(
    string Type,
    JsonElement Props,
    IReadOnlyList<MfmNode>? Children);

public sealed class MfmParserInterop(IJSRuntime jsRuntime) : IMfmParserInterop
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private IJSObjectReference? module;

    public async ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
        string text,
        bool plain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "MFM input exceeds the browser parser limit.");
        }

        module ??= await BrowserModuleImporter.ImportAsync(
            jsRuntime,
            "./_content/ActivityPub.Misskey.Blazor/js/mfm-parser.js",
            cancellationToken);
        string json = await module.InvokeAsync<string>("parse", cancellationToken, text, plain);
        return JsonSerializer.Deserialize<MfmNode[]>(json, SerializerOptions) ?? [];
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }
}
