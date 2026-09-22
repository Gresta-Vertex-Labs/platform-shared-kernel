using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Monetary;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Configures the columns the domain interfaces imply, so no entity configuration has to inherit a base class.
/// </summary>
/// <remarks>
/// <para>Applied at convention level, so an explicit configuration of the same property always wins.</para>
/// <list type="bullet">
/// <item><description><see cref="IHasCreatedAudit"/>: <c>CreatedBy</c> required, 256 characters; <c>CreatedOn</c>
/// required; both <b>ignored after the first save</b>, so an update — including <c>Update()</c> of a detached
/// entity, which marks every property modified — can never overwrite creation provenance.</description></item>
/// <item><description><see cref="IHasAudit"/>: <c>ModifiedBy</c> optional, 256 characters; <c>ModifiedOn</c> optional.</description></item>
/// <item><description><see cref="ISoftDeletable"/>: <c>DeletedBy</c> 256 characters.</description></item>
/// <item><description><see cref="IHasTenant"/>: <c>TenantId</c> required and indexed (unless an index already starts with it).</description></item>
/// <item><description><see cref="IHasVersion"/>: <c>Version</c> required, so a loaded aggregate continues its event numbering.</description></item>
/// <item><description><see cref="IHasConcurrency"/>: <c>RowVersion</c> is a concurrency token (bound to PostgreSQL's
/// <c>xmin</c> by the context when running on PostgreSQL).</description></item>
/// <item><description><see cref="Money"/>: <c>Amount</c> precision 19, scale 4.</description></item>
/// </list>
/// </remarks>
internal sealed class DomainColumnConvention : IModelFinalizingConvention
{
    private const int ActorMaxLength = 256;
    private const int MoneyPrecision = 19;
    private const int MoneyScale = 4;

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(IHasCreatedAudit).IsAssignableFrom(clrType))
            {
                Configure(entityType, nameof(IHasCreatedAudit.CreatedBy), required: true, maxLength: ActorMaxLength, writeOnce: true);
                Configure(entityType, nameof(IHasCreatedAudit.CreatedOn), required: true, writeOnce: true);
            }

            if (typeof(IHasAudit).IsAssignableFrom(clrType))
            {
                Configure(entityType, nameof(IHasAudit.ModifiedBy), required: false, maxLength: ActorMaxLength);
                Configure(entityType, nameof(IHasAudit.ModifiedOn), required: false);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
                Configure(entityType, nameof(ISoftDeletable.DeletedBy), required: null, maxLength: ActorMaxLength);

            if (typeof(IHasVersion).IsAssignableFrom(clrType))
                Configure(entityType, nameof(IHasVersion.Version), required: true);

            if (typeof(IHasConcurrency).IsAssignableFrom(clrType))
                entityType.FindProperty(nameof(IHasConcurrency.RowVersion))?.Builder.IsConcurrencyToken(true);

            if (typeof(IHasTenant).IsAssignableFrom(clrType))
                ConfigureTenant(entityType);

            ConfigureMoney(entityType);
        }
    }

    private static void Configure(
        IConventionEntityType entityType,
        string propertyName,
        bool? required,
        int? maxLength = null,
        bool writeOnce = false)
    {
        // Declared on a base type: configured when the base type is visited.
        if (entityType.FindDeclaredProperty(propertyName)?.Builder is not { } property)
            return;

        if (required is not null)
            property.IsRequired(required);

        if (maxLength is not null)
            property.HasMaxLength(maxLength);

        if (writeOnce)
            property.AfterSave(PropertySaveBehavior.Ignore);
    }

    private static void ConfigureTenant(IConventionEntityType entityType)
    {
        if (entityType.FindDeclaredProperty(nameof(IHasTenant.TenantId)) is not { } tenantId)
            return;

        tenantId.Builder.IsRequired(true);

        if (entityType.IsOwned())
            return;

        // Survives into migration snapshots, where entity types have no CLR type (EnableTenantRowLevelSecurityForModel).
        entityType.Builder.HasAnnotation(PersistenceModelAnnotationNames.Tenant, true);

        var alreadyLeading = entityType.GetIndexes().Any(i => ReferenceEquals(i.Properties[0], tenantId))
            || entityType.GetKeys().Any(k => ReferenceEquals(k.Properties[0], tenantId));

        if (!alreadyLeading)
            entityType.Builder.HasIndex([tenantId]);
    }

    private static void ConfigureMoney(IConventionTypeBase typeBase)
    {
        foreach (var complexProperty in typeBase.GetDeclaredComplexProperties())
        {
            var complexType = complexProperty.ComplexType;

            if (complexProperty.ClrType == typeof(Money)
                && complexType.FindProperty(nameof(Money.Amount))?.Builder is { } amount)
            {
                amount.HasPrecision(MoneyPrecision);
                amount.HasScale(MoneyScale);
            }

            ConfigureMoney(complexType);
        }
    }
}
