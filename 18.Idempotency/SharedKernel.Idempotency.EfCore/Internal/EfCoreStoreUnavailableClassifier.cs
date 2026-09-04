using System.Net.Sockets;
using Npgsql;

namespace SharedKernel.Idempotency.EfCore.Internal;

/// <summary>
/// Narrow exception classifier recognizing only genuine PostgreSQL connectivity/timeout failures
/// (D-07) — never a blanket <c>catch (Exception)</c>. Used uniformly by every store method so a
/// mid-flight outage is handled identically regardless of which call it interrupts.
/// </summary>
internal static class EfCoreStoreUnavailableClassifier
{
    /// <summary>Maximum number of <see cref="Exception.InnerException"/> hops walked before giving up.</summary>
    /// <remarks>
    /// EF Core's <c>NpgsqlExecutionStrategy</c> wraps a genuine connectivity failure in its own
    /// <see cref="InvalidOperationException"/> ("An exception has been raised that is likely due to
    /// a transient failure.") even when no <c>EnableRetryOnFailure</c> is configured — the real
    /// <see cref="NpgsqlException"/>/<see cref="TimeoutException"/>/<see cref="SocketException"/>
    /// is one or two levels down in <see cref="Exception.InnerException"/>, not the top-level
    /// exception type this method's caller catches. A bounded walk keeps this a narrow classifier
    /// rather than degrading into a blanket <c>catch (Exception)</c>.
    /// </remarks>
    private const int MaxInnerExceptionDepth = 5;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> — or one of its
    /// <see cref="Exception.InnerException"/>s, within a bounded depth — represents PostgreSQL
    /// being unreachable, timed out, or a transient connection failure, as opposed to a
    /// programming error or a genuine constraint/data-shape defect.
    /// </summary>
    public static bool IsStoreUnavailable(Exception exception)
    {
        var current = exception;

        for (var depth = 0; current is not null && depth < MaxInnerExceptionDepth; depth++, current = current.InnerException)
        {
            var isConnectivityFailure = current switch
            {
                // Npgsql marks most transport-level failures IsTransient=true, but a bare
                // "connection refused"/timeout at TCP-connect time is sometimes surfaced as a
                // non-transient NpgsqlException wrapping a SocketException/TimeoutException —
                // checked explicitly rather than relying on IsTransient alone.
                NpgsqlException { IsTransient: true } => true,
                NpgsqlException { InnerException: SocketException or TimeoutException } => true,
                TimeoutException => true,
                SocketException => true,
                _ => false,
            };

            if (isConnectivityFailure)
            {
                return true;
            }
        }

        return false;
    }
}
