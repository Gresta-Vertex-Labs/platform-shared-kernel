using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Extensions;

using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.PostgreSql.Conventions;

// ---------------------------------------------------------------------------
// A small model deliberately exercising every identifier kind the convention names: table, column,
// primary key, alternate (unique) key, check constraint, index, foreign key, plus one identifier long
// enough to force 63-byte truncation.
// ---------------------------------------------------------------------------

public sealed class NamingParentEntity
{
    public int RowId { get; set; }
    public string NaturalKey { get; set; } = string.Empty;
    public int PositiveOnlyValue { get; set; }
}

public sealed class NamingChildEntity
{
    public int RowId { get; set; }
    public int NamingParentEntityRowId { get; set; }
}

// A name deliberately far beyond PostgreSQL's 63-byte identifier limit once snake-cased.
public sealed class AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase
{
    public int Id { get; set; }
}

public sealed class NamingParentEntityConfig : IEntityTypeConfiguration<NamingParentEntity>
{
    public void Configure(EntityTypeBuilder<NamingParentEntity> builder)
    {
        builder.HasKey(e => e.RowId);
        builder.HasAlternateKey(e => e.NaturalKey);
        builder.HasIndex(e => e.PositiveOnlyValue);
        builder.ToTable(t => t.HasCheckConstraint("CK_PositiveOnlyValue", "positive_only_value > 0"));
    }
}

public sealed class NamingChildEntityConfig : IEntityTypeConfiguration<NamingChildEntity>
{
    public void Configure(EntityTypeBuilder<NamingChildEntity> builder)
    {
        builder.HasKey(e => e.RowId);
        builder.HasOne<NamingParentEntity>().WithMany().HasForeignKey(e => e.NamingParentEntityRowId);
    }
}

public sealed class NamingCompletenessTestDbContext : DbContext
{
    public DbSet<NamingParentEntity> Parents => Set<NamingParentEntity>();

    public DbSet<NamingChildEntity> Children => Set<NamingChildEntity>();

    public DbSet<AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase> LongNamed =>
        Set<AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase>();

    public NamingCompletenessTestDbContext(DbContextOptions<NamingCompletenessTestDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new NamingParentEntityConfig());
        modelBuilder.ApplyConfiguration(new NamingChildEntityConfig());

        // Explicit long table name — EF Core's default table-naming convention prefers the DbSet
        // PROPERTY name ("LongNamed") over the full CLR type name when a DbSet property exists, so
        // relying on the class name alone would not actually force 63-byte truncation.
        modelBuilder
            .Entity<AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase>()
                .ToTable("AVeryLongTableNameThatWillDefinitelyExceedPostgresSixtyThreeByteIdentifierLimitOnceSnakeCased");
    }
}

