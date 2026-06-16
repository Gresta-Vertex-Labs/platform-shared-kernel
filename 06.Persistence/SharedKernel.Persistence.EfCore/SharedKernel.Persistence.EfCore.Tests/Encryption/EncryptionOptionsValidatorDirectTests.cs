using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-41: Direct unit tests for <see cref="EncryptionOptionsValidator"/> via
/// <see cref="IValidateOptions{TOptions}"/>, without a DI container.
/// </summary>
public sealed class EncryptionOptionsValidatorDirectTests
{
    private static readonly IValidateOptions<EncryptionOptions> Validator = new EncryptionOptionsValidator();

    private static string ValidBase64Key(int byteCount = 32)
        => Convert.ToBase64String(new byte[byteCount]);

    [Fact]
    public void Disabled_ValidationPasses_Regardless_Of_MissingCurrentVersion()
    {
        var options = new EncryptionOptions { Enabled = false };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue("disabled options require no keys or version");
    }

    [Fact]
    public void Disabled_ValidationPasses_With_EmptyKeys()
    {
        var options = new EncryptionOptions { Enabled = false, Keys = [] };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue("disabled options pass even with empty Keys dictionary");
    }

    [Fact]
    public void Enabled_CurrentVersionMissingFromKeys_ValidationFails_WithDescriptiveMessage()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v2"] = ValidBase64Key() } // v1 not in Keys
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("CurrentVersion 'v1' is not present in Keys");
        result.FailureMessage.Should().Contain("v1", "message must name the missing version");
    }

    [Fact]
    public void Enabled_KeyDecodesTo16Bytes_ValidationFails_WithDescriptiveMessage()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = ValidBase64Key(byteCount: 16) } // 16 bytes, not 32
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("key must decode to exactly 32 bytes");
        result.FailureMessage.Should().Contain("32", "message must mention the required byte count");
    }

    [Fact]
    public void Enabled_KeyDecodesTo64Bytes_ValidationFails()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = ValidBase64Key(byteCount: 64) } // 64 bytes, not 32
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("key must decode to exactly 32 bytes, not 64");
    }

    [Fact]
    public void Enabled_AllInvariantsSatisfied_ValidationPasses()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = ValidBase64Key(32) }
        };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue("all invariants are satisfied");
    }

    [Fact]
    public void Enabled_MultipleKeys_OneInvalid_ValidationFails()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys =
            {
                ["v1"] = ValidBase64Key(32),
                ["v2"] = ValidBase64Key(10) // 10 bytes — invalid
            }
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("v2 key decodes to 10 bytes, not 32");
        result.FailureMessage.Should().Contain("v2", "message must name the offending key version");
    }

    [Fact]
    public void Enabled_EmptyCurrentVersion_ValidationFails_WithDescriptiveMessage()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = string.Empty,
            Keys = { ["v1"] = ValidBase64Key() }
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("empty CurrentVersion is not valid when Enabled");
        result.FailureMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Enabled_InvalidBase64InKey_ValidationFails()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = "NOT-VALID-BASE64!!!" }
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("non-Base64 key value must fail validation");
    }
}
