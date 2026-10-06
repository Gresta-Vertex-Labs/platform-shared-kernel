using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Pipeline.Caching;
using SharedKernel.FeatureManagement;
using SharedKernel.Localization;
using SharedKernel.Presentation.GraphQL.Extensions;
using SharedKernel.Presentation.OpenApi;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Localization;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;
using Shop.Catalog.Api;
using Shop.Catalog.Application;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults: OpenTelemetry, health endpoints and the startup gate, plus the capability sources this
// service emits, so the Aspire dashboard shows search, cache, storage, AI and persistence spans.
builder.AddServiceDefaults();
builder.WithApplicationTelemetry();
builder.WithPersistenceTelemetry();
builder.WithCachingTelemetry();
builder.WithSearchTelemetry();
builder.WithIntelligenceTelemetry();
builder.WithStorageTelemetry();

// Who is calling: a Keycloak access token (12.Security.Oidc) becomes IUserContext, and the request context turns it
// into the one IRequestContext the pipeline, the tenant filters and row-level security read.
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddSharedKernelRequestContext();

// Localized problem details: the catalog's messages in English and Turkish, the culture from Accept-Language.
builder.AddSharedKernelLocalization();
builder.Services.Configure<RequestLocalizationOptions>(o =>
    o.SetDefaultCulture("en").AddSupportedCultures("en", "tr").AddSupportedUICultures("en", "tr")
);
builder.Services.AddLocalizationCatalog(catalog =>
    catalog.AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization"))
);

// Feature flags over Microsoft.FeatureManagement, targeted at the caller's tenant.
builder.Services.AddSharedKernelFeatureManagement(
    builder.Configuration,
    flags => flags.ValidateOnStart(CatalogFeatures.NewArrivalBadge)
);

// Every adapter behind the application's ports.
builder.AddCatalogInfrastructure();

// 05.Application: the use cases, MediatR behind ISender, one transaction per command, and query caching with
// post-commit eviction.
builder.Services.AddSharedKernelApplication(
    typeof(CreateProductCommand).Assembly,
    app => app.UseMediatR().WithTransactions().WithCaching()
);

// Readiness: every probe the adapters registered (database, Redis, both search engines, Qdrant, the image store).
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

// 14.Presentation: RFC 9457 problems, versioned REST, OpenAPI in Development, GraphQL.
builder.AddSharedKernelWebApi();
builder.AddSharedKernelOpenApi(options => options.Title = "Shop Catalog API");
builder
    .Services.AddSharedKernelGraphQL(options =>
        options.AllowIntrospection = builder.Environment.IsDevelopment()
    )
    .AddQueryType<CatalogQueries>();

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(pipeline =>
    pipeline.BeforeAuthorization(a => a.UseRequestLocalization())
);

app.MapDefaultHealthCheckEndpoints();
app.MapEndpoints();
app.MapSharedKernelOpenApi();
app.MapGraphQL().RequireAuthorization();

// Hosted services (migrations, index provisioning) have finished by the time the application has started.
app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
