using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Identifiers;
using Xunit;

namespace SharedKernel.Primitives.Tests.Identifiers;

public sealed class UuidV7IdGeneratorTests
{
    // ---- NewId basic shape ----

    [Fact]
    public void NewId_ReturnsNonEmptyGuid()
    {
        IIdGenerator generator = new UuidV7IdGenerator();

        Guid id = generator.NewId();

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void NewId_ReturnsDifferentValuesOnSuccessiveCalls()
    {
        IIdGenerator generator = new UuidV7IdGenerator();

        Guid first = generator.NewId();
        Guid second = generator.NewId();

        Assert.NotEqual(first, second);
    }

    // ---- Uniqueness across a large batch ----

    [Fact]
    public void NewId_GeneratesUniqueValuesAcrossLargeBatch_NoCollisions()
    {
        IIdGenerator generator = new UuidV7IdGenerator();
        const int batchSize = 10_000;

        var ids = new HashSet<Guid>(batchSize);
        for (var i = 0; i < batchSize; i++)
        {
            Assert.True(ids.Add(generator.NewId()), "Detected a colliding Guid within the batch.");
        }

        Assert.Equal(batchSize, ids.Count);
    }

    // ---- Sequential monotonicity (Postgres/SQL Server index-locality rationale) ----
    //
    // Guid.CreateVersion7() fills everything below the 48-bit millisecond timestamp with
    // cryptographically random bits (RFC 9562's "random" sub-method, not "monotonic random"),
    // so two values generated within the *same* millisecond carry no ordering guarantee against
    // each other — only values whose embedded timestamps actually differ are guaranteed to
    // compare as non-decreasing. This is still exactly what restores B-tree insert locality:
    // real production inserts are spread over many milliseconds, and same-millisecond ties still
    // land immediately adjacent in the index regardless of the random tie-break. The test below
    // asserts ordering precisely at that guaranteed granularity — across distinct timestamps —
    // rather than asserting a stronger guarantee the BCL implementation does not make.

    [Fact]
    public void NewId_AcrossDistinctTimestamps_CompareAsNonDecreasing()
    {
        IIdGenerator generator = new UuidV7IdGenerator();
        const int sampleSize = 30;

        var ids = new List<Guid>(sampleSize);
        ulong? lastTimestampMs = null;

        while (ids.Count < sampleSize)
        {
            Guid id = generator.NewId();
            ulong timestampMs = ExtractUnixTimestampMilliseconds(id);

            if (lastTimestampMs is null || timestampMs != lastTimestampMs)
            {
                ids.Add(id);
                lastTimestampMs = timestampMs;
            }
        }

        for (var i = 1; i < ids.Count; i++)
        {
            Assert.True(
                ids[i].CompareTo(ids[i - 1]) > 0,
                $"Guid at index {i} ({ids[i]}) did not sort after its predecessor ({ids[i - 1]}) under CompareTo.");
            Assert.False(
                ids[i] < ids[i - 1],
                $"Guid at index {i} ({ids[i]}) compared less-than its predecessor ({ids[i - 1]}) under the `<` operator.");
        }
    }

    private static ulong ExtractUnixTimestampMilliseconds(Guid id)
    {
        // RFC 9562 UUID v7: the 48 most-significant bits (first 6 octets, big-endian) are the
        // Unix millisecond timestamp.
        byte[] bytes = id.ToByteArray(bigEndian: true);
        return ((ulong)bytes[0] << 40)
            | ((ulong)bytes[1] << 32)
            | ((ulong)bytes[2] << 24)
            | ((ulong)bytes[3] << 16)
            | ((ulong)bytes[4] << 8)
            | bytes[5];
    }

    [Fact]
    public void NewId_ProducesVersion7Guid()
    {
        IIdGenerator generator = new UuidV7IdGenerator();

        Guid id = generator.NewId();
        byte[] bytes = id.ToByteArray(bigEndian: true);

        // RFC 9562: the 4 most-significant bits of octet 6 encode the version number.
        int version = (bytes[6] & 0xF0) >> 4;
        Assert.Equal(7, version);
    }

    // ---- Plain-AddSingleton DI registration (no package-owned extension) ----

    [Fact]
    public void IIdGenerator_ResolvesViaPlainAddSingletonRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

        var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<IIdGenerator>();

        Assert.IsType<UuidV7IdGenerator>(generator);
    }

    [Fact]
    public void IIdGenerator_PlainAddSingletonRegistration_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

        var provider = services.BuildServiceProvider();
        var a = provider.GetRequiredService<IIdGenerator>();
        var b = provider.GetRequiredService<IIdGenerator>();

        Assert.Same(a, b);
    }
}
