namespace ActivityPub.Misskey.Blazor.Components;

internal sealed class StickyOffsetContext
{
    private double top;

    public event Action<double>? Changed;

    public double Top => top;

    public void Set(double value)
    {
        double next = double.IsFinite(value) ? Math.Max(0, value) : 0;
        if (Math.Abs(top - next) < 0.5)
        {
            return;
        }

        top = next;
        Changed?.Invoke(top);
    }
}
