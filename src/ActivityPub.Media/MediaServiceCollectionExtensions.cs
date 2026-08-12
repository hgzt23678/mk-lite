using ActivityPub.Application;
using Amazon;
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
            var configuration = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
                ForcePathStyle = options.ForcePathStyle
            };
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                configuration.ServiceURL = options.ServiceUrl;
                configuration.AuthenticationRegion = options.Region;
            }

            return new AmazonS3Client(configuration);
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
}
