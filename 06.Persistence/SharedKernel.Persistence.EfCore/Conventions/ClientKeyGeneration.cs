using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Identifiers;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// With <c>UseUuidV7Keys()</c>: generates the primary key of every aggregate whose single-column key is a
/// <see cref="Guid"/> or a <c>StronglyTypedId&lt;Guid&gt;</c>, through the registered <see cref="IIdGenerator"/>
/// (UUID v7 by default), whenever the key is still unset when the entity is added.
/// </summary>
/// <remarks>Keys the domain assigns itself are kept; only an unset key (<see cref="Guid.Empty"/> or <see langword="null"/>) is generated.</remarks>
internal static class ClientKeyGeneration
{
    /// <summary>Configures the value generators on <paramref name="modelBuilder"/>.</summary>
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.IsOwned() || entityType.BaseType is not null || entityType.HasSharedClrType)
                continue;

            if (entityType.FindPrimaryKey() is not { Properties: [var key] })
                continue;

            var keyType = key.ClrType;
            if (keyType != typeof(Guid) && DomainTypeMappings.GetStronglyTypedIdValueType(keyType) != typeof(Guid))
                continue;

            modelBuilder.Entity(entityType.ClrType)
                .Property(key.Name)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((_, _) => new IdGeneratorValueGenerator(keyType));
        }
    }

    private sealed class IdGeneratorValueGenerator(Type keyType) : ValueGenerator
    {
        private static readonly ConcurrentDictionary<Type, Func<Guid, object>> Factories = new();
        private static readonly IIdGenerator Fallback = new UuidV7IdGenerator();

        public override bool GeneratesTemporaryValues => false;

        protected override object NextValue(EntityEntry entry)
        {
            var generator = (entry.Context as SharedKernelDbContext)?.Dependencies.KeyGenerator ?? Fallback;
            var id = generator.NewId();

            return keyType == typeof(Guid) ? id : Factories.GetOrAdd(keyType, BuildFactory)(id);
        }

        private static Func<Guid, object> BuildFactory(Type idType)
        {
            var ctor = idType.GetConstructor([typeof(Guid)])
                ?? throw new InvalidOperationException($"'{idType.Name}' has no public constructor taking a Guid.");
            var value = Expression.Parameter(typeof(Guid), "value");
            return Expression.Lambda<Func<Guid, object>>(
                Expression.Convert(Expression.New(ctor, value), typeof(object)), value).Compile();
        }
    }
}
