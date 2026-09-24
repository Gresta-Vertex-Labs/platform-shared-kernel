using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// P-563 P2: the source generator shipped with WebApi maps every <see cref="IEndpointModule"/> of this assembly
/// through the generated <c>app.MapEndpoints()</c>, and requests reach each module's endpoints.
/// </summary>
public sealed class EndpointModuleTests : IAsyncLifetime
{
    private WebApplication? _app;

    private HttpClient Client => _app!.GetTestClient();

    public async Task InitializeAsync() => _app = await WebApiTestHost.StartAsync(app => app.MapEndpoints());

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_module_with_a_route_group_is_mapped()
    {
        var response = await Client.GetAsync(new Uri("/modules/invoices/7", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("invoice 7");
    }

    [Fact]
    public async Task An_internal_module_with_an_explicit_map_is_mapped()
    {
        var response = await Client.GetAsync(new Uri("/modules/customers", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("customers");
    }
}
