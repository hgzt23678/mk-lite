using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ActivityPub.Domain;

public sealed class CollectionMembership : Entity
{
    private CollectionMembership()
    {
    }

    private CollectionMembership(
        Guid id,
        string actorIri,
        string collectionIri,
        string objectIri,
        string addActivityIri,
        DateTimeOffset now)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        CollectionIri = CanonicalIri.RequireAbsoluteHttp(collectionIri, nameof(collectionIri));
        ObjectIri = CanonicalIri.RequireAbsoluteHttp(objectIri, nameof(objectIri));
        AddActivityIri = CanonicalIri.RequireAbsoluteHttp(addActivityIri, nameof(addActivityIri));
        EnsureSameOrigin(ActorIri, CollectionIri, "Only the owning actor can add to a collection.");
        State = FederatedRelationState.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public string CollectionIri { get; private set; } = string.Empty;
    public string ObjectIri { get; private set; } = string.Empty;
    public string AddActivityIri { get; private set; } = string.Empty;
    public string? RemoveActivityIri { get; private set; }
    public FederatedRelationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CollectionMembership Add(
        string actorIri,
        string collectionIri,
        string objectIri,
        string addActivityIri,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, collectionIri, objectIri, addActivityIri, now);

    public void Remove(string actorIri, string removeActivityIri, DateTimeOffset now)
    {
        EnsureActor(actorIri);
        State = FederatedRelationState.Reversed;
        RemoveActivityIri = CanonicalIri.RequireAbsoluteHttp(removeActivityIri, nameof(removeActivityIri));
        UpdatedAt = now;
    }

    public void UndoAdd(string actorIri, DateTimeOffset now)
    {
        EnsureActor(actorIri);
        State = FederatedRelationState.Reversed;
        UpdatedAt = now;
    }

    public void UndoRemove(string actorIri, DateTimeOffset now)
    {
        EnsureActor(actorIri);
        if (RemoveActivityIri is null)
        {
            throw new DomainException("Collection membership has no Remove to undo.");
        }

        State = FederatedRelationState.Active;
        RemoveActivityIri = null;
        UpdatedAt = now;
    }

