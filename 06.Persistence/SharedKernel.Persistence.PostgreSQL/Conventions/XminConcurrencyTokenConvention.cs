using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// An EF Core <see cref="IModelFinalizingConvention"/> that binds the <c>RowVersion</c> property of
/// every <see cref="IHasConcurrency"/> entity to PostgreSQL's real <c>xmin</c> system column — the
/// genuine, working optimistic-concurrency mechanism for this provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists:</strong> <c>EntityTypeConfigurationBase</c> (in
/// <c>SharedKernel.Persistence.EfCore</c>) marks the <c>RowVersion</c> property with the
/// provider-neutral <c>.IsConcurrencyToken()</c> only — that package must never reference Npgsql,
/// so it cannot call <c>.UseXminAsConcurrencyToken()</c> (an
/// <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>-only extension method) itself. This convention
/// completes the wiring on the PostgreSQL side: it locates the already-marked concurrency-token
/// property and reconfigures it to bind to <c>xmin</c> instead of an ordinary application-managed
/// <c>bytea</c> column (which nothing in Postgres auto-populates on <c>UPDATE</c>, unlike SQL
/// Server's native <c>rowversion</c> type).
/// </para>
/// <para>
/// <strong>Reconfiguration applied</strong> to the property already marked
/// <see cref="Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty.IsConcurrencyToken"/>:
/// <list type="bullet">
/// <item><description><c>HasColumnName("xmin")</c></description></item>
/// <item><description><c>HasColumnType("xid")</c></description></item>
/// <item><description><c>HasConversion(new XminRowVersionValueConverter())</c></description></item>
/// <item><description><c>ValueGenerated.OnAddOrUpdate</c> — the database supplies a fresh value on both insert and update</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Registration and ordering:</strong> registered automatically by <c>UsePostgreSQL()</c>
/// via the same <see cref="IConventionSetPlugin"/> service-replacement mechanism
/// <see cref="SnakeCaseNamingConvention"/> uses — no manual <c>ConfigureConventions</c> override is
/// required in the consuming <c>DbContext</c>. This convention is registered to run <strong>after</strong>
/// <see cref="SnakeCaseNamingConvention"/> in the model-finalizing pipeline so that its explicit
/// <c>"xmin"</c> column name always wins over the snake-cased default (<c>"row_version"</c>) that
/// would otherwise apply — <c>"xmin"</c> is already lowercase/snake_case-compatible, so this
/// ordering is a correctness requirement, not merely a readability preference.
/// </para>
/// </remarks>
internal sealed class XminConcurrencyTokenConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Skip a derived TPH type — its RowVersion property (if any) is inherited from, and
            // already reconfigured on, the root entity type this loop visits separately.
            if (entityType.BaseType is not null)
                continue;

            if (!typeof(IHasConcurrency).IsAssignableFrom(entityType.ClrType))
                continue;

            var property = entityType.FindProperty(nameof(IHasConcurrency.RowVersion));

            // Fail loudly rather than silently skip. IHasConcurrency is the
            // entity's own declared intent to participate in optimistic concurrency — an entity
            // type that implements it but was never actually marked.IsConcurrencyToken() (e.g. a
            // hand-written IEntityTypeConfiguration<T> that forgot the call, or bypassed
            // EntityTypeConfigurationBase entirely) would otherwise silently lose concurrency
            // protection on PostgreSQL with no error anywhere, discoverable only by a concurrent
            // update quietly clobbering another writer's change.
            if (property is null || !property.IsConcurrencyToken)
            {
                throw new InvalidOperationException(
                    $"Entity type '{entityType.ClrType.Name}' implements "
                        + $"'{nameof(IHasConcurrency)}' but its '{nameof(IHasConcurrency.RowVersion)}' "
                        + "property is not configured as a concurrency token. Ensure the entity's "
                        + "configuration calls '.IsConcurrencyToken()' (EntityTypeConfigurationBase "
                        + "does this automatically) before 'UsePostgreSQL()' binds it to 'xmin'.");
            }

            property.SetColumnName("xmin");
            property.SetColumnType("xid");
            property.SetValueConverter(new XminRowVersionValueConverter());
            property.SetValueGenerated(ValueGenerated.OnAddOrUpdate);
        }
    }
}
