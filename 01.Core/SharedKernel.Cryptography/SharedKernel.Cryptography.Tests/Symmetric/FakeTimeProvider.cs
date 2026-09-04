namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Minimal hand-rolled <see cref="TimeProvider"/> fake — avoids adding
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> as a new test-project NuGet dependency for
/// what is just one overridden method plus a mutable "now" field. Mirrors
/// <c>SharedKernel.Primitives.Tests.Clocks.SystemClockTimeProviderTests</c>'s private fake.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
