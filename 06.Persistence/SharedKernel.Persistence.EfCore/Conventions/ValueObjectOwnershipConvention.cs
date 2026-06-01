using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Utility class that auto-applies <c>OwnsOne</c> for all scalar properties whose CLR type
/// implements <see cref="IValueObject"/>, eliminating the need for manual <c>OwnsOne</c> calls
/// in every entity configuration.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Usage — call <see cref="Apply"/> at the end of <c>OnModelCreating</c>:</strong>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     base.OnModelCreating(modelBuilder);
///     ValueObjectOwnershipConvention.Apply(modelBuilder);
/// }
/// </code>
/// </para>
/// <para>
/// <strong>OwnsMany limitation:</strong> Collections of value objects are <em>not</em> processed
/// by this convention. They require <c>OwnsMany</c> with an explicit table/shadow-key configuration.
/// Register those explicitly in entity configuration classes.
/// </para>
/// <para>
/// <strong>Explicit-config guard:</strong> If a navigation or owned-type is already declared for
/// the property, the convention skips it to avoid conflicts with explicit <c>OwnsOne</c> calls.
/// </para>
/// </remarks>
public static class ValueObjectOwnershipConvention
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
        var candidates = new List<(Type OwnerType, Type ValueObjectType, string NavName)>();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Skip already-owned types — they are already targets.
            if (entityType.IsOwned())
                continue;

            foreach (var clrProperty in entityType.ClrType.GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                var propType = clrProperty.PropertyType;

                // Skip non-class types, strings, and collection types.
                if (!propType.IsClass || propType == typeof(string) || IsCollectionType(propType))
                    continue;

                // Skip if not IValueObject.
                if (!typeof(IValueObject).IsAssignableFrom(propType))
                    continue;

                // Skip if already configured as an owned navigation under this owner.
                if (IsAlreadyOwned(entityType, propType, clrProperty.Name))
                    continue;

                candidates.Add((entityType.ClrType, propType, clrProperty.Name));
            }
        }

        // Apply OwnsOne via the standard ModelBuilder fluent API.
        foreach (var (ownerType, valueObjectType, navName) in candidates)
        {
            modelBuilder.Entity(ownerType).OwnsOne(valueObjectType, navName);
        }
    }

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
