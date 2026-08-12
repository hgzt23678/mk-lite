using Ganss.Xss;

namespace ActivityPub.Federation.Protocol;

public interface IIncomingHtmlSanitizer
{
    string Sanitize(string html);
}

public sealed class IncomingHtmlSanitizer : IIncomingHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return _sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedTags.UnionWith(["a", "abbr", "b", "blockquote", "br", "code", "del", "em", "i", "li", "ol", "p", "pre", "span", "strong", "sub", "sup", "ul"]);
        sanitizer.AllowedAttributes.UnionWith(["class", "href", "rel", "title"]);
        sanitizer.AllowedSchemes.UnionWith(["https", "http"]);
        sanitizer.KeepChildNodes = true;
        sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is AngleSharp.Dom.IElement element && string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            {
                element.SetAttribute("rel", "nofollow noopener noreferrer ugc");
                element.RemoveAttribute("target");
            }
        };
        return sanitizer;
    }
}
