using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Primitives.Clocks;

namespace Shop.Ordering.Domain;

/// <summary>Strongly-typed identifier of a <see cref="TotpEnrollment"/>.</summary>
public sealed record TotpEnrollmentId(Guid Value) : StronglyTypedId<Guid>(Value);

/// <summary>
/// A user's authenticator-app secret, used for step-up before sensitive actions. Not tenant data: it belongs to the
/// identity. The secret is encrypted at rest.
/// </summary>
public sealed class TotpEnrollment : AuditableAggregateRoot<TotpEnrollmentId>
{
    private TotpEnrollment(TotpEnrollmentId id, string subjectId, string secretBase32, IClock clock)
        : base(id, clock)
    {
        SubjectId = subjectId;
        SecretBase32 = secretBase32;
    }

    private TotpEnrollment() { }

    /// <summary>The identity provider's subject id.</summary>
    public string SubjectId { get; private set; } = string.Empty;

    /// <summary>The shared secret, base32 (RFC 4648), encrypted at rest.</summary>
    public string SecretBase32 { get; private set; } = string.Empty;

    public static TotpEnrollment Create(string subjectId, string secretBase32, IClock clock) =>
        new(new TotpEnrollmentId(Guid.CreateVersion7()), subjectId, secretBase32, clock);

    /// <summary>Replaces the secret (re-enrollment).</summary>
    public void Replace(string secretBase32) => SecretBase32 = secretBase32;
}
