using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage.S3.Internal;

namespace SharedKernel.Storage.S3.Tests;

/// <summary>Start-up validation messages are sentences that name the setting and the remedy and quote no endpoint, bucket or key.</summary>
public sealed class OptionsMessageTests
{
    [Fact]
    public void Connection_failures_are_sentences_naming_the_connection_and_the_setting()
    {
        var options = new S3StorageOptions
        {
            ServiceUrl = "ftp://secret-host.internal:9443",
            AccessKeyId = "key-without-secret",
            SessionToken = "token",
            MaxRetries = 99,
            RequestTimeout = TimeSpan.Zero,
        };

        var result = new S3StorageOptionsValidator().Validate("Archive", options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().NotBeEmpty().And.AllSatisfy(f =>
        {
            f.Should().StartWith("S3 connection 'Archive': ").And.EndWith(".");
            f.Should().NotContain("secret-host").And.NotContain("9443").And.NotContain("key-without-secret");
        });
    }

    [Fact]
    public void Store_failures_are_sentences_that_never_quote_the_bucket_value()
    {
        var options = new S3StoreOptions
        {
            Bucket = "Secret_Bucket",
            KeyPrefix = "no-slash",
            MaxPresignExpiry = TimeSpan.FromDays(9),
            MultipartPartSize = 1,
        };

        var result = new S3StoreOptionsValidator().Validate("files", options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(4).And.AllSatisfy(f =>
        {
            f.Should().StartWith("Storage store 'files': ").And.EndWith(".");
            f.Should().NotContain("Secret_Bucket");
        });
        result.Failures.Should().Contain(f => f.Contains("'SharedKernel:Storage:Stores:files:Bucket'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_connection_name_is_a_sentence_that_names_the_remedy()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IStorageBuilder storage = services.AddSharedKernelStorage();
        storage.AddS3Compatible("One", configuration, _ => throw new InvalidOperationException("unused"));

        Action again = () => storage.AddS3Compatible("One", configuration, _ => throw new InvalidOperationException("unused"));

        again.Should().Throw<InvalidOperationException>()
            .WithMessage("An S3 storage connection named 'One' is already registered. Connection names are unique, so choose another name.");
    }
}
