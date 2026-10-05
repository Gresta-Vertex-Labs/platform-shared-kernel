using FluentAssertions;
using Xunit;

namespace SharedKernel.Linter.Tests;

/// <summary>
/// Asserts the shipped <c>.editorconfig</c> is internally coherent.
/// </summary>
/// <remarks>
/// <para>
/// EditorConfig fails silently. A naming rule whose <c>symbols</c> names a group that was never
/// declared, or whose <c>style</c> names a style that does not exist, is not an error — the rule
/// simply never applies, and the convention it was meant to enforce quietly is not enforced. The
/// same is true of a misspelled key. Nothing in a build reports any of it, which is why it is
/// worth a test.
/// </para>
/// </remarks>
public class EditorConfigTests
{
    private static readonly string[] ValidSeverities =
    [
        "none",
        "silent",
        "suggestion",
        "warning",
        "error",
    ];

    [Fact]
    public void EveryNamingRule_IsComplete()
    {
        var rules = EditorConfigFile.NamingRules();

        rules
            .Should()
            .NotBeEmpty(because: "a platform style config without naming conventions is not one");

        foreach (var (name, properties) in rules)
        {
            properties
                .Should()
                .ContainKey(
                    "symbols",
                    because: $"naming rule '{name}' does nothing without a symbol group"
                );
            properties
                .Should()
                .ContainKey("style", because: $"naming rule '{name}' does nothing without a style");
            properties
                .Should()
                .ContainKey(
                    "severity",
                    because: $"naming rule '{name}' defaults to none without a severity"
                );
        }
    }

    [Fact]
    public void EveryNamingRule_ReferencesADeclaredSymbolGroup()
    {
        var declared = EditorConfigFile.NamingSymbolGroups().Keys;

        foreach (var (name, properties) in EditorConfigFile.NamingRules())
        {
            var symbols = properties["symbols"];
            declared
                .Should()
                .Contain(
                    symbols,
                    because: $"naming rule '{name}' points at symbol group '{symbols}', which is never declared, "
                        + "so the rule silently never applies"
                );
        }
    }

    [Fact]
    public void EveryNamingRule_ReferencesADeclaredStyle()
    {
        var declared = EditorConfigFile.NamingStyles().Keys;

        foreach (var (name, properties) in EditorConfigFile.NamingRules())
        {
            var style = properties["style"];
            declared
                .Should()
                .Contain(
                    style,
                    because: $"naming rule '{name}' points at style '{style}', which is never declared, "
                        + "so the rule silently never applies"
                );
        }
    }

    [Fact]
    public void EveryDeclaredSymbolGroup_IsUsedByARule()
    {
        var used = EditorConfigFile
            .NamingRules()
            .Values.Select(properties => properties["symbols"])
            .ToHashSet();

        foreach (var group in EditorConfigFile.NamingSymbolGroups().Keys)
        {
            used.Should()
                .Contain(group, because: $"symbol group '{group}' is declared but no rule uses it");
        }
    }

    [Fact]
    public void EveryDeclaredStyle_IsUsedByARule()
    {
        var used = EditorConfigFile
            .NamingRules()
            .Values.Select(properties => properties["style"])
            .ToHashSet();

        foreach (var style in EditorConfigFile.NamingStyles().Keys)
        {
            used.Should()
                .Contain(style, because: $"style '{style}' is declared but no rule uses it");
        }
    }

    [Fact]
    public void EveryDeclaredSymbolGroup_DeclaresItsApplicableKinds()
    {
        foreach (var (name, properties) in EditorConfigFile.NamingSymbolGroups())
        {
            properties
                .Should()
                .ContainKey(
                    "applicable_kinds",
                    because: $"symbol group '{name}' matches nothing without applicable_kinds"
                );
        }
    }

    [Fact]
    public void EveryNamingRuleSeverity_IsAValidToken()
    {
        foreach (var (name, properties) in EditorConfigFile.NamingRules())
        {
            ValidSeverities
                .Should()
                .Contain(
                    properties["severity"],
                    because: $"naming rule '{name}' declares severity '{properties["severity"]}', which EditorConfig "
                        + "does not recognise, so the rule is ignored"
                );
        }
    }

