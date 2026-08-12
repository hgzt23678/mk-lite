using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal static class RelationshipStreamEventFactory
{
    public static async Task<IReadOnlyList<StreamEvent>> CreateAsync(
        FederationDbContext db,
        ActivityRecord activity,
        IEnumerable<FollowRelation> relationships,
        bool isLocal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(relationships);

        FollowRelation[] mutations = relationships.DistinctBy(value => value.Id).ToArray();
        if (mutations.Length == 0)
        {
            return [];
        }

        string[] actorIris = mutations
            .SelectMany(value => new[] { value.FollowerIri, value.FollowedIri })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var localActors = await db.LocalActors
            .Where(value => actorIris.Contains(value.Iri))
            .Select(value => new { value.Id, value.Iri, value.IsSuspended })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var remoteActors = await db.RemoteActors
            .Where(value => actorIris.Contains(value.Iri) && value.GoneAt == null)
            .Select(value => new { value.Id, value.Iri })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, Guid> actorIds = localActors
            .Select(value => new KeyValuePair<string, Guid>(value.Iri, value.Id))
            .Concat(remoteActors.Select(value => new KeyValuePair<string, Guid>(value.Iri, value.Id)))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        HashSet<string> activeLocalActors = localActors
            .Where(value => !value.IsSuspended)
            .Select(value => value.Iri)
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<StreamEvent>(mutations.Length * 2);
        foreach (FollowRelation relationship in mutations)
        {
            AddForRecipient(relationship.FollowerIri, relationship.FollowedIri, relationship);
            AddForRecipient(relationship.FollowedIri, relationship.FollowerIri, relationship);
        }

        return result;

        void AddForRecipient(string recipientActorIri, string targetActorIri, FollowRelation relationship)
        {
            if (!activeLocalActors.Contains(recipientActorIri) || !actorIds.TryGetValue(targetActorIri, out Guid targetActorId))
            {
                return;
            }

            result.Add(StreamEvent.FromRelationshipMutation(
                activity,
                relationship,
                targetActorId,
                recipientActorIri,
                isLocal));
        }
    }
}
