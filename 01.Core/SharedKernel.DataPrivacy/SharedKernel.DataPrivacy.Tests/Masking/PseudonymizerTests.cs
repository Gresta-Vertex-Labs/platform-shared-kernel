using System.Security.Cryptography;
using System.Text;
using SharedKernel.DataPrivacy.Masking;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Masking;

public sealed class PseudonymizerTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void SameValue_GivesTheSameToken()
    {
        var pseudonymizer = new Pseudonymizer(Key);

        Assert.Equal(pseudonymizer.Pseudonymize("customer-42"), new Pseudonymizer(Key).Pseudonymize("customer-42"));
        Assert.NotEqual(pseudonymizer.Pseudonymize("customer-42"), pseudonymizer.Pseudonymize("customer-43"));
    }

    [Fact]
    public void Token_IsTheFirst128BitsOfTheHmacInBase64Url()
    {
        byte[] hmac = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes("customer-42"));
        string expected = Convert.ToBase64String(hmac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        string token = new Pseudonymizer(Key).Pseudonymize("customer-42");

        Assert.Equal(expected, token);
        Assert.Equal(Pseudonymizer.TokenLength, token.Length);
    }

    [Fact]
    public void DifferentKey_GivesADifferentToken()
    {
        byte[] other = [.. Key];
        other[0] ^= 0xFF;

        Assert.NotEqual(new Pseudonymizer(Key).Pseudonymize("x"), new Pseudonymizer(other).Pseudonymize("x"));
    }

    [Fact]
    public void LongInput_IsHashedInFull()
    {
        var pseudonymizer = new Pseudonymizer(Key);
        string a = new('a', 1000);
        string b = new string('a', 999) + "b";

        Assert.NotEqual(pseudonymizer.Pseudonymize(a), pseudonymizer.Pseudonymize(b));
        Assert.Equal(Pseudonymizer.TokenLength, pseudonymizer.Pseudonymize(a).Length);
    }

    [Fact]
    public void EmptyInput_GivesEmpty()
    {
        var pseudonymizer = new Pseudonymizer(Key);

        Assert.Equal(string.Empty, pseudonymizer.Pseudonymize((string?)null));
        Assert.Equal(string.Empty, pseudonymizer.Pseudonymize(string.Empty));
    }

    [Fact]
    public void ShortKey_Throws() =>
        Assert.Throws<ArgumentException>(() => new Pseudonymizer(new byte[31]));

    [Fact]
    public void Key_IsCopied()
    {
        byte[] key = [.. Key];
        var pseudonymizer = new Pseudonymizer(key);
        string before = pseudonymizer.Pseudonymize("x");

        key[0] ^= 0xFF;

        Assert.Equal(before, pseudonymizer.Pseudonymize("x"));
    }
}
