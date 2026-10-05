using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Testing.Notifications;

namespace SharedKernel.Testing.SelfTests.Notifications;

/// <summary>
/// Proves <see cref="InMemoryNotificationDeliveryObserver"/> against
/// <c>INotificationDeliveryObserver</c>'s documented contract — no consuming domain has adopted
/// this fake yet, so this self-test is the only behavioral proof today, per the SelfTests routing
/// rule.
/// </summary>
public sealed class InMemoryNotificationDeliveryObserverTests
{
    private static NotificationDeliveryContext CreateContext(Guid? deliveryId = null) => new(
        deliveryId ?? Guid.NewGuid(),
        NotificationChannel.Email,
        "test@example.com",
        "receipt-template");

    [Fact]
    public async Task OnAttemptAsync_RecordsAttempt()
    {
        var observer = new InMemoryNotificationDeliveryObserver();
        var context = CreateContext();

        await observer.OnAttemptAsync(context, 1, CancellationToken.None);

        Assert.Single(observer.Attempts);
        Assert.Equal(context, observer.Attempts[0].Context);
        Assert.Equal(1, observer.Attempts[0].AttemptNumber);
    }

    [Fact]
    public async Task OnCompletedAsync_RecordsCompletion()
    {
        var observer = new InMemoryNotificationDeliveryObserver();
        var context = CreateContext();
        var result = new NotificationDeliveryResult(context.NotificationDeliveryId, true, "provider-id", null);

        await observer.OnCompletedAsync(context, result, CancellationToken.None);

        Assert.Single(observer.Completions);
        Assert.Equal(result, observer.Completions[0].Result);
    }

    [Fact]
    public async Task ShouldHaveSucceeded_SuccessfulCompletion_ReturnsResult()
    {
        var observer = new InMemoryNotificationDeliveryObserver();
        var context = CreateContext();
        var result = new NotificationDeliveryResult(context.NotificationDeliveryId, true, "provider-id", null);
        await observer.OnCompletedAsync(context, result, CancellationToken.None);

        var found = observer.ShouldHaveSucceeded(context.NotificationDeliveryId);

        Assert.Equal(result, found);
    }

    [Fact]
    public async Task ShouldHaveSucceeded_FailedCompletion_Throws()
    {
        var observer = new InMemoryNotificationDeliveryObserver();
        var context = CreateContext();
        var result = new NotificationDeliveryResult(context.NotificationDeliveryId, false, null, "boom");
        await observer.OnCompletedAsync(context, result, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => observer.ShouldHaveSucceeded(context.NotificationDeliveryId));
    }

    [Fact]
    public async Task ShouldHaveFailed_FailedCompletion_ReturnsResult()
    {
        var observer = new InMemoryNotificationDeliveryObserver();
        var context = CreateContext();
        var result = new NotificationDeliveryResult(context.NotificationDeliveryId, false, null, "boom");
        await observer.OnCompletedAsync(context, result, CancellationToken.None);

        var found = observer.ShouldHaveFailed(context.NotificationDeliveryId);

        Assert.Equal(result, found);
    }

    [Fact]
    public async Task ShouldHaveFailed_SuccessfulCompletion_Throws()
    {
        var observer = new InMemoryNotificationDeliveryObserver();
        var context = CreateContext();
        var result = new NotificationDeliveryResult(context.NotificationDeliveryId, true, "provider-id", null);
        await observer.OnCompletedAsync(context, result, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => observer.ShouldHaveFailed(context.NotificationDeliveryId));
    }

    [Fact]
    public void ShouldHaveSucceeded_NoCompletion_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new InMemoryNotificationDeliveryObserver().ShouldHaveSucceeded(Guid.NewGuid()));

    [Fact]
    public async Task OnAttemptAsync_NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new InMemoryNotificationDeliveryObserver().OnAttemptAsync(null!, 1, CancellationToken.None));
}