    private void EnsureActor(string actorIri)
    {
        if (!string.Equals(ActorIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("Only the actor that added a collection member can remove it.");
        }
    }

    private static void EnsureSameOrigin(string left, string right, string message)
    {
        var leftUri = new Uri(left);
        var rightUri = new Uri(right);
        if (!string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(leftUri.IdnHost, rightUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            leftUri.Port != rightUri.Port)
        {
            throw new DomainException(message);
        }
    }
}

public sealed class LikeRelation : Entity
{
    private LikeRelation()
    {
    }

    private LikeRelation(
        Guid id,
        string actorIri,
        string objectIri,
        string activityIri,
        FederatedReaction reaction,
        DateTimeOffset now)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        ObjectIri = CanonicalIri.RequireAbsoluteHttp(objectIri, nameof(objectIri));
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        ArgumentNullException.ThrowIfNull(reaction);
        Reaction = reaction.Value;
        CustomEmojiIri = reaction.CustomEmojiIri;
        CustomEmojiName = reaction.CustomEmojiName;
        CustomEmojiUrl = reaction.CustomEmojiUrl;
        CustomEmojiMediaType = reaction.CustomEmojiMediaType;
        State = FederatedRelationState.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public string ObjectIri { get; private set; } = string.Empty;
    public string ActivityIri { get; private set; } = string.Empty;
    public string? Reaction { get; private set; }
    public string EffectiveReaction => Reaction ?? FederatedReaction.DefaultValue;
    public string? CustomEmojiIri { get; private set; }
    public string? CustomEmojiName { get; private set; }
    public string? CustomEmojiUrl { get; private set; }
    public string? CustomEmojiMediaType { get; private set; }
    public FederatedRelationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LikeRelation Create(string actorIri, string objectIri, string activityIri, DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, objectIri, activityIri, FederatedReaction.Create(null, actorIri), now);

    public static LikeRelation Create(
        string actorIri,
        string objectIri,
        string activityIri,
        FederatedReaction reaction,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, objectIri, activityIri, reaction, now);

    public void Undo(string actorIri, DateTimeOffset now)
    {
        if (!string.Equals(ActorIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("Only the actor that created the relation can undo it.");
        }

        State = FederatedRelationState.Reversed;
        UpdatedAt = now;
    }
}

public sealed class EmojiReactionRelation : Entity
{
    private EmojiReactionRelation()
    {
    }

    private EmojiReactionRelation(
        Guid id,
        string actorIri,
        string objectIri,
        string activityIri,
        FederatedReaction reaction,
        DateTimeOffset now)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        ObjectIri = CanonicalIri.RequireAbsoluteHttp(objectIri, nameof(objectIri));
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        ArgumentNullException.ThrowIfNull(reaction);
        Reaction = reaction.Value;
        CustomEmojiIri = reaction.CustomEmojiIri;
        CustomEmojiName = reaction.CustomEmojiName;
        CustomEmojiUrl = reaction.CustomEmojiUrl;
        CustomEmojiMediaType = reaction.CustomEmojiMediaType;
        State = FederatedRelationState.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public string ObjectIri { get; private set; } = string.Empty;
    public string ActivityIri { get; private set; } = string.Empty;
    public string Reaction { get; private set; } = string.Empty;
    public string? CustomEmojiIri { get; private set; }
    public string? CustomEmojiName { get; private set; }
    public string? CustomEmojiUrl { get; private set; }
    public string? CustomEmojiMediaType { get; private set; }
    public FederatedRelationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmojiReactionRelation Create(
        string actorIri,
        string objectIri,
        string activityIri,
        FederatedReaction reaction,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, objectIri, activityIri, reaction, now);

    public void Undo(string actorIri, DateTimeOffset now)
    {
        if (!string.Equals(ActorIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("Only the actor that created the emoji reaction can undo it.");
        }

        State = FederatedRelationState.Reversed;
        UpdatedAt = now;
    }
}

public sealed partial record FederatedReaction
{
    public const string DefaultValue = "👍";
    private const int MaximumReactionLength = 256;
    private static readonly Dictionary<string, string> LegacyReactions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["like"] = "👍",
            ["love"] = "❤",
            ["laugh"] = "😆",
            ["hmm"] = "🤔",
            ["surprise"] = "😮",
            ["congrats"] = "🎉",
            ["angry"] = "💢",
            ["confused"] = "😥",
            ["rip"] = "😇",
            ["pudding"] = "🍮",
            ["star"] = "⭐"
        };

    private FederatedReaction(
        string value,
        string? customEmojiIri,
        string? customEmojiName,
        string? customEmojiUrl,
        string? customEmojiMediaType)
    {
        Value = value;
        CustomEmojiIri = customEmojiIri;
        CustomEmojiName = customEmojiName;
        CustomEmojiUrl = customEmojiUrl;
        CustomEmojiMediaType = customEmojiMediaType;
    }

    public string Value { get; }
    public string? CustomEmojiIri { get; }
    public string? CustomEmojiName { get; }
    public string? CustomEmojiUrl { get; }
    public string? CustomEmojiMediaType { get; }
    public bool IsCustomEmoji => Value.Length >= 3 && Value[0] == ':' && Value[^1] == ':';

    public static FederatedReaction Create(
        string? value,
        string actorIri,
        string? customEmojiIri = null,
        string? customEmojiName = null,
        string? customEmojiUrl = null,
        string? customEmojiMediaType = null)
    {
        string canonicalActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        string normalized = Normalize(value, new Uri(canonicalActorIri).IdnHost);
        bool isCustom = normalized[0] == ':';
        if (!isCustom && new[] { customEmojiIri, customEmojiName, customEmojiUrl, customEmojiMediaType }.Any(x => x is not null))
        {
            throw new DomainException("Unicode reactions cannot carry custom emoji metadata.");
        }

        string? normalizedName = null;
        if (isCustom)
        {
            string token = normalized[1..^1];
            int separator = token.IndexOf('@');
            string shortcode = separator < 0 ? token : token[..separator];
            normalizedName = customEmojiName is null ? $":{shortcode}:" : NormalizeEmojiName(customEmojiName, shortcode);
        }

        return new(
            normalized,
            NormalizeOptionalIri(customEmojiIri, nameof(customEmojiIri)),
            normalizedName,
            NormalizeOptionalIri(customEmojiUrl, nameof(customEmojiUrl)),
            NormalizeMediaType(customEmojiMediaType));
    }

    private static string Normalize(string? value, string actorHost)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? DefaultValue : value.Trim();
        if (candidate.Length > MaximumReactionLength || candidate.Any(char.IsControl))
        {
            throw new DomainException("Reaction is too long or contains control characters.");
        }

        if (LegacyReactions.TryGetValue(candidate, out string? legacy))
        {
            return legacy;
        }

        Match custom = CustomEmojiPattern().Match(candidate);
        if (custom.Success)
        {
            string host = custom.Groups["host"].Success
                ? NormalizeHost(custom.Groups["host"].Value)
                : actorHost.ToLowerInvariant();
            return $":{custom.Groups["name"].Value}@{host}:";
        }

        if (candidate.Contains('\uFE0F', StringComparison.Ordinal) &&
            !candidate.Contains('\u200D', StringComparison.Ordinal) &&
            !candidate.Contains('\u20E3', StringComparison.Ordinal))
        {
            candidate = candidate.Replace("\uFE0F", string.Empty, StringComparison.Ordinal);
        }

        int[] elements = StringInfo.ParseCombiningCharacters(candidate);
        if (elements.Length != 1 || Encoding.UTF8.GetByteCount(candidate) > 128 || !ContainsEmojiSymbol(candidate))
        {
            throw new DomainException("Reaction must be one Unicode emoji or a custom emoji shortcode.");
        }

        return candidate;
    }

    private static bool ContainsEmojiSymbol(string candidate)
    {
        bool specialSequence = candidate.Contains('\u200D', StringComparison.Ordinal) ||
                               candidate.Contains('\u20E3', StringComparison.Ordinal);
        foreach (Rune rune in candidate.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.MathSymbol ||
                rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                return true;
            }
        }

        return specialSequence;
    }

    private static string NormalizeHost(string host)
    {
        if (!Uri.TryCreate($"https://{host}/", UriKind.Absolute, out Uri? uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.IdnHost.Length is 0 or > 253 ||
            uri.IdnHost.Contains(':', StringComparison.Ordinal))
        {
            throw new DomainException("Custom emoji reaction contains an invalid host.");
        }

        return uri.IdnHost.ToLowerInvariant();
    }

    private static string NormalizeEmojiName(string value, string expectedShortcode)
    {
        string candidate = value.Trim();
        if (!string.Equals(candidate, $":{expectedShortcode}:", StringComparison.Ordinal))
        {
            throw new DomainException("Custom emoji tag name does not match the reaction shortcode.");
        }

        return candidate;
    }

    private static string? NormalizeOptionalIri(string? value, string parameterName) =>
        value is null ? null : CanonicalIri.RequireAbsoluteHttp(value, parameterName);

    private static string? NormalizeMediaType(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string candidate = value.Trim();
        if (candidate.Length is 0 or > 128 || candidate.Any(char.IsControl) || !candidate.Contains('/', StringComparison.Ordinal))
        {
            throw new DomainException("Custom emoji media type is invalid.");
        }

        return candidate.ToLowerInvariant();
    }

    [GeneratedRegex("^:(?<name>[A-Za-z0-9_+\\-]{1,64})(?:@(?<host>[A-Za-z0-9.-]{1,253}))?:$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CustomEmojiPattern();
}

public sealed class AnnounceRelation : Entity
{
    private AnnounceRelation()
    {
    }

    private AnnounceRelation(Guid id, string actorIri, string objectIri, string activityIri, DateTimeOffset now)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        ObjectIri = CanonicalIri.RequireAbsoluteHttp(objectIri, nameof(objectIri));
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        State = FederatedRelationState.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public string ObjectIri { get; private set; } = string.Empty;
    public string ActivityIri { get; private set; } = string.Empty;
    public FederatedRelationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AnnounceRelation Create(string actorIri, string objectIri, string activityIri, DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, objectIri, activityIri, now);

    public void Undo(string actorIri, DateTimeOffset now)
    {
        if (!string.Equals(ActorIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("Only the actor that created the relation can undo it.");
        }

        State = FederatedRelationState.Reversed;
        UpdatedAt = now;
    }
}

public sealed class ActorMove : Entity
{
    private ActorMove()
    {
    }

    private ActorMove(Guid id, string actorIri, string targetActorIri, string activityIri, DateTimeOffset now)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        TargetActorIri = CanonicalIri.RequireAbsoluteHttp(targetActorIri, nameof(targetActorIri));
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        if (string.Equals(ActorIri, TargetActorIri, StringComparison.Ordinal))
        {
            throw new DomainException("An actor cannot move to itself.");
        }

        State = FederatedRelationState.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public string TargetActorIri { get; private set; } = string.Empty;
    public string ActivityIri { get; private set; } = string.Empty;
    public FederatedRelationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ActorMove Create(string actorIri, string targetActorIri, string activityIri, DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, targetActorIri, activityIri, now);

    public void Undo(string actorIri, DateTimeOffset now)
    {
        if (!string.Equals(ActorIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("Only the moved actor can undo a Move.");
        }

        State = FederatedRelationState.Reversed;
        UpdatedAt = now;
    }
}
