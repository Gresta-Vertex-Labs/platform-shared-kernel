using System.Text;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class TotpGeneratorTests
{
    private static readonly byte[] Sha1Seed = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly byte[] Sha256Seed = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    private static readonly byte[] Sha512Seed = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_015);

    private readonly FakeClock _clock = new(Now);
    private readonly TotpGenerator _generator;

    public TotpGeneratorTests()
    {
        _generator = new TotpGenerator(_clock);
    }

    [Theory]
    [InlineData(59L, "94287082", "46119246", "90693936")]
    [InlineData(1111111109L, "07081804", "68084774", "25091201")]
    [InlineData(1111111111L, "14050471", "67062674", "99943326")]
    [InlineData(1234567890L, "89005924", "91819424", "93441116")]
    [InlineData(2000000000L, "69279037", "90698825", "38618901")]
    [InlineData(20000000000L, "65353130", "77737706", "47863826")]
    public void GenerateCode_Rfc6238AppendixB_MatchesExpected(long unixSeconds, string sha1, string sha256, string sha512)
    {
        DateTimeOffset time = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        Assert.Equal(sha1, _generator.GenerateCode(Sha1Seed, time, new TotpParameters { Digits = 8, Algorithm = HotpAlgorithm.Sha1 }));
        Assert.Equal(sha256, _generator.GenerateCode(Sha256Seed, time, new TotpParameters { Digits = 8, Algorithm = HotpAlgorithm.Sha256 }));
        Assert.Equal(sha512, _generator.GenerateCode(Sha512Seed, time, new TotpParameters { Digits = 8, Algorithm = HotpAlgorithm.Sha512 }));
    }

    [Theory]
    [InlineData(59L, "94287082", HotpAlgorithm.Sha1)]
    [InlineData(1234567890L, "91819424", HotpAlgorithm.Sha256)]
    [InlineData(20000000000L, "47863826", HotpAlgorithm.Sha512)]
    public void TryValidateCode_Rfc6238Vector_MatchesExactStep(long unixSeconds, string code, HotpAlgorithm algorithm)
    {
        byte[] seed = algorithm switch
        {
            HotpAlgorithm.Sha1 => Sha1Seed,
            HotpAlgorithm.Sha256 => Sha256Seed,
            _ => Sha512Seed,
        };
        var parameters = new TotpParameters { Digits = 8, Algorithm = algorithm };

        Assert.True(_generator.TryValidateCode(seed, code, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), out long step, parameters));
        Assert.Equal(unixSeconds / 30, step);
    }

    [Fact]
    public void GenerateCode_WithoutTimestamp_UsesClock()
    {
        Assert.Equal(_generator.GenerateCode(Sha1Seed, Now), _generator.GenerateCode(Sha1Seed));

        _clock.UtcNow = Now.AddSeconds(30);

        Assert.Equal(_generator.GenerateCode(Sha1Seed, Now.AddSeconds(30)), _generator.GenerateCode(Sha1Seed));
    }

    [Fact]
    public void GenerateCode_NullParameters_UsesDefaults()
    {
        Assert.Equal(_generator.GenerateCode(Sha1Seed, Now, TotpParameters.Default), _generator.GenerateCode(Sha1Seed, Now, null));
        Assert.Equal(6, _generator.GenerateCode(Sha1Seed, Now).Length);
    }

    [Fact]
    public void GenerateCode_SameStep_ReturnsSameCode()
    {
        DateTimeOffset stepStart = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);

        Assert.Equal(_generator.GenerateCode(Sha1Seed, stepStart), _generator.GenerateCode(Sha1Seed, stepStart.AddSeconds(29)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public void TryValidateCode_CodeWithinDriftWindow_ReturnsMatchedStep(int offset)
    {
        long current = Now.ToUnixTimeSeconds() / 30;
        string code = _generator.GenerateCode(Sha1Seed, Now.AddSeconds(30 * offset));

        Assert.True(_generator.TryValidateCode(Sha1Seed, code, Now, out long step));
        Assert.Equal(current + offset, step);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(10)]
    public void TryValidateCode_CodeOutsideDriftWindow_ReturnsFalse(int offset)
    {
        string code = _generator.GenerateCode(Sha1Seed, Now.AddSeconds(30 * offset));

        Assert.False(_generator.TryValidateCode(Sha1Seed, code, Now, out long step));
        Assert.Equal(-1, step);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void TryValidateCode_ZeroDrift_AcceptsOnlyExactStep(int offset)
    {
        var parameters = new TotpParameters { DriftSteps = 0 };
        string exact = _generator.GenerateCode(Sha1Seed, Now, parameters);
        string adjacent = _generator.GenerateCode(Sha1Seed, Now.AddSeconds(30 * offset), parameters);

        Assert.True(_generator.TryValidateCode(Sha1Seed, exact, Now, out _, parameters));
        Assert.False(_generator.TryValidateCode(Sha1Seed, adjacent, Now, out _, parameters));
    }

    [Fact]
    public void TryValidateCode_LargerDrift_AcceptsWiderWindow()
    {
        var parameters = new TotpParameters { DriftSteps = 3 };
        string code = _generator.GenerateCode(Sha1Seed, Now.AddSeconds(-90), parameters);

        Assert.True(_generator.TryValidateCode(Sha1Seed, code, Now, out long step, parameters));
        Assert.Equal((Now.ToUnixTimeSeconds() / 30) - 3, step);
    }

    [Fact]
    public void TryValidateCode_CustomStepAndDigits_RoundTrips()
    {
        var parameters = new TotpParameters { StepSeconds = 60, Digits = 7, Algorithm = HotpAlgorithm.Sha256 };

        string code = _generator.GenerateCode(Sha256Seed, Now, parameters);

        Assert.Equal(7, code.Length);
        Assert.True(_generator.TryValidateCode(Sha256Seed, code, Now, out long step, parameters));
        Assert.Equal(Now.ToUnixTimeSeconds() / 60, step);
        Assert.False(_generator.TryValidateCode(Sha256Seed, code, Now, out _, parameters with { StepSeconds = 30 }));
    }

    [Fact]
    public void TryValidateCode_WithoutTimestamp_UsesClock()
    {
        string code = _generator.GenerateCode(Sha1Seed, Now);

        Assert.True(_generator.TryValidateCode(Sha1Seed, code, out long step));
        Assert.Equal(Now.ToUnixTimeSeconds() / 30, step);

        _clock.UtcNow = Now.AddMinutes(5);

        Assert.False(_generator.TryValidateCode(Sha1Seed, code, out _));
    }

    [Fact]
    public void TryValidateCode_AtUnixEpoch_SkipsNegativeSteps()
    {
        DateTimeOffset epoch = DateTimeOffset.FromUnixTimeSeconds(5);
        string code = _generator.GenerateCode(Sha1Seed, epoch);

        Assert.True(_generator.TryValidateCode(Sha1Seed, code, epoch, out long step));
        Assert.Equal(0, step);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData("12345")]
    [InlineData("1234567")]
    public void TryValidateCode_MalformedCode_ReturnsFalse(string code)
    {
        Assert.False(_generator.TryValidateCode(Sha1Seed, code, Now, out long step));
        Assert.Equal(-1, step);
    }

    [Fact]
    public void TryValidateCode_CodeWithSeparators_IsAccepted()
    {
        string code = _generator.GenerateCode(Sha1Seed, Now);

        Assert.True(_generator.TryValidateCode(Sha1Seed, $"{code[..3]} {code[3..]}", Now, out _));
    }

    [Fact]
    public void Members_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new TotpGenerator(null!));
        Assert.Throws<ArgumentNullException>(() => _generator.TryValidateCode(Sha1Seed, null!, Now, out _));
        Assert.Throws<ArgumentException>(() => _generator.GenerateCode(new byte[15], Now));
        Assert.Throws<ArgumentException>(() => _generator.TryValidateCode(new byte[15], "123456", Now, out _));
    }
}
