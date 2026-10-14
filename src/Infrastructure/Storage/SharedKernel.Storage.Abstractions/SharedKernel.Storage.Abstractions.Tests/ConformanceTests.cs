using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.Abstractions.Tests;

/// <summary>
/// The public constants and codes of the storage domain pinned literally: they are wire names that dashboards,
/// alerts and clients match on.
/// </summary>
public sealed partial class TelemetryNamesTests
{
    [Fact]
    public void The_source_and_meter_are_the_shared_storage_name()
    {
        StorageTelemetryNames.SourceName.Should().Be("SharedKernel.Storage");
        StorageTelemetryNames.MeterName.Should().Be("SharedKernel.Storage");
    }

    [Fact]
    public void The_instruments_are_sharedkernel_storage_names()
    {
        StorageTelemetryNames.OperationDurationInstrumentName.Should().Be("sharedkernel.storage.operation.duration");
        StorageTelemetryNames.BytesInstrumentName.Should().Be("sharedkernel.storage.bytes");
    }

    [Fact]
    public void The_tag_keys_are_pinned()
    {
        StorageTagKeys.Store.Should().Be("storage.store");
        StorageTagKeys.Operation.Should().Be("storage.operation");
        StorageTagKeys.Provider.Should().Be("storage.provider");
        StorageTagKeys.Direction.Should().Be("storage.direction");
    }

    [Fact]
    public void Every_error_code_is_lowercase_storage_dot_snake_case_and_unique()
    {
        string[] codes = [.. typeof(StorageErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)];

        codes.Should().HaveCount(12).And.OnlyHaveUniqueItems();
        codes.Should().OnlyContain(code => CodePattern().IsMatch(code));
        codes.Should().BeEquivalentTo(
            "storage.not_found",
            "storage.access_denied",
            "storage.invalid_key",
            "storage.invalid_request",
            "storage.expiry_too_long",
            "storage.already_exists",
            "storage.precondition_failed",
            "storage.checksum_mismatch",
            "storage.invalid_range",
            "storage.not_supported",
            "storage.unavailable",
            "storage.provider_error");
    }

    [Fact]
    public void Every_code_maps_to_one_documented_error_type_in_this_table()
    {
        // The single table of code to ErrorType: it decides the HTTP status, the gRPC status and the span's error.type.
        var table = new Dictionary<string, ErrorType>
        {
            [StorageErrorCodes.NotFound] = ErrorType.NotFound,
            [StorageErrorCodes.AccessDenied] = ErrorType.Forbidden,
            [StorageErrorCodes.InvalidKey] = ErrorType.Validation,
            [StorageErrorCodes.InvalidRequest] = ErrorType.Validation,
            [StorageErrorCodes.ExpiryTooLong] = ErrorType.Validation,
            [StorageErrorCodes.AlreadyExists] = ErrorType.Conflict,
            [StorageErrorCodes.PreconditionFailed] = ErrorType.Conflict,
            [StorageErrorCodes.ChecksumMismatch] = ErrorType.Validation,
            [StorageErrorCodes.InvalidRange] = ErrorType.Validation,
            [StorageErrorCodes.NotSupported] = ErrorType.Unexpected,
            [StorageErrorCodes.Unavailable] = ErrorType.Unavailable,
            [StorageErrorCodes.ProviderError] = ErrorType.Unexpected,
        };
        Error[] errors =
        [
            StorageErrors.NotFound("s", "k"),
            StorageErrors.AccessDenied("s"),
            StorageErrors.InvalidKey("k", "it is empty."),
            StorageErrors.InvalidRequest("Bad."),
            StorageErrors.ExpiryTooLong(TimeSpan.FromDays(9), TimeSpan.FromDays(7)),
            StorageErrors.AlreadyExists("s", "k"),
            StorageErrors.PreconditionFailed("s", "k"),
            StorageErrors.ChecksumMismatch("s", "k"),
            StorageErrors.InvalidRange("s", "k"),
            StorageErrors.NotSupported("s", "conditional writes"),
            StorageErrors.Unavailable("s", "upload"),
            StorageErrors.ProviderError("s", "copy"),
        ];

        errors.Select(e => e.Code).Should().BeEquivalentTo(table.Keys);
        errors.Should().AllSatisfy(e => e.Type.Should().Be(table[e.Code], e.Code));
    }

