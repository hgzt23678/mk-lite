using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Server;

internal static class LocalActorCommand
{
    public static async Task RunAsync(
        IServiceProvider services,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Length is < 1 or > 2)
        {
            throw new ArgumentException("Usage: create-local-actor <username> [display-name]");
        }

        string username = arguments[0];
        string displayName = arguments.Length == 2 ? arguments[1] : username;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ILocalActorAdministration administration = scope.ServiceProvider.GetRequiredService<ILocalActorAdministration>();
        LocalActorAdministrationResult result = await administration.CreateAsync(
            username,
            ActorKind.Person,
            displayName,
            string.Empty,
            manuallyApprovesFollowers: false,
            discoverable: true,
            indexable: true,
            operatorId: "privileged-cli",
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
