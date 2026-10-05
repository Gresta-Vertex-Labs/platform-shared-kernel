using System.Globalization;
using System.Text;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class LocalizationCatalogBuilderTests : IDisposable
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sk-localization-" + Guid.NewGuid().ToString("N"));

    public LocalizationCatalogBuilderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    private string WriteFile(string name, string json)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
    }

    private static string Text(ILocalizationCatalog catalog, string code, CultureInfo culture)
        => catalog.TryGetTemplate(code, culture, out MessageTemplate? template) ? template.Text : "<missing>";

    [Fact]
    public void Add_LaterEntryForSameCodeAndCulture_Wins()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add("greeting", Turkish, "Selam")
            .Add("greeting", Turkish, "Merhaba")
            .Build();

        Assert.Equal("Merhaba", Text(catalog, "greeting", Turkish));
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void Add_InvalidTemplate_ThrowsFormatException_BlankArguments_Throw()
    {
        var builder = new LocalizationCatalogBuilder();

        Assert.Throws<FormatException>(() => builder.Add("x", Turkish, "{0}"));
        Assert.Throws<ArgumentException>(() => builder.Add(" ", Turkish, "text"));
        Assert.Throws<ArgumentException>(() => builder.Add("x", Turkish, " "));
        Assert.Throws<ArgumentNullException>(() => builder.Add("x", null!, "text"));
    }

    [Fact]
    public void AddJson_FlatAndNestedKeys_BecomeDottedCodes_CommentsAndTrailingCommasAllowed()
    {
        const string json = """
            {
              // comment
              "order.not_found": "{orderId} bulunamadı.",
              "payment": { "declined": { "card": "Kart reddedildi." } },
            }
            """;

        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().AddJson(Json(json), Turkish).Build();

        Assert.Equal("{orderId} bulunamadı.", Text(catalog, "order.not_found", Turkish));
        Assert.Equal("Kart reddedildi.", Text(catalog, "payment.declined.card", Turkish));
    }

    [Fact]
    public void AddJson_LeavesTheStreamOpen()
    {
        MemoryStream stream = Json("""{ "a": "b" }""");

        new LocalizationCatalogBuilder().AddJson(stream, Turkish);

        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("""{ "a": 1 }""", "expected a string or an object")]
    [InlineData("""{ "a": "x", "a": "y" }""", "more than once")]
    [InlineData("""{ "a.b": "x", "a": { "b": "y" } }""", "more than once")]
    [InlineData("""{ "a": "" }""", "empty translation")]
    [InlineData("""{ "a": "{0}" }""", "positional")]
    [InlineData("""[ "a" ]""", "must contain a JSON object")]
    [InlineData("""{ "a": "x" """, "not valid JSON")]
    [InlineData("""{ "": "x" }""", "empty key")]
    public void AddJson_InvalidContent_ThrowsFormatException_NamingTheProblem(string json, string expected)
    {
        FormatException ex = Assert.Throws<FormatException>(
            () => new LocalizationCatalogBuilder().AddJson(Json(json), Turkish));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddJsonFile_CultureFromFileName_AndErrorsNameTheFileAndCode()
    {
        string path = WriteFile("tr-TR.json", """{ "greeting": "Merhaba" }""");
        string broken = WriteFile("de.json", """{ "greeting": "Hallo {" }""");

        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().AddJsonFile(path).Build();
        FormatException ex = Assert.Throws<FormatException>(() => new LocalizationCatalogBuilder().AddJsonFile(broken));

        Assert.Equal("Merhaba", Text(catalog, "greeting", CultureInfo.GetCultureInfo("tr-TR")));
        Assert.Contains("de.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'greeting'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddJsonFile_FileNameThatIsNotACulture_Throws()
    {
        string path = WriteFile("messages.json", """{ "a": "b" }""");

        ArgumentException ex = Assert.Throws<ArgumentException>(() => new LocalizationCatalogBuilder().AddJsonFile(path));

        Assert.Contains("messages", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddJsonFile_ExplicitCulture_IgnoresFileName_MissingFileThrows()
    {
        string path = WriteFile("messages.json", """{ "a": "b" }""");

        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().AddJsonFile(path, Turkish).Build();

        Assert.Equal("b", Text(catalog, "a", Turkish));
        Assert.Throws<FileNotFoundException>(
            () => new LocalizationCatalogBuilder().AddJsonFile(Path.Combine(_directory, "nope.json"), Turkish));
    }

    [Fact]
    public void AddJsonDirectory_LoadsEveryCultureFile_AndReportsCultures()
    {
        WriteFile("en.json", """{ "greeting": "Hello" }""");
        WriteFile("tr.json", """{ "greeting": "Merhaba" }""");
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(_directory, "nested", "xx.json"), "not read");

        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().AddJsonDirectory(_directory).Build();

        Assert.Equal("Hello", Text(catalog, "greeting", English));
        Assert.Equal("Merhaba", Text(catalog, "greeting", Turkish));
        Assert.Equal(["en", "tr"], catalog.Cultures.Select(c => c.Name));
    }

    [Fact]
    public void AddJsonDirectory_MissingOrEmpty_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => new LocalizationCatalogBuilder().AddJsonDirectory(Path.Combine(_directory, "missing")));
        Assert.Throws<ArgumentException>(() => new LocalizationCatalogBuilder().AddJsonDirectory(_directory));
    }

    [Fact]
    public void AddEmbeddedJson_LoadsResourcesUnderThePrefix_CultureFromName()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .AddEmbeddedJson(typeof(LocalizationCatalogBuilderTests).Assembly, "SharedKernel.Localization.Tests.TestData.Embedded.")
            .Build();

        Assert.Equal("Merhaba {name}.", Text(catalog, "greeting", Turkish));
        Assert.Equal("Hello {name}.", Text(catalog, "greeting", English));
    }

    [Fact]
    public void AddEmbeddedJson_PrefixMatchingNothing_ThrowsWithAHint()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new LocalizationCatalogBuilder()
            .AddEmbeddedJson(typeof(LocalizationCatalogBuilderTests).Assembly, "Wrong.Prefix."));

        Assert.Contains("Wrong.Prefix.<culture>.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sources_AreLayered_LaterSourceOverridesEarlier()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .AddJson(Json("""{ "a": "shared", "b": "shared" }"""), Turkish)
            .AddJson(Json("""{ "b": "override" }"""), Turkish)
            .Build();

        Assert.Equal("shared", Text(catalog, "a", Turkish));
        Assert.Equal("override", Text(catalog, "b", Turkish));
    }

    [Fact]
    public void Build_Snapshots_LaterAddsDoNotChangeABuiltCatalog()
    {
        var builder = new LocalizationCatalogBuilder().Add("a", Turkish, "one");
        InMemoryLocalizationCatalog first = builder.Build();

        builder.Add("a", Turkish, "two");

        Assert.Equal("one", Text(first, "a", Turkish));
        Assert.Equal("two", Text(builder.Build(), "a", Turkish));
    }
}