    [GeneratedRegex(@"^storage\.[a-z]+(_[a-z]+)*$")]
    private static partial Regex CodePattern();
}

/// <summary>Every exception the registry and the tenant view throw is one sentence ending in a period that names a remedy.</summary>
public sealed class ExceptionMessageTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    private static void ShouldBeASentence(Exception exception)
    {
        // ArgumentException appends " (Parameter 'name')" itself; the sentence is what precedes it.
        string message = exception is ArgumentException { ParamName: not null } argument
            ? argument.Message[..argument.Message.LastIndexOf(" (Parameter", StringComparison.Ordinal)]
            : exception.Message;
        message.Should().EndWith(".", "an exception message is a sentence");
        message.Should().NotContain(Tenant.ToString(), "a tenant id never reaches an exception message");
        message.Should().NotContain("tenants/").And.NotContain("rule ", "internal rule numbers stay out");
        message.Should().NotContain("--").And.NotContain("—");
    }

    private static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    [Fact]
    public void Registry_failures_are_sentences_that_quote_names_and_name_the_remedy()
    {
        using ServiceProvider shared = StoreRegistryTests.Build(("invoices", false), ("exports", false));
        using ServiceProvider onlyTenant = StoreRegistryTests.Build(("documents", true));
        using ServiceProvider empty = new ServiceCollection().AddSharedKernelStorage().Services.BuildServiceProvider();
        IFileStorageFactory sharedFactory = shared.GetRequiredService<IFileStorageFactory>();
        IFileStorageFactory tenantFactory = onlyTenant.GetRequiredService<IFileStorageFactory>();

        Exception[] failures =
        [
            Catch(() => shared.GetRequiredService<IFileStorage>()),
            Catch(() => onlyTenant.GetRequiredService<IFileStorage>()),
            Catch(() => empty.GetRequiredService<IFileStorageFactory>().GetStore("ghost")),
            Catch(() => sharedFactory.GetStore("ghost")),
            Catch(() => tenantFactory.GetStore("documents")),
            Catch(() => sharedFactory.GetTenantStore("invoices")),
            Catch(() => tenantFactory.Open(new FileReference { Store = "documents", Key = "a" })),
            Catch(() => sharedFactory.Open(new FileReference { Store = "invoices", TenantId = Tenant, Key = "a" })),
        ];

        failures.Should().AllSatisfy(ShouldBeASentence);
        failures.Select(f => f.Message).Should().Contain(m => m.Contains("'ghost'", StringComparison.Ordinal))
            .And.Contain(m => m.Contains("'invoices', 'exports'", StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_and_tenant_view_failures_are_sentences()
    {
        IStorageBuilder builder = new ServiceCollection().AddSharedKernelStorage();
        builder.AddStore(new FileStoreRegistration("invoices", false, _ => new RecordingFileStorage("invoices"), (_, _) => Task.FromResult(Result.Success())));
        using ServiceProvider nullFactory = StoreRegistryTests.Build(("invoices", false, new RecordingFileStorage("other")));
        IStorageBuilder nullBuilder = new ServiceCollection().AddSharedKernelStorage();
        nullBuilder.AddStore(new FileStoreRegistration("broken", false, _ => null!, (_, _) => Task.FromResult(Result.Success())));
        using ServiceProvider broken = nullBuilder.Services.BuildServiceProvider();
        using ServiceProvider tenantProvider = StoreRegistryTests.Build(("documents", true));
        ITenantFileStorage tenants = tenantProvider.GetRequiredService<ITenantFileStorage>();

        Exception[] failures =
        [
            Catch(() => builder.AddStore(new FileStoreRegistration("INVOICES", false, _ => new RecordingFileStorage("INVOICES"), (_, _) => Task.FromResult(Result.Success())))),
            Catch(() => new FileStoreRegistration("bad name", false, _ => new RecordingFileStorage("x"), (_, _) => Task.FromResult(Result.Success()))),
            Catch(() => nullFactory.GetRequiredService<IFileStorage>()),
            Catch(() => broken.GetRequiredService<IFileStorage>()),
            Catch(() => tenants.ForTenant(default)),
        ];

        failures.Should().AllSatisfy(ShouldBeASentence);
        failures[0].Message.Should().Contain("'INVOICES'").And.Contain("choose another name");
    }
}
