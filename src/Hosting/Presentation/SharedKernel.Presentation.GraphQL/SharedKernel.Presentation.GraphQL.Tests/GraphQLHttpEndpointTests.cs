using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.GraphQL.Extensions;

namespace SharedKernel.Presentation.GraphQL.Tests;

/// <summary>
/// The README's quick start over real HTTP: <c>AddSharedKernelGraphQL()</c>, then <c>app.MapGraphQL()</c>. The other
/// suites drive the request executor directly, which does not need the ASP.NET Core server services.
/// </summary>
public sealed class GraphQLHttpEndpointTests
{
    [Fact]
    public async Task MapGraphQL_AfterAddSharedKernelGraphQL_AnswersAQueryOverHttp()
    {
        await using var app = await StartAsync(services => services.AddSharedKernelGraphQL().AddQueryType<Query>());
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/graphql", new { query = "{ greeting }" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetProperty("greeting").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task MapGraphQL_AfterASecondAddSharedKernelGraphQLCall_StillAnswers()
    {
        await using var app = await StartAsync(services =>
        {
            services.AddSharedKernelGraphQL();
            services.AddSharedKernelGraphQL().AddQueryType<Query>();
        });
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/graphql", new { query = "{ greeting }" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        configure(builder.Services);
        var app = builder.Build();
        app.MapGraphQL();
        await app.StartAsync();
        return app;
    }

    public sealed class Query
    {
        public string Greeting => "hello";
    }
}
