namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Optional readiness-probe companion contract for an <see cref="IEncryptionKeyProvider"/> and/or
/// <see cref="IEnvelopeEncryptionProvider"/> implementation backed by a real external dependency
/// (a KMS/HSM) worth checking for reachability.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in and additive.</b> Distinct from <see cref="IEncryptionKeyProvider"/> and
/// <see cref="IEnvelopeEncryptionProvider"/> — neither of those contracts changes. A provider
/// backed by nothing external (a configuration-bound key list, an in-memory or null provider) has
/// no reachability to probe and is not expected to implement this interface; it stays opt-in by
/// design, implemented only by a provider that genuinely has something external to check.
/// </para>
/// <para>
/// <b>Never a cryptographic operation.</b> The implementation must query the real,
/// already-configured external dependency via a cheap, read-only, non-cryptographic call (e.g.
/// resolving a key's current metadata/version) — it must never perform a genuine wrap, unwrap,
/// sign, or verify operation as part of the probe. Those register as real key usage in a KMS's own
/// audit trail, which a readiness check must not generate as a side effect.
/// </para>
/// <para>
/// <b>Never throws for an ordinary reachability failure.</b> An unreachable or unauthorized
/// dependency is reported as <see cref="EncryptionKeyProviderHealth.IsHealthy"/>
/// <see langword="false"/>, not a thrown exception — mirroring <c>07.Messaging</c>'s
/// <c>IMessageBusProbe</c>/<c>MessageBusHealth</c> shape, which this contract deliberately
/// mirrors. This is a narrow, deliberate carve-out from a KMS-backed provider's usual
/// fail-closed-via-exception contract (see <see cref="IEncryptionKeyProvider"/>'s own remarks): a
/// readiness probe's entire purpose is to report status to a health-check pipeline, not to gate a
/// cryptographic operation, so it must behave like every other health check on this platform and
/// surface failure as data rather than as a thrown exception. A genuinely unexpected/programmer
/// error (e.g. a null argument on a hypothetical future overload) is not "an ordinary reachability
/// failure" and is not covered by this carve-out.
/// </para>
/// <para>
/// <c>SharedKernel.Cryptography</c> ships this probe primitive only, never an
/// <c>IHealthCheck</c> implementation — matching every existing readiness-probe precedent on this
/// platform (<c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c>/<c>10.Intelligence</c>/
/// <c>07.Messaging</c>/<c>17.Workflows</c>/<c>19.Scheduling</c>). Wiring an implementation into
/// <c>AddHealthChecks()</c> remains <c>13.ServiceDefaults</c>'s concern.
/// </para>
/// </remarks>
public interface IEncryptionKeyProviderProbe
{
    /// <summary>
    /// Reports the current reachability of the external key-management dependency this provider
    /// is backed by.
    /// </summary>
    /// <param name="ct">A token to observe while probing.</param>
    /// <returns>
    /// An <see cref="EncryptionKeyProviderHealth"/> describing whether the dependency is reachable.
    /// </returns>
    Task<EncryptionKeyProviderHealth> ProbeAsync(CancellationToken ct = default);
}
