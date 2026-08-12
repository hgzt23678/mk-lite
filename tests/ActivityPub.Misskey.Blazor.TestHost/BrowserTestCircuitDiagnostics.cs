using Microsoft.AspNetCore.Components.Server.Circuits;

internal sealed class BrowserTestCircuitDiagnostics : CircuitHandler
{
    private int activeCircuits;
    private int closedCircuits;

    public int ActiveCircuits => Volatile.Read(ref activeCircuits);

    public int ClosedCircuits => Volatile.Read(ref closedCircuits);

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _ = circuit;
        _ = cancellationToken;
        Interlocked.Increment(ref activeCircuits);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _ = circuit;
        _ = cancellationToken;
        Interlocked.Decrement(ref activeCircuits);
        Interlocked.Increment(ref closedCircuits);
        return Task.CompletedTask;
    }
}
