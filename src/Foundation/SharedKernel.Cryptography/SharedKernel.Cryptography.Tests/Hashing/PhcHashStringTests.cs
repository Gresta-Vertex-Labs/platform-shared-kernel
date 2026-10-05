using System.Text;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class PhcHashStringTests
{
    // "saltsalt" and "hash" in unpadded standard Base64.
    private const string Salt = "c2FsdHNhbHQ";
    private const string Hash = "aGFzaA";

    private static readonly byte[] SaltBytes = Encoding.ASCII.GetBytes("saltsalt");
    private static readonly byte[] HashBytes = Encoding.ASCII.GetBytes("hash");

    [Fact]
    public void ToString_WithVersionAndParameters_WritesPhcFormat()
    {
        var phc = new PhcHashString("argon2id", 19, [new("m", "65536"), new("t", "3"), new("p", "4")], SaltBytes, HashBytes);

        Assert.Equal($"$argon2id$v=19$m=65536,t=3,p=4${Salt}${Hash}", phc.ToString());
    }

    [Theory]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$pbkdf2-sha256$i=600000$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$argon2id$v=19$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$scrypt$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$a=x/y+z.w-1$c2FsdHNhbHQ$aGFzaA")]
    public void TryParse_WellFormed_RoundTrips(string value)
    {
        Assert.True(PhcHashString.TryParse(value, out PhcHashString? parsed));
        Assert.Equal(value, parsed.ToString());
    }

    [Fact]
    public void TryParse_WithVersionAndParameters_ExposesParts()
    {
        Assert.True(PhcHashString.TryParse($"$argon2id$v=19$m=65536,t=3${Salt}${Hash}", out PhcHashString? parsed));

        Assert.Equal("argon2id", parsed.AlgorithmId);
        Assert.Equal(19, parsed.Version);
        Assert.Equal([new KeyValuePair<string, string>("m", "65536"), new("t", "3")], parsed.Parameters);
        Assert.Equal(SaltBytes, parsed.Salt.ToArray());
        Assert.Equal(HashBytes, parsed.Hash.ToArray());
    }

    [Fact]
    public void TryParse_WithoutVersionOrParameters_ExposesParts()
    {
        Assert.True(PhcHashString.TryParse($"$scrypt${Salt}${Hash}", out PhcHashString? parsed));

        Assert.Equal("scrypt", parsed.AlgorithmId);
        Assert.Null(parsed.Version);
        Assert.Empty(parsed.Parameters);
        Assert.Equal(SaltBytes, parsed.Salt.ToArray());
        Assert.Equal(HashBytes, parsed.Hash.ToArray());
    }

    [Fact]
    public void TryParse_VersionZero_IsAccepted()
    {
        Assert.True(PhcHashString.TryParse($"$alg$v=0${Salt}${Hash}", out PhcHashString? parsed));
        Assert.Equal(0, parsed.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("$a$b")]
    [InlineData("pbkdf2-sha256$i=1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$PBKDF2$i=1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$pbkdf2$I=1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$$aGFzaA")]
    [InlineData("$alg$c2FsdHNhbHQ$")]
    [InlineData("$alg$c2FsdHNhbHQ=$aGFzaA")]
    [InlineData("$alg$c2FsdHNhbHQ$aGFzaA==")]
    [InlineData("$alg$c2FsdHNhbHQ$aGFzaB")]
    [InlineData("$alg$c2FsdHNhbHR$aGFzaA")]
    [InlineData("$alg$c2FsdHNhbHQ$a")]
    [InlineData("$alg$c2Fsd*NhbHQ$aGFzaA")]
    [InlineData("$alg$a=1,a=2$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$m=1,v=2$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$v=1$v=2$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$v=01$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$v=$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$v=-1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$novalue$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$=1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$a=$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$a=x*y$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$A=1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$alg$v=1$a=1$c2FsdHNhbHQ$aGFzaA$extra")]
    [InlineData("$alg$a=1$b=2$c2FsdHNhbHQ$aGFzaA")]
    public void TryParse_Malformed_ReturnsFalse(string? value)
    {
        Assert.False(PhcHashString.TryParse(value, out PhcHashString? parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_LongerThanMaxLength_ReturnsFalse()
    {
        string salt = Convert.ToBase64String(new byte[800]).TrimEnd('=');
        string value = $"$alg${salt}${Hash}";
        Assert.True(value.Length > PhcHashString.MaxLength);

        Assert.False(PhcHashString.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_AtMaxLength_IsAccepted()
    {
        string prefix = "$alg$";
        string suffix = $"${Hash}";
        int saltChars = PhcHashString.MaxLength - prefix.Length - suffix.Length;
        int saltBytes = saltChars / 4 * 3;
        string salt = Convert.ToBase64String(new byte[saltBytes]).TrimEnd('=');
        string value = prefix + salt + suffix;
        Assert.True(value.Length <= PhcHashString.MaxLength);

        Assert.True(PhcHashString.TryParse(value, out _));
    }

    [Fact]
    public void TryGetParameter_ReturnsValueWhenPresent()
    {
        PhcHashString phc = Create(("i", "100000"), ("k", "p1"));

        Assert.True(phc.TryGetParameter("k", out string? value));
        Assert.Equal("p1", value);
        Assert.False(phc.TryGetParameter("m", out string? missing));
        Assert.Null(missing);
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("7", true, 7)]
    [InlineData("100000", true, 100000)]
    [InlineData("2147483647", true, int.MaxValue)]
    [InlineData("0100", false, 0)]
    [InlineData("00", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("+1", false, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("2147483648", false, 0)]
    public void TryGetInt32Parameter_AcceptsOnlyCanonicalNonNegativeIntegers(string text, bool expected, int expectedValue)
    {
        PhcHashString phc = Create(("i", text));

        Assert.Equal(expected, phc.TryGetInt32Parameter("i", out int value));
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void TryGetInt32Parameter_Absent_ReturnsFalse()
    {
        Assert.False(Create().TryGetInt32Parameter("i", out _));
    }

    [Fact]
    public void WithParameter_Absent_AppendsAtEnd()
    {
        PhcHashString original = Create(("i", "1"));

        PhcHashString updated = original.WithParameter("k", "p1");

        Assert.Equal([new KeyValuePair<string, string>("i", "1"), new("k", "p1")], updated.Parameters);
        Assert.Single(original.Parameters);
        Assert.Equal(original.Salt.ToArray(), updated.Salt.ToArray());
        Assert.Equal(original.Hash.ToArray(), updated.Hash.ToArray());
    }

    [Fact]
    public void WithParameter_Present_ReplacesInPlace()
    {
        PhcHashString original = Create(("a", "1"), ("b", "2"), ("c", "3"));

        PhcHashString updated = original.WithParameter("b", "20");

        Assert.Equal([new KeyValuePair<string, string>("a", "1"), new("b", "20"), new("c", "3")], updated.Parameters);
        Assert.Equal("2", original.Parameters[1].Value);
    }

    [Fact]
    public void WithParameter_InvalidValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => Create().WithParameter("k", "bad value"));
    }

    [Fact]
    public void WithoutParameter_Present_RemovesIt()
    {
        PhcHashString original = Create(("i", "1"), ("k", "p1"));

        PhcHashString updated = original.WithoutParameter("k");

        Assert.Equal([new KeyValuePair<string, string>("i", "1")], updated.Parameters);
        Assert.Equal(2, original.Parameters.Count);
    }

    [Fact]
    public void WithoutParameter_Absent_ReturnsSameInstance()
    {
        PhcHashString original = Create(("i", "1"));

        Assert.Same(original, original.WithoutParameter("k"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Argon2")]
    [InlineData("alg$x")]
    [InlineData("alg_x")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Constructor_InvalidAlgorithmId_Throws(string algorithmId)
    {
        Assert.Throws<ArgumentException>(() => new PhcHashString(algorithmId, null, [], SaltBytes, HashBytes));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        string? noId = null;
        IReadOnlyList<KeyValuePair<string, string>>? noParameters = null;

        Assert.Throws<ArgumentNullException>(() => new PhcHashString(noId!, null, [], SaltBytes, HashBytes));
        Assert.Throws<ArgumentNullException>(() => new PhcHashString("alg", null, noParameters!, SaltBytes, HashBytes));
    }

    [Fact]
    public void Constructor_NegativeVersion_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhcHashString("alg", -1, [], SaltBytes, HashBytes));
    }

    [Theory]
    [InlineData("v", "1")]
    [InlineData("", "1")]
    [InlineData("M", "1")]
    [InlineData("m", "")]
    [InlineData("m", "a,b")]
    [InlineData("m", "a$b")]
    [InlineData("m", "a=b")]
    public void Constructor_InvalidParameter_Throws(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => new PhcHashString("alg", null, [new(name, value)], SaltBytes, HashBytes));
    }

    [Fact]
    public void Constructor_ValueLongerThan256Characters_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PhcHashString("alg", null, [new("m", new string('a', 257))], SaltBytes, HashBytes));
    }

    [Fact]
    public void Constructor_DuplicateParameter_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PhcHashString("alg", null, [new("m", "1"), new("m", "2")], SaltBytes, HashBytes));
    }

    [Fact]
    public void Constructor_EmptySaltOrHash_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PhcHashString("alg", null, [], ReadOnlySpan<byte>.Empty, HashBytes));
        Assert.Throws<ArgumentException>(() => new PhcHashString("alg", null, [], SaltBytes, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Constructor_CopiesBuffersAndParameters()
    {
        byte[] salt = [1, 2, 3, 4];
        byte[] hash = [5, 6, 7, 8];
        var parameters = new List<KeyValuePair<string, string>> { new("i", "1") };

        var phc = new PhcHashString("alg", null, parameters, salt, hash);
        salt[0] = 99;
        hash[0] = 99;
        parameters.Add(new("k", "p1"));

        Assert.Equal(1, phc.Salt[0]);
        Assert.Equal(5, phc.Hash[0]);
        Assert.Single(phc.Parameters);
    }

    private static PhcHashString Create(params (string Name, string Value)[] parameters) =>
        new("alg", null, [.. parameters.Select(p => new KeyValuePair<string, string>(p.Name, p.Value))], SaltBytes, HashBytes);
}
