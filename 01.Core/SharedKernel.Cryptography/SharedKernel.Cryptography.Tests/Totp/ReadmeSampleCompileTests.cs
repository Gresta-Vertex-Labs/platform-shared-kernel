using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Tests.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Compiles the exact code samples shown in the 01.Core README's "TOTP/HOTP — Second-Factor
/// Codes" section (DO-29), so documentation drift is caught by the build rather than trusted on
/// sight.
/// </summary>
public sealed class ReadmeSampleCompileTests
{
    // --- Enrollment sample ---

    private sealed class TotpEnrollmentService(ISecureRandomGenerator randomGenerator)
    {
        public (byte[] Secret, Uri ProvisioningUri) BeginEnrollment(string accountEmail)
        {
            byte[] secret = randomGenerator.NextBytes(20); // 160 bits — the RFC 4226/6238 default SHA-1 secret length

            Uri provisioningUri = TotpProvisioningUri.Build(
                issuer: "Contoso",
                accountName: accountEmail,
                secret: secret);

            // Persist `secret` (encrypted at rest, e.g. via ISymmetricEncryptionService) against the
            // user's account, then render `provisioningUri` as a QR code for the user to scan.
            return (secret, provisioningUri);
        }
    }

    [Fact]
    public void ReadmeSample_Enrollment_ProducesSecretAndProvisioningUri()
    {
        var service = new TotpEnrollmentService(new CryptoRandomGenerator());

        (byte[] secret, Uri provisioningUri) = service.BeginEnrollment("alice@example.com");

        Assert.Equal(20, secret.Length);
        Assert.Equal("otpauth", provisioningUri.Scheme);
        Assert.Equal("totp", provisioningUri.Host);
    }

    // --- Challenge sample ---

    private sealed class TotpChallengeService(ITotpGenerator totpGenerator)
    {
        public string CurrentCode(byte[] secret) => totpGenerator.GenerateCode(secret);
    }

    // --- Verification sample ---

    private sealed class InMemoryTotpReplayGuard : ITotpReplayGuard
    {
        private readonly ConcurrentDictionary<string, byte> _used = new();

        public ValueTask<bool> TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default)
        {
            // A real implementation persists to a store (e.g. distributed cache) with an expiry of
            // `validityWindow`, so the entry never grows unbounded, and relies on the store's own
            // atomic conditional-write primitive (Redis `SET NX PX`, SQL `INSERT ... ON CONFLICT DO
            // NOTHING`) instead of `ConcurrentDictionary.TryAdd` — omitted here for brevity.
            return ValueTask.FromResult(_used.TryAdd($"{identityKey}:{code}", 0));
        }
    }

    private sealed class TotpLoginStepUpHandler(TotpVerifier totpVerifier)
    {
        // NOTE: `ct` must be passed by name — `TotpVerifier.VerifyAsync` now takes optional
        // `digits`/`stepSeconds`/`driftWindow`/`algorithm` parameters before its trailing `ct`, so
        // a positional 4th argument would bind to `digits`, not `ct`.
        public async Task<bool> VerifySecondFactorAsync(string userId, byte[] secret, string submittedCode, CancellationToken ct) =>
            await totpVerifier.VerifyAsync(userId, secret, submittedCode, ct: ct);
    }

    [Fact]
    public async Task ReadmeSample_Verification_AcceptsFreshCodeAndRejectsReplay()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        IClock clock = new SystemClock(timeProvider);
        ITotpGenerator totpGenerator = new TotpGenerator(new HotpGenerator(), clock);
        var challengeService = new TotpChallengeService(totpGenerator);

        byte[] secret = new CryptoRandomGenerator().NextBytes(20);
        string code = challengeService.CurrentCode(secret);

        var totpVerifier = new TotpVerifier(totpGenerator, new InMemoryTotpReplayGuard());
        var stepUpHandler = new TotpLoginStepUpHandler(totpVerifier);

        bool firstAttempt = await stepUpHandler.VerifySecondFactorAsync("user-1", secret, code, CancellationToken.None);
        bool secondAttempt = await stepUpHandler.VerifySecondFactorAsync("user-1", secret, code, CancellationToken.None);

        Assert.True(firstAttempt);
        Assert.False(secondAttempt);
    }

    // --- Recovery codes sample ---

    private sealed class RecoveryCodeIssuanceService(RecoveryCodeGenerator recoveryCodeGenerator, IOneWayHasher hasher)
    {
        public (IReadOnlyList<string> PlaintextCodesToShowOnce, IReadOnlyList<string> HashesToPersist) IssueRecoveryCodes()
        {
            IReadOnlyList<string> codes = recoveryCodeGenerator.GenerateCodes();
            List<string> hashes = codes.Select(hasher.Hash).ToList();

            // Show `PlaintextCodesToShowOnce` to the user now — this is the only time the plaintext
            // is ever available. Persist only `HashesToPersist`.
            return (codes, hashes);
        }
    }

    private static IOptionsMonitor<CryptographyOptions> CreateOptionsMonitor()
    {
        var monitor = Substitute.For<IOptionsMonitor<CryptographyOptions>>();
        monitor.CurrentValue.Returns(new CryptographyOptions { Pbkdf2Iterations = 1000 });
        return monitor;
    }

    [Fact]
    public void ReadmeSample_RecoveryCodes_IssuesTenCodesEachHashedAndVerifiable()
    {
        IOneWayHasher hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor());
        var service = new RecoveryCodeIssuanceService(
            new RecoveryCodeGenerator(new CryptoRandomGenerator()),
            hasher);

        (IReadOnlyList<string> plaintextCodes, IReadOnlyList<string> hashes) = service.IssueRecoveryCodes();

        Assert.Equal(10, plaintextCodes.Count);
        Assert.Equal(10, hashes.Count);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hashes[0], plaintextCodes[0]));
    }
}
