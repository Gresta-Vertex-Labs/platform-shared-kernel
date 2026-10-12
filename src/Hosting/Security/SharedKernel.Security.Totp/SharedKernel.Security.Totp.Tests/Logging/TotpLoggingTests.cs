using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Logging;

public sealed class TotpLoggingTests
{
    private const string DistinctiveSubject = "subject-kt47-distinctive";
    private const string DistinctiveSession = "session-kt47-distinctive";

    private readonly TotpTestHarness _harness = new();
    private readonly byte[] _secret = TotpTestHarness.NewSecret();

    [Fact]
    public async Task Sweep_ChallengeAndRecoveryPaths_NeverLogSubjectSessionCodeOrSecret()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes(subjectId: DistinctiveSubject);
        TotpChallengeService service = _harness.CreateChallengeService();
        var user = TotpTestHarness.User(DistinctiveSession, DistinctiveSubject);
        string validCode = _harness.CurrentCode(_secret);
        string wrongCode = _harness.WrongCode(_secret);

        await service.VerifyCodeAsync(user, _secret, wrongCode);
        await service.VerifyCodeAsync(user, _secret, validCode);
        await service.VerifyCodeAsync(user, _secret, validCode);
        await service.RedeemRecoveryCodeAsync(user, "not-a-recovery-code");
        await service.RedeemRecoveryCodeAsync(user, enrollment.RecoveryCodes[0]);
        await service.RedeemRecoveryCodeAsync(user, enrollment.RecoveryCodes[0]);

        List<LogRecord> records = [.. _harness.ChallengeLogger.Records, .. _harness.EnrollmentLogger.Records];
        Assert.NotEmpty(records);
        records.ShouldHaveLogged(new EventId(12400));
        records.ShouldHaveLogged(new EventId(12401));
        records.ShouldHaveLogged(new EventId(12402));
        records.ShouldNotLeak(
            [
                DistinctiveSubject,
                DistinctiveSession,
                validCode,
                wrongCode,
                Convert.ToBase64String(_secret),
                Convert.ToHexString(_secret),
                .. enrollment.RecoveryCodes,
            ]);
    }
}
