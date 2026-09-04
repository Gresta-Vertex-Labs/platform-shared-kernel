using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Enrollment;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Enrollment;

public sealed class TotpEnrollmentServiceTests
{
    private readonly TotpEnrollmentService _service = new(new CryptoRandomGenerator());

    [Fact]
    public void GenerateEnrollment_ThrowsArgumentNullException_WhenRandomGeneratorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TotpEnrollmentService(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateEnrollment_ThrowsArgumentException_WhenIssuerIsInvalid(string issuer)
    {
        Assert.Throws<ArgumentException>(() => _service.GenerateEnrollment(issuer, "user@example.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateEnrollment_ThrowsArgumentException_WhenAccountNameIsInvalid(string accountName)
    {
        Assert.Throws<ArgumentException>(() => _service.GenerateEnrollment("Contoso", accountName));
    }

    [Fact]
    public void GenerateEnrollment_ProducesSecretOfRequestedByteLength()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com", secretLengthBytes: 32);

        Assert.Equal(32, enrollment.Secret.Length);
    }

    [Fact]
    public void GenerateEnrollment_SecretBase32_RoundTripsToTheRawSecretBytes()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com");

        var decoded = Base32.Decode(enrollment.SecretBase32);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(enrollment.Secret, decoded.Value);
    }

    [Fact]
    public void GenerateEnrollment_ProvisioningUri_MatchesExpectedOtpAuthShape()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com");

        Assert.Equal("otpauth", enrollment.ProvisioningUri.Scheme);
        Assert.Equal("totp", enrollment.ProvisioningUri.Host);
        Assert.Contains("secret=", enrollment.ProvisioningUri.Query);
        Assert.Contains("issuer=Contoso", enrollment.ProvisioningUri.Query);
    }

    [Fact]
    public void GenerateEnrollment_RecoveryCodes_HasRequestedCountAndIsUnique()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com", recoveryCodeCount: 12);

        Assert.Equal(12, enrollment.RecoveryCodes.Count);
        Assert.Equal(enrollment.RecoveryCodes.Count, enrollment.RecoveryCodes.Distinct().Count());
    }

    [Fact]
    public void GenerateEnrollment_ThrowsArgumentOutOfRangeException_WhenSecretLengthBytesIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GenerateEnrollment("Contoso", "user@example.com", secretLengthBytes: 0));
    }

    [Fact]
    public void GenerateEnrollment_ThrowsArgumentOutOfRangeException_WhenRecoveryCodeCountIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GenerateEnrollment("Contoso", "user@example.com", recoveryCodeCount: 0));
    }
}
