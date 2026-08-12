using System.Text.Json;

namespace ActivityPub.Federation.Protocol;

internal static class JsonSafetyValidator
{
    public static void Validate(ReadOnlySpan<byte> utf8Json, int maximumDepth = 64)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = maximumDepth
        });
        var objectProperties = new Stack<HashSet<string>?>();
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.StartArray:
                        objectProperties.Push(null);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        objectProperties.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        HashSet<string>? properties = objectProperties.Peek();
                        string propertyName = reader.GetString() ?? throw new JsonException("JSON property name is null.");
                        if (properties is null || !properties.Add(propertyName))
                        {
                            throw new ActivityStreamsProtocolException($"Duplicate or misplaced JSON property '{propertyName}'.");
                        }

                        break;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new ActivityStreamsProtocolException("ActivityStreams JSON is malformed or exceeds nesting limits.", exception);
        }

        if (objectProperties.Count != 0)
        {
            throw new ActivityStreamsProtocolException("ActivityStreams JSON ended before all containers were closed.");
        }
    }
}
