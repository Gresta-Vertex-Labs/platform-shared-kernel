using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Keys;

// Every thrown or validation message is a sentence ending in a period that names the remedy.
public sealed class ApiKeyMessageConventionTests
{
    [Fact]
    public void Success_DefaultTenantId_ThrowsASentenceNamingTheRemedy()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ApiKeyValidationResult.Success("client", default(TenantId)));

        Assert.Contains("The tenant id must not be default(TenantId). Pass null for no tenant.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_InvalidPrefix_ThrowsThePrefixRuleSentence()
    {
        var generator = new ApiKeyGenerator(
            new SecureRandomGenerator(),
            Microsoft.Extensions.Options.Options.Create(new ManagedApiKeyOptions { Prefix = "Bad Prefix" }));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(generator.Generate);

        Assert.Equal(ApiKeyFormat.PrefixRule, exception.Message);
        Assert.EndsWith(".", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bad Prefix", exception.Message, StringComparison.Ordinal);
    }
}
