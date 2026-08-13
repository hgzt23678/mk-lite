using ActivityPub.Application;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Media;

public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubMedia(this IServiceCollection services, MediaOptions options, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(isProduction);
        services.AddSingleton(options);
        if (!options.Enabled)
        {
            services.AddScoped<IAnnouncementImageImporter, DisabledAnnouncementImageImporter>();
            return services;
        }

        services.AddSingleton<IAmazonS3>(_ =>
        {
            return new AmazonS3Client(CreateAmazonS3Config(options));
        });
        services.AddSingleton<IMediaObjectStore, S3MediaObjectStore>();
        services.AddSingleton<IMediaMalwareScanner, ClamAvMalwareScanner>();
        services.AddSingleton<IMediaProcessor, FfmpegMediaProcessor>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IRemoteMediaProxyService, RemoteMediaProxyService>();
        services.AddScoped<IAnnouncementImageImporter, AnnouncementImageImporter>();
        if (options.GarbageCollectionEnabled)
        {
            services.AddHostedService<MediaGarbageCollectionWorker>();
        }

        return services;
    }

    internal static AmazonS3Config CreateAmazonS3Config(MediaOptions options)
    {
        var configuration = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
            ForcePathStyle = options.ForcePathStyle
        };
        if (options.Provider == MediaObjectStoreProvider.CloudflareR2 || !string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            configuration.ServiceURL = options.ResolveServiceUri().AbsoluteUri.TrimEnd('/');
            configuration.AuthenticationRegion = options.Region;
        }

        if (options.Provider == MediaObjectStoreProvider.CloudflareR2)
        {
            // R2 does not support the SDK's optional checksum headers. Object integrity is
            // still protected by HTTPS, while MediaService verifies its own SHA-256 digest.
            configuration.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            configuration.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }

        return configuration;
    }
}
