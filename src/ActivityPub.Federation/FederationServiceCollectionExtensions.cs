using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Inbound;
using ActivityPub.Federation.Outbound;
using ActivityPub.Federation.Protocol;
using ActivityPub.Federation.Signatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActivityPub.Federation;

public static class FederationServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubFederation(
        this IServiceCollection services,
        FederationOptions options,
        bool isProduction,
        bool outboundSigningEnabled)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(isProduction);

        services.AddSingleton(options);
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.TryAddSingleton<IFederationInstrumentation>(_ => NullFederationInstrumentation.Instance);
        services.AddSingleton<PublicIriFactory>();
        services.AddSingleton<IFederationDnsResolver, SystemFederationDnsResolver>();
        services.AddSingleton<FederationAddressValidator>();
        services.AddSingleton<ISafeFederationHttpClient, SafeFederationHttpClient>();
        services.AddScoped<IRemoteKeyResolver, RemoteKeyResolver>();
        services.AddScoped<IHttpSignatureVerifier, HttpSignatureVerifier>();
        services.AddScoped<IInboundActivityReceiver, InboundActivityReceiver>();
        services.AddScoped<IActorMoveValidator, ActorMoveValidator>();
        if (outboundSigningEnabled)
        {
            services.AddScoped<IOutboundTransport, ActivityPubOutboundTransport>();
        }
        services.AddScoped<IRemoteRecipientResolver, RemoteRecipientResolver>();
        services.AddScoped<IClientOutboxService, ClientOutboxService>();
        services.AddSingleton<IIncomingHtmlSanitizer, IncomingHtmlSanitizer>();
        services.AddScoped<IUrlPreviewFetcher, UrlPreviewFetcher>();
        services.AddScoped<IUrlPreviewService, UrlPreviewService>();
        return services;
    }
}
