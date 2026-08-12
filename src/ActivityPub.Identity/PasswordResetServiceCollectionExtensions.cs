using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActivityPub.Identity;

public static class PasswordResetServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubPasswordReset(
        this IServiceCollection services,
        PasswordResetOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(options);
        TimeSpan identityTokenLifetime = options.EmailConfirmationTokenLifetime > options.TokenLifetime
            ? options.EmailConfirmationTokenLifetime
            : options.TokenLifetime;
        services.Configure<DataProtectionTokenProviderOptions>(providerOptions =>
            providerOptions.TokenLifespan = identityTokenLifetime);
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IPasswordResetEmailSender, MailKitPasswordResetEmailSender>();
        services.AddScoped<IEmailConfirmationSender>(provider =>
            (IEmailConfirmationSender)provider.GetRequiredService<IPasswordResetEmailSender>());
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        return services;
    }
}
