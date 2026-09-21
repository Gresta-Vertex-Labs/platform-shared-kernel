using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.ValueObjects;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Static utility for auto-configuring value object ownership in EF Core models as EF Core 10 complex
/// types.
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
/// base.OnModelCreating(modelBuilder);
/// ValueObjectOwnershipBuilder.Apply(modelBuilder);
/// }
/// </code>
/// </para>
/// <para>
/// <strong>Complex types, not owned entity types.</strong> A matched property is
/// configured with <c>EntityTypeBuilder.ComplexProperty(Type, string)</c> — EF Core 10's complex-type
/// mapping — instead of the owned-entity-type <c>OwnsOne</c> this utility used before. A complex type
/// has no identity, no separate table by default and no cascade-delete concern of its own: its columns
/// live directly on the owner's row, so a soft-deleted (or otherwise unmodified) owner never orphans or
/// nulls it out the way an owned entity type's row could. Every scalar member of the value object is
/// declared explicitly with <c>ComplexPropertyBuilder.Property(Type, string)</c>, because — unlike
/// owned entity types — EF Core 10 complex types do <strong>not</strong> auto-discover scalar members
/// by convention; an undeclared member fails constructor binding at model-build time
/// (empirically verified against the real EF Core 10.0.5 assembly while building this utility).
/// </para>
/// <para>
/// <strong>Nested value objects are NOT supported and fail loudly (documented limitation).</strong>
/// EF Core 10 cannot bind a constructor parameter to a nested complex — or nested owned — type; only
/// scalar constructor parameters can be bound (verified empirically: a value object containing another
/// constructor-validated value object fails model building with "No suitable constructor was found",
/// regardless of whether the outer type is configured as a complex type or an owned entity type, and
/// regardless of how completely the inner type is configured). Rather than let that opaque EF Core
/// exception surface, <see cref="Apply"/> detects a nested <see cref="IValueObject"/> member ahead of
/// time and throws a clear, actionable <see cref="InvalidOperationException"/> naming the exact
/// property. There is no supported way to make this work automatically for a validated (constructor-only,
/// no public setters) value object: flatten the outer value object so the inner one's fields are
/// declared directly on it, or configure the property manually instead of relying on this utility — for
/// example by giving the nested type its own public parameterless constructor and settable properties
/// (breaking the platform's usual "validate in the constructor" convention for that one type) and
/// configuring it with an explicit <c>ComplexProperty(...)</c>/<c>OwnsOne(...)</c> call. A
/// <see cref="SharedKernel.Domain.ValueObjects.SingleValueObject{TValue}"/>-derived nested member is
/// the one exception — see below.
/// </para>
/// <para>
/// <strong><see cref="Money"/> is excluded — configure it with <c>.Money(...)</c>.</strong> A
/// <see cref="Money"/>-typed property is always configured explicitly through
/// <see cref="Conversions.MoneyEntityTypeBuilderExtensions.Money{TEntity}"/>, never by this generic
/// scan: precision/scale are column choices this utility has no basis to guess, and every
/// <see cref="Money"/> property must be pre-declared complex via
/// the platform's automatic <c>Money</c> mapping in
/// <c>ConfigureConventions</c> regardless. A <see cref="Money"/> member nested inside another value
/// object hits the same nested-value-object wall described above and is rejected the same way.
/// </para>
/// <para>
/// <strong><see cref="SingleValueObject{TValue}"/> is excluded — configure a converter.</strong> A
/// property whose type derives from <see cref="SingleValueObject{TValue}"/> (such as
/// <see cref="Currency"/>) wraps exactly one primitive value and is always meant to be a single scalar
/// column, never a complex type with its own <c>Value</c> sub-column. This utility therefore never
/// configures one — register a hand-written <c>ValueConverter&lt;TSingleValueObject, TValue&gt;</c>
/// globally with <c>configurationBuilder.Properties&lt;TSingleValueObject&gt;().HaveConversion&lt;TConverter&gt;()</c>
/// in <c>ConfigureConventions</c> (mirroring <see cref="Conversions.CurrencyValueConverter"/>). A
/// <see cref="SingleValueObject{TValue}"/> member nested inside another value object being
/// auto-configured by this scan is declared as a bare scalar property and relies on that same global
/// registration — if none exists, EF Core's own model-validation error surfaces at startup.
/// </para>
/// <para>
/// <strong>Collections are not processed by this utility.</strong> A collection of value objects
/// requires either an explicit <c>OwnsMany(...)</c> (per-element identity/lifecycle, own table,
/// cascade-delete semantics — the shape this package's soft-delete cascade rescue already targets) or
/// EF Core 10's new <c>ComplexCollection(...)</c> (no per-element identity; the whole collection is
/// rewritten on every save). This utility does not choose between them automatically — the semantics
/// differ too much to guess, and <c>ComplexCollection</c>'s "no independent row-level concurrency or
/// partial update" behavior has not been evaluated against this package's touch-root/audit/concurrency
/// conventions. Register whichever one fits explicitly in an <c>IEntityTypeConfiguration&lt;TEntity&gt;</c>.
/// </para>
/// <para>
/// <strong>Explicit-config guard:</strong> if a navigation, owned-type or complex property is already
/// declared for the property, the utility skips it to avoid conflicts with explicit configuration.
/// </para>
/// <para>
/// <strong>Startup cost:</strong> <see cref="Apply"/> has O(n×m) startup cost where <c>n</c> is
/// the number of non-owned entity types in the model and <c>m</c> is the number of instance
/// properties per type. This cost is incurred only once at model-build time — it is <em>not</em>
/// a hot path. An early-exit optimisation prevents candidate-list allocation when no
/// <see cref="IValueObject"/> properties are present in the model.
/// </para>
/// </remarks>
internal static class ValueObjectOwnershipBuilder
{
    /// <summary>
    /// Scans all non-owned entity types in the current model and automatically configures a complex
    /// type via <c>ComplexProperty</c> for properties whose type implements <see cref="IValueObject"/>.
    /// </summary>
    /// <param name="modelBuilder">
    /// The <see cref="ModelBuilder"/> from <c>OnModelCreating</c>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A matched value object has a member that is itself a non-<see cref="SingleValueObject{TValue}"/>
    /// value object (including <see cref="Money"/>) — see the type-level remarks for why this cannot be
    /// auto-configured and what to do instead.
    /// </exception>
    public static void Apply(ModelBuilder modelBuilder)
    {
        // Collect candidates first to avoid modifying the collection while iterating.
        List<(Type OwnerType, Type ValueObjectType, string NavName)>? candidates = null;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Skip already-owned types — they are already targets.
            if (entityType.IsOwned())
                continue;

            foreach (var clrProperty in GetCandidateProperties(entityType.ClrType))
            {
                var propType = clrProperty.PropertyType;

                if (!IsEligibleForAutoConfiguration(propType))
                    continue;

                // Skip any property ALREADY mapped as a scalar EF property — most commonly a
                // globally-registered value converter applied via ConfigureConventions (e.g. a
                // SingleValueObject's converter, or StronglyTypedId's), which runs before entity-type/
                // navigation discovery and so has already turned this CLR property into a scalar
                // column by the time this method's reflection-based scan reaches it.
                if (entityType.FindProperty(clrProperty.Name) is not null)
                    continue;

                // Skip if already explicitly configured as a complex property (e.g. Money via
                // MoneyEntityTypeBuilderExtensions.Money(...), or a developer's own ComplexProperty call).
                if (entityType.FindComplexProperty(clrProperty.Name) is not null)
                    continue;

                // Skip if already configured as an owned navigation under this owner (a developer opting
                // out of the complex-type default for this one property, e.g. because it genuinely needs
                // owned-entity-type semantics).
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

        foreach (var (ownerType, valueObjectType, navName) in candidates)
        {
            var complexBuilder = modelBuilder.Entity(ownerType).ComplexProperty(valueObjectType, navName);
            ConfigureMembers(complexBuilder, ownerType, valueObjectType, navName);
        }
    }

    /// <summary>
    /// Declares every instance member of <paramref name="valueObjectType"/> on
    /// <paramref name="complexBuilder"/> so EF Core's constructor-binding convention can find them —
    /// complex types, unlike owned entity types, do not auto-discover scalar members by convention.
    /// </summary>
    private static void ConfigureMembers(
        ComplexPropertyBuilder complexBuilder, Type ownerType, Type valueObjectType, string navName)
    {
        foreach (var member in GetCandidateProperties(valueObjectType))
        {
            var memberType = member.PropertyType;

            // Collections of value objects are out of scope for this utility — see type-level remarks.
            if (IsCollectionType(memberType))
                continue;

            // A SingleValueObject-derived member wraps one primitive and is always a scalar column,
            // relying on a converter registered globally the same way a top-level property would.
            if (IsSingleValueObject(memberType))
            {
                complexBuilder.Property(memberType, member.Name);
                continue;
            }

            // Any other value object nested inside this one (including Money) cannot be bound through
            // a constructor parameter by EF Core 10 — fail loudly with an actionable message instead of
            // letting EF Core's own opaque "No suitable constructor was found" surface.
            if (memberType.IsClass && typeof(IValueObject).IsAssignableFrom(memberType))
            {
                throw new InvalidOperationException(
                    $"'{valueObjectType.Name}.{member.Name}' is itself a value object ('{memberType.Name}'). " +
                    "EF Core cannot bind a nested value object through a constructor parameter, so " +
                    $"'{ownerType.Name}.{navName}' cannot be auto-configured by {nameof(ValueObjectOwnershipBuilder)}. " +
                    $"Flatten '{valueObjectType.Name}' so '{memberType.Name}'’s fields are declared directly on " +
                    $"it, or configure '{ownerType.Name}.{navName}' explicitly instead of relying on the generic scan.");
            }

            // A plain scalar member (string, number, DateTime, enum, Guid,...).
            complexBuilder.Property(memberType, member.Name);
        }
    }

    private static bool IsEligibleForAutoConfiguration(Type propType)
    {
        // Skip non-class types, strings, and collection types.
        if (!propType.IsClass || propType == typeof(string) || IsCollectionType(propType))
            return false;

        // Skip if not IValueObject.
        if (!typeof(IValueObject).IsAssignableFrom(propType))
            return false;

        // Money is always configured explicitly via.Money(...) — see type-level remarks.
        if (propType == typeof(Money))
            return false;

        // A SingleValueObject wraps one primitive and is always a scalar column via a converter,
        // never an auto-owned/complex type — see type-level remarks.
        if (IsSingleValueObject(propType))
            return false;

        return true;
    }

    /// <summary>
    /// Returns whether <paramref name="type"/> derives (directly or transitively) from
    /// <see cref="SingleValueObject{TValue}"/>, walking the base-type chain since
    /// <see cref="Type.IsAssignableFrom(Type)"/> does not match an open generic definition against a
    /// closed one.
    /// </summary>
    private static bool IsSingleValueObject(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SingleValueObject<>))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the instance properties of <paramref name="clrType"/>, public and non-public alike —
    /// EF Core can bind a non-public property just as well as a public one, and excluding non-public
    /// properties silently produced an unmapped/uninitialized column for any value object that exposed
    /// its state through a non-public setter. Indexers are excluded; they cannot be configured as
    /// simple properties. The <see cref="DynamicallyAccessedMembersAttribute"/> on the parameter
    /// suppresses IL2026/IL2075 trim warnings — this call is model-build time only and is not a hot path.
    /// </summary>
    private static IEnumerable<PropertyInfo> GetCandidateProperties(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
        Type clrType) =>
        clrType
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0);

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
