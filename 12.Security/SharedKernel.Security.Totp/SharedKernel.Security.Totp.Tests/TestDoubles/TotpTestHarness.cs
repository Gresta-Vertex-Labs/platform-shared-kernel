using System.Globalization;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;

namespace SharedKernel.Security.Totp.Tests.TestDoubles;

internal sealed class TotpTestHarness
{
    public const string SubjectId = FakeUserContext.DefaultSubjectId;

    public TotpTestHarness() => ReplayGuard = new FakeTotpReplayGuard(Clock);

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 12, 0, 10, TimeSpan.Zero));

    public FakeTotpReplayGuard ReplayGuard { get; }

    public InMemoryTotpStepUpStore StepUps { get; } = new();

    public InMemoryRecoveryCodeStore RecoveryCodes { get; } = new();

    public FakeOneWayHasher Hasher { get; } = new();

    public InMemoryLogger<TotpChallengeService> ChallengeLogger { get; } = new();

    public InMemoryLogger<TotpEnrollmentService> EnrollmentLogger { get; } = new();

    public TotpStepUpOptions StepUpOptions { get; } = new();

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static FakeUserContext User(string? sessionId = "session-1", string subjectId = SubjectId) =>
        new() { SubjectId = subjectId, SessionId = sessionId };

    public ITotpVerifier CreateVerifier() => new TotpVerifier(new TotpGenerator(Clock), ReplayGuard);

    public TotpChallengeService CreateChallengeService(
        ITotpAttemptThrottle? throttle = null,
        ITotpVerifier? verifier = null,
        ITotpStepUpStore? stepUps = null,
        IRecoveryCodeStore? recoveryCodes = null,
        IOneWayHasher? hasher = null) =>
        new(
            verifier ?? CreateVerifier(),
            stepUps ?? StepUps,
            recoveryCodes ?? RecoveryCodes,
            hasher ?? Hasher,
            Clock,
            Microsoft.Extensions.Options.Options.Create(StepUpOptions),
            ChallengeLogger,
            throttle);

    public TotpEnrollmentService CreateEnrollmentService(
        ISecureRandomGenerator? random = null,
        IOneWayHasher? hasher = null,
        ITotpAttemptThrottle? throttle = null,
        ITotpVerifier? verifier = null) =>
        new(
            random ?? new FakeSecureRandomGenerator(),
            new RecoveryCodeGenerator(new FakeSecureRandomGenerator()),
            hasher ?? Hasher,
            verifier ?? CreateVerifier(),
            EnrollmentLogger,
            throttle);

    public string CurrentCode(byte[] secret, TotpParameters? parameters = null) =>
        new TotpGenerator(Clock).GenerateCode(secret, parameters);

    public string WrongCode(byte[] secret, TotpParameters? parameters = null)
    {
        parameters ??= TotpParameters.Default;
        var generator = new TotpGenerator(Clock);
        for (int candidate = 0; ; candidate++)
        {
            string code = candidate.ToString(CultureInfo.InvariantCulture).PadLeft(parameters.Digits, '0');
            if (!generator.TryValidateCode(secret, code, out _, parameters))
            {
                return code;
            }
        }
    }

    public TotpEnrollment EnrollAndSaveRecoveryCodes(int recoveryCodeCount = 10, string subjectId = SubjectId)
    {
        TotpEnrollment enrollment = CreateEnrollmentService().Create("Contoso", "alice@example.com", recoveryCodeCount: recoveryCodeCount);
        RecoveryCodes.Save(subjectId, enrollment.StoredRecoveryCodes);
        return enrollment;
    }
}
