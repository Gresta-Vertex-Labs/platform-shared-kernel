using Microsoft.AspNetCore.Routing;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// A set of endpoints mapped together — one module per resource or feature — found at compile time and mapped by the
/// generated <c>app.MapEndpoints()</c>.
/// </summary>
/// <remarks>
/// <para>
/// A module is a type with one static <see cref="Map"/> method. Everything the endpoints share is declared inside it:
/// the route group, its authorization requirements, its API version, its tags.
/// </para>
/// <code>
/// public sealed class InvoiceEndpoints : IEndpointModule
/// {
///     public static void Map(IEndpointRouteBuilder app)
///     {
///         var invoices = app.MapGroup("/invoices").WithTags("Invoices");
///
///         invoices.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =&gt;
///             sender.Send(new GetInvoice(id), ct).ToOk());
///
///         invoices.MapPost("/", (CreateInvoice command, ISender sender, CancellationToken ct) =&gt;
///             sender.Send(command, ct).ToCreated(invoice =&gt; $"/invoices/{invoice.Id}"));
///     }
/// }
///
/// // Program.cs
/// app.UseSharedKernelWebApi();
/// app.MapEndpoints();
/// </code>
/// <para>
/// <c>MapEndpoints()</c> is generated into the assembly that declares the modules by the source generator this package
/// ships (<c>analyzers/dotnet/cs</c>): an <see langword="internal"/> extension on <see cref="IEndpointRouteBuilder"/>
/// in this namespace that calls <see cref="Map"/> on every module of the assembly, in the ordinal order of their full
/// names. No reflection and no instance is involved. It is generated only when the assembly declares at least one
/// module; each assembly maps its own modules.
/// </para>
/// <para>
/// A module must be a concrete, non-generic type that the assembly itself can reach — public or internal, and not
/// nested in a generic type or inside a private or protected nested type. The generator reports a build error
/// (<c>SKEP001</c>–<c>SKEP003</c>) for a module it cannot call. A type that inherits <see cref="Map"/> from another
/// module instead of declaring its own is not mapped a second time (<c>SKEP004</c>).
/// </para>
/// </remarks>
public interface IEndpointModule
{
    /// <summary>Maps the module's endpoints.</summary>
    /// <param name="app">The application, or the route group, to map the endpoints on.</param>
    static abstract void Map(IEndpointRouteBuilder app);
}
