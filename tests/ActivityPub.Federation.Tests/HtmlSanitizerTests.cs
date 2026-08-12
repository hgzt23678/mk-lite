using ActivityPub.Federation.Protocol;

namespace ActivityPub.Federation.Tests;

public sealed class HtmlSanitizerTests
{
    [Fact]
    public void RemovesScriptsEventsStylesAndUnsafeSchemes()
    {
        var sanitizer = new IncomingHtmlSanitizer();

        string result = sanitizer.Sanitize("<p onclick=\"alert(1)\" style=\"color:red\">safe<script>alert(2)</script><a href=\"javascript:alert(3)\">link</a></p>");

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safe", result, StringComparison.Ordinal);
    }
}
