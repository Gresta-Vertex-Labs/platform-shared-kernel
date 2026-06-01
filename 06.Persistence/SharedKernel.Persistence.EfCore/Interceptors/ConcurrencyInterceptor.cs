using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that converts <see cref="DbUpdateConcurrencyException"/>
/// into a typed <see cref="ConflictException"/> for entities implementing
/// <see cref="IHasConcurrency"/>.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="SaveChangesFailedAsync"/> / <see cref="SaveChangesFailed"/>: when a
/// <see cref="DbUpdateConcurrencyException"/> is thrown and at least one of the conflicting
/// entries implements <see cref="IHasConcurrency"/>, this interceptor rethrows the exception
/// as a <see cref="ConflictException"/> carrying <see cref="Error.Conflict(string, string)"/>.
/// </para>
/// <para>
/// <strong>No retry logic.</strong> Conflict resolution is the application layer's
/// responsibility — the interceptor surfaces the conflict and stops. Callers that require
/// retry behaviour must implement it in a MediatR pipeline behavior or equivalent.
/// </para>
/// <para>
/// Non-concurrency exceptions and <see cref="DbUpdateConcurrencyException"/> instances that
/// do not involve <see cref="IHasConcurrency"/> entries propagate unchanged.
/// </para>
/// </remarks>
public sealed class ConcurrencyInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        HandleFailure(eventData);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        HandleFailure(eventData);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    // Rethrows as ConflictException when DbUpdateConcurrencyException affects IHasConcurrency entities.
    private static void HandleFailure(DbContextErrorEventData eventData)
    {
        if (eventData.Exception is not DbUpdateConcurrencyException concurrencyEx)
            return;

        var affectsHasConcurrency = concurrencyEx.Entries
            .Any(e => e.Entity is IHasConcurrency);

        if (!affectsHasConcurrency)
            return;

        throw new ConflictException(
            Error.Conflict(
                "persistence.concurrency_conflict",
                "A concurrency conflict occurred. The entity was modified by another process. Reload and retry."),
            concurrencyEx);
    }
}
