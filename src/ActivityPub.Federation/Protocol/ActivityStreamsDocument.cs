using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Federation.Protocol;

public sealed record ActivityStreamsDocument(
    string Id,
    string ActorIri,
    IReadOnlyList<string> Types,
    string PrimaryType,
    string? ObjectIri,
    string? ObjectOwnerIri,
    string Origin,
    IReadOnlyList<AudienceAddress> Audience,
    Visibility Visibility,
    DateTimeOffset? PublishedAt,
    bool IsSupportedActivity,
    JsonElement Root,
    byte[] RawBody);

public sealed class ActivityStreamsProtocolException : Exception
{
    public ActivityStreamsProtocolException(string message)
        : base(message)
    {
    }

    public ActivityStreamsProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
