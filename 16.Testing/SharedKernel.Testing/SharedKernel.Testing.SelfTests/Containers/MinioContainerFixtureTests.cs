using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Containers;

/// <summary>
/// Proves <see cref="MinioContainerFixture"/> per the existing <c>PostgreSqlContainerFixtureTests</c>/
/// <c>RedisContainerFixtureTests</c>/<c>RabbitMqContainerFixtureTests</c> pattern in
/// <c>Containers/ContainerFixtureTests.cs</c> — Docker-gated: these tests require a local Docker
/// daemon to actually pull and start the pinned <c>minio/minio</c> image, mirroring how the other
/// three sibling fixtures are proven in this same project with no explicit skip/availability check.
/// </summary>
public sealed class MinioContainerFixtureTests
{
    [Fact]
    public void ServiceUrl_ReadBeforeInitialize_Throws()
    {
        var fixture = new MinioContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.ServiceUrl);
    }

    [Fact]
    public void AccessKeyId_ReadBeforeInitialize_Throws()
    {
        var fixture = new MinioContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.AccessKeyId);
    }

    [Fact]
    public void SecretAccessKey_ReadBeforeInitialize_Throws()
    {
        var fixture = new MinioContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.SecretAccessKey);
    }

    [Fact]
    public void DefaultBucket_ReadBeforeInitialize_Throws()
    {
        var fixture = new MinioContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.DefaultBucket);
    }

    [Fact]
    public void ForcePathStyle_ReadableBeforeInitialize_IsAlwaysTrue()
    {
        // Unlike the connection-derived properties above, ForcePathStyle is a fixed constant fact
        // about MinIO (path-style addressing is required) — it never depends on the started container.
        var fixture = new MinioContainerFixture();
        Assert.True(fixture.ForcePathStyle);
    }

    [Fact]
    public async Task FullLifecycle_StartsBootstrapsDefaultBucket_AndStopsCleanly()
    {
        var fixture = new MinioContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(fixture.ServiceUrl));
            Assert.False(string.IsNullOrWhiteSpace(fixture.AccessKeyId));
            Assert.False(string.IsNullOrWhiteSpace(fixture.SecretAccessKey));
            Assert.False(string.IsNullOrWhiteSpace(fixture.DefaultBucket));

            using var client = new AmazonS3Client(
                new BasicAWSCredentials(fixture.AccessKeyId, fixture.SecretAccessKey),
                new AmazonS3Config { ServiceURL = fixture.ServiceUrl, ForcePathStyle = fixture.ForcePathStyle });

            // HeadBucketAsync throws AmazonS3Exception (404) if the bucket does not exist —
            // completing without an exception is the proof the default bucket was bootstrapped.
            await client.HeadBucketAsync(new HeadBucketRequest { BucketName = fixture.DefaultBucket });
        }
        finally
        {
            // Proves DisposeAsync stops the container without throwing.
            await fixture.DisposeAsync();
        }
    }
}
