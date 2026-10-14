using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.GraphQL.Errors;
using SharedKernel.Presentation.GraphQL.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.GraphQL.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext T-08: each GraphQL error is logged once under the package's own sub-block: 14400 at Error with
/// the exception for a server category, 14401 at Debug for a client category. Nothing a caller sent reaches a record.
/// </summary>
public sealed class GraphQLErrorLoggingTests
{
    private static readonly string Category = typeof(SharedKernelErrorFilter).FullName!;

    [Fact]
    public async Task ServerCategoryError_IsLoggedAtError_WithTheException()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GraphQLTestHost.StartAsync(loggerFactory: logs);

        using var result = await app.PostAsync("{ boom }");

        var records = logs.GetLogger(Category).Records;
        var record = records.ShouldHaveLogged(new EventId(14400), LogLevel.Error);
        record.Exception.Should().BeOfType<InvalidOperationException>();
        record.TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be(SharedKernel.Primitives.Errors.ErrorCodes.Unexpected.Default);
        record.TryGetProperty("StatusCode", out var status).Should().BeTrue();
        status.Should().Be(500);
    }

    [Fact]
    public async Task ClientCategoryError_IsLoggedAtDebug()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GraphQLTestHost.StartAsync(loggerFactory: logs);

        using var result = await app.PostAsync("{ fail(type: \"NotFound\") }");

        var records = logs.GetLogger(Category).Records;
        records.ShouldHaveLogged(new EventId(14401), LogLevel.Debug).Exception.Should().BeOfType<NotFoundException>();
        records.ShouldNotHaveLogged(new EventId(14400));
    }

    [Fact]
    public async Task RequestError_IsLoggedAtDebug_WithoutAnException()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GraphQLTestHost.StartAsync(loggerFactory: logs);

        using var result = await app.PostAsync("{ nope }");

        var records = logs.GetLogger(Category).Records;
        records.ShouldHaveLogged(new EventId(14401), LogLevel.Debug).Exception.Should().BeNull();
        records.ShouldHaveLoggedCount(new EventId(14401), 1);
    }

    [Fact]
    public async Task ARequestWithSeveralErrors_LogsOneRecordPerError()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GraphQLTestHost.StartAsync(loggerFactory: logs);

        using var result = await app.PostAsync("{ a: boom b: timeout c: fail(type: \"Conflict\") }");

        result.Root.GetProperty("errors").GetArrayLength().Should().Be(3);
        var records = logs.GetLogger(Category).Records;
        records.ShouldHaveLoggedCount(new EventId(14400), 2);
        records.ShouldHaveLoggedCount(new EventId(14401), 1);
        records.Should().HaveCount(3, "an error is logged once, not again when HotChocolate re-wraps a null propagation");
    }

    [Fact]
    public async Task Records_StayInTheSubBlock_FollowTheConventions_AndCarryNothingTheCallerSent()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GraphQLTestHost.StartAsync(loggerFactory: logs);

        using var a = await app.PostAsync("{ boom }");
        using var b = await app.PostAsync("{ fail(type: \"Unavailable\") }");
        using var c = await app.PostAsync("{ validationMany }");
        using var d = await app.PostAsync("{ nope }");

        var records = logs.GetLogger(Category).Records;
        records.Should().HaveCount(4);
        records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Presentation, 4);
        records.ShouldFollowLogConvention();

        // The exception travels as the exception parameter; its message is the service's own. Nothing else
        // (the message, the template, any state value) names it.
        foreach (var record in records)
        {
            record.Message.Should().NotContain("hunter2").And.NotContain("db.internal");
            (record.State ?? []).Select(property => property.Value?.ToString()).Should().NotContain(value => value != null && value.Contains("hunter2"));
        }

        logs.ShouldHaveCategory(typeof(SharedKernelErrorFilter));
    }
}
