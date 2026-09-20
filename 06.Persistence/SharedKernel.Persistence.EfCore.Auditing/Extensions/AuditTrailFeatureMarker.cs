namespace SharedKernel.Persistence.EfCore.Auditing.Extensions;

/// <summary>
/// Marks that <c>WithAuditTrail()</c> has already run on a given <c>IServiceCollection</c> — the
/// re-entrancy guard for that method's idempotency, deliberately SEPARATE from
/// <c>IAuditTrailWriter</c>'s own registration.
/// </summary>
/// <remarks>
/// Gating re-entrancy on "is an <c>IAuditTrailWriter</c> already registered" (the pre-existing shape)
/// silently breaks the moment a consumer, a decorator, or a <c>16.Testing</c> fake registers its own
/// <c>IAuditTrailWriter</c> BEFORE calling <c>WithAuditTrail()</c> — the whole method would then
/// short-circuit before registering the options validation, the model configurator, the immutability
/// interceptor, the mutation guard, or <c>IAuditQueryService</c>, none of which have anything to do with
/// which <c>IAuditTrailWriter</c> implementation eventually wins resolution. This dedicated, empty
/// marker type carries no meaning beyond "has this method already run" — a pre-registered custom
/// writer still gets every supporting registration, and still wins resolution via
/// <c>TryAddScoped</c>.
/// </remarks>
internal sealed class AuditTrailFeatureMarker;
