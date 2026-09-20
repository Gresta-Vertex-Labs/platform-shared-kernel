namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// Classifies the kind of actor a persistence-layer write or audit entry is attributed to.
/// </summary>
public enum ActorKind
{
    /// <summary>A human end user, typically identified by an authenticated subject id.</summary>
    User = 0,

    /// <summary>A machine caller acting on its own identity (service-to-service, API key, mTLS).</summary>
    Service = 1,

    /// <summary>
    /// The platform itself, acting with no caller identity present (a background job, a startup
    /// seeder, or an unauthenticated fallback). This is the failure-mode default — never confused
    /// with <see cref="Service"/>, which always names a specific machine identity.
    /// </summary>
    System = 2,
}
