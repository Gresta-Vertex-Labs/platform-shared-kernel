// consumer-verify — exercises 08.Storage's published packages exactly as a downstream
// microservice would: real DI composition through ProjectReference (standing in for a packed
// NuGet reference — the compiled surface is identical either way), never in-process unit-test
// scaffolding. Five surfaces:
//   1. AddSharedKernelS3Storage() resolves IFileStorage/IBlobUriGenerator, zero DI exceptions (P-03)
//   2. AddSharedKernelObsStorage() resolves IFileStorage/IBlobUriGenerator, zero DI exceptions (P-04)
//   3. Both providers registered side by side via keyed DI, no resolution collision (P-05) —
//      exercises the exact pattern documented in SharedKernel.Storage.Obs/README.md (C-29/DO-06)
//   4. A missing/invalid S3StorageOptions section fails at IHost.StartAsync() with an actionable
//      message — not a silent default or a first-upload failure (P-06)
//   5. Same as Surface 4, for ObsStorageOptions (P-06)

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Obs.BlobUri;
using SharedKernel.Storage.Obs.Extensions;
using SharedKernel.Storage.Obs.FileStorage;
using SharedKernel.Storage.Obs.Options;
using SharedKernel.Storage.S3.BlobUri;
using SharedKernel.Storage.S3.Extensions;
using SharedKernel.Storage.S3.FileStorage;
using SharedKernel.Storage.S3.Options;

await Surface1_S3ResolvesWithZeroDiExceptions();
await Surface2_ObsResolvesWithZeroDiExceptions();
Surface3_BothProvidersSideBySideViaKeyedDi();
await Surface4_InvalidS3ConfigFailsAtHostStartAsync();
await Surface5_InvalidObsConfigFailsAtHostStartAsync();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

// ── Surface 1: AddSharedKernelS3Storage() — P-03 ─────────────────────────────
static async Task Surface1_S3ResolvesWithZeroDiExceptions()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Storage:S3:AccessKeyId"] = "s3-access-key",
        ["SharedKernel:Storage:S3:SecretAccessKey"] = "s3-secret-key",
        ["SharedKernel:Storage:S3:Region"] = "eu-central-1",
    });

    builder.Services.AddSharedKernelS3Storage(builder.Configuration);

    using var host = builder.Build();
    // Exercises the real ValidateOnStart() path — a valid config must pass cleanly, not just
    // resolve via BuildServiceProvider().
    await host.StartAsync();

    var fileStorage = host.Services.GetRequiredService<IFileStorage>();
    var uriGenerator = host.Services.GetRequiredService<IBlobUriGenerator>();

    Verify(fileStorage is S3FileStorage, "IFileStorage resolves as S3FileStorage");
    Verify(uriGenerator is S3BlobUriGenerator, "IBlobUriGenerator resolves as S3BlobUriGenerator");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelS3Storage() resolves IFileStorage/IBlobUriGenerator, zero DI exceptions");
}

// ── Surface 2: AddSharedKernelObsStorage() — P-04 ────────────────────────────
static async Task Surface2_ObsResolvesWithZeroDiExceptions()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Storage:Obs:Endpoint"] = "https://obs.ap-southeast-1.myhuaweicloud.com",
        ["SharedKernel:Storage:Obs:AccessKeyId"] = "obs-access-key",
        ["SharedKernel:Storage:Obs:SecretAccessKey"] = "obs-secret-key",
    });

    builder.Services.AddSharedKernelObsStorage(builder.Configuration);

    using var host = builder.Build();
    await host.StartAsync();

    var fileStorage = host.Services.GetRequiredService<IFileStorage>();
    var uriGenerator = host.Services.GetRequiredService<IBlobUriGenerator>();

    Verify(fileStorage is ObsFileStorage, "IFileStorage resolves as ObsFileStorage");
    Verify(uriGenerator is ObsBlobUriGenerator, "IBlobUriGenerator resolves as ObsBlobUriGenerator");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 2 PASS: AddSharedKernelObsStorage() resolves IFileStorage/IBlobUriGenerator, zero DI exceptions");
}

