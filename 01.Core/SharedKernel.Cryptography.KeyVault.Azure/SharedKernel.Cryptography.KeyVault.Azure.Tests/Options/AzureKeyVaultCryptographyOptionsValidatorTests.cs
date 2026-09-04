using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Options;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Options;

public sealed class AzureKeyVaultCryptographyOptionsValidatorTests
{
    private readonly AzureKeyVaultCryptographyOptionsValidator _validator = new();

    private static AzureKeyVaultCryptographyOptions ValidOptions() => new()
    {
        VaultUri = new Uri("https://my-vault.vault.azure.net/"),
        CurrentKeyId = "primary",
        KeyNames = new Dictionary<string, string> { ["primary"] = "tenant-data-key" },
    };

    [Fact]
    public void Validate_WellFormedOptions_Succeeds()
    {
        ValidateOptionsResult result = _validator.Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyKeyNames_Fails()
    {
        AzureKeyVaultCryptographyOptions options = ValidOptions();
        options.KeyNames = new Dictionary<string, string>();

        ValidateOptionsResult result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("KeyNames", result.FailureMessage);
    }

    [Fact]
    public void Validate_CurrentKeyIdNotPresentInKeyNames_Fails()
    {
        AzureKeyVaultCryptographyOptions options = ValidOptions();
        options.CurrentKeyId = "unknown-key";

        ValidateOptionsResult result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("unknown-key", result.FailureMessage);
        Assert.Contains("CurrentKeyId", result.FailureMessage);
    }

    [Fact]
    public void Validate_KeyNameContainingReservedSeparator_Fails()
    {
        AzureKeyVaultCryptographyOptions options = ValidOptions();
        options.KeyNames = new Dictionary<string, string> { ["primary:v2"] = "tenant-data-key" };
        options.CurrentKeyId = "primary:v2";

        ValidateOptionsResult result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("reserved", result.FailureMessage);
    }

    [Fact]
    public void Validate_NullCurrentKeyId_DoesNotThrow_AndDefersToRequiredDataAnnotation()
    {
        // AzureKeyVaultCryptographyOptions.CurrentKeyId carries [Required] — a null value is
        // caught by SharedKernel.Configuration's Data Annotations pass, not this validator.
        // This validator must still behave gracefully (never throw) if invoked directly with a
        // null CurrentKeyId, e.g. from a unit test that bypasses the Data Annotations pipeline.
        AzureKeyVaultCryptographyOptions options = ValidOptions();
        options.CurrentKeyId = null;

        ValidateOptionsResult result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null, null!));
    }
}
