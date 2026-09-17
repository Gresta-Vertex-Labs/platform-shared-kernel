using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Presentation.WebApi.RateLimiting;
using SharedKernel.Presentation.WebApi.Tests.Authorization;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Logging;

/// <summary>
/// Tests for the structured security-audit <c>[LoggerMessage]</c> entries added across the four
/// HTTP-rejection paths (WO-063, P-414) — asserted via <c>16.Testing</c>'s structured-log test
/// double, never a compile-only/reflection-only check.
/// </summary>
public class SecurityAuditLoggingTests
{
    private const string SecretBearerToken = "Bearer super-secret-token-abc123-do-not-log";

    // CorsPolicyOptionsValidator is internal to SharedKernel.Presentation.WebApi, so it cannot be
    // referenced via typeof() from this test assembly. The real BCL Logger<T> adapter's category
    // name is the type's full name — matches this literal exactly.
    private const string CorsPolicyOptionsValidatorCategoryName = "SharedKernel.Presentation.WebApi.Cors.CorsPolicyOptionsValidator";

    [Fact]
    public async Task AuthorizationRequirementEndpointFilter_Rejection_LogsEventId14002()
    {
        var inMemoryLogger = new InMemoryLogger<AuthorizationRequirementEndpointFilter>();
        var filter = new AuthorizationRequirementEndpointFilter(inMemoryLogger);
        var userContext = new FakeUserContext { Roles = [] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequireRoleAttribute("Admin"));
        context.HttpContext.Request.Headers.Authorization = SecretBearerToken;

        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        inMemoryLogger.Records.ShouldHaveLogged(new EventId(14002), LogLevel.Warning);
    }

    [Fact]
    public async Task AuthorizationRequirementEndpointFilter_Rejection_NeverLogsRawBearerToken()
    {
        var inMemoryLogger = new InMemoryLogger<AuthorizationRequirementEndpointFilter>();
        var filter = new AuthorizationRequirementEndpointFilter(inMemoryLogger);
        var userContext = new FakeUserContext { Roles = [] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequireRoleAttribute("Admin"));
        context.HttpContext.Request.Headers.Authorization = SecretBearerToken;

        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        AssertNoRecordContainsSecret(inMemoryLogger.Records, SecretBearerToken);
    }

    [Fact]
    public async Task IdempotencyKeyRequirementEndpointFilter_Rejection_LogsEventId14003()
    {
        var inMemoryLogger = new InMemoryLogger<IdempotencyKeyRequirementEndpointFilter>();
        var filter = new IdempotencyKeyRequirementEndpointFilter(inMemoryLogger);
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireIdempotencyKeyAttribute());
        context.HttpContext.Request.Headers.Authorization = SecretBearerToken;

        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        inMemoryLogger.Records.ShouldHaveLogged(new EventId(14003), LogLevel.Warning);
    }

    [Fact]
    public async Task IdempotencyKeyRequirementEndpointFilter_Rejection_NeverLogsRawBearerToken()
    {
        var inMemoryLogger = new InMemoryLogger<IdempotencyKeyRequirementEndpointFilter>();
        var filter = new IdempotencyKeyRequirementEndpointFilter(inMemoryLogger);
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireIdempotencyKeyAttribute());
        context.HttpContext.Request.Headers.Authorization = SecretBearerToken;

        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        AssertNoRecordContainsSecret(inMemoryLogger.Records, SecretBearerToken);
    }

    [Fact]
    public async Task CorsPolicyOptionsValidator_ValidationFailure_LogsEventId14004()
    {
        // CorsPolicyOptionsValidator is internal — exercised indirectly through the real
        // AddSharedKernelCors/IHost.StartAsync() startup-validation path, mirroring
        // CorsExtensionsTests' own established technique for this internal type.
        var loggerFactory = new InMemoryLoggerFactory();

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddInMemoryLoggerFactory();
                services.AddSingleton<ILoggerFactory>(loggerFactory);
                services.AddSharedKernelCors(options => options.AllowCredentials = true);
            })
            .Build();

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>();

        var logger = loggerFactory.GetLogger(CorsPolicyOptionsValidatorCategoryName);
        logger.Records.ShouldHaveLogged(new EventId(14004), LogLevel.Critical);
    }

    [Fact]
    public async Task CorsPolicyOptionsValidator_ValidationFailure_NeverLogsSensitiveConfigurationValues()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        const string sensitiveHeaderValue = "X-Internal-Secret-Tenant-Key-abc123";

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddInMemoryLoggerFactory();
                services.AddSingleton<ILoggerFactory>(loggerFactory);
                // A wildcard origin (not the secret itself) triggers the failure; the sensitive
                // value lives in an unrelated configuration field to prove the fixed failure
                // message never absorbs any caller-supplied configuration value, secret-shaped
                // or not.
                services.AddSharedKernelCors(options =>
                {
                    options.AllowCredentials = true;
                    options.AllowedOrigins.Add("*");
                    options.AllowedHeaders.Add(sensitiveHeaderValue);
                });
            })
            .Build();

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>();

        var logger = loggerFactory.GetLogger(CorsPolicyOptionsValidatorCategoryName);
        AssertNoRecordContainsSecret(logger.Records, sensitiveHeaderValue);
    }

    [Fact]
    public void RateLimitRejectionProblemDetails_Rejection_LogsEventId14005()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var httpContext = CreateHttpContextWithLoggerFactory(loggerFactory);
        httpContext.Request.Headers.Authorization = SecretBearerToken;

        RateLimitRejectionProblemDetails.Create(httpContext, TimeSpan.FromSeconds(1), policyName: "strict");

        var logger = loggerFactory.GetLogger(typeof(RateLimitRejectionProblemDetails).FullName!);
        logger.Records.ShouldHaveLogged(new EventId(14005), LogLevel.Warning);
    }

    [Fact]
    public void RateLimitRejectionProblemDetails_Rejection_NeverLogsRawBearerToken()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var httpContext = CreateHttpContextWithLoggerFactory(loggerFactory);
        httpContext.Request.Headers.Authorization = SecretBearerToken;

        RateLimitRejectionProblemDetails.Create(httpContext, TimeSpan.FromSeconds(1), policyName: "strict");

        var logger = loggerFactory.GetLogger(typeof(RateLimitRejectionProblemDetails).FullName!);
        AssertNoRecordContainsSecret(logger.Records, SecretBearerToken);
    }

    private static HttpContext CreateHttpContextWithLoggerFactory(ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    /// <summary>
    /// Asserts no captured record's rendered <see cref="LogRecord.Message"/> or any structured
    /// <see cref="LogRecord.State"/> property value contains <paramref name="secret"/> — proving the
    /// call site genuinely omits sensitive material, rather than merely never having been exercised
    /// with it present.
    /// </summary>
    private static void AssertNoRecordContainsSecret(IReadOnlyList<LogRecord> records, string secret)
    {
        foreach (var record in records)
        {
            record.Message.Should().NotContain(secret);

            if (record.State is null)
            {
                continue;
            }

            foreach (var property in record.State)
            {
                var value = property.Value?.ToString();
                if (value is not null)
                {
                    value.Should().NotContain(secret);
                }
            }
        }
    }
}
