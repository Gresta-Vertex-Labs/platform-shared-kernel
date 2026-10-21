using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>The options marker whose validation checks a package context registered with <c>AddMigrationOnStartup</c>.</summary>
internal sealed class PackageContextMigrationCheck<TContext>
    where TContext : DbContext;

/// <summary>
/// Start-time checks for a context migrated through <c>AddMigrationOnStartup</c>: not pooled (its connection is
/// swapped for the migration data source) and a history table of its own (never the EF Core default shared with the
/// service's context). Messages carry no connection string or host.
/// </summary>
internal sealed class PackageContextMigrationValidator<TContext>(IServiceProvider services)
    : IValidateOptions<PackageContextMigrationCheck<TContext>>
    where TContext : DbContext
{
    public ValidateOptionsResult Validate(string? name, PackageContextMigrationCheck<TContext> options)
    {
        var contextName = typeof(TContext).Name;

        using var scope = services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var contextOptions = context.GetService<IDbContextOptions>();

        if (contextOptions.FindExtension<CoreOptionsExtension>()?.MaxPoolSize is not null)
        {
            return ValidateOptionsResult.Fail(
                $"'{contextName}' is pooled. Register it with AddDbContext instead of AddDbContextPool so it can be migrated on startup.");
        }

        var relational = contextOptions.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
        if (relational is null)
        {
            return ValidateOptionsResult.Fail(
                $"'{contextName}' needs a relational provider to be migrated on startup. Configure it with UseNpgsql.");
        }

        var table = relational.MigrationsHistoryTableName ?? HistoryRepository.DefaultTableName;
        var schema = relational.MigrationsHistoryTableSchema;
        var defaultSchema = string.IsNullOrEmpty(schema) || schema == "public";

        if (table == HistoryRepository.DefaultTableName && defaultSchema)
        {
            return ValidateOptionsResult.Fail(
                $"'{contextName}' uses the default migrations history table, which it would share with another context. " +
                "Set a table of its own with MigrationsHistoryTable in the provider options.");
        }

        return ValidateOptionsResult.Success;
    }
}
