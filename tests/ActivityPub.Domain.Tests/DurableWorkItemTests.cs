using System.Text;
using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class DurableWorkItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiredLeaseCanBeRecoveredByAnotherWorker()
    {
        Delivery delivery = CreateDelivery();
        delivery.AcquireLease("worker-a", Now, TimeSpan.FromMinutes(1));

        delivery.AcquireLease("worker-b", Now.AddMinutes(1), TimeSpan.FromMinutes(2));

        Assert.Equal("worker-b", delivery.LeaseOwner);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(WorkItemState.Leased, delivery.State);
    }

    [Fact]
    public void ActiveLeaseCannotBeStolen()
    {
        Delivery delivery = CreateDelivery();
        delivery.AcquireLease("worker-a", Now, TimeSpan.FromMinutes(1));

        Assert.Throws<DomainException>(() =>
            delivery.AcquireLease("worker-b", Now.AddSeconds(59), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void DomainSlotDeferralDoesNotConsumeAnAttempt()
    {
        Delivery delivery = CreateDelivery();
        delivery.AcquireLease("worker-a", Now, TimeSpan.FromMinutes(1));

        delivery.ReleaseLeaseWithoutAttempt("worker-a", Now, Now.AddSeconds(10));

        Assert.Equal(0, delivery.AttemptCount);
        Assert.Equal(WorkItemState.Pending, delivery.State);
        Assert.Equal(Now.AddSeconds(10), delivery.AvailableAt);
    }

    [Fact]
    public void DeadLetterCanBeManuallyRequeued()
    {
        Delivery delivery = CreateDelivery();
        delivery.AcquireLease("worker-a", Now, TimeSpan.FromMinutes(1));
        delivery.DeadLetter("worker-a", Now.AddSeconds(1), "http-400", "permanent rejection");

        delivery.RequeueFromDeadLetter(Now.AddMinutes(1));

        Assert.Equal(WorkItemState.Pending, delivery.State);
        Assert.Null(delivery.CompletedAt);
        Assert.Null(delivery.LastErrorCode);
    }

    private static Delivery CreateDelivery() => Delivery.Create(
        Guid.NewGuid(),
        "https://local.example/activities/8ac15e9a-84a2-43e7-92da-42503581d1c7",
        "https://remote.example/inbox",
        "https://local.example/users/alice",
        Encoding.UTF8.GetBytes("{\"type\":\"Create\"}"),
        SignatureProfile.LegacyCavage,
        Now);
}
