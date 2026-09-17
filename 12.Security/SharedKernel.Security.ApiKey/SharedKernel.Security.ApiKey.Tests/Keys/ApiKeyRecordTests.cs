using SharedKernel.Security.ApiKey.Keys;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Keys;

public sealed class ApiKeyRecordTests
{
    [Theory]
    [InlineData("", "hash", "client")]
    [InlineData("id", " ", "client")]
    [InlineData("id", "hash", "")]
    public void Constructor_BlankRequiredValue_Throws(string keyId, string keyHash, string clientId)
    {
        Assert.Throws<ArgumentException>(() => new ApiKeyRecord(keyId, keyHash, clientId));
    }

    [Fact]
    public void Constructor_RequiredValuesOnly_HasNoGrantsExpiryOrRevocation()
    {
        var record = new ApiKeyRecord("id", "hash", "client");

        Assert.Equal("id", record.KeyId);
        Assert.Equal("hash", record.KeyHash);
        Assert.Equal("client", record.ClientId);
        Assert.Null(record.TenantId);
        Assert.Empty(record.Roles);
        Assert.Empty(record.Permissions);
        Assert.Null(record.ExpiresAt);
        Assert.Null(record.RevokedAt);
    }
}
