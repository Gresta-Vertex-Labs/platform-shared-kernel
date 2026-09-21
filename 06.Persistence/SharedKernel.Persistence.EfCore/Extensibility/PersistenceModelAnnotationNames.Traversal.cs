using System.ComponentModel;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SharedKernel.Persistence.EfCore.Extensibility;

internal static partial class PersistenceModelAnnotationNames
{
    /// <summary>
    /// Enumerates every scalar property of <paramref name="type"/>, including the properties reachable through
    /// complex properties at any depth (nested value objects and complex collections), paired with the chain of
    /// complex properties that leads to it.
    /// </summary>
    /// <param name="type">An entity type or complex type, from a mutable, convention or runtime model.</param>
    /// <returns>
    /// One entry per property. <c>ComplexPath</c> is empty for a property declared directly on
    /// <paramref name="type"/>; otherwise it lists the complex properties from the outermost to the one declaring
    /// the property.
    /// </returns>
    /// <remarks>
    /// The one traversal shared by the core package's <c>EncryptAnnotationRegisteredGuardConvention</c> and every
    /// consumer in <c>SharedKernel.Persistence.EfCore.Encryption</c> (model convention, save/materialization
    /// interceptor, query guard, maintenance job), so no component can see a shape another one misses.
    /// Inherited properties are included, as <see cref="IReadOnlyTypeBase.GetProperties"/> includes them.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IEnumerable<(IReadOnlyList<IReadOnlyComplexProperty> ComplexPath, IReadOnlyProperty Property)> GetPropertiesIncludingComplex(
        IReadOnlyTypeBase type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Walk(type, []);

        static IEnumerable<(IReadOnlyList<IReadOnlyComplexProperty>, IReadOnlyProperty)> Walk(
            IReadOnlyTypeBase current, IReadOnlyComplexProperty[] path)
        {
            foreach (var property in current.GetProperties())
                yield return (path, property);

            foreach (var complexProperty in current.GetComplexProperties())
            {
                IReadOnlyComplexProperty[] childPath = [.. path, complexProperty];
                foreach (var entry in Walk(complexProperty.ComplexType, childPath))
                    yield return entry;
            }
        }
    }
}
