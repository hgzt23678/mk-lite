using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class ExternalEntityIdTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExternalIdsCannotReferenceAnEmptyInternalIdentifier()
    {
        Assert.Throws<DomainException>(() => ExternalEntityId.Create(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            Guid.Empty,
            "1",
            1,
            Now));
    }

    [Fact]
    public void RetirementIsIdempotentAndCannotPrecedeCreation()
    {
        ExternalEntityId mapping = ExternalEntityId.Create(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            Guid.NewGuid(),
            "00abc123zz",
            42,
            Now);

        Assert.Throws<DomainException>(() => mapping.Retire(Now.AddTicks(-1)));
        mapping.Retire(Now.AddDays(1));
        mapping.Retire(Now.AddDays(2));

        Assert.Equal(Now.AddDays(1), mapping.RetiredAt);
    }
}
