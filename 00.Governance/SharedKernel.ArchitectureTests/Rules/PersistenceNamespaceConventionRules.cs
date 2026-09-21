using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Locks the consumer-facing namespace layout of the 06.Persistence packages (P-558): a multi-tenant service needs a
/// handful of <c>using</c> directives, not one per package and feature folder.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Registration and builder extensions — receivers <c>IServiceCollection</c>, <c>IHostApplicationBuilder</c>,
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;</c>, <c>DbContextOptionsBuilder</c> — live in
/// <see cref="RegistrationNamespace"/>, whichever persistence assembly declares them (a namespace spans assemblies).
/// </description></item>
/// <item><description>
/// EF Core model-configuration, migration and query helpers — receivers <c>ModelBuilder</c>,
/// <c>EntityTypeBuilder&lt;T&gt;</c>, <c>PropertyBuilder&lt;T&gt;</c>, <c>ComplexTypePropertyBuilder&lt;T&gt;</c>,
/// <c>MigrationBuilder</c>, <c>DatabaseFacade</c>, <c>IQueryable&lt;T&gt;</c>, <c>DbSet&lt;T&gt;</c> — live in
/// <see cref="EfCoreHelpersNamespace"/>.
/// </description></item>
/// </list>
/// Receivers are matched by simple type name, so the rule needs no reference to EF Core or the hosting packages.
/// Only public extension methods of public static classes are checked.
/// </remarks>
public static class PersistenceNamespaceConventionRules
{
    /// <summary>The namespace of every persistence registration and builder extension.</summary>
    public const string RegistrationNamespace = "SharedKernel.Persistence";

    /// <summary>The namespace of every EF Core model-configuration, migration and query helper.</summary>
    public const string EfCoreHelpersNamespace = "SharedKernel.Persistence.EfCore";

    private static readonly HashSet<string> RegistrationReceivers = new(StringComparer.Ordinal)
    {
        "IServiceCollection",
        "IHostApplicationBuilder",
        "EfCorePersistenceBuilder`1",
        "DbContextOptionsBuilder",
        "DbContextOptionsBuilder`1",
    };

    private static readonly HashSet<string> EfCoreHelperReceivers = new(StringComparer.Ordinal)
    {
        "ModelBuilder",
        "ModelConfigurationBuilder",
        "EntityTypeBuilder",
        "EntityTypeBuilder`1",
        "PropertyBuilder",
        "PropertyBuilder`1",
        "ComplexTypePropertyBuilder`1",
        "MigrationBuilder",
        "DatabaseFacade",
        "IQueryable`1",
        "DbSet`1",
    };

    /// <summary>
    /// Returns one message per public extension method of <paramref name="assemblies"/> whose declaring class is not in
    /// the namespace its receiver type requires. An empty list means the convention holds.
    /// </summary>
    /// <param name="assemblies">The persistence production assemblies.</param>
    /// <returns>The violations, empty when there are none.</returns>
    public static IReadOnlyList<string> FindMisplacedExtensions(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var violations = new List<string>();
        foreach (var type in assemblies.SelectMany(a => a.GetExportedTypes()))
        {
            if (!type.IsAbstract || !type.IsSealed || !type.IsDefined(typeof(ExtensionAttribute), false))
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!method.IsDefined(typeof(ExtensionAttribute), false))
                    continue;

                var receiver = method.GetParameters()[0].ParameterType.Name;
                var expected = RegistrationReceivers.Contains(receiver) ? RegistrationNamespace
                    : EfCoreHelperReceivers.Contains(receiver) ? EfCoreHelpersNamespace
                    : null;

                if (expected is not null && !string.Equals(type.Namespace, expected, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{type.FullName}.{method.Name}(this {receiver}) is declared in '{type.Namespace}'; "
                        + $"extensions on {receiver} belong in '{expected}'.");
                }
            }
        }

        return violations.Distinct(StringComparer.Ordinal).ToList();
    }
}
