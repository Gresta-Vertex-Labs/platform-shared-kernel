using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;

namespace SharedKernel.ServiceDefaults.Security.Tests;

/// <summary>
/// Log conventions of the request-context middleware: level, template, sub-block 2 (13200-13299), the
/// <c>ILogger&lt;RequestContextMiddleware&gt;</c> category, and that no tenant, actor, client, address or caller-supplied
/// value reaches a record. The startup failure message is a sentence with its remedy.
/// </summary>
public sealed class RequestContextLoggingTests
{
    private const string Canary = "canary-correlation-7f3a91-with-spaces and a very long tail that fails validation ###";

    private static async Task<(InMemoryLoggerFactory Factory, List<LogRecord> Records)> RunAsync(string? inboundCorrelationId)
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddScoped<IUserContext>(_ => new FakeUserContext
        {
            TenantId = new TenantId(Guid.Parse("c0ffee00-0000-4000-8000-0000000000aa")),
            SubjectId = "canary-subject-id",
            ClientId = "canary-client-id",
        });
        services.AddSharedKernelRequestContext();
        await using var provider = services.BuildServiceProvider();
        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();

        var context = new DefaultHttpContext { RequestServices = provider.CreateScope().ServiceProvider };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.77");
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));
        if (inboundCorrelationId is not null)
        {
            context.Request.Headers[WellKnownHeaders.CorrelationId] = inboundCorrelationId;
        }

        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask, provider.GetRequiredService<ILogger<RequestContextMiddleware>>());
        await middleware.InvokeAsync(context);
        return (factory, [.. factory.Loggers.Values.SelectMany(l => l.Records)]);
    }

    [Fact]
    public async Task ARequestWithoutACorrelationId_Logs13200AtDebug()
    {
        var (factory, records) = await RunAsync(null);

        records.ShouldHaveLogged(13200, LogLevel.Debug);
        records.ShouldHaveLoggedTemplate(13200, "Created a correlation id for a request that carried none.");
        records.ShouldFollowLogConvention();
        records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.ServiceDefaults, 2);
        factory.ShouldHaveCategory(typeof(RequestContextMiddleware));
    }

    [Fact]
    public async Task ARejectedCorrelationId_Logs13201WithTheLengthOnly_AndNoCanaryReachesAnyRecord()
    {
        var (factory, records) = await RunAsync(Canary);

        var record = records.ShouldHaveLogged(13201, LogLevel.Warning);
        record.TryGetProperty("CorrelationIdLength", out var length).Should().BeTrue();
        length.Should().Be(Canary.Length);
        records.ShouldHaveLoggedTemplate(13201, "Replaced an inbound correlation id of length {CorrelationIdLength} that failed validation.");
        records.ShouldFollowLogConvention();
        records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.ServiceDefaults, 2);
        records.ShouldNotLeak(Canary, "canary-correlation", "canary-subject-id", "canary-client-id", "c0ffee00", "203.0.113.77");
        factory.ShouldHaveCategory(typeof(RequestContextMiddleware));
    }

    [Fact]
    public void UseWithoutTheServices_FailsWithASentenceThatNamesTheRemedy()
    {
        var app = WebApplication.CreateBuilder().Build();

        var act = () => app.UseSharedKernelRequestContext();

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "UseSharedKernelRequestContext() requires AddSharedKernelRequestContext(). Call services.AddSharedKernelRequestContext() before building the application.");
    }
}
