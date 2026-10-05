namespace SharedKernel.Idempotency.Abstractions;

/// <summary>
/// Selects the purposes a store is registered for: <c>p =&gt; p.ForRequests().ForMessages()</c>.
/// </summary>
/// <remarks>Passed to a provider's registration method; each selected purpose becomes one keyed registration.</remarks>
public sealed class IdempotencyPurposeSelection
{
    private readonly List<IdempotencyPurpose> _purposes = [];

    /// <summary>The selected purposes, in selection order, without duplicates.</summary>
    public IReadOnlyList<IdempotencyPurpose> Purposes => _purposes;

    /// <summary>Selects <see cref="IdempotencyPurpose.Request"/> — the application pipeline's command idempotency.</summary>
    /// <returns>This selection.</returns>
    public IdempotencyPurposeSelection ForRequests() => For(IdempotencyPurpose.Request);

    /// <summary>Selects <see cref="IdempotencyPurpose.Message"/> — message-consumer deduplication.</summary>
    /// <returns>This selection.</returns>
    public IdempotencyPurposeSelection ForMessages() => For(IdempotencyPurpose.Message);

    /// <summary>Selects <paramref name="purpose"/>. Selecting a purpose twice has no further effect.</summary>
    /// <param name="purpose">The purpose to select.</param>
    /// <returns>This selection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    public IdempotencyPurposeSelection For(IdempotencyPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown idempotency purpose.");

        if (!_purposes.Contains(purpose))
            _purposes.Add(purpose);

        return this;
    }
}
