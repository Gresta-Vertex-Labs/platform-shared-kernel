namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// A zero-member capability marker declaring that an <see cref="IEncryptionKeyProvider"/>
/// implementation genuinely never performs a blocking network/IPC round trip inside
/// <see cref="IEncryptionKeyProvider.GetCurrentKeyAsync(CancellationToken)"/> or
/// <see cref="IEncryptionKeyProvider.GetKeyAsync(string, CancellationToken)"/> — both members
/// always complete synchronously (an already-completed <see cref="ValueTask{TResult}"/>).
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
/// code path behind <see cref="IEncryptionKeyProvider.GetCurrentKeyAsync(CancellationToken)"/> /
/// <see cref="IEncryptionKeyProvider.GetKeyAsync(string, CancellationToken)"/> resolves without
/// I/O before implementing this interface — e.g. a provider reading key material already held in
/// memory or resolved once from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// at startup.
/// </para>
/// <para>
/// Consulted by <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/>,
/// which <see cref="Symmetric.AesGcmEncryptionService"/> uses once, at construction time, to
/// decide whether its retained synchronous <c>Encrypt</c>/<c>Decrypt</c>/<c>EncryptToString</c>/
/// <c>DecryptToString</c> members may bridge onto this provider at all, or must instead throw a
/// structural <see cref="NotSupportedException"/> directing the caller to the corresponding
/// <c>*Async</c> overload.
/// </para>
/// </remarks>
public interface ISynchronousEncryptionKeyProvider : IEncryptionKeyProvider;
