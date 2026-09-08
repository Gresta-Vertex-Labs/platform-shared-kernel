namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// A zero-member capability marker declaring that an <see cref="IAsymmetricKeyProvider"/>
/// implementation genuinely never performs a blocking network/IPC round trip inside
/// <see cref="IAsymmetricKeyProvider.GetRsaKeyAsync(string, CancellationToken)"/> or
/// <see cref="IAsymmetricKeyProvider.GetEcdsaKeyAsync(string, CancellationToken)"/> — both
/// members always complete synchronously (an already-completed <see cref="ValueTask{TResult}"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS AN EXPLICIT, AUTHOR-ASSERTED SAFETY CLAIM — NEVER INFERRED.</b> Implementing this
/// interface is a promise the implementing type's author makes about its own runtime behavior;
/// nothing in the platform infers or verifies it automatically. Getting the claim wrong (marking
/// a type that can actually block) reintroduces, silently, exactly the thread-blocking hazard
/// this marker exists to make structural instead of hidden.
/// </para>
/// <para>
/// <b>A KMS/HSM-BACKED PROVIDER (AZURE KEY VAULT, AWS KMS, HASHICORP VAULT, OR ANY OTHER
/// NETWORK-BOUND KEY RESOLUTION) MUST NEVER IMPLEMENT THIS MARKER.</b> Such a provider can incur
/// a genuine network/IPC round trip on any call — including a call that looks identical in shape
/// to a config-backed one. A hand-rolled implementer must be able to genuinely assert that every
/// code path behind <see cref="IAsymmetricKeyProvider.GetRsaKeyAsync(string, CancellationToken)"/> /
/// <see cref="IAsymmetricKeyProvider.GetEcdsaKeyAsync(string, CancellationToken)"/> resolves
/// without I/O before implementing this interface — e.g. a provider reading key material already
/// held in memory or resolved once from
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> at startup.
/// </para>
/// <para>
/// Consulted by <see cref="AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)"/>,
/// which <see cref="Signing.RsaSignatureService"/>/<see cref="Signing.EcdsaSignatureService"/>
/// use once, at construction time, to decide whether their retained synchronous
/// <c>Sign</c>/<c>Verify</c> members may bridge onto this provider at all, or must instead throw
/// a structural <see cref="NotSupportedException"/> directing the caller to the corresponding
/// <c>*Async</c> overload.
/// </para>
/// <para>
/// Unlike <see cref="Symmetric.ISynchronousEncryptionKeyProvider"/>'s companion
/// <see cref="Symmetric.EncryptionKeyProviderCapabilities"/>, this marker's capability check is a
/// direct check only — no decorator-unwrapping logic exists here, because no caching decorator
/// (the asymmetric analog of <see cref="Symmetric.CachedEncryptionKeyProvider"/>) exists for
/// <see cref="IAsymmetricKeyProvider"/> as of this phase (P-493/WO-081).
/// </para>
/// </remarks>
public interface ISynchronousAsymmetricKeyProvider : IAsymmetricKeyProvider;
