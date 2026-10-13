using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="LogTemplateAssertion"/> and <see cref="ErrorCodeFormatAssertion"/> (WO-087 / P-583 D-09): each violation shape fires,
/// a conforming fixture passes, the caller-supplied placeholder list, protocol prefixes and exemptions are honoured.
/// </summary>
public sealed class LogTemplateAndErrorCodeAssertionTests
{
    private static readonly LogTemplateOptions TemplateOptions = new(PlatformReport.FreeTextPlaceholders);

    private static ErrorCodeFormatOptions CodeOptions(IReadOnlyDictionary<string, string>? exempt = null) =>
        new(PlatformReport.ProtocolMirrorPrefixes, exempt);

    private static ObservabilityReport Templates(string assemblyName, params string[] messages)
    {
        var methods = string.Concat(messages.Select((message, index) =>
            $"[LoggerMessage(EventId = {8000 + index}, Level = LogLevel.Information, Message = {ToLiteral(message)})] public static void M{index}(ILogger logger, string id) {{ }}\n"));
        var source = $"using Microsoft.Extensions.Logging;\nnamespace {assemblyName} {{ public static class Log {{ {methods} }} }}";
        var target = ObservabilityFixture.Target(assemblyName, ObservabilityFixture.StorageFolder, source);
        return LogTemplateAssertion.Find([target], ObservabilityFixture.Registry(), TemplateOptions);
    }

