using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.RowLevelSecurity;

/// <summary>
/// The row-level security privilege check against real policies and role memberships (finding S4), and the catalog
/// reads behind the model-driven "every tenant table is protected" check (finding F6).
/// </summary>
public sealed class RowLevelSecurityPrivilegeTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    // The check reads every table of the database, so each test gets a database of its own: a policy one test
    // creates must not show up in another's report. Roles are cluster-wide and carry the same suffix.
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    private string Database => "rp_" + _suffix;

    public async Task InitializeAsync()
    {
        await using var admin = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var command = admin.CreateCommand($"CREATE DATABASE {Database}");
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string Suffix() => _suffix;

    private string DatabaseConnectionString(string? role = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = Database, Pooling = false };
        if (role is not null)
        {
            builder.Username = role;
            builder.Password = "pw";
        }

        return builder.ConnectionString;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(DatabaseConnectionString());
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private string RoleConnectionString(string role) => DatabaseConnectionString(role);

    /// <summary>A tenant table with the platform's tenant policy and two tenants' rows; returns the table name.</summary>
    private async Task<string> CreateTenantTableAsync(string suffix, string grantTo)
    {
        var table = $"rp_t_{suffix}";
        var predicate = TenantSessionSql.PolicyPredicate("tenant_id");
        await ExecuteAsync($"""
            CREATE TABLE {table} (id int PRIMARY KEY, tenant_id uuid NOT NULL);
            INSERT INTO {table} VALUES (1, '{Guid.NewGuid()}'), (2, '{Guid.NewGuid()}');
            GRANT SELECT ON {table} TO {grantTo};
            ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {table}_tenant_isolation ON {table} USING ({predicate}) WITH CHECK ({predicate});
            """);
        return table;
    }

    private async Task<(RowLevelSecurityPrivilegeReport Report, long VisibleRows)> CheckAsAsync(string role, string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(RoleConnectionString(role));
        await using var connection = await dataSource.OpenConnectionAsync();
        var report = await RowLevelSecurityPrivileges.CheckAsync(connection);
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection); // no tenant bound
        return (report, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task MemberOfTheCrossTenantPolicyRole_IsReported()
    {
        // The probe of finding S4: an application role that is a member of the cross-tenant role sees every tenant's
        // rows with no tenant bound, and the old check (superuser, BYPASSRLS, ownership) passed it.
        var s = Suffix();
        await ExecuteAsync($"CREATE ROLE rp_cross_{s} NOLOGIN; CREATE ROLE rp_app_{s} LOGIN PASSWORD 'pw' IN ROLE rp_cross_{s};");
        var table = await CreateTenantTableAsync(s, $"rp_app_{s}, rp_cross_{s}");
        await ExecuteAsync($"CREATE POLICY {table}_cross_tenant ON {table} TO rp_cross_{s} USING (true) WITH CHECK (true);");

        var (report, visible) = await CheckAsAsync($"rp_app_{s}", table);

        visible.Should().Be(2, "this is the bypass the check must catch");
        report.IsSubjectToRowLevelSecurity.Should().BeFalse();
        report.NonTenantPermissivePolicies.Should().ContainSingle()
            .Which.Should().Contain($"{table}_cross_tenant").And.Contain($"rp_cross_{s}");
        report.Problems.Should().ContainSingle().Which.Should().Contain("must not be a member of the cross-tenant role");
    }

    [Fact]
    public async Task NonInheritedMembership_IsReported_BecauseTheMemberCanSetRole()
    {
        var s = Suffix();
        await ExecuteAsync($"CREATE ROLE rp_cross_{s} NOLOGIN; CREATE ROLE rp_app_{s} LOGIN PASSWORD 'pw' NOINHERIT IN ROLE rp_cross_{s};");
        var table = await CreateTenantTableAsync(s, $"rp_app_{s}, rp_cross_{s}");
        await ExecuteAsync($"CREATE POLICY {table}_cross_tenant ON {table} TO rp_cross_{s} USING (true);");

        (await CheckAsAsync($"rp_app_{s}", table)).Report.NonTenantPermissivePolicies.Should().ContainSingle();
    }

    [Fact]
    public async Task SeparateCrossTenantRole_AndTenantPolicyNamingTheAppRole_PassTheCheck()
    {
        var s = Suffix();
        await ExecuteAsync($"CREATE ROLE rp_cross_{s} LOGIN PASSWORD 'pw'; CREATE ROLE rp_app_{s} LOGIN PASSWORD 'pw';");
        var table = await CreateTenantTableAsync(s, $"rp_app_{s}, rp_cross_{s}");
        await ExecuteAsync($"""
            CREATE POLICY {table}_cross_tenant ON {table} TO rp_cross_{s} USING (true) WITH CHECK (true);
            CREATE POLICY {table}_app_tenant ON {table} TO rp_app_{s} USING ({TenantSessionSql.PolicyPredicate("tenant_id")});
            """);

        var (report, visible) = await CheckAsAsync($"rp_app_{s}", table);

        report.IsSubjectToRowLevelSecurity.Should().BeTrue();
        report.Problems.Should().BeEmpty();
        visible.Should().Be(0);
    }

    [Fact]
    public async Task PublicPermissivePolicyWithoutTheTenant_OnAProtectedTable_IsReported()
    {
        var s = Suffix();
        await ExecuteAsync($"CREATE ROLE rp_app_{s} LOGIN PASSWORD 'pw';");
        var table = await CreateTenantTableAsync(s, $"rp_app_{s}");
        await ExecuteAsync($"CREATE POLICY {table}_everyone ON {table} FOR SELECT USING (true);");

        var report = (await CheckAsAsync($"rp_app_{s}", table)).Report;

        report.NonTenantPermissivePolicies.Should().ContainSingle().Which.Should().Contain("PUBLIC");
    }

    [Fact]
    public async Task RestrictivePolicies_AndTablesWithoutATenantPolicy_AreNotReported()
    {
        var s = Suffix();
        await ExecuteAsync($"CREATE ROLE rp_app_{s} LOGIN PASSWORD 'pw';");
        var table = await CreateTenantTableAsync(s, $"rp_app_{s}");
        await ExecuteAsync($"""
            CREATE POLICY {table}_not_archived ON {table} AS RESTRICTIVE USING (id > 0);
            CREATE TABLE rp_other_{s} (id int, owner name);
            ALTER TABLE rp_other_{s} ENABLE ROW LEVEL SECURITY;
            CREATE POLICY rp_other_{s}_owner ON rp_other_{s} USING (owner = current_user);
            """);

        (await CheckAsAsync($"rp_app_{s}", table)).Report.Problems.Should().BeEmpty();
    }

    [Fact]
    public async Task Catalog_ReportsProtection_PerTable_InTheOrderGiven()
    {
        var s = Suffix();
        await ExecuteAsync($"CREATE ROLE rp_app_{s} LOGIN PASSWORD 'pw';");
        var protectedTable = await CreateTenantTableAsync(s, $"rp_app_{s}");
        await ExecuteAsync($"""
            CREATE TABLE rp_unforced_{s} (id int, tenant_id uuid);
            ALTER TABLE rp_unforced_{s} ENABLE ROW LEVEL SECURITY;
            CREATE POLICY p ON rp_unforced_{s} USING ({TenantSessionSql.PolicyPredicate("tenant_id")});
            CREATE TABLE rp_plain_{s} (id int, tenant_id uuid);
            """);

        await using var dataSource = NpgsqlDataSource.Create(DatabaseConnectionString());
        await using var connection = await dataSource.OpenConnectionAsync();
        var status = await RowLevelSecurityCatalog.GetTableStatusAsync(
            connection, [$"public.{protectedTable}", $"rp_unforced_{s}", $"rp_plain_{s}", $"rp_missing_{s}"]);

        status.Select(t => t.IsProtected).Should().Equal(true, false, false, false);
        status[1].Should().Match<RowLevelSecurityTableStatus>(t => t.RowSecurityEnabled && !t.RowSecurityForced && t.HasTenantPolicy);
        status[2].Should().Match<RowLevelSecurityTableStatus>(t => t.Exists && !t.RowSecurityEnabled && !t.HasTenantPolicy);
        status[3].Exists.Should().BeFalse();
    }
}
