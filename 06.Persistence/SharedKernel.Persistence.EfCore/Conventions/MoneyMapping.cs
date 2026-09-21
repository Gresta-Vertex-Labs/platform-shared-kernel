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
            if (entityType.HasSharedClrType)
                continue;

            foreach (var property in DeclaredMoneyProperties(entityType))
            {
                if (entityType.FindComplexProperty(property.Name) is { } existing)
                {
                    ApplyDefaultPrecision(existing);
                    continue;
                }

                if (entityType.FindMember(property.Name) is not null
                    || ((IConventionEntityType)entityType).IsIgnored(property.Name))
                {
                    continue;
                }

                var required = Nullability.Create(property).ReadState != NullabilityState.Nullable;
                var complex = entityType.IsOwned()
                    ? null
                    : modelBuilder.Entity(entityType.ClrType).ComplexProperty(typeof(Money), property.Name);

                if (complex is not null)
                    Configure(complex, required);
            }
        }
    }

    private static void Configure(ComplexPropertyBuilder complex, bool required)
    {
        complex.IsRequired(required);
        complex.Property(nameof(Money.Amount)).HasPrecision(MoneyEntityTypeBuilderExtensions.DefaultPrecision, MoneyEntityTypeBuilderExtensions.DefaultScale);
        complex.Property(nameof(Money.Currency)).HasConversion<CurrencyValueConverter>().HasMaxLength(3).IsFixedLength();
    }

    private static void ApplyDefaultPrecision(IMutableComplexProperty complexProperty)
    {
        if (complexProperty.ComplexType.FindProperty(nameof(Money.Amount)) is { } amount && amount.GetPrecision() is null)
        {
            amount.SetPrecision(MoneyEntityTypeBuilderExtensions.DefaultPrecision);
            amount.SetScale(MoneyEntityTypeBuilderExtensions.DefaultScale);
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
