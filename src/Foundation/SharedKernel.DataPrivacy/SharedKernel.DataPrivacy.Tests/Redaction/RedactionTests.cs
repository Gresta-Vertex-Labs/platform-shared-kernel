using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.DataPrivacy.Classification;
using SharedKernel.DataPrivacy.Masking;
using SharedKernel.DataPrivacy.Redaction;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Redaction;

public sealed partial class RedactionTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();

    private static IRedactorProvider Provider(Action<IRedactionBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddRedaction(configure);
        return services.BuildServiceProvider().GetRequiredService<IRedactorProvider>();
    }

    public static TheoryData<string, string, string> Masked => new()
    {
        { nameof(PrivacyTaxonomy.PersonName), "Ayşe Yılmaz", "A*** Y***" },
        { nameof(PrivacyTaxonomy.EmailAddress), "j.doe@example.com", "j***@example.com" },
        { nameof(PrivacyTaxonomy.PhoneNumber), "+90 532 123 45 67", "+** *** *** 45 67" },
        { nameof(PrivacyTaxonomy.NationalId), "10000000146", "*******0146" },
        { nameof(PrivacyTaxonomy.IpAddress), "192.168.1.23", "192.168.1.0" },
        { nameof(PrivacyTaxonomy.BankAccount), "TR330006100519786457841326", "TR********************1326" },
        { nameof(PrivacyTaxonomy.PaymentCard), "4111111111111111", "411111******1111" },
    };

    [Theory]
    [MemberData(nameof(Masked))]
    public void SetPrivacyRedactors_MasksIdentifiersThatCanBePartlyShown(string classification, string input, string expected)
    {
        IRedactorProvider provider = Provider(b => b.SetPrivacyRedactors());

        Redactor redactor = provider.GetRedactor(new DataClassification(PrivacyTaxonomy.TaxonomyName, classification));

        Assert.Equal(expected, redactor.Redact(input));
    }

    [Fact]
    public void SetPrivacyRedactors_SuppressesEverythingElse()
    {
        IRedactorProvider provider = Provider(b => b.SetPrivacyRedactors());
        string[] masked = Masked.Select(row => (string)row[0]).ToArray();

        foreach (DataClassification classification in PrivacyTaxonomy.All.Where(c => !masked.Contains(c.Value)))
        {
            Assert.Equal(PiiMasking.RedactedSentinel, provider.GetRedactor(classification).Redact("sensitive value"));
        }
    }

    [Fact]
    public void SetPrivacyRedactors_WithAPseudonymizer_TokenizesOnlineIdentifiers()
    {
        IRedactorProvider provider = Provider(b => b.SetPrivacyRedactors(new Pseudonymizer(Key)));

        string token = provider.GetRedactor(PrivacyTaxonomy.OnlineIdentifier).Redact("user-42");

        Assert.Equal(new Pseudonymizer(Key).Pseudonymize("user-42"), token);
        Assert.Equal(PiiMasking.RedactedSentinel, provider.GetRedactor(PrivacyTaxonomy.Health).Redact("diabetes"));
        Assert.Equal("j***@example.com", provider.GetRedactor(PrivacyTaxonomy.EmailAddress).Redact("j.doe@example.com"));
    }

    [Fact]
    public void SetRedactor_AfterSetPrivacyRedactors_OverridesOneClassification()
    {
        IRedactorProvider provider = Provider(b => b.SetPrivacyRedactors().SetRedactor<SuppressingRedactor>(PrivacyTaxonomy.EmailAddress));

        Assert.Equal(PiiMasking.RedactedSentinel, provider.GetRedactor(PrivacyTaxonomy.EmailAddress).Redact("j.doe@example.com"));
    }

    [Fact]
    public void LoggerMessage_RedactsTaggedParameters()
    {
        var sink = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddRedaction(b => b.SetPrivacyRedactors(new Pseudonymizer(Key)));
        services.AddLogging(b => b.EnableRedaction(o => o.ApplyDiscriminator = false).AddProvider(sink));
        using ServiceProvider provider = services.BuildServiceProvider();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("orders");

        LogOrderPlaced(logger, "j.doe@example.com", "user-42", "4111111111111111", 3);

        LogRecord record = Assert.Single(sink.Records);
        Assert.Equal("j***@example.com", record.State["Email"]);
        Assert.Equal(new Pseudonymizer(Key).Pseudonymize("user-42"), record.State["UserId"]);
        Assert.Equal("411111******1111", record.State["card"]);
        Assert.Equal("3", record.State["ItemCount"]);
        Assert.DoesNotContain(record.State.Values, v => v is not null && (v.Contains("j.doe") || v.Contains("4111111111111111")));
    }

    [Fact]
    public void LogProperties_RedactsTaggedMembers()
    {
        var sink = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddRedaction(b => b.SetPrivacyRedactors());
        services.AddLogging(b => b.EnableRedaction(o => o.ApplyDiscriminator = false).AddProvider(sink));
        using ServiceProvider provider = services.BuildServiceProvider();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("patients");

        LogPatientAdmitted(logger, new Patient("Ayşe Yılmaz", "10000000146", "asthma", "W-12"));

        LogRecord record = Assert.Single(sink.Records);
        Assert.Equal("A*** Y***", record.State["patient.Name"]);
        Assert.Equal("*******0146", record.State["patient.NationalId"]);
        Assert.Equal(PiiMasking.RedactedSentinel, record.State["patient.Diagnosis"]);
        Assert.Equal("W-12", record.State["patient.Ward"]);
    }

    [Fact]
    public void WithTheDefaultDiscriminator_NothingLeaks()
    {
        var sink = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddRedaction(b => b.SetPrivacyRedactors());
        services.AddLogging(b => b.EnableRedaction().AddProvider(sink));
        using ServiceProvider provider = services.BuildServiceProvider();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("patients");

        LogOrderPlaced(logger, "j.doe@example.com", "user-42", "4111111111111111", 3);
        LogPatientAdmitted(logger, new Patient("Ayşe Yılmaz", "10000000146", "asthma", "W-12"));

        string[] values = sink.Records.SelectMany(r => r.State.Values).OfType<string>().ToArray();
        Assert.DoesNotContain(values, v => v.Contains("j.doe") || v.Contains("user-42") || v.Contains("1111111111") || v.Contains("Ayşe") || v.Contains("10000000146") || v.Contains("asthma"));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Order placed by {Email} ({UserId}) with {ItemCount} items.")]
    private static partial void LogOrderPlaced(
        ILogger logger,
        [EmailAddressData] string email,
        [OnlineIdentifierData] string userId,
        [PaymentCardData] string card,
        int itemCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Patient admitted.")]
    private static partial void LogPatientAdmitted(ILogger logger, [LogProperties] Patient patient);

    public sealed record Patient(
        [property: PersonNameData] string Name,
        [property: NationalIdData] string NationalId,
        [property: HealthData] string Diagnosis,
        string Ward);

    private sealed record LogRecord(string Message, IReadOnlyDictionary<string, string?> State);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogRecord> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Records);

        public void Dispose()
        {
        }

        private sealed class Capturing(List<LogRecord> records) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(p => p.Key, p => p.Value?.ToString())
                    : [];
                records.Add(new LogRecord(formatter(state, exception), values));
            }
        }
    }
}
