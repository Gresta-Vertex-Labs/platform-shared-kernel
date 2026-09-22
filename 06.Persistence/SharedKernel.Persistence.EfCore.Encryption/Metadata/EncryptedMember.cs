using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

namespace SharedKernel.Persistence.EfCore.Encryption.Metadata;

/// <summary>One encrypted property as seen from one entity type, possibly nested in complex properties.</summary>
internal sealed class EncryptedMember
{
    private readonly Action<object, string?> _setter;

    public EncryptedMember(IEntityType entityType, IReadOnlyList<IComplexProperty> complexPath, IProperty property)
    {
        EntityType = entityType;
        ComplexPath = complexPath;
        Property = property;
        Purpose = (string)property.FindAnnotation(EncryptionAnnotationNames.Purpose)!.Value!;
        Path = string.Join('.', complexPath.Select(c => c.Name).Append(property.Name));
        DisplayName = $"{entityType.ShortName()}.{Path}";

        if (property.FindAnnotation(EncryptionAnnotationNames.BlindIndexNormalization)?.Value is int normalization)
        {
            BlindIndexNormalization = (BlindIndexNormalization)normalization;
            BlindIndexNormalizer = property.FindAnnotation(EncryptionAnnotationNames.BlindIndexNormalizer)?.Value as string;
            var shadowName = (string?)property.FindAnnotation(EncryptionAnnotationNames.BlindIndexProperty)?.Value
                ?? throw new InvalidOperationException($"'{DisplayName}' has a blind index but no blind-index property; the model was not finalized by field encryption.");
            BlindIndexProperty = entityType.FindProperty(shadowName)
                ?? throw new InvalidOperationException($"'{DisplayName}': blind-index property '{shadowName}' is missing.");
        }

        _setter = BuildSetter(property);
    }

    public IEntityType EntityType { get; }

    public IReadOnlyList<IComplexProperty> ComplexPath { get; }

    public IProperty Property { get; }

    public string Purpose { get; }

    /// <summary>The dotted path from the entity, e.g. <c>Billing.Line1</c>.</summary>
    public string Path { get; }

    /// <summary><c>Entity.Path</c>, for messages.</summary>
    public string DisplayName { get; }

    public IProperty? BlindIndexProperty { get; }

    public bool HasBlindIndex => BlindIndexProperty is not null;

    public BlindIndexNormalization BlindIndexNormalization { get; }

    public string? BlindIndexNormalizer { get; }

    /// <summary>The object that declares the property (the entity, or the innermost complex instance), or <see langword="null"/> when a complex value on the path is null.</summary>
    public object? GetDeclaringInstance(object entity)
    {
        var instance = entity;
        foreach (var complexProperty in ComplexPath)
        {
            instance = complexProperty.GetGetter().GetClrValue(instance);
            if (instance is null)
                return null;
        }

        return instance;
    }

    public string? GetValue(object declaringInstance) => (string?)Property.GetGetter().GetClrValue(declaringInstance);

    public void SetValue(object declaringInstance, string? value) => _setter(declaringInstance, value);

    /// <summary>The change-tracking entry of this property, or <see langword="null"/> when a complex value on the path is null.</summary>
    public PropertyEntry? GetPropertyEntry(EntityEntry entry)
    {
        if (ComplexPath.Count == 0)
            return entry.Property(Property.Name);

        var complexEntry = entry.ComplexProperty(ComplexPath[0].Name);
        if (complexEntry.CurrentValue is null)
            return null;

        for (var i = 1; i < ComplexPath.Count; i++)
        {
            complexEntry = complexEntry.ComplexProperty(ComplexPath[i].Name);
            if (complexEntry.CurrentValue is null)
                return null;
        }

        return complexEntry.Property(Property.Name);
    }

    // EF Core exposes a compiled getter publicly but not a setter; EF itself resolves the member to write through
    // (the property or its backing field), and one compiled delegate per property is cached for the model.
    private static Action<object, string?> BuildSetter(IProperty property)
    {
        var member = ((IPropertyBase)property).GetMemberInfo(forMaterialization: true, forSet: true);

        // A get-only auto-property's backing field is init-only, which an expression tree cannot assign; EF itself
        // writes such a field during materialization the same way.
        if (member is FieldInfo { IsInitOnly: true } readOnlyField)
            return (target, value) => readOnlyField.SetValue(target, value);

        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(string), "value");
        var declaringType = member.DeclaringType!;
        var target = Expression.Convert(instance, declaringType);
        Expression assign = member switch
        {
            PropertyInfo propertyInfo => Expression.Assign(Expression.Property(target, propertyInfo), value),
            FieldInfo fieldInfo => Expression.Assign(Expression.Field(target, fieldInfo), value),
            _ => throw new InvalidOperationException($"No settable member for '{property.DeclaringType.ShortName()}.{property.Name}'."),
        };

        return Expression.Lambda<Action<object, string?>>(assign, instance, value).Compile();
    }
}
