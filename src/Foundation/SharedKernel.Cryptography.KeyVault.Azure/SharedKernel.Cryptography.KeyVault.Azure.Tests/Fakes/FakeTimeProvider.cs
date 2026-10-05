namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;

internal sealed class FakeTimeProvider : TimeProvider
{
    private long _utcTicks = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero).UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcTicks, delta.Ticks);
}
