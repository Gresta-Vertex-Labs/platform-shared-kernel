using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// At startup — after the startup migrations — verifies that every tenant table of the model has row-level security
/// enabled and forced and carries the tenant policy (<c>EnableTenantRowLevelSecurity</c>), so a table added without
/// its policy is caught before it serves traffic.
/// </summary>
internal sealed class RowLevelSecurityCoverageCheck<TContext>(
    IServiceProvider services,
    RowLevelSecurityCheckMode? configuredMode,
    ILogger<RowLevelSecurityCoverageCheck<TContext>> logger) : IHostedLifecycleService
    where TContext : SharedKernelDbContext
{
    private readonly ILogger _logger = logger;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var mode = configuredMode
            ?? (services.GetService<IHostEnvironment>()?.IsDevelopment() == true ? RowLevelSecurityCheckMode.Warn : RowLevelSecurityCheckMode.Fail);
        if (mode == RowLevelSecurityCheckMode.Off)
            return;

        if (services.GetService<IPersistenceStartup>() is { } startup)
            await startup.WaitAsync(cancellationToken).ConfigureAwait(false);

        var gaps = await FindGapsAsync(services, cancellationToken).ConfigureAwait(false);
        if (gaps.Count == 0)
            return;

        foreach (var (table, gap) in gaps)
            EfCoreLog.TenantTableNotProtected(_logger, typeof(TContext).Name, table, gap);

        if (mode == RowLevelSecurityCheckMode.Fail)
        {
            throw new InvalidOperationException(
                $"Row-level security is enabled for '{typeof(TContext).Name}', but these tenant tables are not protected: "
                + string.Join("; ", gaps.Select(g => Describe(g.Table, g.Gap))) + ". Add 'migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel)' "
                + "(or EnableTenantRowLevelSecurity per table) to a migration.");
        }
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Returns one finding per tenant table that is missing, unprotected or without the tenant policy, as readable text.</summary>
    internal static async Task<IReadOnlyList<string>> FindUnprotectedTablesAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        [.. (await FindGapsAsync(services, cancellationToken).ConfigureAwait(false)).Select(g => Describe(g.Table, g.Gap))];

    /// <summary>Returns one gap per tenant table that is missing, unprotected or without the tenant policy.</summary>
    internal static async Task<IReadOnlyList<(string Table, RowLevelSecurityGap Gap)>> FindGapsAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<ICallerDbContextFactory<TContext>>();
        var context = await factory.CreateDbContextAsync(AnonymousRequestContext.Instance, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var tables = RowLevelSecurityMigrationBuilderExtensions.TenantTables(DesignTimeModel(context));
            var findings = new List<(string Table, RowLevelSecurityGap Gap)>();
            if (tables.Count == 0)
                return findings;

            var qualified = tables
                .Select(t => t.Schema is null ? $"\"{t.Table}\"" : $"\"{t.Schema}\".\"{t.Table}\"")
                .ToList();
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var statuses = await RowLevelSecurityCatalog
                    .GetTableStatusAsync(context.Database.GetDbConnection(), qualified, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var status in statuses)
                {
                    if (GapOf(status) is { } gap)
                        findings.Add((status.Table, gap));
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }

            return findings;
        }
    }

    // The design-time model keeps every relational annotation; a compiled model has none, so fall back to the runtime model.
    private static Microsoft.EntityFrameworkCore.Metadata.IReadOnlyModel DesignTimeModel(TContext context)
    {
        try
        {
            return context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        }
        catch (InvalidOperationException)
        {
            return context.Model;
        }
    }

    private static RowLevelSecurityGap? GapOf(RowLevelSecurityTableStatus status) =>
        status switch
        {
            { Exists: false } => RowLevelSecurityGap.TableMissing,
            { RowSecurityEnabled: false } => RowLevelSecurityGap.RowSecurityDisabled,
            { RowSecurityForced: false } => RowLevelSecurityGap.RowSecurityNotForced,
            { HasTenantPolicy: false } => RowLevelSecurityGap.TenantPolicyMissing,
            _ => null,
        };

    private static string Describe(string table, RowLevelSecurityGap gap) =>
        $"'{table}' " + gap switch
        {
            RowLevelSecurityGap.TableMissing => "does not exist",
            RowLevelSecurityGap.RowSecurityDisabled => "does not have row-level security enabled",
            RowLevelSecurityGap.RowSecurityNotForced => "does not force row-level security (the table owner bypasses it)",
            RowLevelSecurityGap.TenantPolicyMissing => "has no policy that reads the tenant setting",
            _ => throw new System.Diagnostics.UnreachableException(),
        };
}
