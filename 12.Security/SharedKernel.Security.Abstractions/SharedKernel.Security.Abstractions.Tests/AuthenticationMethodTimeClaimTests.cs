using System.Security.Claims;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class AuthenticationMethodTimeClaimTests
{
    private static readonly DateTimeOffset VerifiedAt = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);

    [Fact]
    public void Create_WritesMethodAndUnixSeconds()
    {
        Claim claim = AuthenticationMethodTimeClaim.Create("otp", VerifiedAt);

        Assert.Equal(SecurityClaimTypes.AuthenticationMethodTime, claim.Type);
        Assert.Equal("otp 1790000000", claim.Value);
    }

    [Fact]
    public void Create_FractionOfASecond_IsRoundedDown()
    {
        // Rounding down never makes a method look more recent than it is.
        Claim claim = AuthenticationMethodTimeClaim.Create("otp", VerifiedAt.AddMilliseconds(999));

        Assert.Equal("otp 1790000000", claim.Value);
    }

    [Fact]
    public void Create_TimeWithOffset_RecordsTheInstant()
    {
        DateTimeOffset local = VerifiedAt.ToOffset(TimeSpan.FromHours(3));

        Assert.Equal(TimeSpan.FromHours(3), local.Offset);
        Assert.Equal("otp 1790000000", AuthenticationMethodTimeClaim.Create("otp", local).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_MissingMethod_Throws(string? method)
    {
        Assert.ThrowsAny<ArgumentException>(() => AuthenticationMethodTimeClaim.Create(method!, VerifiedAt));
    }

    [Fact]
    public void Create_TimeBeforeUnixEpoch_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AuthenticationMethodTimeClaim.Create("otp", DateTimeOffset.UnixEpoch.AddTicks(-1)));
    }

    [Fact]
    public void Read_CreatedClaims_RoundTrip()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
        [
            AuthenticationMethodTimeClaim.Create("otp", VerifiedAt),
            AuthenticationMethodTimeClaim.Create("hwk", VerifiedAt.AddMinutes(-3)),
        ]);

        Assert.Equal(2, times.Count);
        Assert.Equal(VerifiedAt, times["otp"]);
        Assert.Equal(VerifiedAt.AddMinutes(-3), times["hwk"]);
        Assert.Equal(TimeSpan.Zero, times["otp"].Offset);
    }

    [Fact]
    public void Read_SeveralTimesForOneMethod_KeepsTheLatest()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
        [
            AuthenticationMethodTimeClaim.Create("otp", VerifiedAt.AddMinutes(-30)),
            AuthenticationMethodTimeClaim.Create("otp", VerifiedAt),
            AuthenticationMethodTimeClaim.Create("otp", VerifiedAt.AddMinutes(-10)),
        ]);

        Assert.Equal(VerifiedAt, Assert.Single(times).Value);
    }

    [Fact]
    public void Read_MethodsCompareOrdinally()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
        [
            AuthenticationMethodTimeClaim.Create("otp", VerifiedAt),
            AuthenticationMethodTimeClaim.Create("OTP", VerifiedAt.AddMinutes(-1)),
        ]);

        Assert.Equal(VerifiedAt, times["otp"]);
        Assert.Equal(VerifiedAt.AddMinutes(-1), times["OTP"]);
    }

    [Fact]
    public void Read_MethodContainingASpace_ReadsBackUnchanged()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
            [AuthenticationMethodTimeClaim.Create("security key", VerifiedAt)]);

        Assert.Equal(VerifiedAt, times["security key"]);
    }

    [Fact]
    public void Read_ClaimsOfOtherTypes_AreIgnored()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
        [
            new Claim(SecurityClaimTypes.AuthenticationMethod, "otp 1790000000"),
            new Claim(SecurityClaimTypes.AuthTime, "1790000000"),
            new Claim("AMR_TIME", "otp 1790000000"),
        ]);

        Assert.Empty(times);
    }

    [Theory]
    [InlineData("")]
    [InlineData("otp")]
    [InlineData("otp ")]
    [InlineData(" 1790000000")]
    [InlineData("   1790000000")]
    [InlineData("otp -5")]
    [InlineData("otp +5")]
    [InlineData("otp 1.5")]
    [InlineData("otp 1,790,000,000")]
    [InlineData("otp soon")]
    [InlineData("otp 1790000000 ")]
    [InlineData("otp 253402300800")]
    [InlineData("otp 99999999999999999999")]
    public void Read_MalformedValue_IsIgnored(string value)
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
            [new Claim(SecurityClaimTypes.AuthenticationMethodTime, value)]);

        Assert.Empty(times);
    }

    [Fact]
    public void Read_LatestRepresentableSecond_IsAccepted()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = AuthenticationMethodTimeClaim.Read(
            [new Claim(SecurityClaimTypes.AuthenticationMethodTime, "otp 253402300799")]);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(253_402_300_799), times["otp"]);
    }

    [Fact]
    public void Read_NoClaims_IsEmpty()
    {
        Assert.Empty(AuthenticationMethodTimeClaim.Read([]));
    }

    [Fact]
    public void Read_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AuthenticationMethodTimeClaim.Read(null!));
    }
}
