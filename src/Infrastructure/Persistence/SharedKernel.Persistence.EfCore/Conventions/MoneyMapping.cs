using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Monetary;
using SharedKernel.Persistence.EfCore.Conversions;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Maps every <see cref="Money"/> property of the model's entity types as a two-column complex type
/// (<c>{property}_amount numeric(19,4)</c>, <c>{property}_currency char(3)</c>) without any configuration, required
/// unless the property is declared <c>Money?</c>. <c>builder.Money(e =&gt; e.Price, ...)</c> changes the defaults.
/// </summary>
internal static class MoneyMapping
{
    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>Maps the <see cref="Money"/> properties that no configuration mapped or ignored.</summary>
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.HasSharedClrType || entityType.IsOwned())
                continue;

            foreach (var property in DeclaredMoneyProperties(entityType))
            {
                var existing = entityType.FindComplexProperty(property.Name);
                if (existing is null
                    && (entityType.FindMember(property.Name) is not null
                        || ((IConventionEntityType)entityType).IsIgnored(property.Name)))
                {
                    continue;
                }

                // EF Core discovers a Money property as a complex type (it is declared complex before discovery) but not
                // its get-only members, so the complex type could not be materialized; declare them, keeping every
                // setting an explicit builder.Money(...) call made.
                var complex = modelBuilder.Entity(entityType.ClrType).ComplexProperty(typeof(Money), property.Name);
                if (existing is null)
                    complex.IsRequired(Nullability.Create(property).ReadState != NullabilityState.Nullable);

                var amount = complex.Property(nameof(Money.Amount));
                if (amount.Metadata.GetPrecision() is null)
                    amount.HasPrecision(MoneyEntityTypeBuilderExtensions.DefaultPrecision, MoneyEntityTypeBuilderExtensions.DefaultScale);

                var currency = complex.Property(nameof(Money.Currency));
                if (currency.Metadata.GetValueConverter() is null && currency.Metadata.GetProviderClrType() is null)
                    currency.HasConversion<CurrencyValueConverter>();
                if (currency.Metadata.GetMaxLength() is null)
                    currency.HasMaxLength(3).IsFixedLength();
            }
        }
    }

    // Money properties declared on this entity type's CLR type (and on unmapped base classes), not on a mapped base
    // entity type, which maps them itself.
    private static IEnumerable<PropertyInfo> DeclaredMoneyProperties(IMutableEntityType entityType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var mappedBase = entityType.BaseType?.ClrType;

        for (var type = entityType.ClrType; type is not null && type != typeof(object) && type != mappedBase; type = type.BaseType)
        {
            if (mappedBase is not null && type.IsAssignableFrom(mappedBase))
                yield break;

            foreach (var property in type.GetProperties(flags))
            {
                if (property.PropertyType == typeof(Money) && property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
                    yield return property;
            }
        }
    }
}
