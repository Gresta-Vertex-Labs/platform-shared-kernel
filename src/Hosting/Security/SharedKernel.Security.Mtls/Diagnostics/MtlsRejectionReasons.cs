namespace SharedKernel.Security.Mtls.Diagnostics;

// The log-only rejection code used when a validator gives none (event 12300). A validator supplies its own stable code;
// RejectionReasonText bounds it before it is logged.
internal static class MtlsRejectionReasons
{
    internal const string Rejected = "Rejected";
}
