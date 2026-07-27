namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// Abstracts the current system time. Inject <see cref="IClock"/> instead of using
/// <c>DateTime.UtcNow</c> or <c>DateTimeOffset.UtcNow</c> directly — this keeps time
/// deterministic in tests.
/// </summary>
/// <remarks>
/// <para>
/// Register the production implementation with:
/// <code>services.AddSingleton&lt;IClock, SystemClock&gt;();</code>
/// or via the convenience extension <c>services.AddClock()</c> (available in SharedKernel.Primitives).
/// </para>
/// <para>
/// In tests, substitute a fake implementation that returns a fixed time.
/// </para>
/// <para>
/// <see cref="SystemClock"/> internally sources <see cref="UtcNow"/> from an injected
/// <see cref="System.TimeProvider"/> instead of calling <see cref="DateTimeOffset.UtcNow"/>
/// directly (P-295). This is purely an internal implementation detail — this interface's own
/// contract (<see cref="UtcNow"/>, <see cref="Today"/>) is unaffected, and no call site outside
/// <see cref="SystemClock"/> itself should reference <see cref="System.TimeProvider"/> directly.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current UTC date and time as a <see cref="DateTimeOffset"/>.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Gets the current UTC date as a <see cref="DateOnly"/>.</summary>
    DateOnly Today { get; }
}
