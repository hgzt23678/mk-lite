using System.Text;
using ActivityPub.Federation.Protocol;
using SharpFuzz;

const int MaximumInputBytes = 1_048_576;
string target = Environment.GetEnvironmentVariable("ACTIVITYPUB_FUZZ_TARGET") ?? "activitystreams";

Fuzzer.OutOfProcess.Run(stream =>
{
    byte[] input = ReadBounded(stream, MaximumInputBytes);
    switch (target)
    {
        case "activitystreams":
            FuzzActivityStreams(input);
            break;
        case "html":
            _ = new IncomingHtmlSanitizer().Sanitize(Encoding.UTF8.GetString(input));
            break;
        default:
            throw new InvalidOperationException(
                "ACTIVITYPUB_FUZZ_TARGET must be either 'activitystreams' or 'html'.");
    }
});

static byte[] ReadBounded(Stream stream, int maximumBytes)
{
    byte[] buffer = new byte[maximumBytes + 1];
    int length = 0;
    while (length < buffer.Length)
    {
        int read = stream.Read(buffer, length, buffer.Length - length);
        if (read == 0)
        {
            break;
        }

        length += read;
    }

    return length > maximumBytes ? [] : buffer[..length];
}

static void FuzzActivityStreams(byte[] input)
{
    if (input.Length == 0)
    {
        return;
    }

    try
    {
        ActivityStreamsDocument parsed = ActivityStreamsParser.ParseActivity(input);
        _ = ActivityStreamsSerializer.StripBlindRecipientsAndSerialize(parsed.Root);
    }
    catch (ActivityStreamsProtocolException)
    {
        // Protocol rejection is an expected result; other exceptions remain fuzz crashes.
    }
}
