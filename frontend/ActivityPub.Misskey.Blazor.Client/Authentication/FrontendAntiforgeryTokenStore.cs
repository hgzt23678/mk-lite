namespace ActivityPub.Misskey.Blazor.Client.Authentication;

public sealed class FrontendAntiforgeryTokenStore
{
    public const string RequiredHeaderName = "X-CSRF-TOKEN";

    private string? requestToken;

    public string? RequestToken => Volatile.Read(ref requestToken);

    public void Replace(string? headerName, string? token)
    {
        if (!string.Equals(headerName, RequiredHeaderName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(token) ||
            token.Length > 2_048 ||
            token.Any(char.IsControl))
        {
            Clear();
            return;
        }

        Volatile.Write(ref requestToken, token);
    }

    public void Clear() => Volatile.Write(ref requestToken, null);
}
