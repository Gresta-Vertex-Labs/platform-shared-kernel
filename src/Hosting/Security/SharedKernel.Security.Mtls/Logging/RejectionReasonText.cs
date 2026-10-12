namespace SharedKernel.Security.Mtls.Logging;

// A consumer validator's FailureReason is logged, so only a short, identifier-shaped code is passed through; anything
// else (a client id, a certificate subject, free text, a multi-line value) is replaced by a fixed value.
internal static class RejectionReasonText
{
    internal const string Fallback = "Rejected";

    private const int MaxLength = 64;

    public static string Bound(string? reason)
    {
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxLength)
        {
            return Fallback;
        }

        foreach (char c in reason)
        {
            bool allowed = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-';
            if (!allowed)
            {
                return Fallback;
            }
        }

        return reason;
    }
}
