using System.Threading;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Encryption;

namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>
/// Registry of <see cref="IEncryptedEntityBatchProcessor"/> instances, one per encrypted entity
/// CLR type, keyed by <see cref="Type"/>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type whose model is inspected.</typeparam>
/// <remarks>
/// <para>
/// Populated lazily on first use from <typeparamref name="TContext"/>'s EF Core model: for each
/// non-owned entity type carrying at least one <c>"SharedKernel:Encrypt"</c>-annotated property
/// (the same discovery query <see cref="EncryptionRotationService{TContext}"/> performs), a
/// <c>new EncryptedEntityBatchProcessor&lt;TEntity&gt;()</c> is constructed via
/// <c>Activator.CreateInstance(typeof(EncryptedEntityBatchProcessor&lt;&gt;).MakeGenericType(clrType))</c>.
/// </para>
/// <para>
/// This is the documented, justified, model-build-time exception to the SK0xxx
/// <c>MakeGenericMethod</c>/<c>Invoke</c> reflection rule — the same class of exception as
/// <c>ValueObjectOwnershipBuilder</c>'s startup-time model scan. The set of entity types is
/// enumerable from <c>context.Model</c> (not arbitrary user input),
/// <c>EncryptedEntityBatchProcessor&lt;TEntity&gt;</c> has a parameterless constructor, and
/// construction happens once per entity type, not per rotation call.
/// </para>
/// <para>
/// Registered as a singleton; immutable after the first (lazy, thread-safe) population.
/// </para>
/// </remarks>
public sealed class EncryptedEntityBatchProcessorRegistry<TContext>
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly Lock _lock = new();
    private Dictionary<Type, IEncryptedEntityBatchProcessor>? _processors;

    /// <summary>
    /// Initialises a new <see cref="EncryptedEntityBatchProcessorRegistry{TContext}"/>.
    /// </summary>
    /// <param name="contextFactory">Factory used to create a throwaway context for model inspection.</param>
    public EncryptedEntityBatchProcessorRegistry(IDbContextFactory<TContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Attempts to resolve the <see cref="IEncryptedEntityBatchProcessor"/> registered for
    /// <paramref name="clrType"/>.
    /// </summary>
    /// <param name="clrType">The encrypted entity's CLR type.</param>
    /// <param name="processor">The matching processor, or <see langword="null"/> if not found.</param>
    /// <returns><see langword="true"/> if a processor was found; otherwise <see langword="false"/>.</returns>
    public bool TryGet(Type clrType, out IEncryptedEntityBatchProcessor? processor)
    {
        var processors = EnsurePopulated();
        return processors.TryGetValue(clrType, out processor);
    }

    private Dictionary<Type, IEncryptedEntityBatchProcessor> EnsurePopulated()
    {
        if (_processors is not null)
            return _processors;

        lock (_lock)
        {
            if (_processors is not null)
                return _processors;

            using var context = _contextFactory.CreateDbContext();
            var processors = new Dictionary<Type, IEncryptedEntityBatchProcessor>();

            foreach (var entityType in context.Model.GetEntityTypes())
            {
                if (entityType.IsOwned())
                    continue;

                var hasEncryptedProperty = entityType.GetProperties()
                    .Any(p => p.FindAnnotation(PropertyBuilderEncryptExtensions.AnnotationKey)?.Value is true
                              && p.ClrType == typeof(string));

                if (!hasEncryptedProperty || entityType.ClrType is null)
                    continue;

                var processorType = typeof(EncryptedEntityBatchProcessor<>).MakeGenericType(entityType.ClrType);
                var processor = (IEncryptedEntityBatchProcessor)Activator.CreateInstance(processorType)!;
                processors[entityType.ClrType] = processor;
            }

            _processors = processors;
            return _processors;
        }
    }
}
