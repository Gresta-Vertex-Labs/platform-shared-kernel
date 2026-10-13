using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Npgsql.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Persistence.Npgsql</c> sub-block of <c>06.Persistence</c>: EventIds 6300-6399.
/// </summary>
internal static class NpgsqlLoggingEventIds
{
    /// <summary>First id of the sub-block (6300).</summary>
    public const int Base = LoggingEventIdRanges.Persistence + (3 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A data source uses an SSL mode below <c>VerifyFull</c>.</summary>
    public const int InsecureSslMode = Base + 0;

    /// <summary>An advisory migration lock was acquired.</summary>
    public const int AdvisoryMigrationLockAcquired = Base + 1;

    /// <summary>Acquiring an advisory migration lock timed out.</summary>
    public const int AdvisoryMigrationLockTimedOut = Base + 2;

    /// <summary>An advisory migration lock was released.</summary>
    public const int AdvisoryMigrationLockReleased = Base + 3;

    /// <summary>The row-level security privilege check found a role that bypasses it.</summary>
    public const int RowLevelSecurityPrivilegeProblem = Base + 4;

    /// <summary>The row-level security privilege check could not reach the database.</summary>
    public const int RowLevelSecurityPrivilegeCheckSkipped = Base + 5;

    /// <summary>The row-level security privilege check passed.</summary>
    public const int RowLevelSecurityPrivilegeCheckPassed = Base + 6;
}
