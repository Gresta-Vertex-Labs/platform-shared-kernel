using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Logging;

internal static partial class ConventionalLogMessages
{
    [LoggerMessage(EventId = 90101, Level = LogLevel.Information, Message = "Index '{IndexName}' was created.")]
    public static partial void IndexCreated(ILogger logger, string indexName);

    [LoggerMessage(EventId = 90102, Level = LogLevel.Error, Message = "Index '{IndexName}' could not be created.")]
    public static partial void IndexFailed(ILogger logger, Exception exception, string indexName);

    [LoggerMessage(EventId = 90103, Level = LogLevel.Warning, Message = "Retry {Attempt} of {Max} was scheduled.")]
    public static partial void RetryScheduled(ILogger logger, int attempt, int max);
}

public sealed class LogTelemetryAssertionsTests
{
    private static InvalidOperationException Fails(Action assertion) => Assert.Throws<InvalidOperationException>(assertion);

    private static void Raw(InMemoryLogger logger, int id, LogLevel level, string template, Exception? exception = null, params KeyValuePair<string, object?>[] values)
    {
        List<KeyValuePair<string, object?>> state = [.. values, new("{OriginalFormat}", template)];
        logger.Log(level, new EventId(id), state, exception, (_, _) => template);
    }

    // ---- Template --------------------------------------------------------------------------------------

    [Fact]
    public void Template_IsTheOriginalFormat_ForGeneratedAndPlainCalls_AndNullWithoutState()
    {
        var logger = new InMemoryLogger();

        ConventionalLogMessages.IndexCreated(logger, "orders");
#pragma warning disable CA2254, SK0010 // Deliberately a plain call: the template must be read from the formatted state.
        logger.LogInformation(new EventId(90110), "Plain {Name}.", "x");
#pragma warning restore CA2254, SK0010
        logger.Log(LogLevel.Information, new EventId(90111), "just text", null, (s, _) => s);

        Assert.Equal("Index '{IndexName}' was created.", logger.Records[0].Template);
        Assert.Equal("Plain {Name}.", logger.Records[1].Template);
        Assert.Null(logger.Records[2].Template);
    }

    [Fact]
    public void ShouldHaveLoggedTemplate_MatchesTheTemplateNotTheRenderedMessage()
    {
        var logger = new InMemoryLogger();
        ConventionalLogMessages.IndexCreated(logger, "orders");

        var record = logger.Records.ShouldHaveLoggedTemplate(new EventId(90101), "Index '{IndexName}' was created.");

        Assert.Equal("Index 'orders' was created.", record.Message);
        var wrongTemplate = Fails(() => logger.Records.ShouldHaveLoggedTemplate(new EventId(90101), "Index 'orders' was created."));
        Assert.Contains("found templates ['Index '{IndexName}' was created.']", wrongTemplate.Message);
        Assert.Contains("no record with that EventId", Fails(() => logger.Records.ShouldHaveLoggedTemplate(new EventId(1), "x")).Message);
    }

    // ---- Exception -------------------------------------------------------------------------------------

    [Fact]
    public void ExceptionAssertions_PassAndFail()
    {
        var logger = new InMemoryLogger();
        ConventionalLogMessages.IndexCreated(logger, "orders");
        ConventionalLogMessages.IndexFailed(logger, new TimeoutException("slow"), "orders");

        logger.Records.ShouldHaveLoggedWithException(new EventId(90102), typeof(TimeoutException));
        logger.Records.ShouldHaveLoggedWithException(new EventId(90102), typeof(Exception));
        logger.Records.ShouldHaveLoggedWithoutException(new EventId(90101));

        Assert.Contains("'System.ArgumentException'", Fails(() => logger.Records.ShouldHaveLoggedWithException(new EventId(90102), typeof(ArgumentException))).Message);
        Assert.Contains("'(none)'", Fails(() => logger.Records.ShouldHaveLoggedWithException(new EventId(90101), typeof(Exception))).Message);
        Assert.Contains("all 1 carried one", Fails(() => logger.Records.ShouldHaveLoggedWithoutException(new EventId(90102))).Message);
        Assert.Contains("no record with that EventId", Fails(() => logger.Records.ShouldHaveLoggedWithoutException(new EventId(5))).Message);
    }

    // ---- Leaks -----------------------------------------------------------------------------------------

    [Fact]
    public void ShouldNotLeak_ScansMessageStateAndExceptionMessage()
    {
        var logger = new InMemoryLogger();
        ConventionalLogMessages.IndexCreated(logger, "orders");
        logger.Records.ShouldNotLeak("tenant-42");

        var inState = new InMemoryLogger();
        ConventionalLogMessages.IndexCreated(inState, "tenant-42-index");
        var message = Fails(() => inState.Records.ShouldNotLeak("tenant-42"));
        Assert.Contains("EventId 90101 message contains 'tenant-42'", message.Message);
        Assert.Contains("property 'IndexName' contains 'tenant-42'", message.Message);

        var inException = new InMemoryLogger();
        ConventionalLogMessages.IndexFailed(inException, new InvalidOperationException("for tenant-42"), "orders");
        Assert.Contains("exception message", Fails(() => inException.Records.ShouldNotLeak("tenant-42")).Message);
        Assert.Throws<ArgumentException>(() => logger.Records.ShouldNotLeak(""));
    }

    // ---- Conventions -----------------------------------------------------------------------------------

