using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Security.Totp.Internal;

// RFC 4226 section 7.3: check the throttle before each attempt and record every attempt. Without a registered
// ITotpAttemptThrottle the check always passes.
internal static class TotpAttempts
{
    public static async ValueTask<bool> IsThrottledAsync(ITotpAttemptThrottle? throttle, string subjectId, CancellationToken cancellationToken)
    {
        if (throttle is null)
        {
            return false;
        }

        if (await throttle.IsThrottledAsync(subjectId, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        await throttle.RecordAttemptAsync(subjectId, cancellationToken).ConfigureAwait(false);
        return false;
    }
}
