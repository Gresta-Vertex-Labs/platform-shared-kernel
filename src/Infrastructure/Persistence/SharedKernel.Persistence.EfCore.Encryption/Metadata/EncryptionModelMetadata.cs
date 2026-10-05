using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption.Metadata;

/// <summary>Every encrypted property of a finalized model, computed once per model and cached.</summary>
/// <remarks>
/// Built with <see cref="PersistenceModelAnnotationNames.GetPropertiesIncludingComplex"/>, the traversal the model
/// convention and the core guard convention use, so the interceptor, the query guard and the maintenance job see
/// exactly the properties the convention validated.
/// </remarks>
internal sealed class EncryptionModelMetadata
{
    private static readonly ConditionalWeakTable<IModel, EncryptionModelMetadata> Cache = new();

    private readonly Dictionary<IReadOnlyEntityType, EncryptedMember[]> _byEntityType = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(Module Module, int Token), (string Description, bool IsContainer)> _guardedClrMembers = [];
    private readonly Dictionary<(IReadOnlyEntityType EntityType, string Name), string> _guardedNames = [];

    private EncryptionModelMetadata(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var members = new List<EncryptedMember>();
            foreach (var (complexPath, property) in PersistenceModelAnnotationNames.GetPropertiesIncludingComplex(entityType))
            {
                if (property.FindAnnotation(EncryptionAnnotationNames.Purpose) is null)
                    continue;

                var member = new EncryptedMember(entityType, [.. complexPath.Cast<IComplexProperty>()], (IProperty)property);
                members.Add(member);

                Guard(member.Property, $"'{member.DisplayName}' is encrypted");
                if (complexPath.Count == 0)
                    _guardedNames[(entityType, property.Name)] = $"'{member.DisplayName}' is encrypted";
                else
                    _guardedNames[(entityType, complexPath[0].Name)] = $"'{entityType.ShortName()}.{complexPath[0].Name}' contains encrypted '{member.DisplayName}'";

                foreach (var container in complexPath)
                    Guard((IPropertyBase)container, $"'{container.DeclaringType.ShortName()}.{container.Name}' contains encrypted '{member.DisplayName}'", containerOnly: true);
            }

            if (members.Count > 0)
            {
                _byEntityType[entityType] = [.. members];
                IsTenantedByEntityType[entityType] = typeof(IHasTenant).IsAssignableFrom(entityType.ClrType);
            }
        }
    }

    /// <summary>Whether the model has any encrypted property.</summary>
    public bool HasEncryptedMembers => _byEntityType.Count > 0;

    public Dictionary<IReadOnlyEntityType, bool> IsTenantedByEntityType { get; } = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyCollection<EncryptedMember[]> AllMembersByEntityType => _byEntityType.Values;

    public static EncryptionModelMetadata For(IModel model) => Cache.GetValue(model, static m => new EncryptionModelMetadata(m));

    /// <summary>The encrypted properties of <paramref name="entityType"/>, inherited ones included.</summary>
    public IReadOnlyList<EncryptedMember> GetMembers(IReadOnlyEntityType entityType) =>
        _byEntityType.TryGetValue(entityType, out var members) ? members : [];

    /// <summary>Finds the encrypted property at <paramref name="path"/> (dotted, from the entity).</summary>
    public EncryptedMember? FindMember(IReadOnlyEntityType entityType, string path) =>
        GetMembers(entityType).FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.Ordinal));

    /// <summary>Why a CLR member must not appear in a query, or <see langword="null"/> when it may.</summary>
    public (string Description, bool IsContainer)? DescribeGuardedMember(MemberInfo member) =>
        _guardedClrMembers.TryGetValue((member.Module, member.MetadataToken), out var guarded) ? guarded : null;

    /// <summary>Why <c>EF.Property(x, name)</c> on <paramref name="entityType"/> must not appear in a query, or <see langword="null"/>.</summary>
    public string? DescribeGuardedName(IReadOnlyEntityType entityType, string name)
    {
        for (var type = entityType; type is not null; type = type.BaseType)
        {
            if (_guardedNames.TryGetValue((type, name), out var description))
                return description;
        }

        foreach (var derived in entityType.GetDerivedTypes())
        {
            if (_guardedNames.TryGetValue((derived, name), out var description))
                return description;
        }

        return null;
    }

    private void Guard(IPropertyBase property, string description, bool containerOnly = false)
    {
        MemberInfo? member = property.PropertyInfo ?? (MemberInfo?)property.FieldInfo;
        if (member is null)
            return;

        var key = (member.Module, member.MetadataToken);
        if (containerOnly && _guardedClrMembers.ContainsKey(key))
            return;

        _guardedClrMembers[key] = (description, containerOnly);
    }
}