    [Fact]
    public void EveryDiagnosticSeverity_IsAValidToken()
    {
        var severities = EditorConfigFile.DiagnosticSeverities();

        severities.Should().NotBeEmpty();

        foreach (var (id, severity) in severities)
        {
            ValidSeverities
                .Should()
                .Contain(
                    severity,
                    because: $"dotnet_diagnostic.{id}.severity is '{severity}', which is not a valid token"
                );
        }
    }

    [Fact]
    public void CSharpSection_DeclaresTheWrapColumnCSharpierReads()
    {
        // CSharpier reads max_line_length from .editorconfig as its print width. With no
        // .csharpierrc shipped, this key is the ONLY place the wrap column is configured, so its
        // absence would silently fall back to CSharpier's own default and diverge from the
        // guide the IDE draws.
        var text = EditorConfigFile.Text;

        text.Should()
            .Contain(
                "max_line_length",
                because: "it is the single source of the formatter's print width"
            );
    }

    [Fact]
    public void NoSection_AsksForAByteOrderMark()
    {
        // CSharpier writes files without a BOM and does not read the charset key at all, so
        // declaring utf-8-bom guarantees a standoff: the IDE adds the mark on save, the formatter
        // strips it on the next run, and the first line of the file churns in every diff. Caught
        // on this package's own project files while adopting the formatter.
        var charsets = EditorConfigFile
            .AllSectionEntries()
            .Where(entry => entry.Key == "charset")
            .Select(entry => entry.Value)
            .ToArray();

        charsets.Should().NotBeEmpty();
        charsets
            .Should()
            .AllBe(
                "utf-8",
                because: "the shipped formatter strips the BOM, so asking for one puts the IDE and the "
                    + "formatter in permanent disagreement"
            );
    }

    [Fact]
    public void File_IsRootedSoThePlatformStyleIsAuthoritative()
    {
        // Without root = true, EditorConfig keeps walking up past the consumer's repository and
        // merges whatever it finds, so the platform style stops being deterministic.
        EditorConfigFile
            .Text.TrimStart()
            .Split('\n')
            .Should()
            .Contain(line => line.Trim() == "root = true");
    }

    [Theory]
    [InlineData("*.{sh,bash}", "lf")]
    [InlineData("*.{cmd,bat}", "crlf")]
    public void ScriptSections_PinTheLineEndingTheInterpreterRequires(
        string section,
        string expected
    )
    {
        // A shell script with CRLF fails on its shebang line; a .bat with LF misbehaves on
        // Windows. These two are correctness, not taste, so they must not inherit the default.
        var values = EditorConfigFile.Section(section);

        values.Should().ContainKey("end_of_line");
        values["end_of_line"].Should().Be(expected);
    }

    [Fact]
    public void MarkdownSection_DoesNotTrimTrailingWhitespace()
    {
        // Two trailing spaces is a hard line break in Markdown; trimming them changes rendered
        // output, so the global default has to be overridden here.
        var values = EditorConfigFile.Section("*.md");

        values.Should().ContainKey("trim_trailing_whitespace");
        values["trim_trailing_whitespace"].Should().Be("false");
    }

    [Fact]
    public void TestSection_RelaxesTheMemberNamingRule()
    {
        // Underscores in test method names are how a test states its scenario. If the production
        // naming rule applied there, every test in every consuming service would warn.
        var sections = EditorConfigFile.SectionNames();

        sections
            .Should()
            .Contain(
                name => name.Contains("Tests", StringComparison.Ordinal),
                because: "production naming conventions must not fire on test method names"
            );
    }

    [Fact]
    public void NoSectionIsDeclaredTwiceWithConflictingIndentation()
    {
        // Repeated [*.cs] sections are legal and used here to group concerns, but two of them
        // setting indent_size differently would make the effective value depend on file order.
        var indentValues = EditorConfigFile
            .AllSectionEntries()
            .Where(entry => entry.Key == "indent_size")
            .GroupBy(entry => entry.Section)
            .Where(group => group.Select(entry => entry.Value).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        indentValues
            .Should()
            .BeEmpty(because: "a section must not set indent_size to two different values");
    }
}
