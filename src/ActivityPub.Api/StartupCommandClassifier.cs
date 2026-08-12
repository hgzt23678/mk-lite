namespace ActivityPub.Server;

internal static class StartupCommandClassifier
{
    public static bool IsMaintenanceCommand(string[] args) =>
        args is ["migrate"] or
            ["dependency-probe", ..] or
            ["create-local-actor", ..];
}
