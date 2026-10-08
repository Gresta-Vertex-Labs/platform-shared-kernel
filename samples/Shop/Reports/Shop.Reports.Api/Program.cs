using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Reporting;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;
using SharedKernel.Storage;
using Shop.Reports.Api;
using Shop.Reports.Api.Sales;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.WithApplicationTelemetry();
builder.WithPersistenceTelemetry();
builder.WithMessagingTelemetry();
builder.WithReportingTelemetry();

// Merchants with a Keycloak token; the tenant from the signed claim.
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o =>
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o)
);

// 06.Persistence without EF Core: the schema first (registered first, so it runs first), then Dapper sessions that bind
// the caller's tenant for row-level security.
builder.Services.AddHostedService<ReportsSchema>();
builder.Services.AddSharedKernelNpgsql(builder.Configuration, ReportsSchema.ConnectionName);
builder.Services.AddSharedKernelDapper(builder.Configuration);
builder.Services.AddScoped<ISalesReader, SalesReader>();
builder.Services.AddClock();

// 08.Storage: downloads on S3, the archive on Huawei OBS (both MinIO here); one tenant view each.
var storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration).AddTenantStore(ReportStores.Downloads);
storage.AddObs(builder.Configuration).AddTenantStore(ReportStores.Archive);

// 20.Reporting: CSV, Excel and PDF exporters, and HTML to PDF through Gotenberg.
builder
    .Services.AddSharedKernelReporting()
    .AddCsv(builder.Configuration)
    .AddSpreadsheet(builder.Configuration)
    .AddPdf(builder.Configuration)
    .AddGotenberg(builder.Configuration);

// 07.Messaging: Billing's receipts, each recorded as a sale of the publishing tenant.
builder
    .Services.AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(
        builder.Configuration.GetConnectionString(ReportsMessaging.ConnectionName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ReportsMessaging.ConnectionName} is not configured."
            )
    )
    .WithRetry()
    .WithInboundRequestContext()
    .AddConsumer<RecordSaleConsumer>()
    .Build();

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR());
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

builder.AddSharedKernelWebApi();

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(pipeline =>
    pipeline.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>())
);

app.MapDefaultHealthCheckEndpoints();
app.MapEndpoints();

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Reports' bus connection (<c>ConnectionStrings:rabbitmq</c>, set by the AppHost).</summary>
internal static class ReportsMessaging
{
    public const string ConnectionName = "rabbitmq";
}

/// <summary>Exposed for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
