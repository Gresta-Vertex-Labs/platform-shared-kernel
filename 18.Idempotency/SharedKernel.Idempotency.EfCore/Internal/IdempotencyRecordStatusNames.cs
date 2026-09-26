using SharedKernel.Idempotency.EfCore.Entities;

namespace SharedKernel.Idempotency.EfCore.Internal;

/// <summary>
/// String literals matching <see cref="IdempotencyRecordStatus"/>'s default
/// <c>HasConversion&lt;string&gt;()</c> serialization (each member's own name via
/// <see cref="object.ToString"/>) — used by the raw-SQL upsert in
/// <see cref="Store.EfCoreIdempotencyStore"/>, which embeds these as SQL literals rather
/// than binding the typed enum value.
/// </summary>
internal static class IdempotencyRecordStatusNames
{
    /// <summary>Matches <see cref="IdempotencyRecordStatus.InProgress"/>.</summary>
    internal const string InProgress = nameof(IdempotencyRecordStatus.InProgress);

    /// <summary>Matches <see cref="IdempotencyRecordStatus.Completed"/>.</summary>
    internal const string Completed = nameof(IdempotencyRecordStatus.Completed);
}
