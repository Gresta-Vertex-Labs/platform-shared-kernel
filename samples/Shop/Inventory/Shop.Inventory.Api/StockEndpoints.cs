using Dapper;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Presentation.WebApi;
using Shop.Inventory.Api.Reconciliation;
using Shop.Inventory.Api.Stock;

namespace Shop.Inventory.Api;

/// <summary>The body of a stock change.</summary>
public sealed record SetStockRequest(int OnHand);

/// <summary>Merchants' REST surface (OIDC), plus the operational view the end-to-end tests read.</summary>
public sealed class StockEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/stock").RequireAuthorization();

        stock
            .MapPut(
                "/{sku}",
                (string sku, SetStockRequest body, ISender sender, CancellationToken ct) =>
                    sender.Send(new SetStockCommand(sku, body.OnHand), ct).ToNoContent()
            )
            .WithName("SetStock");

        stock
            .MapGet(
                "/{sku}",
                (string sku, ISender sender, CancellationToken ct) =>
                    sender.Send(new GetStockQuery(sku), ct).ToOk()
            )
            .WithName("GetStock");

        // Every execution of the reconciliation job, newest first: proof the scheduler ran each occurrence once.
        app.MapGet(
                "/ops/job-runs",
                async (
                    ICrossTenantScope crossTenant,
                    IDbSessionFactory sessions,
                    CancellationToken ct
                ) =>
                {
                    using (crossTenant.Enter("job run report"))
                    {
                        await using var session = await sessions.OpenReadOnlyAsync(ct);
                        var rows = await session.Connection.QueryAsync<(
                            string Job,
                            DateTime FireTime,
                            string Replica,
                            int Items
                        )>(
                            session.Command(
                                "SELECT job, fire_time, replica, items FROM job_runs ORDER BY id DESC LIMIT 200",
                                cancellationToken: ct
                            )
                        );
                        // Npgsql reads timestamptz as a UTC DateTime.
                        return Results.Ok(
                            rows.Select(row => new JobRun(
                                row.Job,
                                new DateTimeOffset(row.FireTime, TimeSpan.Zero),
                                row.Replica,
                                row.Items
                            ))
                        );
                    }
                }
            )
            .RequireAuthorization();
    }
}
