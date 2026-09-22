using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>Finding A13: the query guard, and blind-index lookups.</summary>
[Collection("EncryptionPostgres")]
public sealed class QueryTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly TestRequestContext _request = new();
    private ServiceProvider _services = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        _request.TenantId = _tenant;
        _cs = EncryptionHost.Database(fixture.ConnectionString, "enc_query");
        _services = EncryptionHost.Build<CustomerDbContext>(_cs, _request);
        await using var scope = _services.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        context.Customers.AddRange(
            new Customer { TenantId = _tenant, Name = "a", Email = "Ada@Example.com", Billing = new Address { City = "Izmir", Bank = new BankAccount { Iban = "TR33 0006 1005" } } },
            new Customer { TenantId = _tenant, Name = "b", Email = "bob@example.com", Billing = new Address { City = "Bursa", Bank = new BankAccount { Iban = "TR99 1111" } } });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    public static TheoryData<string> RejectedQueries => new()
    {
        "equality", "not-equal", "starts-with", "order-by", "group-by", "select", "anonymous-select",
        "complex-select", "nested-where", "ef-property", "execute-update", "execute-update-value", "join-key",
    };

    [Theory]
    [MemberData(nameof(RejectedQueries))]
    public async Task UsingAnEncryptedPropertyInAQuery_IsRejectedBeforeItRuns(string shape)
    {
        await using var scope = _services.CreateAsyncScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers;

        Func<Task> act = shape switch
        {
            "equality" => () => customers.Where(x => x.Email == "bob@example.com").ToListAsync(),
            "not-equal" => () => customers.CountAsync(x => x.Email != "bob@example.com"),
            "starts-with" => () => customers.Where(x => x.Email.StartsWith("bob")).ToListAsync(),
            "order-by" => () => customers.OrderBy(x => x.Email).ToListAsync(),
            "group-by" => () => customers.GroupBy(x => x.Email).Select(g => g.Count()).ToListAsync(),
            "select" => () => customers.Select(x => x.Email).ToListAsync(),
            "anonymous-select" => () => customers.Select(x => new { x.Id, x.Note }).ToListAsync(),
            "complex-select" => () => customers.Select(x => x.Billing).ToListAsync(),
            "nested-where" => () => customers.Where(x => x.Billing.Bank.Iban == "TR99 1111").ToListAsync(),
            "ef-property" => () => customers.Where(x => EF.Property<string>(x, "Email") == "x").ToListAsync(),
            "execute-update" => () => customers.ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "plain@example.com")),
            "execute-update-value" => () => customers.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, x => x.Email)),
            "join-key" => () => customers.Join(customers, a => a.Email, b => b.Email, (a, b) => a.Id).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("encrypted").And.Contain("WhereEncryptedEquals");
    }

    [Fact]
    public async Task ExecuteUpdateOnAnEncryptedColumn_NeverReachesTheDatabase()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var customers = scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers;
            var act = () => customers.ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "plain@example.com"));
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        await using var readScope = _services.CreateAsyncScope();
        (await readScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().ToListAsync())
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task QueriesThatDoNotUseAnEncryptedValue_AreAllowed()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        (await context.Customers.Where(x => x.Billing.City == "Bursa").ToListAsync()).Should().ContainSingle();
        (await context.Customers.OrderBy(x => x.Name).Select(x => x).ToListAsync()).Should().HaveCount(2);
        (await context.Customers.Select(x => new { x.Id, Customer = x }).ToListAsync()).Should().HaveCount(2);
        (await context.Customers.CountAsync(x => x.Note == null)).Should().Be(2);
        (await context.Customers.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "renamed"))).Should().Be(2);

        // A column named like an encrypted one in unrelated SQL is not touched (the old regex guard threw here).
        (await context.Database.SqlQueryRaw<string>("SELECT t.email AS \"Value\" FROM (SELECT 'a' AS email) t WHERE t.email = 'a'").ToListAsync())
            .Should().ContainSingle();
    }

    [Fact]
    public async Task WhereEncryptedEquals_ReadsPurposeNormalizationAndTenantFromTheModelAndContext()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        (await context.Customers.WhereEncryptedEquals(x => x.Email, "  ADA@example.com ").SingleAsync()).Name.Should().Be("a");
        (await context.Customers.WhereEncryptedEquals(x => x.Email, "nobody@example.com").ToListAsync()).Should().BeEmpty();

        // Nested complex property with flags plus a named normalizer.
        (await context.Customers.WhereEncryptedEquals(x => x.Billing.Bank.Iban, "tr99 1111").SingleAsync()).Name.Should().Be("b");

        // Composes with other operators, including the IQueryable form.
        (await context.Customers.Where(x => x.Name == "b").WhereEncryptedEquals(context, x => x.Email, "BOB@EXAMPLE.COM").CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task WhereEncryptedEquals_IsTenantBound_AndSendsTheIndexAsAParameter()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        var query = context.Customers.IgnoreQueryFilters().WhereEncryptedEquals(context, x => x.Email, "bob@example.com", tenantId: Guid.NewGuid());
        (await query.ToListAsync()).Should().BeEmpty();
        // ToQueryString prints parameter values as "-- @p=..." comments; the statement itself holds only the parameter.
        var sql = string.Join('\n', query.ToQueryString().Split('\n').Where(line => !line.StartsWith("--", StringComparison.Ordinal)));
        sql.Should().NotContain("v1:").And.Contain("email_blind_index = @");
    }

    [Fact]
    public async Task WhereEncryptedEquals_OnAPropertyWithoutBlindIndex_ExplainsWhatToAdd()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        var act = () => context.Customers.WhereEncryptedEquals(x => x.Note, "vip");
        act.Should().Throw<InvalidOperationException>().WithMessage("*WithBlindIndex*");
    }
}
