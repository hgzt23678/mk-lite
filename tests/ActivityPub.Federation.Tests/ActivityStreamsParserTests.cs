using System.Text;
using System.Text.Json;
using ActivityPub.Domain;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Federation.Tests;

public sealed class ActivityStreamsParserTests
{
    [Fact]
    public void ParsesMisskeyCustomEmojiReactionAndTag()
    {
        byte[] json = """
            {
              "id": "https://remote.example/likes/01",
              "type": "Like",
              "actor": "https://remote.example/users/alice",
              "object": "https://local.example/objects/01",
              "_misskey_reaction": ":party:",
              "content": "ignored-by-priority",
              "tag": {
                "id": "https://remote.example/emojis/party",
                "type": ["Emoji", "UnknownExtension"],
                "name": ":party:",
                "icon": {
                  "type": "Image",
                  "mediaType": "image/webp",
                  "url": "https://cdn.remote.example/emoji/party.webp"
                }
              }
            }
            """u8.ToArray();

        ActivityStreamsDocument activity = ActivityStreamsParser.ParseActivity(json);
        FederatedReaction reaction = ActivityReactionParser.Parse(activity.Root, activity.ActorIri);

        Assert.Equal(":party@remote.example:", reaction.Value);
        Assert.Equal("https://remote.example/emojis/party", reaction.CustomEmojiIri);
        Assert.Equal("https://cdn.remote.example/emoji/party.webp", reaction.CustomEmojiUrl);
        Assert.Equal("image/webp", reaction.CustomEmojiMediaType);
    }

    [Theory]
    [InlineData("EmojiReaction")]
    [InlineData("EmojiReact")]
    public void AcceptsFediverseReactionActivityAliases(string type)
    {
        byte[] json = System.Text.Encoding.UTF8.GetBytes($$"""
            {
              "id": "https://remote.example/activities/reaction-01",
              "type": "{{type}}",
              "actor": "https://remote.example/users/alice",
              "object": "https://local.example/objects/01",
              "content": "🎉"
            }
            """);

        ActivityStreamsDocument activity = ActivityStreamsParser.ParseActivity(json);

        Assert.True(activity.IsSupportedActivity);
        Assert.Equal(type, activity.PrimaryType);
        Assert.Equal("🎉", ActivityReactionParser.Parse(activity.Root, activity.ActorIri).Value);
    }

    [Fact]
    public void QualifiesLitePubRemoteCustomEmojiFromTagOrigin()
    {
        byte[] json = """
            {
              "id": "https://akkoma.example/activities/reaction-02",
              "type": "EmojiReact",
              "actor": "https://akkoma.example/users/alice",
              "object": "https://local.example/objects/01",
              "content": ":blob:",
              "tag": [{
                "id": "https://emoji-origin.example/emojis/blob",
                "type": "Emoji",
                "name": ":blob:",
                "icon": {"type":"Image","url":"https://cdn.example/blob.png"}
              }]
            }
            """u8.ToArray();

        ActivityStreamsDocument activity = ActivityStreamsParser.ParseActivity(json);
        FederatedReaction reaction = ActivityReactionParser.Parse(activity.Root, activity.ActorIri);

        Assert.Equal(":blob@emoji-origin.example:", reaction.Value);
        Assert.Equal("https://emoji-origin.example/emojis/blob", reaction.CustomEmojiIri);
    }

    [Fact]
    public void ParsesStringAndArrayFormsAndPreservesExtensions()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {
              "@context": "https://www.w3.org/ns/activitystreams",
              "id": "https://remote.example/activities/1",
              "type": ["Create", "https://vendor.example/types/SpecialCreate"],
              "actor": { "type": "Link", "href": "https://remote.example/users/alice" },
              "object": {
                "id": "https://remote.example/objects/1",
                "type": ["Note", "https://vendor.example/types/SpecialNote"],
                "attributedTo": "https://remote.example/users/alice",
                "vendor:extension": { "kept": true }
              },
              "to": "https://www.w3.org/ns/activitystreams#Public",
              "cc": ["https://remote.example/users/alice/followers"]
            }
            """);

        ActivityStreamsDocument activity = ActivityStreamsParser.ParseActivity(json);

        Assert.Equal("Create", activity.PrimaryType);
        Assert.Equal(Visibility.Public, activity.Visibility);
        Assert.Equal(2, activity.Types.Count);
        Assert.True(activity.Root.GetProperty("object").TryGetProperty("vendor:extension", out _));
    }

    [Fact]
    public void PleromaObjectActorAndEmbeddedAudienceAreNormalizedForRouting()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {
              "id": "https://pleroma.example/activities/1",
              "type": "Create",
              "actor": "https://pleroma.example/users/alice",
              "object": {
                "id": "https://pleroma.example/objects/1",
                "type": "Note",
                "actor": "https://pleroma.example/users/alice",
                "to": "https://www.w3.org/ns/activitystreams#Public",
                "cc": "https://pleroma.example/users/alice/followers"
              }
            }
            """);

        ActivityStreamsDocument activity = ActivityStreamsParser.ParseActivity(json);

        Assert.Equal("https://pleroma.example/users/alice", activity.ObjectOwnerIri);
        Assert.Equal(Visibility.Public, activity.Visibility);
        Assert.Contains(activity.Audience, address =>
            address.Iri == "https://pleroma.example/users/alice/followers" &&
            address.Field == AudienceField.Cc);
    }

    [Fact]
    public void UnknownActivityIsAcceptedWithoutBeingMarkedSupported()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {
              "id": "https://remote.example/activities/unknown",
              "type": "VendorSpecificActivity",
              "actor": "https://remote.example/users/alice",
              "object": "https://remote.example/objects/1",
              "to": "https://local.example/users/bob"
            }
            """);

        ActivityStreamsDocument activity = ActivityStreamsParser.ParseActivity(json);

        Assert.False(activity.IsSupportedActivity);
        Assert.Equal("VendorSpecificActivity", activity.PrimaryType);
        Assert.Equal(Visibility.MentionedOnly, activity.Visibility);
    }

    [Fact]
    public void RejectsCrossOriginMutation()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {
              "id": "https://remote.example/activities/2",
              "type": "Update",
              "actor": "https://remote.example/users/alice",
              "object": {
                "id": "https://victim.example/objects/1",
                "type": "Note",
                "attributedTo": "https://remote.example/users/alice"
              },
              "to": "https://local.example/users/bob"
            }
            """);

        Assert.Throws<ActivityStreamsProtocolException>(() => ActivityStreamsParser.ParseActivity(json));
    }

    [Fact]
    public void RejectsDuplicateJsonPropertiesBeforeParsing()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {
              "id": "https://remote.example/activities/3",
              "id": "https://remote.example/activities/replaced",
              "type": "Create",
              "actor": "https://remote.example/users/alice"
            }
            """);

        Assert.Throws<ActivityStreamsProtocolException>(() => ActivityStreamsParser.ParseActivity(json));
    }

    [Fact]
    public void DeliverySerializationRemovesBlindRecipientsRecursively()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "id": "https://remote.example/activities/4",
              "type": "Create",
              "bto": "https://remote.example/users/private",
              "object": {
                "type": "Note",
                "bcc": ["https://remote.example/users/secret"]
              }
            }
            """);

        byte[] result = ActivityStreamsSerializer.StripBlindRecipientsAndSerialize(document.RootElement);
        using JsonDocument serialized = JsonDocument.Parse(result);

        Assert.False(serialized.RootElement.TryGetProperty("bto", out _));
        Assert.False(serialized.RootElement.GetProperty("object").TryGetProperty("bcc", out _));
    }
}
