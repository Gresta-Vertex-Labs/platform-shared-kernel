using Microsoft.Extensions.Options;
using SharedKernel.Communication.Rest.Options;

namespace SharedKernel.Communication.Rest.Tests.Options;

/// <summary>
/// T-19: RestClientOptionsValidator startup-validation tests.
/// </summary>
public sealed class RestClientOptionsValidatorTests
{
    private static readonly RestClientOptionsValidator Validator = new();

    [Fact]
    public void Validate_WithTimeoutSecondsZero_ReturnsFail()
    {
        var options = new RestClientOptions { BaseAddress = "http://test", TimeoutSeconds = 0 };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TimeoutSeconds");
    }

    [Fact]
    public void Validate_WithNegativeTimeoutSeconds_ReturnsFail()
    {
        var options = new RestClientOptions { BaseAddress = "http://test", TimeoutSeconds = -1 };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TimeoutSeconds");
    }

    [Fact]
    public void Validate_WithWhitespaceBaseAddress_ReturnsFail()
    {
        var options = new RestClientOptions { BaseAddress = "   ", TimeoutSeconds = 30 };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("BaseAddress");
    }

    [Fact]
    public void Validate_WithEmptyStringBaseAddress_ReturnsFail()
    {
        var options = new RestClientOptions { BaseAddress = string.Empty, TimeoutSeconds = 30 };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("BaseAddress");
    }

    [Fact]
    public void Validate_WithValidOptions_ReturnsSuccess()
    {
        var options = new RestClientOptions { BaseAddress = "http://test-service", TimeoutSeconds = 30 };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithNullBaseAddress_ReturnsSuccess()
    {
        // Null BaseAddress is valid — service discovery may be in use
        var options = new RestClientOptions { BaseAddress = null, TimeoutSeconds = 30 };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    public void Validate_WithPositiveTimeoutSeconds_ReturnsSuccess(int timeoutSeconds)
    {
        var options = new RestClientOptions { BaseAddress = "http://test", TimeoutSeconds = timeoutSeconds };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }
}
