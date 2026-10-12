using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.GraphQL.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.GraphQL.Tests.TestSupport;

/// <summary>
/// Builds a real in-process host the way a service does: the request context first, then <c>AddSharedKernelGraphQL()</c>
/// and <c>app.MapGraphQL()</c>, and posts queries over HTTP.
/// </summary>
internal static class GraphQLTestHost
{
    public const string Production = "Production";

    public const string Development = "Development";

    public const string CorrelationId = "gql-correlation-42";

    /// <summary>A value that must never reach a client outside Development.</summary>
    public const string Canary = "Server=db.internal;Password=hunter2";

    public static async Task<WebApplication> StartAsync(
        string environment = Production,
        InMemoryLoggerFactory? loggerFactory = null,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        if (loggerFactory is not null)
        {
            builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);
        }

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<IUserContext>(static services => UserContextResolver.Resolve(
            services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            services.GetServices<IUserContextMapper>()));
        builder.Services.AddSharedKernelRequestContext();
        builder.Services.AddSharedKernelGraphQL().AddQueryType<ErrorsQuery>();
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseSharedKernelRequestContext();
        app.MapGraphQL();

        await app.StartAsync();
        return app;
    }

    /// <summary>Posts <paramref name="query"/> and returns the parsed response body and the response itself.</summary>
    public static async Task<GraphQLResult> PostAsync(this WebApplication app, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        request.Headers.Add(WellKnownHeaders.CorrelationId, CorrelationId);

        var response = await app.GetTestClient().SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new GraphQLResult(response, text, JsonDocument.Parse(text));
    }
}

/// <summary>A GraphQL response: the raw text, the parsed body and the HTTP response.</summary>
internal sealed class GraphQLResult(HttpResponseMessage response, string text, JsonDocument body) : IDisposable
{
    public HttpResponseMessage Response { get; } = response;

    public string Text { get; } = text;

    public JsonElement Root => body.RootElement;

    /// <summary>The first error of the response.</summary>
    public JsonElement Error(int index = 0) => Root.GetProperty("errors")[index];

    /// <summary>The <c>extensions</c> of the first error.</summary>
    public JsonElement Extensions(int index = 0) => Error(index).GetProperty("extensions");

    public void Dispose()
    {
        body.Dispose();
        Response.Dispose();
    }
}

/// <summary>The query type of the test schema: every failure the filter distinguishes.</summary>
public sealed class ErrorsQuery
{
    public string Greeting => "hello";

    /// <summary>Throws the exception of an error of the named type, its message carrying a canary.</summary>
    public string Fail(string type)
    {
        var errorType = Enum.Parse<ErrorType>(type);
        throw new Error($"test.{type.ToLowerInvariant()}", $"{type} failed at {GraphQLTestHost.Canary}.", errorType).ToException();
    }

    public string Boom() => throw new InvalidOperationException(GraphQLTestHost.Canary);

    public string Timeout() => throw new TimeoutException(GraphQLTestHost.Canary);

    public string InternalCancellation() => throw new TaskCanceledException(GraphQLTestHost.Canary);

    public string ValidationOne() => throw new ValidationException(Field("customer.name_required", "Name is required.", "Name"));

    public string ValidationMany() => throw new ValidationException(
    [
        Field("customer.name_required", "Name is required.", "Name"),
        Field("customer.name_too_long", "Name is too long.", "Name"),
        Field("customer.email_invalid", "Email is invalid.", "Email"),
    ]);

    public string Unavailable() => throw Error.Unavailable("search.unreachable", $"Host {GraphQLTestHost.Canary} is down.").ToException();

    private static Error Field(string code, string message, string path) =>
        Error.Validation(code, message) with
        {
            MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = path },
        };
}
