using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Persistence;

namespace SharedKernel.Idempotency.EfCore.Internal;

/// <summary>
/// Builds <see cref="IdempotencyDbContext"/> for <c>dotnet ef</c> when a maintainer regenerates the shipped migration:
/// <c>dotnet ef migrations add {Name} --context IdempotencyDbContext</c> from the package folder. Not part of the
/// public API; consumers who keep their own migration write their own factory (see <c>README.md</c>).
/// </summary>
/// <remarks>
/// The connection string comes from the <see cref="ConnectionVariable"/> environment variable and is only used to
/// shape the provider (adding a migration needs no database); without it a credential-free local placeholder is used.
/// </remarks>
internal sealed class IdempotencyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdempotencyDbContext>
{
    /// <summary>The environment variable holding the design-time connection string.</summary>
    internal const string ConnectionVariable = "SK_IDEMPOTENCY_DESIGN_CONNECTION";

    private const string Placeholder = "Host=localhost;Database=idempotency_design";

    /// <inheritdoc />
    public IdempotencyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = Placeholder;

        var options = new DbContextOptionsBuilder<IdempotencyDbContext>();
        options.UsePostgres(NpgsqlDataSource.Create(connectionString));
        IdempotencyDbContextOptions.DisableRetry(options);
        IdempotencyDbContextOptions.UseHistoryTable(options);

        return new IdempotencyDbContext(options.Options);
    }
}
