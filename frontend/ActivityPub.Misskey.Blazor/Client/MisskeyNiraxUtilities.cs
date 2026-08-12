using System.Collections.ObjectModel;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyRouteDefinition(
    string Path,
    string Name,
    bool LoginRequired = false,
    IReadOnlyDictionary<string, string>? Query = null,
    string? HashParameter = null,
    IReadOnlyList<MisskeyRouteDefinition>? Children = null);

public sealed record MisskeyResolvedRoute(
    MisskeyRouteDefinition Route,
    IReadOnlyDictionary<string, string> Parameters,
    MisskeyResolvedRoute? Child = null);

public static class MisskeyNiraxUtilities
{
    public static MisskeyResolvedRoute? Resolve(
        IReadOnlyList<MisskeyRouteDefinition> routes,
        string path)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        bool hasExplicitScheme = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        Uri? absolute = null;
        bool isAbsolute = hasExplicitScheme && Uri.TryCreate(path, UriKind.Absolute, out absolute);
        string rawPath;
        string query;
        string fragment;
        if (isAbsolute)
        {
            rawPath = absolute!.AbsolutePath;
            query = absolute.Query;
            fragment = absolute.Fragment;
        }
        else
        {
            int fragmentIndex = path.IndexOf('#');
            fragment = fragmentIndex >= 0 ? path[fragmentIndex..] : string.Empty;
            string pathAndQuery = fragmentIndex >= 0 ? path[..fragmentIndex] : path;
            int queryIndex = pathAndQuery.IndexOf('?');
            query = queryIndex >= 0 ? pathAndQuery[queryIndex..] : string.Empty;
            rawPath = queryIndex >= 0 ? pathAndQuery[..queryIndex] : pathAndQuery;
            if (!rawPath.StartsWith('/')) rawPath = "/" + rawPath;
        }

        string[] parts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SafeUnescape)
            .Select(part =>
            {
                int queryDelimiter = part.IndexOfAny(['?', '#']);
                return queryDelimiter >= 0 ? part[..queryDelimiter] : part;
            })
            .ToArray();
        return Match(routes, parts, query, fragment);
    }

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Any(char.IsControl))
        {
            throw new ArgumentException("Route path must be a same-origin path.", nameof(path));
        }
        return path;
    }

    private static MisskeyResolvedRoute? Match(
        IReadOnlyList<MisskeyRouteDefinition> routes,
        string[] parts,
        string query,
        string fragment)
    {
        foreach (MisskeyRouteDefinition route in routes)
        {
            ParsedRoute parsed = Parse(route.Path);
            if (!parsed.TryMatch(parts, out Dictionary<string, string> parameters)) continue;
            if (parsed.Consumed < parts.Length)
            {
                if (route.Children is null) continue;
                MisskeyResolvedRoute? child = Match(route.Children, parts.Skip(parsed.Consumed).ToArray(), query, fragment);
                if (child is null) continue;
                return new(route, new ReadOnlyDictionary<string, string>(parameters), child);
            }

            if (route.Children is not null)
            {
                MisskeyResolvedRoute? child = Match(route.Children, [], query, fragment);
                if (child is not null) return new(route, new ReadOnlyDictionary<string, string>(parameters), child);
            }

            if (route.Query is not null)
            {
                foreach ((string key, string value) in ParseQuery(query))
                {
                    if (route.Query.TryGetValue(key, out string? target)) parameters[target] = value;
                }
            }
            if (route.HashParameter is not null && fragment.Length > 1)
            {
                parameters[route.HashParameter] = SafeUnescape(fragment[1..]);
            }
            return new(route, new ReadOnlyDictionary<string, string>(parameters));
        }
        return null;
    }

    private static IEnumerable<(string Key, string Value)> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query)) yield break;
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] values = pair.Split('=', 2);
            string key = SafeUnescape(values[0].Replace('+', ' '));
            string value = values.Length == 2 ? SafeUnescape(values[1].Replace('+', ' ')) : string.Empty;
            if (key.Length > 0) yield return (key, value);
        }
    }

    private static string SafeUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static ParsedRoute Parse(string path)
    {
        List<RoutePart> parts = [];
        foreach (string part in path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon < 0)
            {
                parts.Add(new(part, null, false, false));
                continue;
            }

            string prefix = part[..colon];
            string name = part[(colon + 1)..];
            bool wildcard = name.EndsWith("(*)", StringComparison.Ordinal);
            bool optional = name.EndsWith('?');
            name = name.Replace("(*)", string.Empty, StringComparison.Ordinal).TrimEnd('?');
            parts.Add(new(null, name, wildcard, optional, prefix));
        }
        return new(parts);
    }

    private sealed record ParsedRoute(IReadOnlyList<RoutePart> Parts)
    {
        public int Consumed { get; private set; }

        public bool TryMatch(string[] source, out Dictionary<string, string> parameters)
        {
            parameters = new(StringComparer.Ordinal);
            Consumed = 0;
            foreach (RoutePart part in Parts)
            {
                if (part.Wildcard)
                {
                    if (Consumed < source.Length) parameters[part.Name!] = string.Join('/', source.Skip(Consumed));
                    Consumed = source.Length;
                    return true;
                }

                if (Consumed >= source.Length)
                {
                    if (part.Optional) continue;
                    return false;
                }

                string value = source[Consumed];
                if (part.Literal is not null && !string.Equals(part.Literal, value, StringComparison.Ordinal)) return false;
                if (part.Name is not null)
                {
                    if (part.Prefix is not null)
                    {
                        if (!value.StartsWith(part.Prefix, StringComparison.Ordinal)) return false;
                        parameters[part.Name] = value[part.Prefix.Length..];
                    }
                    else parameters[part.Name] = value;
                }
                Consumed++;
            }
            return true;
        }
    }

    private sealed record RoutePart(string? Literal, string? Name, bool Wildcard, bool Optional, string? Prefix = null);
}
