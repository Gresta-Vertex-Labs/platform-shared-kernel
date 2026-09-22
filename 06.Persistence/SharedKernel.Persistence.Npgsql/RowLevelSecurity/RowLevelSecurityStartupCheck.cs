using global::Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Diagnostics;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.Npgsql.RowLevelSecurity;

/// <summary>
/// Runs <see cref="RowLevelSecurityPrivileges.CheckAsync"/> at host startup for the application data
/// sources (default and, when separately configured, read-only) of the default database, when row-level
/// security is enabled.
/// </summary>
/// <remarks>
/// A database that cannot be reached at startup is logged and skipped rather than failing the host: the
/// check guards against a misconfigured role, not against an outage.
/// </remarks>
internal sealed class RowLevelSecurityStartupCheck(
    IServiceProvider serviceProvider,
    IOptionsMonitor<NpgsqlPersistenceOptions> options,
    ILogger<RowLevelSecurityStartupCheck>? logger = null) : IHostedService
{
    private readonly ILogger _logger = logger ?? NullLogger<RowLevelSecurityStartupCheck>.Instance;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var current = options.Get(Microsoft.Extensions.Options.Options.DefaultName);
        if (!current.RowLevelSecurity.Enabled || current.RowLevelSecurity.PrivilegeCheck == RowLevelSecurityPrivilegeCheck.Disabled)
            return;

        List<string> problems = [];

        await CheckAsync("default", serviceProvider.GetRequiredService<NpgsqlDataSource>(), problems, cancellationToken)
            .ConfigureAwait(false);

        if (serviceProvider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.ReadOnly) is { } readOnly)
            await CheckAsync(NpgsqlDataSourceKeys.ReadOnly, readOnly, problems, cancellationToken).ConfigureAwait(false);

        if (problems.Count > 0 && current.RowLevelSecurity.PrivilegeCheck == RowLevelSecurityPrivilegeCheck.Fail)
        {
            throw new InvalidOperationException(
                "Row-level security is enabled, but the application database role can bypass it: "
                    + string.Join("; ", problems) + ". Connect as an unprivileged role (see the "
                    + "SharedKernel.Persistence.Npgsql README for the role script), or set "
                    + $"'{current.SectionPath}:RowLevelSecurity:PrivilegeCheck' to 'Warn'.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CheckAsync(
        string dataSourceName, NpgsqlDataSource dataSource, List<string> problems, CancellationToken cancellationToken)
    {
        RowLevelSecurityPrivilegeReport report;
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            report = await RowLevelSecurityPrivileges.CheckAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException exception) when (exception is not PostgresException)
        {
            _logger.RowLevelSecurityPrivilegeCheckSkipped(dataSourceName, exception);
            return;
        }

        if (report.IsSubjectToRowLevelSecurity)
        {
            _logger.RowLevelSecurityPrivilegeCheckPassed(dataSourceName, report.RoleName);
            return;
        }

        foreach (var problem in report.Problems)
        {
            _logger.RowLevelSecurityPrivilegeProblem(dataSourceName, problem);
            problems.Add(problem);
        }
    }
}