/// <summary>
/// snake_case naming (<c>EFCore.NamingConventions</c>, applied by <c>UsePostgreSQL()</c>) plus the 63-byte
/// <see cref="SharedKernel.Persistence.EfCore.Conventions.PostgreSqlIdentifierLengthConvention"/>
/// completeness: primary/alternate keys, check constraints, indexes, and foreign keys all get renamed,
/// and any identifier exceeding PostgreSQL's 63-byte limit is truncated with a stable, uniquifying
/// hash suffix. Pure model-building — no live database connection needed.
/// </summary>
public sealed class PostgreSqlNamingConventionCompletenessTests
{
    private static NamingCompletenessTestDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<NamingCompletenessTestDbContext>();
        builder.UsePostgreSQL(TestNpgsqlDataSources.Get("Host=localhost;Database=naming_completeness_test;Username=test;Password=test"));
        return new NamingCompletenessTestDbContext(builder.Options);
    }

    [Fact]
    public void PrimaryKey_Name_IsSnakeCase()
    {
        using var ctx = CreateContext();
        var entityType = ctx.Model.FindEntityType(typeof(NamingParentEntity))!;

        var pkName = entityType.FindPrimaryKey()!.GetName();

        pkName.Should().NotBeNull();
        pkName.Should().Be(pkName!.ToLowerInvariant(), "EF Core's default PK_* name must be converted to snake_case");
        pkName.Should().NotContain("PK");
    }

    [Fact]
    public void AlternateKey_Name_IsSnakeCase()
    {
        using var ctx = CreateContext();
        var entityType = ctx.Model.FindEntityType(typeof(NamingParentEntity))!;

        var alternateKey = entityType.GetKeys().First(k => !k.IsPrimaryKey());
        var name = alternateKey.GetName();

        name.Should().NotBeNull();
        name.Should().Be(name!.ToLowerInvariant());
    }

    [Fact]
    public void CheckConstraint_ExplicitName_IsKeptAsConfigured()
    {
        using var ctx = CreateContext();

        // Check constraints are design-time-only metadata, stripped from the read-optimized
        // runtime model ctx.Model returns — the design-time model is the one a migration is
        // actually generated from, and the one XminConcurrencyTokenConvention/PostgreSqlIdentifierLengthConvention
        // (both IModelFinalizingConvention) run against.
        var designTimeModel = ctx.GetService<IDesignTimeModel>().Model;
        var entityType = designTimeModel.FindEntityType(typeof(NamingParentEntity))!;

        var checkConstraint = entityType.GetCheckConstraints().Single();

        // EFCore.NamingConventions (P-558) rewrites only names EF Core generated itself; an explicitly
        // configured name is the developer's choice and is kept verbatim (the former hand-rolled convention
        // lower-cased it). Name check constraints in snake_case at the call site.
        checkConstraint.Name.Should().Be("CK_PositiveOnlyValue");
    }

    [Fact]
    public void Index_Name_IsSnakeCase()
    {
        using var ctx = CreateContext();
        var entityType = ctx.Model.FindEntityType(typeof(NamingParentEntity))!;

        var index = entityType.GetIndexes().Single();

        index.GetDatabaseName().Should().Be(index.GetDatabaseName()!.ToLowerInvariant());
        index.GetDatabaseName().Should().Contain("positive_only_value");
    }

    [Fact]
    public void ForeignKey_ConstraintName_IsSnakeCase()
    {
        using var ctx = CreateContext();
        var childEntityType = ctx.Model.FindEntityType(typeof(NamingChildEntity))!;

        var foreignKey = childEntityType.GetForeignKeys().Single();

        foreignKey.GetConstraintName().Should().Be(foreignKey.GetConstraintName()!.ToLowerInvariant());
    }

    [Fact]
    public void ForeignKeyColumn_Name_IsSnakeCase()
    {
        using var ctx = CreateContext();
        var childEntityType = ctx.Model.FindEntityType(typeof(NamingChildEntity))!;

        var property = childEntityType.FindProperty(nameof(NamingChildEntity.NamingParentEntityRowId))!;

        property.GetColumnName().Should().Be("naming_parent_entity_row_id");
    }

    [Fact]
    public void TableName_LongerThan63Bytes_IsTruncatedWithStableHashSuffix()
    {
        using var ctx = CreateContext();
        var entityType = ctx.Model.FindEntityType(
            typeof(AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase))!;

        var tableName = entityType.GetTableName();

        tableName.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetByteCount(tableName!).Should().BeLessThanOrEqualTo(63);
        // The 8-hex-character stable hash suffix this convention appends when truncating.
        tableName.Should().MatchRegex("_[0-9a-f]{8}$");
    }

    [Fact]
    public void TableName_Truncation_IsDeterministic_AcrossSeparateModelBuilds()
    {
        using var ctx1 = CreateContext();
        using var ctx2 = CreateContext();

        var name1 = ctx1.Model.FindEntityType(
            typeof(AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase))!.GetTableName();
        var name2 = ctx2.Model.FindEntityType(
            typeof(AVeryLongEntityNameThatWillExceedPostgresSixtyThreeByteIdentifierLimitWhenConvertedToSnakeCase))!.GetTableName();

        name1.Should().Be(name2, "the same long name must always truncate to the identical identifier, in this process and in a fresh migration build on another machine");
    }
}
