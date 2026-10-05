using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.KeyDerivation;

namespace SharedKernel.Cryptography.Tests.KeyDerivation;

public sealed class SubkeyDerivationTests
{
    private static readonly byte[] RootKey = [.. Enumerable.Range(0, 32).Select(i => (byte)(i * 7))];

    [Fact]
    public void DeriveKey_SameInputs_ReturnsSameKey()
    {
        byte[] first = SubkeyDerivation.DeriveKey(RootKey, "documents", "tenant-1"u8);
        byte[] second = SubkeyDerivation.DeriveKey(RootKey, "documents", "tenant-1"u8);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(RootKey, first);
    }

    [Fact]
    public void DeriveKey_DifferentPurpose_ReturnsDifferentKey()
    {
        Assert.NotEqual(
            SubkeyDerivation.DeriveKey(RootKey, "documents", "tenant-1"u8),
            SubkeyDerivation.DeriveKey(RootKey, "webhooks", "tenant-1"u8));
    }

    [Fact]
    public void DeriveKey_DifferentContext_ReturnsDifferentKey()
    {
        Assert.NotEqual(
            SubkeyDerivation.DeriveKey(RootKey, "documents", "tenant-1"u8),
            SubkeyDerivation.DeriveKey(RootKey, "documents", "tenant-2"u8));
        Assert.NotEqual(
            SubkeyDerivation.DeriveKey(RootKey, "documents", ReadOnlySpan<byte>.Empty),
            SubkeyDerivation.DeriveKey(RootKey, "documents", "\0"u8));
    }

    [Fact]
    public void DeriveKey_DifferentRootKey_ReturnsDifferentKey()
    {
        byte[] otherRoot = [.. RootKey];
        otherRoot[^1] ^= 0x01;

        Assert.NotEqual(
            SubkeyDerivation.DeriveKey(RootKey, "documents", ReadOnlySpan<byte>.Empty),
            SubkeyDerivation.DeriveKey(otherRoot, "documents", ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("ab", "c", "a", "bc")]
    [InlineData("tenant", "1", "tenant1", "")]
    [InlineData("x", "yz", "xy", "z")]
    public void DeriveKey_PairsWhoseConcatenationCollides_ReturnDifferentKeys(
        string purpose1,
        string context1,
        string purpose2,
        string context2)
    {
        Assert.Equal(purpose1 + context1, purpose2 + context2);

        Assert.NotEqual(
            SubkeyDerivation.DeriveKey(RootKey, purpose1, Encoding.UTF8.GetBytes(context1)),
            SubkeyDerivation.DeriveKey(RootKey, purpose2, Encoding.UTF8.GetBytes(context2)));
    }

    [Fact]
    public void DeriveKey_ContextThatMimicsALengthPrefix_ReturnsDifferentKey()
    {
        byte[] first = SubkeyDerivation.DeriveKey(RootKey, "a", ReadOnlySpan<byte>.Empty);
        byte[] second = SubkeyDerivation.DeriveKey(RootKey, "a", [0, 0, 0, 0]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeriveKey_MatchesHkdfWithDocumentedInfoLayout()
    {
        const string purpose = "documents-şğ";
        byte[] context = Encoding.UTF8.GetBytes("tenant-42");
        byte[] purposeBytes = Encoding.UTF8.GetBytes(purpose);
        byte[] info = new byte[4 + purposeBytes.Length + 4 + context.Length];
        BinaryPrimitives.WriteInt32BigEndian(info, purposeBytes.Length);
        purposeBytes.CopyTo(info, 4);
        BinaryPrimitives.WriteInt32BigEndian(info.AsSpan(4 + purposeBytes.Length), context.Length);
        context.CopyTo(info, 8 + purposeBytes.Length);

        byte[] expected = HKDF.DeriveKey(HashAlgorithmName.SHA256, RootKey, 32, salt: [], info);

        Assert.Equal(expected, SubkeyDerivation.DeriveKey(RootKey, purpose, context));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void DeriveKey_SpanOverload_MatchesArrayOverload(int length)
    {
        byte[] destination = new byte[length];

        SubkeyDerivation.DeriveKey(RootKey, "documents", "ctx"u8, destination);

        Assert.Equal(SubkeyDerivation.DeriveKey(RootKey, "documents", "ctx"u8, length), destination);
    }

    [Fact]
    public void DeriveKey_RootKeyOfAnyLengthAtLeast32_IsAccepted()
    {
        byte[] longRoot = RandomNumberGenerator.GetBytes(64);

        Assert.Equal(32, SubkeyDerivation.DeriveKey(longRoot, "p", ReadOnlySpan<byte>.Empty).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    public void DeriveKey_RootKeyShorterThan32Bytes_Throws(int length)
    {
        byte[] root = new byte[length];

        Assert.Throws<ArgumentException>(() => SubkeyDerivation.DeriveKey(root, "p", ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => SubkeyDerivation.DeriveKey(root, "p", ReadOnlySpan<byte>.Empty, new byte[32]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(65)]
    public void DeriveKey_SubkeyLengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SubkeyDerivation.DeriveKey(RootKey, "p", ReadOnlySpan<byte>.Empty, length));
        Assert.Throws<ArgumentException>(() => SubkeyDerivation.DeriveKey(RootKey, "p", ReadOnlySpan<byte>.Empty, new byte[length]));
    }

    [Fact]
    public void DeriveKey_EmptyOrNullPurpose_Throws()
    {
        Assert.Throws<ArgumentException>(() => SubkeyDerivation.DeriveKey(RootKey, string.Empty, ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => SubkeyDerivation.DeriveKey(RootKey, null!, ReadOnlySpan<byte>.Empty));
    }
}
