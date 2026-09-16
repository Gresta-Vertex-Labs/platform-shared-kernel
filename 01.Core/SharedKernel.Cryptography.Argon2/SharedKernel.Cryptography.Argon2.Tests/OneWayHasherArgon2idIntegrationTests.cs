using System.Text;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using Xunit;

namespace SharedKernel.Cryptography.Argon2.Tests;

public sealed class OneWayHasherArgon2idIntegrationTests
{
    private const string Secret = "correct horse battery staple";

    [Fact]
    public void Hash_Argon2idSelected_WritesArgon2idHashThatVerifies()
    {
        OneWayHasher hasher = CreateHasher(Argon2idOneWayHashAlgorithm.Id);

        string hash = hasher.Hash(Secret);

        Assert.StartsWith("$argon2id$v=19$m=7168,t=2,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Secret));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Secret + "!"));
    }

    [Fact]
    public void Verify_Pbkdf2HashAfterSwitchingToArgon2id_ReturnsSuccessRehashNeeded()
    {
        string pbkdf2Hash = CreateHasher(Pbkdf2OneWayHashAlgorithm.Id).Hash(Secret);
        OneWayHasher hasher = CreateHasher(Argon2idOneWayHashAlgorithm.Id);

        Assert.StartsWith("$pbkdf2-sha256$i=100000$", pbkdf2Hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(pbkdf2Hash, Secret));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(pbkdf2Hash, Secret + "!"));

        string upgraded = hasher.Hash(Secret);
        Assert.StartsWith("$argon2id$", upgraded, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(upgraded, Secret));
    }

    [Fact]
    public void Verify_Argon2idHashAfterSwitchingBackToPbkdf2_ReturnsSuccessRehashNeeded()
    {
        string argon2Hash = CreateHasher(Argon2idOneWayHashAlgorithm.Id).Hash(Secret);

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, CreateHasher(Pbkdf2OneWayHashAlgorithm.Id).Verify(argon2Hash, Secret));
    }

    [Fact]
    public void Verify_Argon2idHashAfterCostIncrease_ReturnsSuccessRehashNeeded()
    {
        string hash = CreateHasher(Argon2idOneWayHashAlgorithm.Id).Hash(Secret);

        OneWayHasher stronger = CreateHasher(Argon2idOneWayHashAlgorithm.Id, argon2: Argon2TestData.CreateAlgorithm(iterations: 3));

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, stronger.Verify(hash, Secret));
    }

    [Fact]
    public void Hash_Argon2idWithPepper_StoresPepperIdAndVerifies()
    {
        OneWayHasher hasher = CreateHasher(Argon2idOneWayHashAlgorithm.Id, pepperId: "p1");

        string hash = hasher.Hash(Secret);

        Assert.StartsWith("$argon2id$v=19$m=7168,t=2,p=1,k=p1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Secret));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Secret + "!"));
    }

    [Fact]
    public void Verify_PepperedArgon2idHash_IsKeyedWithPepper()
    {
        string hash = CreateHasher(Argon2idOneWayHashAlgorithm.Id, pepperId: "p1").Hash(Secret);

        Assert.True(PhcHashString.TryParse(hash, out PhcHashString? parsed));
        Assert.False(Argon2TestData.CreateAlgorithm().Verify(parsed.WithoutParameter("k"), Encoding.UTF8.GetBytes(Secret)));
    }

    [Fact]
    public void Verify_UnpepperedArgon2idHashAfterPepperIntroduced_ReturnsSuccessRehashNeeded()
    {
        string hash = CreateHasher(Argon2idOneWayHashAlgorithm.Id).Hash(Secret);

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, CreateHasher(Argon2idOneWayHashAlgorithm.Id, pepperId: "p1").Verify(hash, Secret));
    }

    [Fact]
    public void Verify_Argon2idHashWithoutArgon2Registered_ReturnsFailed()
    {
        string hash = CreateHasher(Argon2idOneWayHashAlgorithm.Id).Hash(Secret);
        CryptographyOptions options = CreateOptions(Pbkdf2OneWayHashAlgorithm.Id, pepperId: null);
        var monitor = new TestOptionsMonitor<CryptographyOptions>(options);
        var hasher = new OneWayHasher([CreatePbkdf2()], monitor);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Secret));
    }

    [Fact]
    public void Verify_HostileArgon2idCostInStoredHash_ReturnsFailed()
    {
        const string hostile = "$argon2id$v=19$m=4194304,t=10,p=16$AQIDBAUGBwgJCgsMDQ4PEA$AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA";

        Assert.Equal(HashVerificationResult.Failed, CreateHasher(Argon2idOneWayHashAlgorithm.Id).Verify(hostile, Secret));
    }

    private static OneWayHasher CreateHasher(string algorithm, string? pepperId = null, Argon2idOneWayHashAlgorithm? argon2 = null)
    {
        var monitor = new TestOptionsMonitor<CryptographyOptions>(CreateOptions(algorithm, pepperId));
        return new OneWayHasher([CreatePbkdf2(), argon2 ?? Argon2TestData.CreateAlgorithm()], monitor);
    }

    private static Pbkdf2OneWayHashAlgorithm CreatePbkdf2() =>
        new(new TestOptionsMonitor<Pbkdf2Options>(new Pbkdf2Options { Iterations = Pbkdf2Options.MinimumIterations }));

    private static CryptographyOptions CreateOptions(string algorithm, string? pepperId)
    {
        var options = new CryptographyOptions();
        options.OneWayHashing.Algorithm = algorithm;
        options.OneWayHashing.Peppers["p1"] = Convert.ToBase64String(Argon2TestData.Salt(32));
        options.OneWayHashing.CurrentPepperId = pepperId;
        return options;
    }
}
