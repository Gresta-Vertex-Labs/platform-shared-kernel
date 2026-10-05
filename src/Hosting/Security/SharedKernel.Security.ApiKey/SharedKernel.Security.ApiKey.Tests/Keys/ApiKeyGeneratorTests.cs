using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharedKernel.Cryptography.Random;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Keys;

public sealed partial class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ValidPrefix_ProducesDocumentedShape()
    {
        ApiKeyGenerator generator = CreateGenerator("acme_live");

        GeneratedApiKey generated = generator.Generate();

        Match match = KeyShape().Match(generated.Key);
        Assert.True(match.Success, "The generated key does not match {prefix}_{16 base62}_{32 base62}{6 base62}.");
        Assert.Equal("acme_live", match.Groups["prefix"].Value);
        Assert.Equal(generated.KeyId, match.Groups["keyId"].Value);
    }

    [Fact]
    public void Generate_Key_RoundTripsThroughParser()
    {
        GeneratedApiKey generated = CreateGenerator("acme_live").Generate();

        bool parsed = ApiKeyFormat.TryParse(generated.Key, out string? prefix, out string? keyId);

        Assert.True(parsed);
        Assert.Equal("acme_live", prefix);
        Assert.Equal(generated.KeyId, keyId);
    }

    [Fact]
    public void Generate_KeyHash_IsBase64UrlSha256OfKey()
    {
        GeneratedApiKey generated = CreateGenerator("acme").Generate();

        string expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(generated.Key)));

        Assert.Equal(expected, generated.KeyHash);
    }

    [Fact]
    public void Generate_ManyKeys_KeysAndKeyIdsAreUnique()
    {
        ApiKeyGenerator generator = CreateGenerator("acme");

        GeneratedApiKey[] keys = [.. Enumerable.Range(0, 2000).Select(_ => generator.Generate())];

        Assert.Equal(keys.Length, keys.Select(k => k.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(keys.Length, keys.Select(k => k.KeyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(keys.Length, keys.Select(k => k.KeyHash).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_ManyKeys_UseTheWholeBase62Alphabet()
    {
        ApiKeyGenerator generator = CreateGenerator("acme");

        var seen = new HashSet<char>();
        for (int i = 0; i < 500; i++)
        {
            GeneratedApiKey generated = generator.Generate();
            seen.UnionWith(generated.Key["acme_".Length..^6].Replace("_", string.Empty, StringComparison.Ordinal));
        }

        Assert.Equal(62, seen.Count);
    }

    [Fact]
    public void ToString_GeneratedKey_OmitsKeyAndHash()
    {
        GeneratedApiKey generated = CreateGenerator("acme").Generate();

        string text = generated.ToString();

        Assert.Contains(generated.KeyId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(generated.Key, text, StringComparison.Ordinal);
        Assert.DoesNotContain(generated.Key["acme_".Length..], text, StringComparison.Ordinal);
        Assert.DoesNotContain(generated.KeyHash, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("1acme")]
    [InlineData("acme__live")]
    [InlineData("acme_")]
    public void Generate_InvalidPrefix_Throws(string prefix)
    {
        ApiKeyGenerator generator = CreateGenerator(prefix);

        Assert.Throws<InvalidOperationException>(() => generator.Generate());
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ApiKeyGenerator(null!, Microsoft.Extensions.Options.Options.Create(new ManagedApiKeyOptions())));
        Assert.Throws<ArgumentNullException>(() => new ApiKeyGenerator(new SecureRandomGenerator(), null!));
    }

    private static ApiKeyGenerator CreateGenerator(string prefix) =>
        new(new SecureRandomGenerator(), Microsoft.Extensions.Options.Options.Create(new ManagedApiKeyOptions { Prefix = prefix }));

    [GeneratedRegex("^(?<prefix>[a-z][a-z0-9_]{1,31})_(?<keyId>[0-9A-Za-z]{16})_(?<secret>[0-9A-Za-z]{32})(?<checksum>[0-9A-Za-z]{6})$")]
    private static partial Regex KeyShape();
}
