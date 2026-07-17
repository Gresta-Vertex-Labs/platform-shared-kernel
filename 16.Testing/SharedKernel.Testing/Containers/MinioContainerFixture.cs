using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Testcontainers.Minio;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned MinIO container for integration tests, with a default
/// bucket bootstrapped automatically during <see cref="InitializeAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </para>
/// <para>
/// Property names (<see cref="ServiceUrl"/>/<see cref="AccessKeyId"/>/<see cref="SecretAccessKey"/>/
/// <see cref="ForcePathStyle"/>/<see cref="DefaultBucket"/>) match
/// <c>SharedKernel.Storage.S3</c>'s <c>S3StorageOptions</c> 1:1 so
/// <c>SharedKernel.Storage.S3.Tests</c> can bind this fixture directly with zero renaming.
/// <c>SharedKernel.Storage.Obs.Tests</c> binds the same <see cref="ServiceUrl"/> value into
/// <c>ObsStorageOptions</c>'s differently-named <c>Endpoint</c> property — a straight 1:1 property
/// assignment, never provider-specific branching.
/// </para>
/// <para>
/// This is the ONLY type in <c>Containers/</c> permitted to carry an <c>AWSSDK.S3</c> reference
/// (scoped to the bucket-bootstrap step only) — the other three fixtures remain isolated to their
/// own single <c>Testcontainers.*</c> package. This fixture deliberately takes NO
/// <c>ProjectReference</c> to <c>SharedKernel.Storage.Abstractions</c>: it exposes flat scalar
/// connection properties only, exactly like its three siblings expose a raw
/// <c>ConnectionString</c>, so it has zero build-time dependency on <c>08.Storage</c>'s own code.
/// </para>
/// </remarks>
public sealed class MinioContainerFixture : IAsyncLifetime
{
    private const string DefaultBucketName = "sharedkernel-test-bucket";

    private readonly MinioContainer _container = new MinioBuilder()
        .WithImage("minio/minio:RELEASE.2024-01-16T16-07-38Z")
        .Build();

    private bool _started;

    /// <summary>Gets the MinIO service endpoint URL for the running container.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string ServiceUrl
    {
        get
        {
            EnsureStarted();
            return _container.GetConnectionString();
        }
    }

    /// <summary>
    /// Gets the access key id, sourced from the started container's own root user — never a
    /// SharedKernel-invented value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string AccessKeyId
    {
        get
        {
            EnsureStarted();
            return _container.GetAccessKey();
        }
    }

    /// <summary>Gets the secret access key, sourced the same way as <see cref="AccessKeyId"/>.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string SecretAccessKey
    {
        get
        {
            EnsureStarted();
            return _container.GetSecretKey();
        }
    }

    /// <summary>
    /// Gets the default bucket name, bootstrapped automatically during <see cref="InitializeAsync"/> —
    /// both S3 and OBS test suites receive an already-existing bucket with zero provider-specific
    /// setup of their own.
    /// </summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string DefaultBucket
    {
        get
        {
            EnsureStarted();
            return DefaultBucketName;
        }
    }

    /// <summary>
    /// Gets a value indicating whether path-style bucket addressing must be used. Always
    /// <see langword="true"/> — MinIO requires path-style addressing — exposed as a property so no
    /// consumer has to hardcode this fact itself.
    /// </summary>
    public bool ForcePathStyle => true;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _started = true;

        using var client = CreateClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = DefaultBucketName }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    private AmazonS3Client CreateClient() =>
        new(
            new BasicAWSCredentials(_container.GetAccessKey(), _container.GetSecretKey()),
            new AmazonS3Config { ServiceURL = _container.GetConnectionString(), ForcePathStyle = true });

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                "This property cannot be read before InitializeAsync has completed.");
        }
    }
}
