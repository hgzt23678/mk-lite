using System.Diagnostics;
using ActivityPub.Media;

namespace ActivityPub.Media.Tests;

public sealed class FfmpegMediaProcessorTests
{
    [Fact]
    public async Task ReencodesImageAndCreatesBoundedThumbnail()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string input = Path.Combine(directory, "input.jpg");
            await RunAsync("/usr/bin/ffmpeg",
                "-nostdin", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=128x96", "-frames:v", "1", "-metadata", "comment=private-metadata", input);
            var processor = new FfmpegMediaProcessor(Options());

            ProcessedMedia result = await processor.ProcessAsync(input, "image/jpeg", directory, CancellationToken.None);

            Assert.Equal(128, result.Width);
            Assert.Equal(96, result.Height);
            Assert.True(File.Exists(result.Path));
            Assert.True(File.Exists(result.ThumbnailPath));
            string metadata = await RunAsync("/usr/bin/ffprobe", "-v", "error", "-show_entries", "format_tags=comment", "-of", "default=nw=1", result.Path);
            Assert.DoesNotContain("private-metadata", metadata, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDimensionsBeforePublishingOutput()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string input = Path.Combine(directory, "input.png");
            await RunAsync("/usr/bin/ffmpeg",
                "-nostdin", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96", "-frames:v", "1", input);
            MediaOptions options = Options();
            var processor = new FfmpegMediaProcessor(new MediaOptions
            {
                Enabled = true,
                Bucket = "test",
                MaximumImageWidth = 64,
                MaximumImageHeight = 64,
                FfmpegPath = options.FfmpegPath,
                FfprobePath = options.FfprobePath
            });

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                processor.ProcessAsync(input, "image/png", directory, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MediaOptions Options() => new()
    {
        Enabled = true,
        Bucket = "test",
        FfmpegPath = "/usr/bin/ffmpeg",
        FfprobePath = "/usr/bin/ffprobe",
        MaximumImageWidth = 1_024,
        MaximumImageHeight = 1_024
    };

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "activitypub-media-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> RunAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Test media process did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string stderr = await error;
        Assert.True(process.ExitCode == 0, stderr);
        return await output;
    }
}
