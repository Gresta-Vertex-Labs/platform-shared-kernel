using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SharedKernel.Persistence.EfCore.Migrations;

namespace SharedKernel.Persistence.EfCore.Tests.PostgreSql.Migrations;

/// <summary>
/// Row-level-security migration DDL: policy names stay within PostgreSQL's 63-byte identifier limit (which it
/// would otherwise truncate silently) and the Down helper drops exactly the names the Up helper created.
/// </summary>
public sealed partial class RowLevelSecurityMigrationBuilderExtensionsTests
{
    [GeneratedRegex("POLICY (?:IF EXISTS )?\"(?<name>[^\"]+)\"")]
    private static partial Regex PolicyNamePattern { get; }

    [Fact]
    public void ShortTableName_KeepsTheReadablePolicyNames()
    {
        var up = Sql(b => b.EnableTenantRowLevelSecurity("orders", crossTenantRole: "app_admin"));

        PolicyNames(up).Should().Equal("orders_tenant_isolation", "orders_cross_tenant");
    }

    [Fact]
    public void LongTableName_PolicyNamesFitIn63Bytes_AndDownDropsTheSameNames()
    {
        var table = "customer_subscription_billing_adjustment_history_" + new string('x', 12); // 61 bytes

        var up = PolicyNames(Sql(b => b.EnableTenantRowLevelSecurity(table, crossTenantRole: "app_admin")));
        var down = PolicyNames(Sql(b => b.DisableTenantRowLevelSecurity(table)));

        up.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        up.Should().OnlyContain(name => Encoding.UTF8.GetByteCount(name) <= 63);
        down.Should().BeEquivalentTo(up, "Down must drop exactly the policies Up created");
    }

    [Fact]
    public void TableNameLongerThan63Bytes_IsRejected_InsteadOfSilentlyTruncatedByPostgreSql()
    {
        var act = () => Sql(b => b.EnableTenantRowLevelSecurity(new string('t', 64)));

        act.Should().Throw<ArgumentException>().WithMessage("*63-byte*");
    }

    private static List<string> Sql(Action<MigrationBuilder> build)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        build(builder);
        return builder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
    }

    private static List<string> PolicyNames(IEnumerable<string> statements) =>
        statements.Select(s => PolicyNamePattern.Match(s))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .ToList();
}
