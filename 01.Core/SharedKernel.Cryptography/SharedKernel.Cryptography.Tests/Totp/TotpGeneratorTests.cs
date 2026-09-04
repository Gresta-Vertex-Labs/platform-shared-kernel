using System.Text;
using SharedKernel.Cryptography.Tests.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Covers <see cref="TotpGenerator"/> (C-70/T-55/T-56) against RFC 6238 Appendix B's published
/// test vectors (SHA-1/SHA-256/SHA-512, 8-digit codes, step = 30s, at the RFC's documented
/// timestamps) plus the configurable clock-drift window behavior.
/// </summary>
public sealed class TotpGeneratorTests
{
    // RFC 6238 Appendix B: the seed is the ASCII digit pattern "1234567890" repeated and
    // truncated to 20 bytes (SHA1), 32 bytes (SHA256), or 64 bytes (SHA512) — NOT the same
    // 20-byte secret reused across all three algorithms. Generated programmatically here rather
    // than hardcoded to guarantee correctness against the RFC's own construction rule.
    private static byte[] Seed(int length) =>
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("1234567890", (length / 10) + 1))[..length]);

    private static readonly byte[] Seed20 = Seed(20);
    private static readonly byte[] Seed32 = Seed(32);
    private static readonly byte[] Seed64 = Seed(64);

    // RFC 6238 Appendix B published test vectors — 8-digit codes, 30-second step.
    public static TheoryData<long, HotpAlgorithm, byte[], string> Rfc6238Vectors()
    {
        var data = new TheoryData<long, HotpAlgorithm, byte[], string>
        {
            { 59L, HotpAlgorithm.Sha1, Seed20, "94287082" },
            { 59L, HotpAlgorithm.Sha256, Seed32, "46119246" },
            { 59L, HotpAlgorithm.Sha512, Seed64, "90693936" },
            { 1111111109L, HotpAlgorithm.Sha1, Seed20, "07081804" },
            { 1111111109L, HotpAlgorithm.Sha256, Seed32, "68084774" },
            { 1111111109L, HotpAlgorithm.Sha512, Seed64, "25091201" },
            { 1111111111L, HotpAlgorithm.Sha1, Seed20, "14050471" },
            { 1111111111L, HotpAlgorithm.Sha256, Seed32, "67062674" },
            { 1111111111L, HotpAlgorithm.Sha512, Seed64, "99943326" },
            { 1234567890L, HotpAlgorithm.Sha1, Seed20, "89005924" },
            { 1234567890L, HotpAlgorithm.Sha256, Seed32, "91819424" },
            { 1234567890L, HotpAlgorithm.Sha512, Seed64, "93441116" },
            { 2000000000L, HotpAlgorithm.Sha1, Seed20, "69279037" },
            { 2000000000L, HotpAlgorithm.Sha256, Seed32, "90698825" },
            { 2000000000L, HotpAlgorithm.Sha512, Seed64, "38618901" },
            { 20000000000L, HotpAlgorithm.Sha1, Seed20, "65353130" },
            { 20000000000L, HotpAlgorithm.Sha256, Seed32, "77737706" },
            { 20000000000L, HotpAlgorithm.Sha512, Seed64, "47863826" },
        };
        return data;
    }

    private static TotpGenerator NewGenerator(IClock? clock = null) =>
        new(new HotpGenerator(), clock ?? new SystemClock(new FakeTimeProvider(DateTimeOffset.UnixEpoch)));

    [Theory]
    [MemberData(nameof(Rfc6238Vectors))]
    public void GenerateCode_MatchesRfc6238AppendixBVectors(long unixSeconds, HotpAlgorithm algorithm, byte[] seed, string expectedCode)
    {
        TotpGenerator generator = NewGenerator();
        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        string actual = generator.GenerateCode(seed, timestamp, digits: 8, stepSeconds: 30, algorithm: algorithm);

        Assert.Equal(expectedCode, actual);
    }

    [Theory]
    [MemberData(nameof(Rfc6238Vectors))]
    public void ValidateCode_AcceptsTheMatchingRfc6238Vector(long unixSeconds, HotpAlgorithm algorithm, byte[] seed, string expectedCode)
    {
        TotpGenerator generator = NewGenerator();
        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        bool valid = generator.ValidateCode(seed, expectedCode, timestamp, digits: 8, stepSeconds: 30, driftWindow: 0, algorithm: algorithm);

        Assert.True(valid);
    }

    [Fact]
    public void GenerateCode_UsesInjectedClock_NeverRealWallClock()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(59));
        var clock = new SystemClock(timeProvider);
        TotpGenerator generator = NewGenerator(clock);

        string viaClock = generator.GenerateCode(Seed20, digits: 8, stepSeconds: 30);
        string viaExplicitTimestamp = generator.GenerateCode(Seed20, DateTimeOffset.FromUnixTimeSeconds(59), digits: 8, stepSeconds: 30);

        Assert.Equal("94287082", viaClock);
        Assert.Equal(viaExplicitTimestamp, viaClock);
    }

    [Fact]
    public void ValidateCode_CodeOneStepBeforeNow_AcceptedWithinDriftWindow()
    {
        // "now" is step 100 (t=3000s at a 30s step). Generate the code for step 99 (t=2970s) —
        // one step in the past — and confirm it validates against "now" with driftWindow=1.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        TotpGenerator generator = NewGenerator(clock);

        string previousStepCode = generator.GenerateCode(Seed20, DateTimeOffset.FromUnixTimeSeconds(2970), stepSeconds: 30);

        bool valid = generator.ValidateCode(Seed20, previousStepCode, stepSeconds: 30, driftWindow: 1);

        Assert.True(valid);
    }

    [Fact]
    public void ValidateCode_CodeOneStepAfterNow_AcceptedWithinDriftWindow()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        TotpGenerator generator = NewGenerator(clock);

        string nextStepCode = generator.GenerateCode(Seed20, DateTimeOffset.FromUnixTimeSeconds(3030), stepSeconds: 30);

        bool valid = generator.ValidateCode(Seed20, nextStepCode, stepSeconds: 30, driftWindow: 1);

        Assert.True(valid);
    }

    [Fact]
    public void ValidateCode_CodeTwoStepsAway_RejectedOutsideDriftWindow()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        TotpGenerator generator = NewGenerator(clock);

        // Two steps in the future (t=3060s) — outside the default driftWindow=1.
        string twoStepsAwayCode = generator.GenerateCode(Seed20, DateTimeOffset.FromUnixTimeSeconds(3060), stepSeconds: 30);

        bool valid = generator.ValidateCode(Seed20, twoStepsAwayCode, stepSeconds: 30, driftWindow: 1);

        Assert.False(valid);
    }

    [Fact]
    public void ValidateCode_WrongCode_Rejected()
    {
        TotpGenerator generator = NewGenerator();

        Assert.False(generator.ValidateCode(Seed20, "00000000", DateTimeOffset.FromUnixTimeSeconds(59), digits: 8));
    }

    [Fact]
    public void GenerateCode_NullSecret_Throws()
    {
        TotpGenerator generator = NewGenerator();

        Assert.Throws<ArgumentNullException>(() => generator.GenerateCode(null!));
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new TotpGenerator(null!, new SystemClock(new FakeTimeProvider(DateTimeOffset.UnixEpoch))));
        Assert.Throws<ArgumentNullException>(() => new TotpGenerator(new HotpGenerator(), null!));
    }
}