    [Fact]
    public void ShouldFollowLogConvention_AcceptsConventionalRecords()
    {
        var logger = new InMemoryLogger();
        ConventionalLogMessages.IndexCreated(logger, "orders");
        ConventionalLogMessages.IndexFailed(logger, new TimeoutException(), "orders");
        ConventionalLogMessages.RetryScheduled(logger, 1, 3);

        Assert.Equal(3, logger.Records.ShouldFollowLogConvention().Count);
    }

    [Theory]
    [InlineData("Index created", "one sentence ending in a period")]
    [InlineData("Index created. Retrying now.", "one sentence ending in a period")]
    [InlineData("Failed with {Error}.", "[L3] placeholder '{Error}' is a forbidden detail placeholder")]
    [InlineData("Failed because of {Reason}.", "forbidden detail placeholder")]
    [InlineData("Failed with {Detail}.", "forbidden detail placeholder")]
    [InlineData("Failed for {attempt}.", "[L3] placeholder '{attempt}' is not PascalCase")]
    [InlineData("Failed for {0}.", "not PascalCase")]
    [InlineData("Index failed — retrying.", "contains an em-dash")]
    [InlineData("Index failed (attempt=3).", "key=value parenthetical")]
    [InlineData("Index {IndexName} was created.", "identifier placeholder '{IndexName}' is not wrapped in single quotes")]
    [InlineData("Setting {SettingKey} was read.", "identifier placeholder '{SettingKey}' is not wrapped in single quotes")]
    [InlineData("Resolved for {TenantId}.", "[L5] placeholder '{TenantId}'")]
    [InlineData("Called {Url}.", "[L5] placeholder '{Url}'")]
    [InlineData("Failed with {ErrorMessage}.", "[L5] placeholder '{ErrorMessage}'")]
    public void ShouldFollowLogConvention_RejectsEachViolatingTemplate(string template, string expected)
    {
        var logger = new InMemoryLogger();
        Raw(logger, 90120, LogLevel.Information, template);

        var message = Fails(() => logger.Records.ShouldFollowLogConvention()).Message;

        Assert.Contains(expected, message);
    }

    [Fact]
    public void ShouldFollowLogConvention_AppliesL4ToErrorLevelAndToExceptionValues_AndListsEveryViolation()
    {
        var logger = new InMemoryLogger();
        Raw(logger, 90130, LogLevel.Error, "Index failed.");
        Raw(logger, 90131, LogLevel.Critical, "Index failed.");
        Raw(logger, 90132, LogLevel.Information, "Index failed for '{IndexName}'.", null, new KeyValuePair<string, object?>("IndexName", new InvalidOperationException()));
        Raw(logger, 90133, LogLevel.Error, "Index failed.", new InvalidOperationException());
        Raw(logger, 90134, LogLevel.Information, "Index failed");
        logger.Log(LogLevel.Information, new EventId(90135), "text", null, (s, _) => s);

        var message = Fails(() => logger.Records.ShouldFollowLogConvention()).Message;

        Assert.Contains("EventId 90130 [L4] is logged at Error without an exception parameter", message);
        Assert.Contains("EventId 90131 [L4] is logged at Critical", message);
        Assert.Contains("EventId 90132 [L4] carries an exception as a placeholder value", message);
        Assert.DoesNotContain("EventId 90133", message);
        Assert.Contains("EventId 90134 [L3]", message);
        Assert.Contains("EventId 90135 [L3] has no message template", message);
        Assert.Contains("violate 5 convention(s)", message);
    }

    // ---- Sub-block and categories ----------------------------------------------------------------------

    [Fact]
    public void ShouldOnlyLogEventIdsInSubBlock_HonoursTheBoundaries()
    {
        var domainBase = LoggingEventIdRanges.Persistence;
        var start = LoggingEventIdRanges.SubBlockStart(domainBase, 2);
        var logger = new InMemoryLogger();
        Raw(logger, start, LogLevel.Information, "First.");
        Raw(logger, start + 99, LogLevel.Information, "Last.");

        logger.Records.ShouldOnlyLogEventIdsInSubBlock(domainBase, 2);

        Raw(logger, start - 1, LogLevel.Information, "Before.");
        Raw(logger, start + 100, LogLevel.Information, "After.");
        var message = Fails(() => logger.Records.ShouldOnlyLogEventIdsInSubBlock(domainBase, 2)).Message;
        Assert.Contains($"{start}-{start + 99}", message);
        Assert.Contains($"[{start - 1}, {start + 100}]", message);
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.Records.ShouldOnlyLogEventIdsInSubBlock(domainBase, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.Records.ShouldOnlyLogEventIdsInSubBlock(1500, 0));
    }

    [Fact]
    public void LoggerFactoryAssertions_CheckCategories()
    {
        var factory = new InMemoryLoggerFactory();
        factory.CreateLogger(typeof(LogTelemetryAssertionsTests).FullName!);
        factory.CreateLogger(typeof(Dictionary<string, int>).FullName!.Split('`')[0]);
        factory.CreateLogger(typeof(Nested).FullName!.Replace('+', '.'));

        factory.ShouldHaveCategory(typeof(LogTelemetryAssertionsTests))
            .ShouldHaveCategory(typeof(Dictionary<,>))
            .ShouldHaveCategory(typeof(Nested))
            .ShouldOnlyHaveCategoriesStartingWith("S");

        Assert.Contains("'System.String'", Fails(() => factory.ShouldHaveCategory(typeof(string))).Message);
        Assert.Contains("'System.Collections.Generic.Dictionary'", Fails(() => factory.ShouldOnlyHaveCategoriesStartingWith("SharedKernel.")).Message);
    }

    private sealed class Nested;
}
