// consumer-verify — composes 08.Storage's packages the way a downstream service does: a real generic
// host, configuration sections, IHost.StartAsync() (which runs every ValidateOnStart check), and
// resolution through DI. No object-store I/O: the provider behaviour is covered by the MinIO suites.
//   1. S3 (default credential chain) and OBS stores side by side, shared and tenant-scoped, resolved by name.
//   2. A single store resolves as unkeyed IFileStorage.
//   3. An invalid store section fails IHost.StartAsync() naming every problem.
//   4. An invalid OBS connection fails IHost.StartAsync().

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Storage;

await Surface1_S3AndObsStoresSideBySide();
await Surface2_SingleStoreResolvesUnkeyed();
await Surface3_InvalidStoreFailsAtStartup();
await Surface4_InvalidObsConnectionFailsAtStartup();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

static async Task Surface1_S3AndObsStoresSideBySide()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Storage:S3:Region"] = "eu-central-1",
        ["SharedKernel:Storage:Obs:Endpoint"] = "https://obs.tr-west-1.myhuaweicloud.com",
        ["SharedKernel:Storage:Obs:AccessKeyId"] = "obs-ak",
        ["SharedKernel:Storage:Obs:SecretAccessKey"] = "obs-sk",
        ["SharedKernel:Storage:Stores:invoices:Bucket"] = "acme-invoices",
        ["SharedKernel:Storage:Stores:invoices:Encryption"] = "Kms",
        ["SharedKernel:Storage:Stores:documents:Bucket"] = "acme-documents",
        ["SharedKernel:Storage:Stores:archive:Bucket"] = "acme-archive",
    });

    IStorageBuilder storage = builder.Services.AddSharedKernelStorage();
    storage.AddS3(builder.Configuration).AddStore("invoices").AddTenantStore("documents");
    storage.AddObs(builder.Configuration).AddStore("archive");

    using IHost host = builder.Build();
    await host.StartAsync();

    IFileStorage invoices = host.Services.GetRequiredKeyedService<IFileStorage>("invoices");
    IFileStorage archive = host.Services.GetRequiredKeyedService<IFileStorage>("archive");
    TenantId tenant1 = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    IFileStorage tenantDocuments = host.Services.GetRequiredKeyedService<ITenantFileStorage>("documents").ForTenant(tenant1);
    IFileStorageFactory factory = host.Services.GetRequiredService<IFileStorageFactory>();

    Require(invoices.StoreName == "invoices" && archive.StoreName == "archive", "keyed stores resolve by name");
    Require(tenantDocuments.TenantId == tenant1, "tenant view is bound to its tenant");
    Require(factory.StoreNames.Count == 3 && factory.IsTenantScoped("documents"), "factory lists every store");
    Require(Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IFileStorage>()), "unkeyed store is ambiguous with two shared stores");
    Require(Throws<InvalidOperationException>(() => factory.GetStore("documents")), "a tenant store is never a shared store");

    await host.StopAsync();
    Console.WriteLine("Surface 1 PASSED — S3 and OBS stores, shared and tenant-scoped, side by side");
}

static async Task Surface2_SingleStoreResolvesUnkeyed()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Storage:S3:ServiceUrl"] = "http://minio:9000",
        ["SharedKernel:Storage:S3:ForcePathStyle"] = "true",
        ["SharedKernel:Storage:S3:AccessKeyId"] = "minio",
        ["SharedKernel:Storage:S3:SecretAccessKey"] = "minio-secret",
        ["SharedKernel:Storage:Stores:uploads:Bucket"] = "uploads",
    });
    builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("uploads");

    using IHost host = builder.Build();
    await host.StartAsync();

    Require(host.Services.GetRequiredService<IFileStorage>().StoreName == "uploads", "the only store resolves unkeyed");

    await host.StopAsync();
    Console.WriteLine("Surface 2 PASSED — a single store resolves as unkeyed IFileStorage");
}

static async Task Surface3_InvalidStoreFailsAtStartup()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Storage:S3:Region"] = "eu-central-1",
        ["SharedKernel:Storage:Stores:invoices:Bucket"] = "Invalid_Bucket",
        ["SharedKernel:Storage:Stores:invoices:KmsKeyId"] = "alias/without-kms-encryption",
    });
    builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("invoices");

    using IHost host = builder.Build();
    OptionsValidationException? failure = await StartExpectingFailure(host);

    Require(failure is { Failures: var f } && f.Count() == 2 && f.All(m => m.StartsWith("Storage store 'invoices'", StringComparison.Ordinal)),
        "every invalid store setting is reported at startup, naming the store");
    Console.WriteLine("Surface 3 PASSED — an invalid store fails IHost.StartAsync()");
}

static async Task Surface4_InvalidObsConnectionFailsAtStartup()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Storage:Obs:Endpoint"] = "https://storage.example.internal",
        ["SharedKernel:Storage:Stores:archive:Bucket"] = "archive",
    });
    builder.Services.AddSharedKernelStorage().AddObs(builder.Configuration).AddStore("archive");

    using IHost host = builder.Build();
    OptionsValidationException? failure = await StartExpectingFailure(host);

    Require(failure is { Failures: var f } && f.Count() == 2, "missing OBS credentials and region are reported at startup");
    Console.WriteLine("Surface 4 PASSED — an invalid OBS connection fails IHost.StartAsync()");
}

static async Task<OptionsValidationException?> StartExpectingFailure(IHost host)
{
    try
    {
        await host.StartAsync();
        return null;
    }
    catch (OptionsValidationException ex)
    {
        return ex;
    }
    catch (AggregateException ex) when (ex.InnerExceptions.OfType<OptionsValidationException>().Any())
    {
        return new OptionsValidationException(
            string.Empty,
            typeof(object),
            ex.InnerExceptions.OfType<OptionsValidationException>().SelectMany(e => e.Failures));
    }
}

static bool Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
}

static void Require(bool condition, string what)
{
    if (!condition)
    {
        throw new InvalidOperationException($"consumer-verify FAILED: {what}");
    }
}
