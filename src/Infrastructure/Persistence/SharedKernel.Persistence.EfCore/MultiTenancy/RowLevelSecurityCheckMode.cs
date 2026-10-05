using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence;

/// <summary>What the startup check of row-level security coverage does when a tenant table is not protected.</summary>
public enum RowLevelSecurityCheckMode
{
    /// <summary>Fails startup. The default outside the Development environment.</summary>
    Fail = 0,

    /// <summary>Logs a warning per unprotected table and starts. The default in the Development environment.</summary>
    Warn = 1,

    /// <summary>Skips the check.</summary>
    Off = 2,
}
