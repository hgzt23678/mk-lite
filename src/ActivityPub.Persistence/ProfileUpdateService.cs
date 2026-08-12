using System.Text.Encodings.Web;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class ProfileUpdateService(
    IDbContextFactory<FederationDbContext> contextFactory) : IProfileUpdateService
{
    public async Task<bool> UpdateAsync(
        string username,
        ProfileUpdateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string normalized = username.ToUpperInvariant();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LocalActor? actor = await db.LocalActors.AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.NormalizedUsername == normalized && !candidate.IsSuspended, cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            return false;
        }

        string displayName = command.Name ?? actor.DisplayName;
        string summaryHtml = command.Description is null ? actor.SummaryHtml : EncodeSummary(command.Description);
        actor.UpdateProfile(
            displayName,
            summaryHtml,
            command.IsLocked ?? actor.ManuallyApprovesFollowers,
            command.Discoverable ?? actor.Discoverable,
            command.Indexable ?? actor.Indexable,
            DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string EncodeSummary(string description)
    {
        string normalized = description.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string encoded = HtmlEncoder.Default.Encode(normalized);
        return "<p>" + encoded.Replace("\n", "<br>", StringComparison.Ordinal) + "</p>";
    }
}
