using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length is < 1 or > 4 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? target))
{
    Console.Error.WriteLine("Usage: ActivityPub.Load <target-uri> [duration-seconds=15] [concurrency=32] [host-header]");
    return 2;
}

int durationSeconds = ParseBounded(args, 1, 15, 1, 3_600, "duration-seconds");
int concurrency = ParseBounded(args, 2, 32, 1, 2_048, "concurrency");
string? hostHeader = args.Length > 3 ? args[3] : null;
if (hostHeader is not null && (hostHeader.Length > 255 || hostHeader.Any(char.IsControl)))
{
    throw new ArgumentException("Host header is invalid.", nameof(args));
}

using var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = concurrency,
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    ConnectTimeout = TimeSpan.FromSeconds(5),
    AutomaticDecompression = DecompressionMethods.All,
    UseProxy = false
};
using var client = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(10)
};

await RunPhaseAsync(client, target, hostHeader, Math.Min(3, durationSeconds), concurrency, collect: false);
LoadResult result = await RunPhaseAsync(client, target, hostHeader, durationSeconds, concurrency, collect: true);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return result.Failed == 0 ? 0 : 1;

static int ParseBounded(string[] values, int index, int fallback, int minimum, int maximum, string name)
{
    if (values.Length <= index)
    {
        return fallback;
    }

    return int.TryParse(values[index], System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed >= minimum && parsed <= maximum
        ? parsed
        : throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
}

static async Task<LoadResult> RunPhaseAsync(
    HttpClient client,
    Uri target,
    string? hostHeader,
    int durationSeconds,
    int concurrency,
    bool collect)
{
    var latencies = new ConcurrentBag<double>();
    var statuses = new ConcurrentDictionary<int, long>();
    long failed = 0;
    using var duration = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
    long started = Stopwatch.GetTimestamp();
    Task[] workers = Enumerable.Range(0, concurrency).Select(_ => SendLoopAsync()).ToArray();
    await Task.WhenAll(workers);
    TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
    double[] ordered = latencies.Order().ToArray();
    long requests = statuses.Values.Sum() + failed;
    return new LoadResult(
        target.AbsoluteUri,
        durationSeconds,
        concurrency,
        requests,
        statuses.Where(x => x.Key is >= 200 and < 300).Sum(x => x.Value),
        failed + statuses.Where(x => x.Key is < 200 or >= 300).Sum(x => x.Value),
        requests / elapsed.TotalSeconds,
        Percentile(ordered, 0.50),
        Percentile(ordered, 0.95),
        Percentile(ordered, 0.99),
        ordered.Length == 0 ? 0 : ordered[^1],
        statuses.OrderBy(x => x.Key).ToDictionary(x => x.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), x => x.Value),
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription,
        Environment.ProcessorCount);

    async Task SendLoopAsync()
    {
        while (!duration.IsCancellationRequested)
        {
            long requestStarted = Stopwatch.GetTimestamp();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                request.Headers.Host = hostHeader;
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    duration.Token).ConfigureAwait(false);
                _ = await response.Content.ReadAsByteArrayAsync(duration.Token).ConfigureAwait(false);
                statuses.AddOrUpdate((int)response.StatusCode, 1, static (_, current) => current + 1);
                if (collect)
                {
                    latencies.Add(Stopwatch.GetElapsedTime(requestStarted).TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (duration.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                Interlocked.Increment(ref failed);
                if (collect)
                {
                    latencies.Add(Stopwatch.GetElapsedTime(requestStarted).TotalMilliseconds);
                }
            }
        }
    }
}

static double Percentile(double[] ordered, double percentile)
{
    if (ordered.Length == 0)
    {
        return 0;
    }

    int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
    return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
}

internal sealed record LoadResult(
    string Target,
    int DurationSeconds,
    int Concurrency,
    long Requests,
    long Succeeded,
    long Failed,
    double RequestsPerSecond,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds,
    IReadOnlyDictionary<string, long> StatusCodes,
    string Framework,
    string OperatingSystem,
    int ProcessorCount);
