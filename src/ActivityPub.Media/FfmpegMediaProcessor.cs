using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ActivityPub.Media;

internal sealed record ProcessedMedia(
    string Path,
    string MediaType,
    long Length,
    int? Width,
    int? Height,
    long? DurationMilliseconds,
    string? ThumbnailPath);

internal interface IMediaProcessor
{
    Task<ProcessedMedia> ProcessAsync(string inputPath, string mediaType, string workingDirectory, CancellationToken cancellationToken);
}

internal sealed class FfmpegMediaProcessor(MediaOptions options) : IMediaProcessor
{
    public async Task<ProcessedMedia> ProcessAsync(
        string inputPath,
        string mediaType,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        MediaProbe probe = await ProbeAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (probe.Width > options.MaximumImageWidth || probe.Height > options.MaximumImageHeight)
        {
            throw new InvalidDataException("Media dimensions exceed the configured limit.");
        }

        if (probe.Duration > options.MaximumMediaDuration)
        {
            throw new InvalidDataException("Media duration exceeds the configured limit.");
        }

        string outputPath = Path.Combine(workingDirectory, "sanitized" + MediaTypeSniffer.Extension(mediaType));
        var arguments = new List<string>
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath,
            "-map_metadata", "-1", "-map_chapters", "-1"
        };
        AddEncodingArguments(arguments, mediaType);
        arguments.Add(outputPath);
        await RunAsync(options.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);
        var output = new FileInfo(outputPath);
        if (!output.Exists || output.Length == 0)
        {
            throw new InvalidDataException("Media processing produced no output.");
        }

        string? thumbnailPath = null;
        if (mediaType.StartsWith("image/", StringComparison.Ordinal) || mediaType.StartsWith("video/", StringComparison.Ordinal))
        {
            thumbnailPath = Path.Combine(workingDirectory, "thumbnail.jpg");
            await RunAsync(options.FfmpegPath,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", outputPath,
                "-vf", "scale=640:-2:force_original_aspect_ratio=decrease",
                "-frames:v", "1", "-map_metadata", "-1", "-c:v", "mjpeg", "-q:v", "3", thumbnailPath
            ], cancellationToken).ConfigureAwait(false);
        }

        return new(
            outputPath,
            mediaType,
            output.Length,
            probe.Width,
            probe.Height,
            probe.Duration is null ? null : checked((long)probe.Duration.Value.TotalMilliseconds),
            thumbnailPath);
    }

    private async Task<MediaProbe> ProbeAsync(string inputPath, CancellationToken cancellationToken)
    {
        string output = await RunAsync(options.FfprobePath,
        [
            "-v", "error", "-show_entries", "stream=width,height:format=duration", "-of", "json", inputPath
        ], cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 16 });
        int? width = null;
        int? height = null;
        if (document.RootElement.TryGetProperty("streams", out JsonElement streams))
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                if (width is null && stream.TryGetProperty("width", out JsonElement widthValue) && widthValue.TryGetInt32(out int parsedWidth))
                {
                    width = parsedWidth;
                }

                if (height is null && stream.TryGetProperty("height", out JsonElement heightValue) && heightValue.TryGetInt32(out int parsedHeight))
                {
                    height = parsedHeight;
                }
            }
        }

        TimeSpan? duration = null;
        if (document.RootElement.TryGetProperty("format", out JsonElement format) &&
            format.TryGetProperty("duration", out JsonElement durationValue) &&
            durationValue.ValueKind == JsonValueKind.String &&
            double.TryParse(durationValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            double.IsFinite(seconds) && seconds >= 0)
        {
            duration = TimeSpan.FromSeconds(seconds);
        }

        return new(width, height, duration);
    }

    private async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start media process '{Path.GetFileName(executable)}'.");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ProcessorTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        string output = await stdout.ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string boundedError = error.Length <= 2_000 ? error : error[..2_000];
            throw new InvalidDataException($"Media processor rejected the input: {boundedError}");
        }

        return output;
    }

    private static void AddEncodingArguments(List<string> arguments, string mediaType)
    {
        string[] profile = mediaType switch
        {
            "image/jpeg" => ["-frames:v", "1", "-c:v", "mjpeg", "-q:v", "2"],
            "image/png" => ["-frames:v", "1", "-c:v", "png"],
            "image/gif" => ["-c:v", "gif"],
            "image/webp" => ["-frames:v", "1", "-c:v", "libwebp", "-quality", "90"],
            "video/mp4" => ["-c:v", "libx264", "-preset", "medium", "-crf", "23", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"],
            "video/webm" => ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-c:a", "libopus", "-b:a", "128k"],
            "audio/mpeg" => ["-vn", "-c:a", "libmp3lame", "-b:a", "192k"],
            "audio/ogg" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType))
        };
        foreach (string value in profile)
        {
            arguments.Add(value);
        }
    }

    private sealed record MediaProbe(int? Width, int? Height, TimeSpan? Duration);
}
