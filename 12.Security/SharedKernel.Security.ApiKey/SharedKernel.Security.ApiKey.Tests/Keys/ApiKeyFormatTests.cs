using SharedKernel.Security.ApiKey.Keys;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Keys;

public sealed class ApiKeyFormatTests
{
    private const string KeyId = "0123456789ABCDEF";
    private const string Secret = "abcdefghijklmnopqrstuvwxyzABCDEF";

    [Theory]
    [InlineData("ab")]
    [InlineData("acme")]
    [InlineData("acme_live")]
    [InlineData("a1")]
    [InlineData("sk_test_2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void IsValidPrefix_ValidPrefix_ReturnsTrue(string prefix)
    {
        Assert.True(ApiKeyFormat.IsValidPrefix(prefix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    [InlineData("Acme")]
    [InlineData("acmE")]
    [InlineData("1acme")]
    [InlineData("_acme")]
    [InlineData("acme__live")]
    [InlineData("acme_")]
    [InlineData("acme-live")]
    [InlineData("acme live")]
    [InlineData("acmé")]
    public void IsValidPrefix_InvalidPrefix_ReturnsFalse(string? prefix)
    {
        Assert.False(ApiKeyFormat.IsValidPrefix(prefix));
    }

    [Fact]
    public void Compose_ValidParts_ProducesPrefixKeyIdSecretAndChecksum()
    {
        string key = ApiKeyFormat.Compose("acme_live", KeyId, Secret);

        Assert.StartsWith($"acme_live_{KeyId}_{Secret}", key, StringComparison.Ordinal);
        Assert.Equal("acme_live".Length + 1 + 16 + 1 + 32 + 6, key.Length);
        Assert.True(key[^6..].All(c => ApiKeyFormat.Alphabet.Contains(c)));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("acme")]
    [InlineData("acme_live")]
    [InlineData("a_b_c_d")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void TryParse_ComposedKey_ReturnsPrefixAndKeyId(string prefix)
    {
        string key = ApiKeyFormat.Compose(prefix, KeyId, Secret);

        bool parsed = ApiKeyFormat.TryParse(key, out string? parsedPrefix, out string? parsedKeyId);

        Assert.True(parsed);
        Assert.Equal(prefix, parsedPrefix);
        Assert.Equal(KeyId, parsedKeyId);
    }

    [Fact]
    public void Compose_SameInput_IsDeterministic()
    {
        Assert.Equal(ApiKeyFormat.Compose("acme", KeyId, Secret), ApiKeyFormat.Compose("acme", KeyId, Secret));
    }

    [Fact]
    public void TryParse_AnySingleCharacterChanged_ReturnsFalse()
    {
        string key = ApiKeyFormat.Compose("acme_live", KeyId, Secret);

        for (int i = 0; i < key.Length; i++)
        {
            foreach (char replacement in new[] { '0', 'z', 'Q', '_' })
            {
                if (key[i] == replacement)
                {
                    continue;
                }

                string mutated = string.Concat(key.AsSpan(0, i), replacement.ToString(), key.AsSpan(i + 1));

                Assert.False(
                    ApiKeyFormat.TryParse(mutated, out _, out _),
                    $"A key with position {i} changed to '{replacement}' was accepted.");
            }
        }
    }

    [Fact]
    public void TryParse_ChecksumFromDifferentBody_ReturnsFalse()
    {
        string key = ApiKeyFormat.Compose("acme", KeyId, Secret);
        string other = ApiKeyFormat.Compose("acme", KeyId, "bcdefghijklmnopqrstuvwxyzABCDEFG");

        string swapped = key[..^6] + other[^6..];

        Assert.False(ApiKeyFormat.TryParse(swapped, out _, out _));
    }

    [Fact]
    public void TryParse_ChecksumOfWrongPrefix_ReturnsFalse()
    {
        string live = ApiKeyFormat.Compose("acme_live", KeyId, Secret);

        string relabelled = "acme_test" + live["acme_live".Length..];

        Assert.False(ApiKeyFormat.TryParse(relabelled, out _, out _));
    }

    [Theory]
    [InlineData(15, 32)]
    [InlineData(17, 32)]
    [InlineData(16, 31)]
    [InlineData(16, 33)]
    public void TryParse_WrongSegmentLength_ReturnsFalse(int keyIdLength, int secretLength)
    {
        string keyId = new('K', keyIdLength);
        string secret = new('s', secretLength);
        string key = ApiKeyFormat.Compose("acme", keyId, secret);

        Assert.False(ApiKeyFormat.TryParse(key, out _, out _));
    }

    [Theory]
    [InlineData("0123456789ABCDE-")]
    [InlineData("0123456789ABCDE_")]
    [InlineData("0123456789ABCDE+")]
    [InlineData("0123456789ABCDEé")]
    public void TryParse_NonBase62KeyId_ReturnsFalse(string keyId)
    {
        string key = ApiKeyFormat.Compose("acme", keyId, Secret);

        Assert.False(ApiKeyFormat.TryParse(key, out _, out _));
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDE/")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDE=")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCD E")]
    public void TryParse_NonBase62Secret_ReturnsFalse(string secret)
    {
        string key = ApiKeyFormat.Compose("acme", KeyId, secret);

        Assert.False(ApiKeyFormat.TryParse(key, out _, out _));
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("1acme")]
    [InlineData("acme__live")]
    [InlineData("acme_")]
    [InlineData("a")]
    public void TryParse_InvalidPrefixWithValidChecksum_ReturnsFalse(string prefix)
    {
        string key = ApiKeyFormat.Compose(prefix, KeyId, Secret);

        Assert.False(ApiKeyFormat.TryParse(key, out _, out _));
    }

    [Fact]
    public void TryParse_PrefixLongerThanMaximum_ReturnsFalse()
    {
        string key = ApiKeyFormat.Compose(new string('a', 33), KeyId, Secret);

        Assert.False(ApiKeyFormat.TryParse(key, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme")]
    [InlineData("not-an-api-key")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature")]
    public void TryParse_ArbitraryString_ReturnsFalse(string value)
    {
        Assert.False(ApiKeyFormat.TryParse(value, out string? prefix, out string? keyId));
        Assert.Null(prefix);
        Assert.Null(keyId);
    }

    [Fact]
    public void TryParse_MissingSeparators_ReturnsFalse()
    {
        string key = ApiKeyFormat.Compose("acme", KeyId, Secret);
        string withoutSeparator = key.Replace($"{KeyId}_", KeyId + "x", StringComparison.Ordinal);

        Assert.False(ApiKeyFormat.TryParse(withoutSeparator, out _, out _));
    }

    [Fact]
    public void Hash_Key_IsBase64UrlSha256OfAsciiBytes()
    {
        string key = ApiKeyFormat.Compose("acme", KeyId, Secret);
        byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(key));

        string hash = ApiKeyFormat.Hash(key);

        Assert.Equal(System.Buffers.Text.Base64Url.EncodeToString(digest), hash);
        Assert.Equal(43, hash.Length);
        Assert.DoesNotContain('=', hash);
        Assert.DoesNotContain('+', hash);
        Assert.DoesNotContain('/', hash);
    }

    [Fact]
    public void Hash_DifferentKeys_Differ()
    {
        Assert.NotEqual(
            ApiKeyFormat.Hash(ApiKeyFormat.Compose("acme", KeyId, Secret)),
            ApiKeyFormat.Hash(ApiKeyFormat.Compose("acme", KeyId, "bcdefghijklmnopqrstuvwxyzABCDEFG")));
    }
}
