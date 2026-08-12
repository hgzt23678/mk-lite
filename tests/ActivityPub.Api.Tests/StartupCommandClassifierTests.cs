using ActivityPub.Server;

namespace ActivityPub.Api.Tests;

public sealed class StartupCommandClassifierTests
{
    [Theory]
    [InlineData("migrate")]
    [InlineData("dependency-probe")]
    [InlineData("dependency-probe", "postgres")]
    [InlineData("create-local-actor", "alice")]
    [InlineData("create-local-actor", "alice", "Alice")]
    public void MaintenanceCommandsDoNotHostTheFrontend(params string[] args)
    {
        Assert.True(StartupCommandClassifier.IsMaintenanceCommand(args));
    }

    [Theory]
    [InlineData()]
    [InlineData("--urls", "http://127.0.0.1:8080")]
    [InlineData("unknown")]
    [InlineData("migrate", "unexpected")]
    public void WebHostAndUnknownArgumentsRetainNormalStartupValidation(params string[] args)
    {
        Assert.False(StartupCommandClassifier.IsMaintenanceCommand(args));
    }
}