    private static string ToLiteral(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [Fact]
    public void Template_ConformingTemplates_NoFinding()
    {
        var report = Templates(
            "Fixture.Templates.Ok",
            "Object '{Key}' was stored in bucket '{Bucket}'.",
            "Request {RequestName} failed with {ErrorType} {ErrorCode}.",
            "Took {Elapsed:0.00} seconds to answer '{Question}'.");

        report.IsClean.Should().BeTrue(report.RenderSites());
    }

    [Theory]
    [InlineData("Object stored", "template.no-terminal-period")]
    [InlineData("Object stored. Then it was verified.", "template.multiple-sentences")]
    [InlineData("Object stored — and verified.", "template.em-dash")]
    [InlineData("Object stored (key={Key}).", "template.key-value-parenthetical")]
    [InlineData("Object failed with {Error}.", "template.free-text-placeholder")]
    [InlineData("Operation failed because {Reason}.", "template.free-text-placeholder")]
    public void Template_EachViolationShape_Fires(string template, string rule)
    {
        var report = Templates("Fixture.Templates." + rule.Replace("template.", string.Empty).Replace("-", string.Empty), template);

        report.Findings.Should().Contain(f => f.Rule == rule);
        report.Findings.Should().OnlyContain(f => f.Domain == "08.Storage");
    }

    [Fact]
    public void Template_APeriodInsideAQuotedPlaceholderOrAFormat_IsNotASecondSentence()
    {
        var report = Templates("Fixture.Templates.Quoted", "Read '{FileName}'.", "Waited {Seconds:0.00} for '{Name}'.");

        report.Findings.Should().NotContain(f => f.Rule == "template.multiple-sentences");
    }

    [Fact]
    public void Template_ReportsEverySiteAndHonoursTheCallersPlaceholderList()
    {
        var registry = ObservabilityFixture.Registry();
        var target = ObservabilityFixture.Target("Fixture.Templates.Callers", ObservabilityFixture.StorageFolder, """
            using Microsoft.Extensions.Logging;
            namespace Fixture.Templates.Callers
            {
                public static class Log
                {
                    [LoggerMessage(8000, LogLevel.Warning, "First {Detail}")] public static void A(ILogger l, string detail) { }
                    [LoggerMessage(8001, LogLevel.Warning, "Second {Detail}")] public static void B(ILogger l, string detail) { }
                }
            }
            """);

        var forbidding = LogTemplateAssertion.Find([target], registry, new LogTemplateOptions(["Detail"]));
        var permissive = LogTemplateAssertion.Find([target], registry, new LogTemplateOptions([]));

        forbidding.Findings.Where(f => f.Rule == "template.free-text-placeholder").Should().HaveCount(2);
        permissive.Findings.Should().NotContain(f => f.Rule == "template.free-text-placeholder");
    }

    [Fact]
    public void Template_Assert_ThrowsOnAFinding()
    {
        var target = ObservabilityFixture.Target("Fixture.Templates.Enforce", ObservabilityFixture.StorageFolder,
            "using Microsoft.Extensions.Logging;\nnamespace Fixture.Templates.Enforce { public static class Log { [LoggerMessage(8000, LogLevel.Warning, \"No period\")] public static void A(ILogger l) { } } }");
        var registry = ObservabilityFixture.Registry();

        LogTemplateAssertion.Find([target], registry, TemplateOptions).Findings.Should().NotBeEmpty();
        var enforce = () => LogTemplateAssertion.Assert([target], registry, TemplateOptions, new StringWriter());
        enforce.Should().Throw<InvalidOperationException>().WithMessage("*template.no-terminal-period*");
    }

    private static ObservabilityReport Codes(string assemblyName, string source, ErrorCodeFormatOptions? options = null)
    {
        var target = ObservabilityFixture.Target(assemblyName, ObservabilityFixture.StorageFolder, source);
        return ErrorCodeFormatAssertion.Find([target], ObservabilityFixture.Registry(), options ?? CodeOptions());
    }

    [Fact]
    public void ErrorCode_ConformingConstantsAndFactoryCalls_NoFinding()
    {
        var report = Codes("Fixture.Codes.Ok", """
            using SharedKernel.Primitives.Errors;
            namespace Fixture.Codes.Ok
            {
                public static class StorageErrorCodes
                {
                    public const string NotFound = "storage.not_found";
                    public const string TooLong = "storage.expiry.too_long";
                    public const string GrpcCode = "grpc.not_found";
                    public const string HttpCode = "http.404";
                    public static class Nested { public const string Inner = "storage.nested_code"; }
                }

                public static class Errors
                {
                    public static Error Missing() => Error.NotFound(StorageErrorCodes.NotFound, "Missing.");
                    public static Error Raw() => Error.Unexpected("storage.raw_code", "Raw.");
                    public static Error Built() => new Error("storage.built", "Built.", ErrorType.Unexpected);
                }
            }
            """);

        report.IsClean.Should().BeTrue(report.RenderSites());
    }

    [Fact]
    public void ErrorCode_EachMalformedShape_FiresAtEveryConstantAndCallSite()
    {
        var report = Codes("Fixture.Codes.Bad", """
            using SharedKernel.Primitives.Errors;
            namespace Fixture.Codes.Bad
            {
                public static class SearchErrorCodes
                {
                    public const string Pascal = "Search.NotFound";
                    public const string NoDomain = "notfound";
                    public const string Dashed = "search.not-found";
                    public const string DigitLed = "search.404";
                    public const string TrailingUnderscore = "search.not_found_";
                    public const string HttpOk = "http.404";
                }

                public static class Errors
                {
                    public static Error A() => Error.Conflict("Storage.Duplicate", "x");
                    public static Error B() => new Error("OnlyOne", "x", ErrorType.Unexpected);
                }
            }
            """);

        report.Findings.Should().OnlyContain(f => f.Rule == "errorcode.format");
        report.Findings.Select(f => f.Detail).Should().HaveCount(7)
            .And.Contain(d => d.Contains("'Search.NotFound'", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("'notfound'", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("'search.not-found'", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("'search.404'", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("'Storage.Duplicate'", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("'OnlyOne'", StringComparison.Ordinal));
        report.Findings.Select(f => f.Detail).Should().NotContain(d => d.Contains("'http.404'", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrorCode_ProtocolValueExemptedByTheCallerWithAReason_Passes()
    {
        const string source = """
            namespace Fixture.Codes.Protocol
            {
                public static class SecurityErrorCodes
                {
                    public const string InvalidToken = "invalid_token";
                    public const string UseDpopNonce = "use_dpop_nonce";
                }
            }
            """;
        var exempt = new Dictionary<string, string>
        {
            ["invalid_token"] = "RFC 6750 value, named in 12.Security CLAUDE.md",
            ["use_dpop_nonce"] = "RFC 9449 value, named in 12.Security CLAUDE.md",
        };

        Codes("Fixture.Codes.Protocol", source).Findings.Should().HaveCount(2);
        Codes("Fixture.Codes.ProtocolExempt", source.Replace("Fixture.Codes.Protocol", "Fixture.Codes.ProtocolExempt", StringComparison.Ordinal), CodeOptions(exempt)).IsClean.Should().BeTrue();
    }

    [Fact]
    public void ErrorCode_ADomainWithoutAnErrorCodesClass_YieldsNoFinding()
    {
        var report = Codes("Fixture.Codes.None", "namespace Fixture.Codes.None { public static class Notes { public const string Text = \"Not An Error Code\"; } }");

        report.IsClean.Should().BeTrue();
    }

    [Fact]
    public void ErrorCode_OptionsRejectAnExemptionWithoutAReason()
    {
        var act = () => CodeOptions(new Dictionary<string, string> { ["x"] = "" });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ErrorCode_Assert_EnforceThrows_ReportDoesNot()
    {
        var target = ObservabilityFixture.Target("Fixture.Codes.Enforce", ObservabilityFixture.StorageFolder,
            "namespace Fixture.Codes.Enforce { public static class XErrorCodes { public const string Bad = \"Bad\"; } }");
        var registry = ObservabilityFixture.Registry();

        ErrorCodeFormatAssertion.Find([target], registry, CodeOptions()).Findings.Should().ContainSingle();
        var enforce = () => ErrorCodeFormatAssertion.Assert([target], registry, CodeOptions(), new StringWriter());
        enforce.Should().Throw<InvalidOperationException>().WithMessage("*errorcode.format*");
    }

    [Fact]
    public void RealAssembly_Primitives_ErrorCodesAreWellFormed()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);
        var target = PlatformReport.TargetFor(graph, "SharedKernel.Primitives");

        var report = ErrorCodeFormatAssertion.Find([target], registry, CodeOptions());

        report.Findings.Where(f => f.Rule != "unscanned").Should().BeEmpty(report.RenderSites());
    }

    [Fact]
    public void RealAssembly_ReportingAbstractions_TemplatesFollowTheStyle()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);
        var target = PlatformReport.TargetFor(graph, "SharedKernel.Reporting.Abstractions");
        target.AssemblyFile.Should().NotBeNull("the solution is built before its tests run");

        var report = LogTemplateAssertion.Find([target], registry, TemplateOptions);

        report.Findings.Should().BeEmpty(report.RenderSites());

        // Not vacuous: the assembly does carry [LoggerMessage] templates, and a stricter caller list reports them.
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(target.AssemblyFile!, new Mono.Cecil.ReaderParameters { InMemory = true });
        var messages = module.GetTypes().SelectMany(t => t.Methods).Count(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "LoggerMessageAttribute"));
        messages.Should().BeGreaterThan(0);
    }
}
