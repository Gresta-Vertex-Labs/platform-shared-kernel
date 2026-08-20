using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Cors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Cors;

public class CorsExtensionsTests
{
    [Fact]
    public async Task StartAsync_AllowCredentialsWithEmptyOrigins_ThrowsAtStartup_NotAtFirstRequest()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddSharedKernelCors(options => options.AllowCredentials = true))
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task StartAsync_AllowCredentialsWithWildcardOrigin_ThrowsAtStartup()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddSharedKernelCors(options =>
            {
                options.AllowedOrigins.Add("*");
                options.AllowCredentials = true;
            }))
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task StartAsync_ValidExplicitOriginWithCredentials_SucceedsAndBuildsWorkingNamedPolicy()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddSharedKernelCors(options =>
            {
                options.AllowedOrigins.Add("https://app.example.com");
                options.AllowCredentials = true;
            }))
            .Build();

        await host.StartAsync();

        var provider = host.Services.GetRequiredService<ICorsPolicyProvider>();
        var httpContext = new DefaultHttpContext();
        var policy = await provider.GetPolicyAsync(httpContext, CorsPolicyNames.Default);

        policy.Should().NotBeNull();
        policy!.Origins.Should().Contain("https://app.example.com");
        policy.SupportsCredentials.Should().BeTrue();

        await host.StopAsync();
    }

    [Fact]
    public async Task Cors_NamedPolicy_AllowsConfiguredOrigin_RejectsUnconfiguredOrigin()
    {
        using var testServer = new TestServer(new WebHostBuilder()
            .ConfigureServices(services => services.AddSharedKernelCors(options =>
                options.AllowedOrigins.Add("https://allowed.example.com")))
            .Configure(app =>
            {
                app.UseCors(CorsPolicyNames.Default);
                app.Run(ctx => ctx.Response.WriteAsync("ok"));
            }));

        using var client = testServer.CreateClient();

        using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        allowedRequest.Headers.Add("Origin", "https://allowed.example.com");
        using var allowedResponse = await client.SendAsync(allowedRequest);
        allowedResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();
        allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("https://allowed.example.com");

        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        rejectedRequest.Headers.Add("Origin", "https://not-allowed.example.com");
        using var rejectedResponse = await client.SendAsync(rejectedRequest);
        rejectedResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
