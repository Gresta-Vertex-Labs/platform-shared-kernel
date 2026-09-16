using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Security.ApiKey.Logging;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private static (ApiKeyAuthenticationHandler Handler, DefaultHttpContext HttpContext, InMemoryLogger<ApiKeyAuthenticationHandler> Logger)
        CreateHandler(ApiKeyAuthenticationOptions options, IApiKeyValidator validator)
    {
        var logger = new InMemoryLogger<ApiKeyAuthenticationHandler>();
        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<ApiKeyAuthenticationOptions>(options),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            validator,
            logger);

        return (handler, new DefaultHttpContext(), logger);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        ApiKeyAuthenticationHandler handler,
        DefaultHttpContext httpContext)
    {
        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationOptions.DefaultScheme,
            displayName: null,
            typeof(ApiKeyAuthenticationHandler));

        await handler.InitializeAsync(scheme, httpContext);
        return await handler.AuthenticateAsync();
    }

    // ---- Valid key ----

    [Fact]
    public async Task ValidKeyInHeader_AuthenticatesSuccessfully()
    {
        var validator = new StubApiKeyValidator
        {
            Result = ApiKeyValidationResult.Valid(
                "client-1",
                roles: ["admin"],
                permissions: ["orders:read"]),
        };
        var (handler, httpContext, _) = CreateHandler(new ApiKeyAuthenticationOptions(), validator);
        httpContext.Request.Headers["X-Api-Key"] = "the-secret-key";

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.True(result.Succeeded);
        Assert.Equal(ApiKeyAuthenticationOptions.DefaultScheme, result.Principal!.Identity!.AuthenticationType);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal("the-secret-key", validator.LastPresentedKey);

        var sut = new ApiKeyUserContext(result.Principal);
        Assert.True(sut.IsAuthenticated);
        Assert.Equal("client-1", sut.Username);
        Assert.True(sut.HasRole("admin"));
        Assert.True(sut.HasPermission("orders:read"));
    }

    // ---- Missing key ----

    [Fact]
    public async Task NoKeyPresented_ReturnsNoResult()
    {
        var validator = new StubApiKeyValidator { Result = ApiKeyValidationResult.Invalid };
        var (handler, httpContext, _) = CreateHandler(new ApiKeyAuthenticationOptions(), validator);

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.False(result.Succeeded);
        Assert.True(result.None);
        Assert.Null(result.Failure);
        Assert.Equal(0, validator.CallCount);
    }

    // ---- Invalid key ----

    [Fact]
    public async Task InvalidKey_FailsAuthentication_AndLogsFailure()
    {
        var validator = new StubApiKeyValidator { Result = ApiKeyValidationResult.Invalid };
        var (handler, httpContext, logger) = CreateHandler(new ApiKeyAuthenticationOptions(), validator);
        httpContext.Request.Headers["X-Api-Key"] = "wrong-key";

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        var record = logger.Records.ShouldHaveLogged(new EventId(12200), LogLevel.Warning);
        Assert.DoesNotContain("wrong-key", record.Message, StringComparison.Ordinal);
    }

    // ---- Header + query agreement / ambiguity ----

    [Fact]
    public async Task HeaderAndQueryPresentWithSameValue_Succeeds()
    {
        var validator = new StubApiKeyValidator
        {
            Result = ApiKeyValidationResult.Valid("client-1"),
        };
        var options = new ApiKeyAuthenticationOptions { QueryParameterName = "api_key" };
        var (handler, httpContext, _) = CreateHandler(options, validator);
        httpContext.Request.Headers["X-Api-Key"] = "same-key";
        httpContext.Request.QueryString = QueryString.Create("api_key", "same-key");

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.True(result.Succeeded);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public async Task HeaderAndQueryPresentWithDifferentValues_FailsAsAmbiguous_WithoutCallingValidator()
    {
        var validator = new StubApiKeyValidator
        {
            Result = ApiKeyValidationResult.Valid("client-1"),
        };
        var options = new ApiKeyAuthenticationOptions { QueryParameterName = "api_key" };
        var (handler, httpContext, logger) = CreateHandler(options, validator);
        httpContext.Request.Headers["X-Api-Key"] = "header-value";
        httpContext.Request.QueryString = QueryString.Create("api_key", "query-value");

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Equal(0, validator.CallCount);
        var record = logger.Records.ShouldHaveLogged(new EventId(12200), LogLevel.Warning);
        Assert.DoesNotContain("header-value", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("query-value", record.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("same-key-X", "same-key-Y")]
    [InlineData("X-same-key", "Y-same-key")]
    [InlineData("same-key", "same-key-but-longer")]
    public async Task HeaderAndQueryDifferingAnywhere_FailsAsAmbiguous(string headerValue, string queryValue)
    {
        var validator = new StubApiKeyValidator
        {
            Result = ApiKeyValidationResult.Valid("client-1"),
        };
        var options = new ApiKeyAuthenticationOptions { QueryParameterName = "api_key" };
        var (handler, httpContext, _) = CreateHandler(options, validator);
        httpContext.Request.Headers["X-Api-Key"] = headerValue;
        httpContext.Request.QueryString = QueryString.Create("api_key", queryValue);

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.False(result.Succeeded);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public async Task QueryOnly_WhenConfigured_Succeeds()
    {
        var validator = new StubApiKeyValidator
        {
            Result = ApiKeyValidationResult.Valid("client-1"),
        };
        var options = new ApiKeyAuthenticationOptions { QueryParameterName = "api_key" };
        var (handler, httpContext, _) = CreateHandler(options, validator);
        httpContext.Request.QueryString = QueryString.Create("api_key", "query-only-key");

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.True(result.Succeeded);
        Assert.Equal("query-only-key", validator.LastPresentedKey);
    }

    [Fact]
    public async Task QueryParameterIgnored_WhenNotConfigured()
    {
        // QueryParameterName left null (default) — a query-string value alone must never authenticate.
        var validator = new StubApiKeyValidator
        {
            Result = ApiKeyValidationResult.Valid("client-1"),
        };
        var (handler, httpContext, _) = CreateHandler(new ApiKeyAuthenticationOptions(), validator);
        httpContext.Request.QueryString = QueryString.Create("api_key", "some-key");

        var result = await AuthenticateAsync(handler, httpContext);

        Assert.False(result.Succeeded);
        Assert.True(result.None);
        Assert.Null(result.Failure);
        Assert.Equal(0, validator.CallCount);
    }

    private sealed class StubApiKeyValidator : IApiKeyValidator
    {
        public ApiKeyValidationResult Result { get; set; } = ApiKeyValidationResult.Invalid;

        public int CallCount { get; private set; }

        public string? LastPresentedKey { get; private set; }

        public Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPresentedKey = presentedKey;
            return Task.FromResult(Result);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
