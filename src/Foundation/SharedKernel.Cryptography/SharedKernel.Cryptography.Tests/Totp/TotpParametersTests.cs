using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class TotpParametersTests
{
    [Fact]
    public void Defaults_AreAuthenticatorAppCompatible()
    {
        TotpParameters parameters = TotpParameters.Default;

        Assert.Equal(6, parameters.Digits);
        Assert.Equal(30, parameters.StepSeconds);
        Assert.Equal(1, parameters.DriftSteps);
        Assert.Equal(HotpAlgorithm.Sha1, parameters.Algorithm);
        Assert.Equal(new TotpParameters(), parameters);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Digits_InRange_IsAccepted(int digits)
    {
        Assert.Equal(digits, new TotpParameters { Digits = digits }.Digits);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(0)]
    public void Digits_OutOfRange_Throws(int digits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TotpParameters { Digits = digits });
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(300)]
    public void StepSeconds_InRange_IsAccepted(int seconds)
    {
        Assert.Equal(seconds, new TotpParameters { StepSeconds = seconds }.StepSeconds);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(301)]
    [InlineData(0)]
    public void StepSeconds_OutOfRange_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TotpParameters { StepSeconds = seconds });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void DriftSteps_InRange_IsAccepted(int steps)
    {
        Assert.Equal(steps, new TotpParameters { DriftSteps = steps }.DriftSteps);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void DriftSteps_OutOfRange_Throws(int steps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TotpParameters { DriftSteps = steps });
    }

    [Theory]
    [InlineData(HotpAlgorithm.Sha1)]
    [InlineData(HotpAlgorithm.Sha256)]
    [InlineData(HotpAlgorithm.Sha512)]
    public void Algorithm_Defined_IsAccepted(HotpAlgorithm algorithm)
    {
        Assert.Equal(algorithm, new TotpParameters { Algorithm = algorithm }.Algorithm);
    }

    [Fact]
    public void Algorithm_Undefined_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TotpParameters { Algorithm = (HotpAlgorithm)3 });
    }

    [Fact]
    public void With_ValidatesNewValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpParameters.Default with { Digits = 10 });
    }

    [Theory]
    [InlineData(30, 1, 90)]
    [InlineData(30, 0, 30)]
    [InlineData(60, 2, 300)]
    [InlineData(15, 5, 165)]
    [InlineData(300, 5, 3300)]
    public void ValidityWindow_IsStepTimesWindowWidth(int stepSeconds, int driftSteps, int expectedSeconds)
    {
        var parameters = new TotpParameters { StepSeconds = stepSeconds, DriftSteps = driftSteps };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), parameters.ValidityWindow);
    }
}
