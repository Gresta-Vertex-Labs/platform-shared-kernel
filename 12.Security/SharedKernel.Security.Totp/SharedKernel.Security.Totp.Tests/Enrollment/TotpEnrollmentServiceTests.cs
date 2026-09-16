using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Enrollment;

public sealed class TotpEnrollmentServiceTests
{
    private static readonly SecureRandomGenerator Random = new();

    private readonly TotpEnrollmentService _service = new(Random, new RecoveryCodeGenerator(Random));

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAnyArgumentIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TotpEnrollmentService(null!, new RecoveryCodeGenerator(Random)));
        Assert.Throws<ArgumentNullException>(() => new TotpEnrollmentService(Random, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Con:toso")]
    public void GenerateEnrollment_ThrowsArgumentException_WhenIssuerIsInvalid(string issuer)
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.GenerateEnrollment(issuer, "user@example.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("user:example.com")]
    public void GenerateEnrollment_ThrowsArgumentException_WhenAccountNameIsInvalid(string accountName)
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.GenerateEnrollment("Contoso", accountName));
    }

    [Fact]
    public void GenerateEnrollment_DefaultsToRecommendedSecretLength()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com");

        Assert.Equal(TotpSecret.DefaultLength, enrollment.Secret.Length);
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
        Assert.Contains($"secret={enrollment.SecretBase32}", enrollment.ProvisioningUri.Query);
        Assert.Contains("issuer=Contoso", enrollment.ProvisioningUri.Query);
        Assert.Contains("digits=6", enrollment.ProvisioningUri.Query);
        Assert.Contains("period=30", enrollment.ProvisioningUri.Query);
    }

    [Fact]
    public void GenerateEnrollment_NullParameters_UsesDefaults()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com");

        Assert.Equal(TotpParameters.Default, enrollment.Parameters);
    }

    [Fact]
    public void GenerateEnrollment_CustomParameters_AreReturnedAndEncodedInProvisioningUri()
    {
        var parameters = new TotpParameters { Digits = 8, StepSeconds = 60, Algorithm = HotpAlgorithm.Sha256 };

        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com", parameters);

        Assert.Same(parameters, enrollment.Parameters);
        Assert.Contains("digits=8", enrollment.ProvisioningUri.Query);
        Assert.Contains("period=60", enrollment.ProvisioningUri.Query);
        Assert.Contains("algorithm=SHA256", enrollment.ProvisioningUri.Query);
    }

    [Fact]
    public void GenerateEnrollment_SecretVerifiesCodesGeneratedWithTheEnrolledParameters()
    {
        var parameters = new TotpParameters { Digits = 8 };
        var clock = new FakeClock();
        var generator = new TotpGenerator(clock);

        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com", parameters);
        var code = generator.GenerateCode(enrollment.Secret, enrollment.Parameters);

        Assert.True(generator.TryValidateCode(enrollment.Secret, code, out _, enrollment.Parameters));
    }

    [Fact]
    public void GenerateEnrollment_RecoveryCodes_HasRequestedCountAndIsUnique()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com", recoveryCodeCount: 12);

        Assert.Equal(12, enrollment.RecoveryCodes.Count);
        Assert.Equal(enrollment.RecoveryCodes.Count, enrollment.RecoveryCodes.Distinct().Count());
    }

    [Fact]
    public void GenerateEnrollment_RecoveryCodes_AreAlreadyInADisplayFormThatNormalizesStably()
    {
        var enrollment = _service.GenerateEnrollment("Contoso", "user@example.com");

        foreach (var code in enrollment.RecoveryCodes)
        {
            var normalized = RecoveryCodeGenerator.Normalize(code);
            Assert.Equal(normalized, RecoveryCodeGenerator.Normalize(code.ToLowerInvariant()));
            Assert.DoesNotContain('-', normalized);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(65)]
    public void GenerateEnrollment_ThrowsArgumentOutOfRangeException_WhenSecretLengthBytesIsOutOfRange(int secretLengthBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.GenerateEnrollment("Contoso", "user@example.com", secretLengthBytes: secretLengthBytes));
    }

    [Fact]
    public void GenerateEnrollment_ThrowsArgumentOutOfRangeException_WhenRecoveryCodeCountIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GenerateEnrollment("Contoso", "user@example.com", recoveryCodeCount: 0));
    }
}
