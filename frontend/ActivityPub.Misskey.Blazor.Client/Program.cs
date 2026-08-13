using ActivityPub.Misskey.Blazor.Client.Authentication;
using ActivityPub.Misskey.Blazor.Client.Localization;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Routing;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Client;

public static class Program
{
    public static async Task Main(string[] args)
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        Uri applicationBaseUri = new(builder.HostEnvironment.BaseAddress, UriKind.Absolute);
        Uri transportOrigin = new(applicationBaseUri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);

        builder.Services.AddAuthorizationCore();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddSingleton<FrontendRuntimeConfigurationState>();
        builder.Services.AddSingleton(services =>
            services.GetRequiredService<FrontendRuntimeConfigurationState>().GetRequiredRuntime());
        builder.Services.AddScoped<FrontendRuntimeConfigurationLoader>();
        builder.Services.AddSingleton(MisskeyFrontendRouteAssemblies.Empty);
        builder.Services.AddSingleton<IMisskeyLocaleCatalog, MisskeyLocaleCatalog>();
        builder.Services.AddScoped<BrowserMisskeyLocalizer>();
        builder.Services.AddScoped<IMisskeyLocalizer>(services =>
            services.GetRequiredService<BrowserMisskeyLocalizer>());

        builder.Services.AddScoped<FrontendAntiforgeryTokenStore>();
        builder.Services.AddScoped<BrowserRequestAntiforgeryInterop>();
        builder.Services.AddSingleton(new FrontendOrigin(transportOrigin));
        builder.Services.AddScoped<FrontendRequestHandler>();
        builder.Services.AddScoped(serviceProvider =>
        {
            var handler = serviceProvider.GetRequiredService<FrontendRequestHandler>();
            handler.InnerHandler = new HttpClientHandler();
            return new HttpClient(handler)
            {
                BaseAddress = transportOrigin,
                Timeout = TimeSpan.FromSeconds(30)
            };
        });
        builder.Services.AddScoped<FrontendSessionClient>();
        builder.Services.AddScoped<FrontendSessionAuthenticationStateProvider>();
        builder.Services.AddScoped<AuthenticationStateProvider>(services =>
            services.GetRequiredService<FrontendSessionAuthenticationStateProvider>());
        builder.Services.AddMisskeyBrowserUi();

        WebAssemblyHost host = builder.Build();
        try
        {
            await host.Services
                .GetRequiredService<FrontendRuntimeConfigurationLoader>()
                .InitializeAsync()
                .ConfigureAwait(false);
            await host.Services
                .GetRequiredService<FrontendSessionAuthenticationStateProvider>()
                .InitializeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Configuration, session and antiforgery bootstrap failures are deliberately
            // reduced to a stable public code. Exception text can contain deployment URLs
            // and must not be copied into the DOM or browser telemetry.
            try
            {
                await host.Services.GetRequiredService<IJSRuntime>().InvokeVoidAsync(
                    "activityPubFrontendBootstrap.fail",
                    "FRONTEND_INITIALIZATION_FAILED").ConfigureAwait(false);
            }
            catch (JSException)
            {
                // The static splash remains if the browser itself cannot execute the
                // same-origin failure renderer.
            }

            return;
        }

        await host.RunAsync().ConfigureAwait(false);
    }
}
