using System.Text.Json;
using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyPageVariableUtilitiesTests
{
    [Fact]
    public void CollectPreservesNestedPageVariableKindsAndDefaults()
    {
        using JsonDocument document = JsonDocument.Parse("""
            [
              {"type":"textInput","name":"title","default":"hello"},
              {"type":"numberInput","name":"count","default":3},
              {"type":"switch","name":"enabled","default":true},
              {"type":"counter","name":"steps"},
              {"type":"section","children":[{"type":"radioButton","name":"mode","default":"a"}]}
            ]
            """);

        IReadOnlyList<MisskeyPageVariable> values = MisskeyPageVariableUtilities.Collect(document.RootElement);
        Assert.Equal(["title", "count", "enabled", "steps", "mode"], values.Select(value => value.Name));
        Assert.Equal(["string", "number", "boolean", "number", "string"], values.Select(value => value.Type));
        Assert.Equal("hello", values[0].Value);
        Assert.Equal(3d, values[1].Value);
        Assert.Equal(true, values[2].Value);
        Assert.Equal(0, values[3].Value);
    }
}
