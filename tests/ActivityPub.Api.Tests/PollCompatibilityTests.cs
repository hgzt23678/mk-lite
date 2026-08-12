using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class PollCompatibilityTests(ActivityPubApiFixture fixture)
{
    private static readonly string[] LocalPollChoices = ["紅茶", "コーヒー"];
    private static readonly int[] BothChoiceIndexes = [0, 1];

    private readonly HttpClient client = CreateClient(fixture, authenticated: true);

    [Fact]
    public async Task CreatePollPersistsQuestionOptionsAndReturnsThePinnedMisskeyContract()
    {
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        using HttpResponseMessage response = await SendCommandAsync(
            client,
            "/api/notes/create",
            "poll-create-" + Guid.NewGuid().ToString("N"),
            new
            {
                text = "永続化されるアンケート",
                visibility = "public",
                poll = new
                {
                    choices = LocalPollChoices,
                    multiple = false,
                    expiresAt = expiresAt.ToUnixTimeMilliseconds()
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement created = json.RootElement.GetProperty("createdNote");
        string noteId = created.GetProperty("id").GetString()!;
        JsonElement poll = created.GetProperty("poll");
        Assert.False(poll.GetProperty("multiple").GetBoolean());
        Assert.Equal(expiresAt.ToUnixTimeMilliseconds(), poll.GetProperty("expiresAt").GetDateTimeOffset().ToUnixTimeMilliseconds());
        JsonElement[] choices = poll.GetProperty("choices").EnumerateArray().ToArray();
        Assert.Equal(LocalPollChoices, choices.Select(value => value.GetProperty("text").GetString()));
        Assert.All(choices, value =>
        {
            Assert.Equal(0, value.GetProperty("votes").GetInt64());
            Assert.False(value.GetProperty("isVoted").GetBoolean());
        });

        Guid objectId = await ResolveMisskeyIdAsync(noteId);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        FederatedObject question = await db.Objects.AsNoTracking().SingleAsync(value => value.Id == objectId);
        QuestionPoll storedPoll = await db.QuestionPolls.AsNoTracking().SingleAsync(value => value.QuestionObjectId == objectId);
        PollOption[] storedChoices = await db.PollOptions.AsNoTracking()
            .Where(value => value.PollId == storedPoll.Id)
            .OrderBy(value => value.ChoiceIndex)
            .ToArrayAsync();
        Assert.Equal("Question", question.Type);
        Assert.False(storedPoll.Multiple);
        Assert.Equal(expiresAt.ToUnixTimeMilliseconds(), storedPoll.ExpiresAt?.ToUnixTimeMilliseconds());
        Assert.Equal(LocalPollChoices, storedChoices.Select(value => value.Title));
        using JsonDocument storedJson = JsonDocument.Parse(question.RawJson);
        Assert.True(storedJson.RootElement.TryGetProperty("oneOf", out _));
        Assert.False(storedJson.RootElement.TryGetProperty("anyOf", out _));
        Assert.Single(await db.StreamEvents.AsNoTracking().Where(value =>
            value.ResourceId == objectId && value.Kind == StreamEventKind.PostCreated).ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentSingleChoiceVotesCommitOneBallotDeliveryAndStreamEvent()
    {
        RemotePoll target = await AddRemotePollAsync(multiple: false, Visibility.Public, DateTimeOffset.UtcNow.AddHours(1));
        using HttpRequestMessage firstRequest = Command(
            "/api/notes/polls/vote",
            "poll-vote-a-" + Guid.NewGuid().ToString("N"),
            new { noteId = target.ExternalId, choice = 0 });
        using HttpRequestMessage secondRequest = Command(
            "/api/notes/polls/vote",
            "poll-vote-b-" + Guid.NewGuid().ToString("N"),
            new { noteId = target.ExternalId, choice = 1 });

        HttpResponseMessage[] responses = await Task.WhenAll(
            client.SendAsync(firstRequest),
            client.SendAsync(secondRequest));
        try
        {
            Assert.Single(responses, value => value.StatusCode == HttpStatusCode.NoContent);
            HttpResponseMessage conflict = Assert.Single(responses, value => value.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal("ALREADY_VOTED", await ReadErrorCodeAsync(conflict));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        QuestionPoll poll = await db.QuestionPolls.AsNoTracking().SingleAsync(value => value.QuestionObjectId == target.ObjectId);
        PollVote vote = await db.PollVotes.AsNoTracking().SingleAsync(value => value.PollId == poll.Id);
        Assert.Equal(-1, vote.BallotKey);
        ActivityRecord activity = await db.Activities.AsNoTracking().SingleAsync(value => value.Iri == vote.ActivityIri);
        Assert.Equal("Create", activity.Type);
        Assert.NotNull(activity.ObjectIri);
        Assert.False(await db.Objects.AsNoTracking().AnyAsync(value => value.Iri == activity.ObjectIri));
        Assert.DoesNotContain("_activitypubServerChoiceIndex", activity.RawJson, StringComparison.Ordinal);
        Delivery delivery = await db.Deliveries.AsNoTracking().SingleAsync(value => value.ActivityId == activity.Id);
        Assert.Equal("https://media-blocked.example/inbox", delivery.EndpointIri);
        Assert.Equal(activity.PayloadHash, PayloadDigest.Sha256Hex(delivery.Payload));
        using JsonDocument payload = JsonDocument.Parse(delivery.Payload);
        JsonElement deliveredVote = payload.RootElement.GetProperty("object");
        Assert.Equal("Note", deliveredVote.GetProperty("type").GetString());
        Assert.Equal(activity.ObjectIri, deliveredVote.GetProperty("id").GetString());
        Assert.Equal(target.Iri, deliveredVote.GetProperty("inReplyTo").GetString());
        Assert.Equal("https://media-blocked.example/users/publisher", deliveredVote.GetProperty("to").GetString());
        Assert.False(deliveredVote.TryGetProperty("_activitypubServerChoiceIndex", out _));
        StreamEvent streamEvent = await db.StreamEvents.AsNoTracking().SingleAsync(value =>
            value.ResourceId == target.ObjectId && value.Kind == StreamEventKind.PollVoted);
        Assert.Equal(vote.ChoiceIndex, streamEvent.PollChoiceIndex);

        using HttpResponseMessage shown = await client.PostAsJsonAsync("/api/notes/show", new { noteId = target.ExternalId });
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        using JsonDocument shownJson = await JsonDocument.ParseAsync(await shown.Content.ReadAsStreamAsync());
        JsonElement[] choices = shownJson.RootElement.GetProperty("poll").GetProperty("choices").EnumerateArray().ToArray();
        Assert.True(choices[vote.ChoiceIndex].GetProperty("isVoted").GetBoolean());
        Assert.Equal(1, choices[vote.ChoiceIndex].GetProperty("votes").GetInt64());
    }

    [Fact]
    public async Task MultipleChoicePollAllowsDifferentChoicesButRejectsTheSameChoiceTwice()
    {
        RemotePoll target = await AddRemotePollAsync(multiple: true, Visibility.Public, DateTimeOffset.UtcNow.AddHours(1));
        using HttpResponseMessage first = await SendCommandAsync(
            client,
            "/api/notes/polls/vote",
            "multi-vote-a-" + Guid.NewGuid().ToString("N"),
            new { noteId = target.ExternalId, choice = 0 });
        using HttpResponseMessage second = await SendCommandAsync(
            client,
            "/api/notes/polls/vote",
            "multi-vote-b-" + Guid.NewGuid().ToString("N"),
            new { noteId = target.ExternalId, choice = 1 });
        using HttpResponseMessage duplicate = await SendCommandAsync(
            client,
            "/api/notes/polls/vote",
            "multi-vote-c-" + Guid.NewGuid().ToString("N"),
            new { noteId = target.ExternalId, choice = 1 });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("ALREADY_VOTED", await ReadErrorCodeAsync(duplicate));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        PollVote[] votes = await db.PollVotes.AsNoTracking()
            .Where(value => value.PollId == target.ObjectId)
            .OrderBy(value => value.ChoiceIndex)
            .ToArrayAsync();
        Assert.Equal(BothChoiceIndexes, votes.Select(value => value.ChoiceIndex));
        Assert.Equal(BothChoiceIndexes, votes.Select(value => value.BallotKey));

        using HttpResponseMessage shown = await client.PostAsJsonAsync("/api/notes/show", new { noteId = target.ExternalId });
        using JsonDocument shownJson = await JsonDocument.ParseAsync(await shown.Content.ReadAsStreamAsync());
        JsonElement[] choices = shownJson.RootElement.GetProperty("poll").GetProperty("choices").EnumerateArray().ToArray();
        Assert.All(choices, choice =>
        {
            Assert.True(choice.GetProperty("isVoted").GetBoolean());
            Assert.Equal(1, choice.GetProperty("votes").GetInt64());
        });
    }

    [Fact]
    public async Task ExpiredAndInvisiblePollsDoNotCreateBallotsActivitiesOrDeliveries()
    {
        RemotePoll expired = await AddRemotePollAsync(multiple: false, Visibility.Public, DateTimeOffset.UtcNow.AddMinutes(-1));
        RemotePoll invisible = await AddRemotePollAsync(multiple: false, Visibility.MentionedOnly, DateTimeOffset.UtcNow.AddHours(1));
        int activityCountBefore;
        int deliveryCountBefore;
        await using (AsyncServiceScope beforeScope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = beforeScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            activityCountBefore = await db.Activities.CountAsync();
            deliveryCountBefore = await db.Deliveries.CountAsync();
        }

        using HttpResponseMessage expiredResponse = await SendCommandAsync(
            client,
            "/api/notes/polls/vote",
            "expired-vote-" + Guid.NewGuid().ToString("N"),
            new { noteId = expired.ExternalId, choice = 0 });
        using HttpResponseMessage invisibleResponse = await SendCommandAsync(
            client,
            "/api/notes/polls/vote",
            "private-vote-" + Guid.NewGuid().ToString("N"),
            new { noteId = invisible.ExternalId, choice = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, expiredResponse.StatusCode);
        Assert.Equal("ALREADY_EXPIRED", await ReadErrorCodeAsync(expiredResponse));
        Assert.Equal(HttpStatusCode.NotFound, invisibleResponse.StatusCode);
        Assert.Equal("NO_SUCH_NOTE", await ReadErrorCodeAsync(invisibleResponse));

        using HttpClient anonymous = CreateClient(fixture, authenticated: false);
        using HttpResponseMessage hidden = await anonymous.PostAsJsonAsync("/api/notes/show", new { noteId = invisible.ExternalId });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> verificationFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await verificationFactory.CreateDbContextAsync();
        Assert.Empty(await verification.PollVotes.Where(value =>
            value.PollId == expired.ObjectId || value.PollId == invisible.ObjectId).ToArrayAsync());
        Assert.Equal(activityCountBefore, await verification.Activities.CountAsync());
        Assert.Equal(deliveryCountBefore, await verification.Deliveries.CountAsync());
    }

    private async Task<RemotePoll> AddRemotePollAsync(
        bool multiple,
        Visibility visibility,
        DateTimeOffset expiresAt)
    {
        string iri = $"https://media-blocked.example/objects/poll-{Guid.NewGuid():N}";
        string choiceProperty = multiple ? "anyOf" : "oneOf";
        var root = new Dictionary<string, object?>
        {
            ["id"] = iri,
            ["type"] = "Question",
            ["attributedTo"] = "https://media-blocked.example/users/publisher",
            ["content"] = "federated poll fixture",
            [choiceProperty] = new object[]
            {
                new { type = "Note", name = "alpha", replies = new { type = "Collection", totalItems = 0 } },
                new { type = "Note", name = "beta", replies = new { type = "Collection", totalItems = 0 } }
            },
            ["endTime"] = expiresAt,
            ["votersCount"] = 0,
            ["to"] = visibility == Visibility.Public
                ? "https://www.w3.org/ns/activitystreams#Public"
                : "https://remote.example/users/bob"
        };
        string raw = JsonSerializer.Serialize(root);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FederatedObject item = FederatedObject.Create(
            iri,
            "https://media-blocked.example/users/publisher",
            "Question",
            visibility,
            raw,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(raw)),
            now,
            now);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        db.Objects.Add(item);
        await db.SaveChangesAsync();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        string externalId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            item.Id,
            item.PublishedAt,
            CancellationToken.None);
        return new(item.Id, item.Iri, externalId);
    }

    private async Task<Guid> ResolveMisskeyIdAsync(string externalId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        return await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            externalId,
            CancellationToken.None)
            ?? throw new InvalidOperationException("The response contained an unresolvable Misskey identifier.");
    }

    private static async Task<HttpResponseMessage> SendCommandAsync<T>(
        HttpClient target,
        string path,
        string idempotencyKey,
        T body)
    {
        using HttpRequestMessage request = Command(path, idempotencyKey, body);
        return await target.SendAsync(request);
    }

    private static HttpRequestMessage Command<T>(string path, string idempotencyKey, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    private static HttpClient CreateClient(ActivityPubApiFixture source, bool authenticated)
    {
        HttpClient result = source.CreateClient(new()
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
        if (authenticated)
        {
            result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-alice");
        }

        return result;
    }

    private sealed record RemotePoll(Guid ObjectId, string Iri, string ExternalId);
}
