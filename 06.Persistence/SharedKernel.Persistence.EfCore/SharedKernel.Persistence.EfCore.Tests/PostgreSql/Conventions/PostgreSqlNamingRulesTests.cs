using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.PostgreSql.Conventions;

public sealed class NamingRulesEntity
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string HTTPSUrl { get; set; } = string.Empty;
    public string Address1Line { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
    public string Explicit { get; set; } = string.Empty;
    public string ThisIsAnExtremelyLongPropertyNameThatBecomesLongerThanSixtyThreeBytesOnceSnakeCased { get; set; } = string.Empty;
}

public sealed class NamingRulesDbContext(DbContextOptions<NamingRulesDbContext> options) : DbContext(options)
{
    public DbSet<NamingRulesEntity> Entities => Set<NamingRulesEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<NamingRulesEntity>().Property(e => e.Explicit).HasColumnName("ExplicitColumn");
}

/// <summary>
/// The naming the PostgreSQL setup produces since P-558 replaced the hand-rolled convention with
/// <c>EFCore.NamingConventions</c>. Pins the library's rules (acronyms, digits) so an upgrade that changes them
/// fails here before it silently renames columns in a consumer's next migration.
/// </summary>
public sealed class PostgreSqlNamingRulesTests
{
    private static NamingRulesDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<NamingRulesDbContext>();
        builder.UsePostgreSQL(TestNpgsqlDataSources.Get("Host=localhost;Database=naming_rules_test;Username=test;Password=test"));
        return new NamingRulesDbContext(builder.Options);
    }

    private static string Column(string propertyName)
    {
        using var ctx = CreateContext();
        return ctx.Model.FindEntityType(typeof(NamingRulesEntity))!.FindProperty(propertyName)!.GetColumnName();
    }

    [Theory]
    [InlineData(nameof(NamingRulesEntity.Id), "id")]
    [InlineData(nameof(NamingRulesEntity.UserName), "user_name")]
    [InlineData(nameof(NamingRulesEntity.HTTPSUrl), "https_url")]
    [InlineData(nameof(NamingRulesEntity.Address1Line), "address1line")] // no underscore after a digit (the old convention wrote address1_line)
    [InlineData(nameof(NamingRulesEntity.Line2), "line2")]
    [InlineData(nameof(NamingRulesEntity.Explicit), "ExplicitColumn")] // an explicit HasColumnName is kept verbatim
    public void ColumnNames_FollowEfCoreNamingConventionsSnakeCaseRules(string property, string expected)
    {
        Column(property).Should().Be(expected);
    }

    [Fact]
    public void TableName_IsSnakeCasedPluralDbSetName()
    {
        using var ctx = CreateContext();

        ctx.Model.FindEntityType(typeof(NamingRulesEntity))!.GetTableName().Should().Be("entities");
    }

    [Fact]
    public void LongColumnName_IsKeptWithin63Bytes_WithStableHashSuffix()
    {
        var column = Column(nameof(NamingRulesEntity.ThisIsAnExtremelyLongPropertyNameThatBecomesLongerThanSixtyThreeBytesOnceSnakeCased));

        System.Text.Encoding.UTF8.GetByteCount(column).Should().BeLessThanOrEqualTo(63);
        column.Should().StartWith("this_is_an_extremely_long_property_name");
    }

    [Theory]
    [InlineData("short_name")]
    [InlineData("exactly_sixty_three_bytes_long_identifier_padded_out_to_63_xxxx")]
    public void Truncate_LeavesNamesWithinTheLimitUnchanged(string name)
    {
        PostgreSqlIdentifierLengthConvention.Truncate(name).Should().Be(name);
    }

    [Fact]
    public void Truncate_TwoLongNamesSharingAPrefix_StayDistinct()
    {
        var prefix = new string('a', 70);

        var first = PostgreSqlIdentifierLengthConvention.Truncate(prefix + "_first");
        var second = PostgreSqlIdentifierLengthConvention.Truncate(prefix + "_second");

        first.Should().NotBe(second);
        System.Text.Encoding.UTF8.GetByteCount(first).Should().Be(63);
        first.Should().MatchRegex("_[0-9a-f]{8}$");
    }

    [Fact]
    public void Truncate_NeverSplitsAMultiByteCharacter()
    {
        var name = new string('a', 53) + "ğğğğğğğğğğ";

        var truncated = PostgreSqlIdentifierLengthConvention.Truncate(name);

        System.Text.Encoding.UTF8.GetByteCount(truncated).Should().BeLessThanOrEqualTo(63);
        truncated.Should().NotContain("�");
    }
}

public sealed class OwnedKeyOwner
{
    public int Id { get; set; }
    public OwnedKeyAddress Address { get; set; } = new();
}

public sealed class OwnedKeyAddress
{
    public string Street { get; set; } = string.Empty;
}

public sealed class OwnedKeyDbContext(DbContextOptions<OwnedKeyDbContext> options) : DbContext(options)
{
    public DbSet<OwnedKeyOwner> Owners => Set<OwnedKeyOwner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OwnedKeyOwner>().OwnsOne(o => o.Address, a =>
        {
            // The configured shared-table key: snake_case naming alone would name it "address_id".
            a.Property<int>("Id");
            a.HasKey("Id");
        });
}

/// <summary>
/// <see cref="OwnedSharedTableKeyColumnConvention"/>: a shared-table owned type keeps its owner's key column
/// under snake_case naming (regression for the EFCore.NamingConventions switch, P-558).
/// </summary>
public sealed class OwnedSharedTableKeyColumnConventionTests
{
    [Fact]
    public void OwnedTypeKey_InOwnersTable_MapsToTheOwnersKeyColumn()
    {
        var builder = new DbContextOptionsBuilder<OwnedKeyDbContext>();
        builder.UsePostgreSQL(TestNpgsqlDataSources.Get("Host=localhost;Database=owned_key_test;Username=test;Password=test"));
        using var ctx = new OwnedKeyDbContext(builder.Options);

        var owned = ctx.Model.FindEntityType(typeof(OwnedKeyAddress))!;

        owned.FindProperty("Id")!.GetColumnName().Should().Be("id");
        owned.FindProperty(nameof(OwnedKeyAddress.Street))!.GetColumnName().Should().Be("address_street");
    }
}
