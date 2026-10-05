using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Enrollment;

public sealed class TotpEnrollmentServiceCreateTests
{
    private readonly TotpTestHarness _harness = new();

    [Fact]
    public void Create_Defaults_Generates20ByteSecretFromInjectedRandom()
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService(random: new FakeSecureRandomGenerator(seed: 42));

        TotpEnrollment enrollment = service.Create("Contoso", "alice@example.com");

        Assert.Equal(TotpSecret.DefaultLength, enrollment.Secret.Length);
        Assert.Equal(new FakeSecureRandomGenerator(seed: 42).GetBytes(TotpSecret.DefaultLength), enrollment.Secret);
        Assert.Same(TotpParameters.Default, enrollment.Parameters);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Create_CustomSecretLength_GeneratesSecretOfThatLength(int length)
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice", secretLengthBytes: length);

        Assert.Equal(length, enrollment.Secret.Length);
    }

    [Fact]
    public void Create_TwoEnrollments_UseDifferentSecrets()
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();

        Assert.NotEqual(service.Create("Contoso", "alice").Secret, service.Create("Contoso", "alice").Secret);
    }

    [Fact]
    public void Create_SecretBase32_DecodesToSecret()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice", secretLengthBytes: 33);

        Assert.DoesNotContain('=', enrollment.SecretBase32);
        Assert.Equal(enrollment.Secret, Base32.Decode(enrollment.SecretBase32).Value);
    }

    [Fact]
    public void Create_ProvisioningUri_MatchesSecretAndParameters()
    {
        var parameters = new TotpParameters { Digits = 8, StepSeconds = 60, Algorithm = HotpAlgorithm.Sha256 };

        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso Bank", "alice@example.com", parameters);

        Uri uri = enrollment.ProvisioningUri;
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("otpauth", uri.Scheme);
        Assert.Equal("totp", uri.Host);
        Assert.Equal("Contoso Bank:alice@example.com", Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
        Assert.Equal(enrollment.SecretBase32, query["secret"].ToString());
        Assert.Equal("Contoso Bank", query["issuer"].ToString());
        Assert.Equal("SHA256", query["algorithm"].ToString());
        Assert.Equal("8", query["digits"].ToString());
        Assert.Equal("60", query["period"].ToString());
        Assert.Same(parameters, enrollment.Parameters);
    }

    [Fact]
    public void Create_DefaultRecoveryCodeCount_IsTen()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice");

        Assert.Equal(10, enrollment.RecoveryCodes.Count);
        Assert.Equal(10, enrollment.StoredRecoveryCodes.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void Create_CustomRecoveryCodeCount_GeneratesThatManyDistinctCodes(int count)
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice", recoveryCodeCount: count);

        Assert.Equal(count, enrollment.RecoveryCodes.Count);
        Assert.Equal(count, enrollment.RecoveryCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(count, enrollment.StoredRecoveryCodes.Count);
    }

    [Fact]
    public void Create_StoredRecoveryCodes_HaveUniqueNonEmptyIds()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice", recoveryCodeCount: 50);

        Assert.All(enrollment.StoredRecoveryCodes, code => Assert.Equal(16, code.Id.Length));
        Assert.Equal(50, enrollment.StoredRecoveryCodes.Select(code => code.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Create_StoredRecoveryCodes_LookupIsFirstTwoNormalizedCharactersAndHashVerifiesNormalizedCode()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice");

        for (int i = 0; i < enrollment.RecoveryCodes.Count; i++)
        {
            string normalized = RecoveryCodeGenerator.Normalize(enrollment.RecoveryCodes[i]);
            StoredRecoveryCode stored = enrollment.StoredRecoveryCodes[i];
            Assert.Equal(normalized[..2], stored.Lookup);
            Assert.Equal(HashVerificationResult.Success, _harness.Hasher.Verify(stored.Hash, normalized));
            Assert.DoesNotContain(normalized, stored.Hash, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Create_StoredHash_IsOfNormalizedFormNotFormattedCode()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice", recoveryCodeCount: 1);
        string code = enrollment.RecoveryCodes[0];
        string hash = enrollment.StoredRecoveryCodes[0].Hash;

        Assert.Contains('-', code);
        Assert.Equal(HashVerificationResult.Failed, _harness.Hasher.Verify(hash, code));
        Assert.Equal(
            HashVerificationResult.Success,
            _harness.Hasher.Verify(hash, RecoveryCodeGenerator.Normalize(code.ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal))));
    }

    [Fact]
    public void Create_WithRealPbkdf2Hasher_StoredHashesVerifyNormalizedVariants()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SharedKernel:Cryptography:Pbkdf2:Iterations"] = "100000" })
            .Build();
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        TotpEnrollment enrollment = _harness.CreateEnrollmentService(hasher: hasher).Create("Contoso", "alice", recoveryCodeCount: 3);

        for (int i = 0; i < enrollment.RecoveryCodes.Count; i++)
        {
            string code = enrollment.RecoveryCodes[i];
            string hash = enrollment.StoredRecoveryCodes[i].Hash;
            string variant = RecoveryCodeGenerator.Normalize(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal));
            Assert.StartsWith("$pbkdf2-sha256$", hash, StringComparison.Ordinal);
            Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, variant));
        }
    }

    [Fact]
    public void Create_ToString_ContainsNeitherSecretNorRecoveryCodes()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService().Create("Contoso", "alice");

        string text = enrollment.ToString();

        Assert.DoesNotContain(enrollment.SecretBase32, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(enrollment.Secret), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(enrollment.Secret), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otpauth", text, StringComparison.OrdinalIgnoreCase);
        Assert.All(enrollment.RecoveryCodes, code =>
        {
            Assert.DoesNotContain(code, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(RecoveryCodeGenerator.Normalize(code), text, StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(enrollment.StoredRecoveryCodes, code => Assert.DoesNotContain(code.Hash, text, StringComparison.Ordinal));
        Assert.Contains("10", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Con:toso", "alice")]
    [InlineData("Contoso", "alice:admin")]
    [InlineData("", "alice")]
    [InlineData("Contoso", "")]
    [InlineData("   ", "alice")]
    [InlineData("Contoso", " ")]
    public void Create_InvalidIssuerOrAccount_ThrowsArgumentException(string issuer, string accountName)
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();

        Assert.ThrowsAny<ArgumentException>(() => service.Create(issuer, accountName));
    }

    [Fact]
    public void Create_NullIssuer_ThrowsArgumentNullException()
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();

        Assert.Throws<ArgumentNullException>(() => service.Create(null!, "alice"));
        Assert.Throws<ArgumentNullException>(() => service.Create("Contoso", null!));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(65)]
    [InlineData(0)]
    public void Create_SecretLengthOutOfRange_ThrowsArgumentOutOfRangeException(int length)
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Create("Contoso", "alice", secretLengthBytes: length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void Create_RecoveryCodeCountOutOfRange_ThrowsArgumentOutOfRangeException(int count)
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Create("Contoso", "alice", recoveryCodeCount: count));
    }
}
