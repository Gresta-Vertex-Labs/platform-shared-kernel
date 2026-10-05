using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Cryptography.Tests.KeyDerivation;

public sealed class PurposeBoundEncryptionKeyProviderTests
{
    private static readonly byte[] AssociatedData = "row-1"u8.ToArray();

    private readonly CryptographicKey _root = TestKeys.Create("root-2026");
    private readonly StaticEncryptionKeyProvider _inner;

    public PurposeBoundEncryptionKeyProviderTests()
    {
        _inner = new StaticEncryptionKeyProvider("root-2026", [_root]);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_ReturnsDerivedKeyWithSameId()
    {
        var provider = new PurposeBoundEncryptionKeyProvider(_inner, "documents", "tenant-1"u8);

        CryptographicKey key = await provider.GetCurrentKeyAsync();

        Assert.Equal("root-2026", key.Id);
        Assert.Equal(32, key.Material.Length);
        Assert.NotEqual(_root.Material.ToArray(), key.Material.ToArray());
        Assert.Equal(SubkeyDerivation.DeriveKey(_root.Material, "documents", "tenant-1"u8), key.Material.ToArray());
    }

    [Fact]
    public async Task GetKeyAsync_ReturnsSameDerivedKeyAsCurrentKey()
    {
        var provider = new PurposeBoundEncryptionKeyProvider(_inner, "documents", "tenant-1"u8);

        CryptographicKey current = await provider.GetCurrentKeyAsync();
        CryptographicKey? byId = await provider.GetKeyAsync("root-2026");

        Assert.NotNull(byId);
        Assert.Equal(current.Material.ToArray(), byId.Material.ToArray());
    }

    [Fact]
    public async Task Derivation_IsDeterministicAcrossInstances()
    {
        var first = new PurposeBoundEncryptionKeyProvider(_inner, "documents", "tenant-1"u8);
        var second = new PurposeBoundEncryptionKeyProvider(_inner, "documents", "tenant-1"u8);

        Assert.Equal((await first.GetCurrentKeyAsync()).Material.ToArray(), (await second.GetCurrentKeyAsync()).Material.ToArray());
    }

    [Fact]
    public async Task GetKeyAsync_UnknownId_ReturnsNull()
    {
        var provider = new PurposeBoundEncryptionKeyProvider(_inner, "documents", ReadOnlySpan<byte>.Empty);

        Assert.Null(await provider.GetKeyAsync("missing"));
    }

    [Fact]
    public async Task Payload_EncryptedForOnePurpose_DoesNotDecryptUnderAnother()
    {
        var documents = new AesGcmEncryptionService(_inner.ForPurpose("documents"));
        var webhooks = new AesGcmEncryptionService(_inner.ForPurpose("webhooks"));
        var root = new AesGcmEncryptionService(_inner);

        EncryptedPayload payload = await documents.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);

        Assert.Equal([1, 2, 3], (await documents.DecryptAsync(payload, AssociatedData)).Value);
        ResultAssert.Failure(await webhooks.DecryptAsync(payload, AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
        ResultAssert.Failure(await root.DecryptAsync(payload, AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task Payload_EncryptedForOneContext_DoesNotDecryptUnderAnother()
    {
        var tenant1 = new AesGcmEncryptionService(_inner.ForPurpose("documents", "tenant-1"u8));
        var tenant2 = new AesGcmEncryptionService(_inner.ForPurpose("documents", "tenant-2"u8));
        var tenant1Again = new AesGcmEncryptionService(_inner.ForPurpose("documents", "tenant-1"u8));

        EncryptedPayload payload = await tenant1.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);

        Assert.Equal([1, 2, 3], (await tenant1Again.DecryptAsync(payload, AssociatedData)).Value);
        ResultAssert.Failure(await tenant2.DecryptAsync(payload, AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task Rotation_OfInnerProvider_CarriesThrough()
    {
        CryptographicKey newRoot = TestKeys.Create("root-2027");
        var before = new AesGcmEncryptionService(new StaticEncryptionKeyProvider("root-2026", [_root]).ForPurpose("documents"));
        var after = new AesGcmEncryptionService(new StaticEncryptionKeyProvider("root-2027", [_root, newRoot]).ForPurpose("documents"));

        EncryptedPayload oldPayload = await before.EncryptAsync(new byte[] { 7 }, AssociatedData);
        EncryptedPayload newPayload = await after.EncryptAsync(new byte[] { 8 }, AssociatedData);

        Assert.Equal("root-2027", newPayload.KeyId);
        Assert.Equal([7], (await after.DecryptAsync(oldPayload, AssociatedData)).Value);
    }

    [Fact]
    public void Constructor_CopiesContext()
    {
        byte[] context = "tenant-1"u8.ToArray();
        var provider = new PurposeBoundSynchronousEncryptionKeyProvider(_inner, "documents", context);

        context[0] = (byte)'X';

        Assert.Equal(SubkeyDerivation.DeriveKey(_root.Material, "documents", "tenant-1"u8), provider.GetCurrentKey().Material.ToArray());
    }

    [Fact]
    public async Task SynchronousProvider_DerivesSameKeysAsAsynchronousProvider()
    {
        PurposeBoundEncryptionKeyProvider asyncProvider = _inner.ForPurpose("documents", "tenant-1"u8);
        PurposeBoundSynchronousEncryptionKeyProvider syncProvider = _inner.ForPurposeSynchronous("documents", "tenant-1"u8);

        CryptographicKey asyncKey = await asyncProvider.GetCurrentKeyAsync();
        CryptographicKey syncKey = syncProvider.GetCurrentKey();

        Assert.Equal(asyncKey.Id, syncKey.Id);
        Assert.Equal(asyncKey.Material.ToArray(), syncKey.Material.ToArray());
        Assert.Equal(asyncKey.Material.ToArray(), syncProvider.GetKey("root-2026")!.Material.ToArray());
        Assert.Null(syncProvider.GetKey("missing"));
    }

    [Fact]
    public async Task SynchronousAndAsynchronousServices_InteroperateForSamePurpose()
    {
        var syncService = new SynchronousAesGcmEncryptionService(_inner.ForPurposeSynchronous("documents"));
        var asyncService = new AesGcmEncryptionService(_inner.ForPurpose("documents"));

        EncryptedPayload payload = syncService.Encrypt([5, 6], AssociatedData);

        Assert.Equal([5, 6], (await asyncService.DecryptAsync(payload, AssociatedData)).Value);
    }

    [Fact]
    public async Task RootKeyShorterThan32Bytes_Throws()
    {
        var shortRoot = new StaticEncryptionKeyProvider("short", [TestKeys.Create("short", 16)]);

        await Assert.ThrowsAsync<ArgumentException>(async () => await shortRoot.ForPurpose("documents").GetCurrentKeyAsync());
        Assert.Throws<ArgumentException>(() => shortRoot.ForPurposeSynchronous("documents").GetCurrentKey());
    }

    [Fact]
    public void RootKeyLongerThan32Bytes_DerivesA32ByteKey()
    {
        var longRoot = new StaticEncryptionKeyProvider("long", [TestKeys.Create("long", 64)]);

        Assert.Equal(32, longRoot.ForPurposeSynchronous("documents").GetCurrentKey().Material.Length);
    }

    [Fact]
    public void Constructors_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new PurposeBoundEncryptionKeyProvider(null!, "p", ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => new PurposeBoundEncryptionKeyProvider(_inner, string.Empty, ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => new PurposeBoundEncryptionKeyProvider(_inner, null!, ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => new PurposeBoundSynchronousEncryptionKeyProvider(null!, "p", ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => new PurposeBoundSynchronousEncryptionKeyProvider(_inner, string.Empty, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Extensions_OnProviderImplementingBothInterfaces_ResolveWithoutAmbiguity()
    {
        StaticEncryptionKeyProvider provider = _inner;

        PurposeBoundEncryptionKeyProvider asyncProvider = provider.ForPurpose("documents");
        PurposeBoundSynchronousEncryptionKeyProvider syncProvider = provider.ForPurposeSynchronous("documents");

        Assert.NotNull(asyncProvider);
        Assert.NotNull(syncProvider);
    }
}
