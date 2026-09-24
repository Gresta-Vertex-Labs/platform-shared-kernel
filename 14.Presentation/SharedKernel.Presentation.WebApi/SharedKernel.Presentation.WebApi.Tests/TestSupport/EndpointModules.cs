using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>An endpoint module with a route group, mapped by the generated <c>MapEndpoints()</c>.</summary>
public sealed class InvoiceModule : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup("/modules/invoices");
        invoices.MapGet("/{id:int}", (int id) => Result<string>.Success($"invoice {id}").ToOk());
    }
}

/// <summary>A second module, internal and implementing <c>Map</c> explicitly.</summary>
internal sealed class CustomerModule : IEndpointModule
{
    static void IEndpointModule.Map(IEndpointRouteBuilder app) =>
        app.MapGet("/modules/customers", () => Results.Text("customers"));
}
