using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>What the startup check of row-level security coverage does when a tenant table is not protected.</summary>
public enum RowLevelSecurityCheckMode
{
    /// <summary>Fails startup. The default outside the Development environment.</summary>
    Fail = 0,

    /// <summary>Logs a warning per unprotected table and starts. The default in the Development environment.</summary>
    Warn = 1,

    /// <summary>Skips the check.</summary>
    Off = 2,
}

/// <summary>
/// At startup — after the startup migrations — verifies that every tenant table of the model has row-level security
/// enabled and forced and carries the tenant policy (<c>EnableTenantRowLevelSecurity</c>), so a table added without
/// its policy is caught before it serves traffic.
/// </summary>
internal sealed class RowLevelSecurityCoverageCheck<TContext>(
    IServiceProvider services,
    RowLevelSecurityCheckMode? configuredMode,
    ILogger<RowLevelSecurityCoverageCheck<TContext>>? logger = null) : IHostedLifecycleService
    where TContext : SharedKernelDbContext
{
    private const string CoverageSql =
        """
        SELECT c.relrowsecurity, c.relforcerowsecurity,
               EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = @policy)
        FROM pg_class c
        WHERE c.oid = to_regclass(@table)
        """;

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

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

        var findings = await FindUnprotectedTablesAsync(services, cancellationToken).ConfigureAwait(false);
        if (findings.Count == 0)
            return;

        foreach (var finding in findings)
            RowLevelSecurityLog.TenantTableNotProtected(_logger, typeof(TContext).Name, finding);

        if (mode == RowLevelSecurityCheckMode.Fail)
        {
            throw new InvalidOperationException(
                $"Row-level security is enabled for '{typeof(TContext).Name}', but these tenant tables are not protected: "
                + string.Join("; ", findings) + ". Add 'migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel)' "
                + "(or EnableTenantRowLevelSecurity per table) to a migration.");
        }
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Returns one finding per tenant table that is missing, unprotected or without the tenant policy.</summary>
    internal static async Task<IReadOnlyList<string>> FindUnprotectedTablesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<ICallerDbContextFactory<TContext>>();
        var context = await factory.CreateDbContextAsync(AnonymousRequestContext.Instance, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var tables = RowLevelSecurityMigrationBuilderExtensions.TenantTables(DesignTimeModel(context));
            var findings = new List<string>();
            if (tables.Count == 0)
                return findings;

            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (table, schema, _) in tables)
                {
                    var qualified = schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
                    if (await ReadAsync(connection, qualified, RowLevelSecurityMigrationBuilderExtensions.TenantPolicyName(table), cancellationToken).ConfigureAwait(false) is { } problem)
                        findings.Add($"'{qualified}' {problem}");
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

    private static async Task<string?> ReadAsync(DbConnection connection, string table, string policy, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CoverageSql;
        AddParameter(command, "table", table);
        AddParameter(command, "policy", policy);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return "does not exist";

        if (!reader.GetBoolean(0))
            return "does not have row-level security enabled";

        if (!reader.GetBoolean(1))
            return "does not force row-level security (the table owner bypasses it)";

        return reader.GetBoolean(2) ? null : $"has no tenant policy '{policy}'";
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
