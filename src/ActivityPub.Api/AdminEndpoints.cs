using System.Security.Claims;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Server;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints, bool keyManagementEnabled)
    {
        RouteGroupBuilder admin = endpoints.MapGroup("/admin")
            .RequireAuthorization("activitypub.admin")
            .RequireRateLimiting("local-api");
        admin.MapGet("/dead-letters", ListDeadLettersAsync);
        admin.MapPost("/dead-letters/{id:guid}/replay", RequeueDeadLetterAsync);
        admin.MapGet("/reports", ListReportsAsync);
        admin.MapPost("/reports/{id:guid}/resolve", ResolveReportAsync);
        admin.MapPost("/domain-policies", CreateDomainPolicyAsync);
        admin.MapDelete("/domain-policies/{id:guid}", RevokeDomainPolicyAsync);
        admin.MapPost("/actor-policies", CreateActorPolicyAsync);
        admin.MapDelete("/actor-policies/{id:guid}", RevokeActorPolicyAsync);
        admin.MapGet("/operations/outbound-delivery", GetOutboundControlAsync);
        admin.MapPut("/operations/outbound-delivery", SetOutboundControlAsync);
        admin.MapPost("/operations/domains/{domain}/cancel-deliveries", CancelDomainDeliveriesAsync);
        admin.MapGet("/federation/queue/stats", GetFederationQueueStatsAsync);
        admin.MapGet("/federation/queue/jobs", ListFederationQueueJobsAsync);
        admin.MapGet("/federation/queue/inbox-jobs", ListFederationInboxJobsAsync);
        admin.MapGet("/legal-holds", ListLegalHoldsAsync);
        admin.MapPost("/legal-holds", PlaceLegalHoldAsync);
        admin.MapDelete("/legal-holds/{id:guid}", ReleaseLegalHoldAsync);
        if (keyManagementEnabled)
        {
            admin.MapPost("/local-actors", CreateLocalActorAsync);
            admin.MapPost("/local-actors/{username}/rotate-key", RotateLocalActorKeyAsync);
        }
        return endpoints;
    }

    private static async Task<IResult> ListDeadLettersAsync(
        DateTimeOffset? before,
        int? limit,
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        Results.Ok(await administration.ListDeadLettersAsync(before, limit ?? 100, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RequeueDeadLetterAsync(
        HttpContext context,
        Guid id,
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        await administration.RequeueDeadLetterAsync(id, OperatorId(context.User), cancellationToken).ConfigureAwait(false)
            ? Results.Accepted($"/admin/dead-letters/{id}")
            : Results.NotFound();

    private static async Task<IResult> ListReportsAsync(
        DateTimeOffset? before,
        int? limit,
        bool? unresolvedOnly,
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        Results.Ok(await administration.ListReportsAsync(before, limit ?? 100, unresolvedOnly ?? true, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ResolveReportAsync(
        HttpContext context,
        Guid id,
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        await administration.ResolveReportAsync(id, OperatorId(context.User), cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> CreateDomainPolicyAsync(
        HttpContext context,
        DomainPolicyRequest request,
        IModerationAdministration administration,
        CancellationToken cancellationToken)
    {
        Guid id = await administration.CreateDomainPolicyAsync(
            request.Domain,
            request.Kind,
            request.Reason,
            OperatorId(context.User),
            request.ExpiresAt,
            cancellationToken).ConfigureAwait(false);
        return Results.Created($"/admin/domain-policies/{id}", new { id });
    }

    private static async Task<IResult> RevokeDomainPolicyAsync(
        HttpContext context,
        Guid id,
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        await administration.RevokeDomainPolicyAsync(id, OperatorId(context.User), cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> CreateActorPolicyAsync(
        HttpContext context,
        ActorPolicyRequest request,
        IModerationAdministration administration,
        CancellationToken cancellationToken)
    {
        Guid id = await administration.CreateActorPolicyAsync(
            request.ActorIri,
            request.Kind,
            request.Reason,
            OperatorId(context.User),
            request.ExpiresAt,
            cancellationToken).ConfigureAwait(false);
        return Results.Created($"/admin/actor-policies/{id}", new { id });
    }

    private static async Task<IResult> RevokeActorPolicyAsync(
        HttpContext context,
        Guid id,
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        await administration.RevokeActorPolicyAsync(id, OperatorId(context.User), cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> GetOutboundControlAsync(
        IModerationAdministration administration,
        CancellationToken cancellationToken) =>
        Results.Ok(await administration.GetOperationalControlAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SetOutboundControlAsync(
        HttpContext context,
        OutboundControlRequest request,
        IModerationAdministration administration,
        CancellationToken cancellationToken)
    {
        await administration.SetOutboundDeliveryPausedAsync(
            request.Paused,
            request.Reason,
            OperatorId(context.User),
            cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelDomainDeliveriesAsync(
        HttpContext context,
        string domain,
        CancelDeliveriesRequest request,
        IModerationAdministration administration,
        CancellationToken cancellationToken)
    {
        int cancelled = await administration.CancelPendingDeliveriesForDomainAsync(
            domain,
            request.Reason,
            OperatorId(context.User),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { cancelled });
    }

    private static async Task<IResult> GetFederationQueueStatsAsync(
        IFederationQueueAdministration administration,
        CancellationToken cancellationToken) =>
        Results.Ok(await administration.GetStatsAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListFederationQueueJobsAsync(
        WorkItemState? state,
        bool? delayed,
        string? remoteDomain,
        DateTimeOffset? before,
        int? limit,
        IFederationQueueAdministration administration,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await administration.ListAsync(
                state,
                delayed,
                remoteDomain,
                before,
                limit ?? 50,
                cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ListFederationInboxJobsAsync(
        WorkItemState? state,
        bool? delayed,
        DateTimeOffset? before,
        int? limit,
        IFederationQueueAdministration administration,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await administration.ListInboxAsync(
                state,
                delayed,
                before,
                limit ?? 50,
                cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static string OperatorId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The administrative token has no subject identifier.");

    private static async Task<IResult> ListLegalHoldsAsync(
        bool? activeOnly,
        int? limit,
        IRawJsonRetentionStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListLegalHoldsAsync(activeOnly ?? true, limit ?? 100, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> PlaceLegalHoldAsync(
        HttpContext context,
        LegalHoldRequest request,
        IRawJsonRetentionStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await store.PlaceLegalHoldAsync(
                request.ResourceKind,
                request.ResourceId,
                request.Reason,
                OperatorId(context.User),
                request.ExpiresAt,
                cancellationToken).ConfigureAwait(false);
            return Results.Created($"/admin/legal-holds/{id}", new { id });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> ReleaseLegalHoldAsync(
        HttpContext context,
        Guid id,
        IRawJsonRetentionStore store,
        CancellationToken cancellationToken) =>
        await store.ReleaseLegalHoldAsync(id, OperatorId(context.User), cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> CreateLocalActorAsync(
        HttpContext context,
        LocalActorRequest request,
        ILocalActorAdministration administration,
        IIncomingHtmlSanitizer sanitizer,
        CancellationToken cancellationToken)
    {
        LocalActorAdministrationResult result = await administration.CreateAsync(
            request.Username,
            request.Kind,
            request.DisplayName,
            sanitizer.Sanitize(request.SummaryHtml),
            request.ManuallyApprovesFollowers,
            request.Discoverable,
            request.Indexable,
            OperatorId(context.User),
            cancellationToken).ConfigureAwait(false);
        return Results.Created($"/users/{Uri.EscapeDataString(request.Username.ToLowerInvariant())}", result);
    }

    private static async Task<IResult> RotateLocalActorKeyAsync(
        HttpContext context,
        string username,
        RotateKeyRequest request,
        ILocalActorAdministration administration,
        CancellationToken cancellationToken)
    {
        LocalActorAdministrationResult? result = await administration.RotateKeyAsync(
            username,
            request.Overlap,
            OperatorId(context.User),
            cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private sealed record DomainPolicyRequest(
        string Domain,
        FederationPolicyKind Kind,
        string Reason,
        DateTimeOffset? ExpiresAt);

    private sealed record ActorPolicyRequest(
        string ActorIri,
        ModerationActionKind Kind,
        string Reason,
        DateTimeOffset? ExpiresAt);

    private sealed record OutboundControlRequest(bool Paused, string Reason);

    private sealed record CancelDeliveriesRequest(string Reason);

    private sealed record LocalActorRequest(
        string Username,
        ActorKind Kind,
        string DisplayName,
        string SummaryHtml,
        bool ManuallyApprovesFollowers,
        bool Discoverable,
        bool Indexable);

    private sealed record RotateKeyRequest(TimeSpan Overlap);

    private sealed record LegalHoldRequest(
        RawJsonResourceKind ResourceKind,
        Guid ResourceId,
        string Reason,
        DateTimeOffset? ExpiresAt);
}