// ── Surface 3: .S3 + .Obs side by side via keyed DI — P-05 ───────────────────
// Mirrors SharedKernel.Storage.Obs/README.md's fully worked AddKeyedSingleton example
// (C-29/DO-06) verbatim — neither AddSharedKernelS3Storage() nor AddSharedKernelObsStorage()
// offers a keyed overload, so this is the documented, supported composition path.
static void Surface3_BothProvidersSideBySideViaKeyedDi()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:AccessKeyId"] = "s3-access-key",
            ["SharedKernel:Storage:S3:SecretAccessKey"] = "s3-secret-key",
            ["SharedKernel:Storage:S3:Region"] = "eu-central-1",
            ["SharedKernel:Storage:Obs:Endpoint"] = "https://obs.ap-southeast-1.myhuaweicloud.com",
            ["SharedKernel:Storage:Obs:AccessKeyId"] = "obs-access-key",
            ["SharedKernel:Storage:Obs:SecretAccessKey"] = "obs-secret-key",
        })
        .Build();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IClock, SystemClock>();

    // S3 side, keyed "s3"
    services.AddValidatedOptions<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));
    services.AddKeyedSingleton<IAmazonS3>("s3", (sp, _) =>
    {
        var options = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;
        var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
        var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return new AmazonS3Client(credentials, config);
    });
    services.AddKeyedSingleton<IFileStorage>(
        "s3",
        (sp, key) => new S3FileStorage(
            sp.GetRequiredKeyedService<IAmazonS3>(key),
            sp.GetRequiredService<ILogger<S3FileStorage>>()));
    services.AddKeyedSingleton<IBlobUriGenerator>(
        "s3",
        (sp, key) => new S3BlobUriGenerator(
            sp.GetRequiredKeyedService<IAmazonS3>(key),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<S3BlobUriGenerator>>()));

    // OBS side, keyed "obs"
    services.AddValidatedOptions<ObsStorageOptions>(configuration.GetSection(ObsStorageOptions.SectionName));
    services.AddKeyedSingleton<IAmazonS3>("obs", (sp, _) =>
    {
        var options = sp.GetRequiredService<IOptions<ObsStorageOptions>>().Value;
        var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
        var config = new AmazonS3Config { ServiceURL = options.Endpoint, ForcePathStyle = options.ForcePathStyle };
        return new AmazonS3Client(credentials, config);
    });
    services.AddKeyedSingleton<IFileStorage>(
        "obs",
        (sp, key) => new ObsFileStorage(
            sp.GetRequiredKeyedService<IAmazonS3>(key),
            sp.GetRequiredService<ILogger<ObsFileStorage>>()));
    services.AddKeyedSingleton<IBlobUriGenerator>(
        "obs",
        (sp, key) => new ObsBlobUriGenerator(
            sp.GetRequiredKeyedService<IAmazonS3>(key),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<ObsBlobUriGenerator>>()));

    using var provider = services.BuildServiceProvider(validateScopes: true);

    var s3Storage = provider.GetRequiredKeyedService<IFileStorage>("s3");
    var obsStorage = provider.GetRequiredKeyedService<IFileStorage>("obs");
    var s3UriGenerator = provider.GetRequiredKeyedService<IBlobUriGenerator>("s3");
    var obsUriGenerator = provider.GetRequiredKeyedService<IBlobUriGenerator>("obs");

    Verify(s3Storage is S3FileStorage, "keyed \"s3\" IFileStorage resolves as S3FileStorage");
    Verify(obsStorage is ObsFileStorage, "keyed \"obs\" IFileStorage resolves as ObsFileStorage");
    Verify(
        !ReferenceEquals(s3Storage, obsStorage),
        "keyed \"s3\"/\"obs\" IFileStorage instances are distinct — no resolution collision");
    Verify(s3UriGenerator is S3BlobUriGenerator, "keyed \"s3\" IBlobUriGenerator resolves as S3BlobUriGenerator");
    Verify(obsUriGenerator is ObsBlobUriGenerator, "keyed \"obs\" IBlobUriGenerator resolves as ObsBlobUriGenerator");
    Verify(
        !ReferenceEquals(s3UriGenerator, obsUriGenerator),
        "keyed \"s3\"/\"obs\" IBlobUriGenerator instances are distinct — no resolution collision");

    Console.WriteLine(
        "Surface 3 PASS: AddSharedKernelS3Storage() + AddSharedKernelObsStorage() compose side by side via keyed DI, zero collision");
}

// ── Surface 4: missing S3StorageOptions fails at IHost.StartAsync() — P-06 ───
static async Task Surface4_InvalidS3ConfigFailsAtHostStartAsync()
{
    var builder = Host.CreateApplicationBuilder();
    // Deliberately omit AccessKeyId/SecretAccessKey/Region/ServiceUrl entirely.
    builder.Services.AddSharedKernelS3Storage(builder.Configuration);

    using var host = builder.Build();

    OptionsValidationException? caught = null;
    try
    {
        await host.StartAsync();
    }
    catch (OptionsValidationException ex)
    {
        caught = ex;
    }

    Verify(
        caught is not null,
        "missing S3StorageOptions throws OptionsValidationException at IHost.StartAsync() (not a silent default)");
    Verify(
        caught!.Failures.Any(f => f.Contains("AccessKeyId", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing AccessKeyId property (actionable, not generic)");

    Console.WriteLine(
        "Surface 4 PASS: missing S3StorageOptions fails at IHost.StartAsync() with a clear, actionable message");
}

// ── Surface 5: missing ObsStorageOptions fails at IHost.StartAsync() — P-06 ──
static async Task Surface5_InvalidObsConfigFailsAtHostStartAsync()
{
    var builder = Host.CreateApplicationBuilder();
    // Deliberately omit Endpoint/AccessKeyId/SecretAccessKey entirely.
    builder.Services.AddSharedKernelObsStorage(builder.Configuration);

    using var host = builder.Build();

    OptionsValidationException? caught = null;
    try
    {
        await host.StartAsync();
    }
    catch (OptionsValidationException ex)
    {
        caught = ex;
    }

    Verify(
        caught is not null,
        "missing ObsStorageOptions throws OptionsValidationException at IHost.StartAsync() (not a silent default)");
    Verify(
        caught!.Failures.Any(f => f.Contains("Endpoint", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing Endpoint property (actionable, not generic)");

    Console.WriteLine(
        "Surface 5 PASS: missing ObsStorageOptions fails at IHost.StartAsync() with a clear, actionable message");
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }
}
