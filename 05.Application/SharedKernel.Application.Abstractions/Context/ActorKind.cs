namespace SharedKernel.Application.Context;

/// <summary>
/// Classifies the kind of actor a request, a persisted change or an audit record is attributed to.
/// </summary>
public enum ActorKind
{
    /// <summary>A human end user, identified by an authenticated subject id.</summary>
    User = 0,

    /// <summary>A machine caller acting on its own identity (service-to-service, API key, mTLS).</summary>
    Service = 1,

    /// <summary>
    /// The platform itself, acting with no caller identity (a background job, a startup seeder, or an
    /// unauthenticated request). Never confused with <see cref="Service"/>, which always names a
    /// specific machine identity.
    /// </summary>
    System = 2,
}
