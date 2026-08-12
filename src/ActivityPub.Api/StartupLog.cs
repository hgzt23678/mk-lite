namespace ActivityPub.Server;

internal static partial class StartupLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Development federation network exceptions are active. RequireHttps={RequireHttps}, AllowLoopback={AllowLoopback}, RestrictToAllowedHosts={RestrictToAllowedHosts}, AllowedHosts={AllowedHosts}")]
    public static partial void DevelopmentFederationNetworkExceptions(
        ILogger logger,
        bool requireHttps,
        bool allowLoopback,
        bool restrictToAllowedHosts,
        string allowedHosts);
}
