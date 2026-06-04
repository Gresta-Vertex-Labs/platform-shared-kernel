using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// A null-object <see cref="IOptionsMonitor{T}"/> that always returns a default-constructed
/// <typeparamref name="T"/> instance. Used as a fallback when the real monitor is not available
/// (e.g., when <c>WithEncryption()</c> has not been called).
/// </summary>
/// <typeparam name="T">The options type. Must have a parameterless constructor.</typeparam>
internal sealed class NullOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class, new()
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NullOptionsMonitor<T> Instance = new();

    private readonly T _value = new();

    private NullOptionsMonitor() { }

    /// <inheritdoc />
    public T CurrentValue => _value;

    /// <inheritdoc />
    public T Get(string? name) => _value;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
