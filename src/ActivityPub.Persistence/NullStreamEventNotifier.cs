using ActivityPub.Application;

namespace ActivityPub.Persistence;

public sealed class NullStreamEventNotifier : IStreamEventNotifier
{
    public bool IsEnabled => false;

    public Task PublishAsync(IReadOnlyList<long> cursors, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.Delay(timeout, cancellationToken);
}
