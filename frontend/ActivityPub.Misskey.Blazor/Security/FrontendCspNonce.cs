using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace ActivityPub.Misskey.Blazor.Security;

public static class FrontendCspNonce
{
    public const string HttpContextItemKey = "ActivityPub.Misskey.Blazor.CspNonce";

    public static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string GetRequired(HttpContext? context)
    {
        if (context?.Items[HttpContextItemKey] is not string nonce || string.IsNullOrWhiteSpace(nonce))
        {
            throw new InvalidOperationException("The frontend CSP nonce was not initialized for this request.");
        }

        return nonce;
    }
}
