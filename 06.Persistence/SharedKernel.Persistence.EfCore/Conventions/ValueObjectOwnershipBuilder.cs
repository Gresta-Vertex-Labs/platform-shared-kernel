using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.ValueObjects.Money;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Static utility for auto-configuring value object ownership in EF Core models.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a static utility method, not an EF Core <c>IModelFinalizingConvention</c>.</strong>
/// Call <see cref="Apply"/> manually at the end of <c>OnModelCreating</c> after all entity
/// configurations are applied. Do <strong>NOT</strong> register this via
/// <c>ConfigureConventions</c> — it will have no effect there because it does not implement
/// <c>IModelFinalizingConvention</c>.
/// </para>
/// <para>
/// <strong>Usage — call <see cref="Apply"/> at the end of <c>OnModelCreating</c>:</strong>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     base.OnModelCreating(modelBuilder);
///     ValueObjectOwnershipBuilder.Apply(modelBuilder);
/// }
/// </code>
/// </para>
/// <para>
/// <strong>OwnsMany limitation:</strong> Collections of value objects are <em>not</em> processed
/// by this utility. They require <c>OwnsMany</c> with an explicit table/shadow-key configuration.
/// Register those explicitly in entity configuration classes.
/// </para>
/// <para>
/// <strong>Explicit-config guard:</strong> If a navigation or owned-type is already declared for
/// the property, the utility skips it to avoid conflicts with explicit <c>OwnsOne</c> calls.
/// </para>
/// <para>
/// <strong>Startup cost:</strong> <see cref="Apply"/> has O(n×m) startup cost where <c>n</c> is
/// the number of non-owned entity types in the model and <c>m</c> is the number of public instance
/// properties per type. This cost is incurred only once at model-build time — it is <em>not</em>
/// a hot path. An early-exit optimisation prevents candidate-list allocation when no
/// <see cref="IValueObject"/> properties are present in the model.
/// </para>
/// </remarks>
public static class ValueObjectOwnershipBuilder
{
    /// <summary>
    /// Scans all non-owned entity types in the current model and automatically applies
    /// <c>OwnsOne</c> for properties whose type implements <see cref="IValueObject"/>.
    /// </summary>
    /// <param name="modelBuilder">
    /// The <see cref="ModelBuilder"/> from <c>OnModelCreating</c>.
    /// </param>
    public static void Apply(ModelBuilder modelBuilder)
    {
        // Collect candidates first to avoid modifying the collection while iterating.
        List<(Type OwnerType, Type ValueObjectType, string NavName)>? candidates = null;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Skip already-owned types — they are already targets.
            if (entityType.IsOwned())
                continue;

            foreach (var clrProperty in GetPublicProperties(entityType.ClrType))
            {
                var propType = clrProperty.PropertyType;

                // Skip non-class types, strings, and collection types.
                if (!propType.IsClass || propType == typeof(string) || IsCollectionType(propType))
                    continue;

                // Skip if not IValueObject.
                if (!typeof(IValueObject).IsAssignableFrom(propType))
                    continue;

                // Skip Money (WO-066/P-440/D-107): a Money property must always be configured
                // explicitly via MoneyEntityTypeBuilderExtensions.OwnsMoney(...), never silently
                // auto-owned here — EF Core cannot bind Money's private, three-argument
                // constructor automatically, and Money's own precision/currency semantics are
                // exactly the kind of precision/security-sensitive concern this package already
                // treats as opt-in-only (mirroring PropertyBuilder<T>.Encrypt()'s philosophy).
                if (propType == typeof(Money))
                    continue;

                // Skip any property ALREADY mapped as a scalar EF property (T-122/T-123 fix,
                // 2026-09-02) — most commonly a globally-registered value converter applied via
                // ConfigureConventions (e.g. ModelConfigurationBuilderExtensions.ConfigureMoney's
                // `Properties<Currency>().HaveConversion<CurrencyValueConverter>()`), which runs
                // before entity-type/navigation discovery and so has already turned this CLR
                // property into a scalar column by the time this method's reflection-based scan
                // reaches it. The Money-specific skip above predates this general check and is
                // kept for its explanatory value, but was never sufficient on its own: any OTHER
                // IValueObject type globally converted the same way (Currency, standalone —
                // confirmed empirically: a bare Currency-typed property combined with
                // ConfigureMoney() + this method crashed model building with "property or
                // navigation ... already exists" before this fix) hit the identical conflict.
                // Checking "is this CLR property already a scalar EF property" generalises the
                // fix to any current or future globally-converted IValueObject, not just Money.
                if (entityType.FindProperty(clrProperty.Name) is not null)
                    continue;

                // Skip if already configured as an owned navigation under this owner.
                if (IsAlreadyOwned(entityType, propType, clrProperty.Name))
                    continue;

                // Lazy-initialise only when the first IValueObject candidate is found.
                candidates ??= [];
                candidates.Add((entityType.ClrType, propType, clrProperty.Name));
            }
        }

        // Early-exit: no IValueObject properties found — nothing to apply.
        if (candidates is null)
            return;

        // Apply OwnsOne via the standard ModelBuilder fluent API.
        foreach (var (ownerType, valueObjectType, navName) in candidates)
        {
            modelBuilder.Entity(ownerType).OwnsOne(valueObjectType, navName);
        }
    }

    /// <summary>
    /// Returns the public instance properties of <paramref name="clrType"/>.
    /// The <see cref="DynamicallyAccessedMembersAttribute"/> on the parameter suppresses IL2026/IL2075
    /// trim warnings — this call is model-build time only and is not a hot path.
    /// </summary>
    private static PropertyInfo[] GetPublicProperties(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type clrType)
        => clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static bool IsCollectionType(Type type) =>
        type.IsArray ||
        (type.IsGenericType &&
         (type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
          type.GetGenericTypeDefinition() == typeof(ICollection<>) ||
          type.GetGenericTypeDefinition() == typeof(IList<>) ||
          type.GetGenericTypeDefinition() == typeof(List<>) ||
          type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) ||
          type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)));

    private static bool IsAlreadyOwned(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType ownerType,
        Type valueObjectType,
        string propertyName)
    {
        foreach (var nav in ownerType.GetNavigations())
        {
            if (nav.Name == propertyName &&
                nav.ForeignKey.IsOwnership &&
                nav.TargetEntityType.ClrType == valueObjectType)
                return true;
        }

        return false;
    }
}
