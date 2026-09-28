using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.DataPrivacy.Classification;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.DataPrivacy.Masking;
using SharedKernel.DataPrivacy.Redaction;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests;

/// <summary>Runs the code shown in <c>SharedKernel.DataPrivacy/README.md</c>.</summary>
public sealed partial class ReadmeSampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public sealed record Customer(
        [property: PersonNameData] string Name,
        [property: EmailAddressData] string Email,
        [property: NationalIdData] string NationalId,
        [property: HealthData] string? Allergies,
        string Segment);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information, Message = "Customer {Email} signed up.")]
    private static partial void SignedUp(ILogger logger, [EmailAddressData] string email);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information, Message = "Customer updated.")]
    private static partial void Updated(ILogger logger, [LogProperties] Customer customer);

    private static (ILogger Logger, List<(string Message, Dictionary<string, string?> State)> Records, ServiceProvider Provider) Host(
        Action<IRedactionBuilder> redaction)
    {
        var sink = new Sink();
        var services = new ServiceCollection();
        services.AddRedaction(redaction);
        services.AddLogging(b => b.EnableRedaction(options => options.ApplyDiscriminator = false).AddProvider(sink));
        ServiceProvider provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ILoggerFactory>().CreateLogger("customers"), sink.Records, provider);
    }

    [Fact]
    public void QuickStart_LogsMaskedValues()
    {
        var (logger, records, provider) = Host(redaction => redaction.SetPrivacyRedactors());
        using (provider)
        {
            SignedUp(logger, "j.doe@example.com");
            Updated(logger, new Customer("Ayşe Yılmaz", "ayse@example.com", "10000000146", "peanuts", "retail"));
        }

        Assert.Equal("Customer j***@example.com signed up.", records[0].Message);
        Dictionary<string, string?> state = records[1].State;
        Assert.Equal("A*** Y***", state["customer.Name"]);
        Assert.Equal("a***@example.com", state["customer.Email"]);
        Assert.Equal("*******0146", state["customer.NationalId"]);
        Assert.Equal("[REDACTED]", state["customer.Allergies"]);
        Assert.Equal("retail", state["customer.Segment"]);
    }

    [LoggerMessage(EventId = 5100, Level = LogLevel.Information, Message = "Customer {Email} (TCKN {NationalId}, card {Card}) was diagnosed with {Diagnosis}.")]
    private static partial void Diagnosed(
        ILogger logger,
        [EmailAddressData] string email,
        [NationalIdData] string nationalId,
        [PaymentCardData] string card,
        [HealthData] string diagnosis);

    [Fact]
    public void Intro_BeforeAndAfter()
    {
        var (logger, records, provider) = Host(redaction => redaction.SetPrivacyRedactors());
        using (provider)
        {
            Diagnosed(logger, "ayse.yilmaz@example.com", "10000000146", "4111111111111111", "asthma");
        }

        Assert.Equal(
            "Customer a***@example.com (TCKN *******0146, card 411111******1111) was diagnosed with [REDACTED].",
            Assert.Single(records).Message);
    }

    [LoggerMessage(EventId = 5104, Level = LogLevel.Warning, Message = "Payment failed for {UserId}.")]
    private static partial void PaymentFailed(ILogger logger, [OnlineIdentifierData] string userId);

    [Fact]
    public void Recipe2_UserIdTokensGroupByUser()
    {
        var pseudonymizer = new Pseudonymizer(Convert.FromBase64String(Convert.ToBase64String(new byte[32])));
        var (logger, records, provider) = Host(redaction => redaction.SetPrivacyRedactors(pseudonymizer));
        using (provider)
        {
            PaymentFailed(logger, "user-42");
            PaymentFailed(logger, "user-42");
            PaymentFailed(logger, "user-7");
        }

        Assert.Equal(records[0].Message, records[1].Message);
        Assert.NotEqual(records[0].Message, records[2].Message);
        Assert.DoesNotContain("user-42", records[0].Message, StringComparison.Ordinal);
        Assert.Equal($"Payment failed for {pseudonymizer.Pseudonymize("user-42")}.", records[0].Message);
    }

    public sealed record Profile(string Name, string Email);

    [Fact]
    public void DataSubjectRequests_ExportJsonMatchesTheReadme()
    {
        var request = new DataSubjectRequest("r-1", "customer-42", Now);
        var export = new DataSubjectExport(request, "customers-api", Now,
        [
            DataSubjectRecord.Create("profile", new Profile("Ayşe Yılmaz", "ayse@example.com"), ReadmeCamelCaseJsonContext.Default.Profile)
                with { Purpose = "Account management" },
        ]);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            export.WriteTo(writer);
        }

        Assert.Equal(
            """{"requestId":"r-1","subjectId":"customer-42","source":"customers-api","exportedAt":"2026-09-18T12:00:00+00:00","records":[{"category":"profile","purpose":"Account management","data":{"name":"Ayşe Yılmaz","email":"ayse@example.com"}}]}""",
            System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void Pitfalls_DefaultDiscriminator_GarblesMasks()
    {
        IRedactorProvider provider = new ServiceCollection()
            .AddRedaction(redaction => redaction.SetPrivacyRedactors())
            .BuildServiceProvider()
            .GetRequiredService<IRedactorProvider>();

        Assert.Equal("TR************************:*******ount", provider.GetRedactor(PrivacyTaxonomy.BankAccount).Redact("TR330006100519786457841326:BankAccount"));
        Assert.Equal(PiiMasking.RedactedSentinel, provider.GetRedactor(PrivacyTaxonomy.IpAddress).Redact("192.168.1.23:Ip"));
    }

    [Fact]
    public void Masking_TableExamples()
    {
        Assert.Equal("j***@example.com", PiiMasking.Email("j.doe@example.com"));
        Assert.Equal("j***@***.com", PiiMasking.Email("jane@example.com", revealDomain: false));
        Assert.Equal("+* (***) ***-4567", PiiMasking.Phone("+1 (555) 123-4567"));
        Assert.Equal("4111 11** **** 1111", PiiMasking.CardNumber("4111 1111 1111 1111"));
        Assert.Equal("DE** **** **** **** **30 00", PiiMasking.Iban("DE89 3704 0044 0532 0130 00"));
        Assert.Equal("*******0146", PiiMasking.NationalId("10000000146"));
        Assert.Equal("A*** N*** Y***", PiiMasking.PersonName("Ayşe Nur Yılmaz"));
        Assert.Equal("192.168.1.0", PiiMasking.IpAddress("192.168.1.23"));
        Assert.Equal("2001:db8:85a3::", PiiMasking.IpAddress("2001:db8:85a3::8a2e:370:7334"));
        Assert.Equal("ORD-********123", PiiMasking.Partial("ORD-2024-000123", 4, 3));
    }

    public static class LoyaltyTaxonomy
    {
        public static DataClassification LoyaltyTier { get; } = new(PrivacyTaxonomy.TaxonomyName, "LoyaltyTier");
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class LoyaltyTierDataAttribute() : DataClassificationAttribute(LoyaltyTaxonomy.LoyaltyTier);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Information, Message = "Tier is {Tier}.")]
    private static partial void TierChanged(ILogger logger, [LoyaltyTierData] string tier);

    [Fact]
    public void Recipe1_AddAKindOfData()
    {
        var (logger, records, provider) = Host(redaction => redaction
            .SetPrivacyRedactors()
            .SetRedactor<SuppressingRedactor>(LoyaltyTaxonomy.LoyaltyTier));
        using (provider)
        {
            TierChanged(logger, "platinum");
        }

        Assert.Equal("Tier is [REDACTED].", Assert.Single(records).Message);
    }

    [Fact]
    public async Task DataSubjectRequests_HandlerFollowsTheRules()
    {
        var store = new CustomerStore();
        store.Customers["customer-42"] = new Customer("Ayşe Yılmaz", "ayse@example.com", "10000000146", null, "retail");
        var handler = new CustomerPrivacyHandler(store, () => Now);
        var request = new DataSubjectRequest("req-1", "customer-42", Now);

        DataSubjectExport export = (await handler.ExportAsync(request)).Value;
        DataSubjectErasureReceipt first = (await handler.EraseAsync(request)).Value;
        DataSubjectErasureReceipt again = (await handler.EraseAsync(request)).Value;
        DataSubjectExport unknown = (await handler.ExportAsync(new DataSubjectRequest("req-2", "nobody", Now))).Value;

        Assert.Equal("Account management", Assert.Single(export.Records).Purpose);
        Assert.Equal(1, first.AnonymizedRecords);
        Assert.False(first.IsComplete);
        Assert.Same(first, again);
        Assert.Empty(unknown.Records);
    }

    [Fact]
    public async Task Recipe3_WriteAnExportToAFile()
    {
        var request = new DataSubjectRequest("req-1", "customer-42", Now);
        var export = new DataSubjectExport(request, "customers-api", Now, []);
        string path = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.json");

        try
        {
            await using (FileStream file = File.Create(path))
            {
                await using var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true });
                export.WriteTo(writer);
            }

            Assert.Equal("customers-api", JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement.GetProperty("source").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CustomerStore
    {
        public ConcurrentDictionary<string, Customer> Customers { get; } = new();

        public ConcurrentDictionary<string, DataSubjectErasureReceipt> Receipts { get; } = new();
    }

    private sealed class CustomerPrivacyHandler(CustomerStore customers, Func<DateTimeOffset> utcNow) : IDataSubjectRequestHandler
    {
        public Task<Result<DataSubjectExport>> ExportAsync(DataSubjectRequest request, CancellationToken cancellationToken = default)
        {
            DataSubjectRecord[] records = customers.Customers.TryGetValue(request.SubjectId, out Customer? customer)
                ? [DataSubjectRecord.Create("profile", customer, ReadmeJsonContext.Default.Customer) with { Purpose = "Account management" }]
                : [];

            return Task.FromResult<Result<DataSubjectExport>>(new DataSubjectExport(request, "customers-api", utcNow(), records));
        }

        public Task<Result<DataSubjectErasureReceipt>> EraseAsync(DataSubjectRequest request, CancellationToken cancellationToken = default)
        {
            if (customers.Receipts.TryGetValue(request.RequestId, out DataSubjectErasureReceipt? earlier))
            {
                return Task.FromResult<Result<DataSubjectErasureReceipt>>(earlier);
            }

            int anonymized = customers.Customers.TryRemove(request.SubjectId, out _) ? 1 : 0;
            var receipt = new DataSubjectErasureReceipt(request, "customers-api", utcNow(),
                ErasedRecords: 0, AnonymizedRecords: anonymized,
                Retained: [new RetainedData("invoices", "Tax Procedure Law 213, Art. 253", utcNow().AddYears(5))]);

            customers.Receipts[request.RequestId] = receipt;
            return Task.FromResult<Result<DataSubjectErasureReceipt>>(receipt);
        }
    }

    [JsonSerializable(typeof(Customer))]
    private sealed partial class ReadmeJsonContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Profile))]
    private sealed partial class ReadmeCamelCaseJsonContext : JsonSerializerContext;

    private sealed class Sink : ILoggerProvider
    {
        public List<(string Message, Dictionary<string, string?> State)> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Records);

        public void Dispose()
        {
        }

        private sealed class Capturing(List<(string, Dictionary<string, string?>)> records) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                records.Add((formatter(state, exception), state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(p => p.Key, p => p.Value?.ToString())
                    : []));
        }
    }
}
