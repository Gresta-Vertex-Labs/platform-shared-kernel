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
    /// The platform itself, acting under a deliberate system identity (a background job, a startup seeder,
    /// typically a <see cref="SystemRequestContext"/>). Never confused with <see cref="Service"/>, which always
    /// names a specific machine identity, nor with <see cref="Anonymous"/>.
    /// </summary>
    System = 2,

    /// <summary>
    /// An unauthenticated caller: nobody was identified. Kept distinct from <see cref="System"/> so that an
    /// audit trail never presents an anonymous request as the platform's own background work.
    /// </summary>
    Anonymous = 3,
}
