using FluentAssertions;
using SharedKernel.Persistence.Npgsql.Coordination;

namespace SharedKernel.Persistence.Npgsql.Tests.Coordination;

/// <summary>
/// Pins <see cref="AdvisoryLockKeyHasher.Compute"/>'s exact FNV-1a 64-bit output
/// for a fixed set of inputs. <see cref="NpgsqlAdvisoryMigrationLock"/> and
/// <see cref="NpgsqlAdvisoryTransactionLock"/> share this ONE helper precisely so a session-level and
/// a transaction-level advisory lock always compute the same key for the same logical resource name —
/// these known vectors guard against a future accidental change to the algorithm silently breaking
/// that cross-lock-flavor (and cross-process/cross-replica) compatibility.
/// </summary>
public sealed class AdvisoryLockKeyHasherTests
{
    // Computed independently via a throwaway console probe running the identical algorithm, not
    // copied from the implementation under test.
    [Theory]
    [InlineData("", -3750763034362895579L)]
    [InlineData("a", -5808556873153909620L)]
    [InlineData("abc", -1792535898324117685L)]
    [InlineData("hello", -6615550055289275125L)]
    [InlineData("sk-audit-chain:test", 586530681944646865L)]
    [InlineData("sk-migration-lock:orders-db", 557900692012111142L)]
    public void Compute_KnownVector_ProducesThePinnedHash(string input, long expected)
    {
        AdvisoryLockKeyHasher.Compute(input).Should().Be(expected);
    }

    [Fact]
    public void Compute_SameInput_IsDeterministicAcrossCalls()
    {
        const string lockKey = "sk-repeatable-key";

        AdvisoryLockKeyHasher.Compute(lockKey).Should().Be(AdvisoryLockKeyHasher.Compute(lockKey));
    }

    [Fact]
    public void Compute_DifferentInputs_ProduceDifferentHashes()
    {
        AdvisoryLockKeyHasher.Compute("lock-a").Should().NotBe(AdvisoryLockKeyHasher.Compute("lock-b"));
    }
}
