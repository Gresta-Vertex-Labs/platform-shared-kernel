using System.Collections.Concurrent;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Observability;

namespace SharedKernel.Testing.Notifications;

/// <summary>
/// In-memory test double for <see cref="INotificationDeliveryObserver"/>. Records every
/// <see cref="OnAttemptAsync"/>/<see cref="OnCompletedAsync"/> call for later assertion.
/// </summary>
/// <remarks>
/// Mirrors <c>InMemoryWebhookDeliveryObserver</c>'s exact shape. Records every call — including
/// when no assertion is ever made — into thread-safe collections; <c>Should*</c> assertion helpers
/// are read-only queries over those collections and never mutate them.
/// </remarks>
public sealed class InMemoryNotificationDeliveryObserver : INotificationDeliveryObserver
{
    private readonly ConcurrentQueue<(NotificationDeliveryContext Context, int AttemptNumber)> _attempts = new();
    private readonly ConcurrentQueue<(NotificationDeliveryContext Context, NotificationDeliveryResult Result)> _completions = new();

    /// <summary>Every <see cref="OnAttemptAsync"/> call recorded so far, in call order.</summary>
    public IReadOnlyList<(NotificationDeliveryContext Context, int AttemptNumber)> Attempts => [.. _attempts];

    /// <summary>Every <see cref="OnCompletedAsync"/> call recorded so far, in call order.</summary>
    public IReadOnlyList<(NotificationDeliveryContext Context, NotificationDeliveryResult Result)> Completions => [.. _completions];

    /// <inheritdoc />
    public Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        _attempts.Enqueue((context, attemptNumber));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);
        _completions.Enqueue((context, result));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the terminal <see cref="NotificationDeliveryResult"/> recorded for
    /// <paramref name="notificationDeliveryId"/> and asserts it succeeded.
    /// </summary>
    /// <param name="notificationDeliveryId">The delivery id to look up.</param>
    /// <exception cref="InvalidOperationException">No completion was recorded, or it did not succeed.</exception>
    public NotificationDeliveryResult ShouldHaveSucceeded(Guid notificationDeliveryId)
    {
        var result = FindCompletion(notificationDeliveryId);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Expected delivery '{notificationDeliveryId}' to have succeeded, but it failed with error '{result.Error}'.");
        }

        return result;
    }

    /// <summary>
    /// Returns the terminal <see cref="NotificationDeliveryResult"/> recorded for
    /// <paramref name="notificationDeliveryId"/> and asserts it failed.
    /// </summary>
    /// <param name="notificationDeliveryId">The delivery id to look up.</param>
    /// <exception cref="InvalidOperationException">No completion was recorded, or it succeeded.</exception>
    public NotificationDeliveryResult ShouldHaveFailed(Guid notificationDeliveryId)
    {
        var result = FindCompletion(notificationDeliveryId);
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Expected delivery '{notificationDeliveryId}' to have failed, but it succeeded.");
        }

        return result;
    }

    private NotificationDeliveryResult FindCompletion(Guid notificationDeliveryId)
    {
        foreach (var (_, result) in _completions)
        {
            if (result.NotificationDeliveryId == notificationDeliveryId)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"Expected a completed delivery for id '{notificationDeliveryId}' but none was recorded.");
    }
}
