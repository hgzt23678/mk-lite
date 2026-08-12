using System.Collections.Concurrent;

internal sealed class BrowserTestDiagnostics
{
    private readonly ConcurrentQueue<BrowserTestDiagnostic> unhandledExceptions = new();
    private readonly ConcurrentQueue<BrowserTestTransportDiagnostic> transportFailures = new();

    public void Record(string category, EventId eventId, Exception exception)
    {
        if (category is not "Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost" and
            not "Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer")
        {
            return;
        }

        unhandledExceptions.Enqueue(new BrowserTestDiagnostic(
            category,
            eventId.Id,
            exception.GetType().Name));
    }

    public IReadOnlyList<BrowserTestDiagnostic> Read() => unhandledExceptions.ToArray();

    public void RecordTransport(string category, EventId eventId)
    {
        if (category == "Microsoft.AspNetCore.Server.Kestrel" &&
            eventId.Id == 23 &&
            string.Equals(eventId.Name, "ApplicationNeverCompleted", StringComparison.Ordinal))
        {
            transportFailures.Enqueue(new BrowserTestTransportDiagnostic(
                category,
                eventId.Id,
                eventId.Name));
        }
    }

    public IReadOnlyList<BrowserTestTransportDiagnostic> ReadTransportFailures() => transportFailures.ToArray();

    public void Reset()
    {
        while (unhandledExceptions.TryDequeue(out _))
        {
        }

        while (transportFailures.TryDequeue(out _))
        {
        }
    }
}

internal sealed record BrowserTestDiagnostic(string Category, int EventId, string ExceptionType);

internal sealed record BrowserTestTransportDiagnostic(string Category, int EventId, string? EventName);

internal sealed class BrowserTestLoggerProvider(BrowserTestDiagnostics diagnostics) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new BrowserTestLogger(categoryName, diagnostics);

    public void Dispose()
    {
    }

    private sealed class BrowserTestLogger(string category, BrowserTestDiagnostics diagnostics) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            diagnostics.RecordTransport(category, eventId);
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                diagnostics.Record(category, eventId, exception);
            }
        }
    }
}
