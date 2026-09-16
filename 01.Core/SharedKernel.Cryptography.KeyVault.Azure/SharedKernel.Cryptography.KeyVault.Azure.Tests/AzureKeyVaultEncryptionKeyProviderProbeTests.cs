using Azure;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultEncryptionKeyProviderProbeTests
{
    private const string SensitiveDetail = "SECRET-sig=q8Zr1xAccessToken-9f2c";

    private readonly EncryptionFixture _fixture = new();

    [Fact]
    public async Task ProbeAsync_MasterKeyReadable_ReportsHealthyWithoutKeyUsage()
    {
        EncryptionKeyProviderHealth health = await _fixture.CreateProvider().ProbeAsync();

        Assert.True(health.IsHealthy);
        Assert.Null(health.Description);
        Assert.Equal([(EncryptionFixture.MasterKeyName, (string?)null)], _fixture.Vault.GetKeyRequests);
        Assert.Equal(0, _fixture.Vault.WrapCalls + _fixture.Vault.UnwrapCalls + _fixture.Vault.SignCalls);
        Assert.Equal(0, _fixture.Vault.ListCalls + _fixture.Vault.GetSecretCalls + _fixture.Vault.SetSecretCalls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task ProbeAsync_KeyVaultRequestFails_ReportsUnhealthyWithStatusOnly(int status)
    {
        _fixture.Vault.GetKeyException = new RequestFailedException(status, $"Caller is not authorized: {SensitiveDetail}");

        EncryptionKeyProviderHealth health = await _fixture.CreateProvider().ProbeAsync();

        Assert.False(health.IsHealthy);
        Assert.NotNull(health.Description);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), health.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveDetail, health.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("not authorized", health.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_NonAzureException_ReportsUnhealthyWithTypeName()
    {
        _fixture.Vault.GetKeyException = new HttpRequestException($"No such host: {SensitiveDetail}");

        EncryptionKeyProviderHealth health = await _fixture.CreateProvider().ProbeAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains(nameof(HttpRequestException), health.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveDetail, health.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_OperationCanceled_Propagates()
    {
        _fixture.Vault.GetKeyException = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _fixture.CreateProvider().ProbeAsync());
    }

    [Fact]
    public async Task ProbeAsync_CancelledToken_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _fixture.CreateProvider().ProbeAsync(cancellation.Token));
    }
}
