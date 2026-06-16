using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>
/// Closed-generic <see cref="IEncryptedEntityBatchProcessor"/> for a specific encrypted entity type
/// <typeparamref name="TEntity"/>.
/// </summary>
/// <typeparam name="TEntity">The encrypted entity's CLR type.</typeparam>
/// <remarks>
/// <c>context.Set&lt;TEntity&gt;()</c> is an ordinary generic method call — not a reflection
/// invocation. Returns <see cref="List{Object}"/> so the caller
/// (<see cref="EncryptionRotationService{TContext}"/>) can iterate with <c>context.Entry(entity)</c>
/// regardless of CLR type.
/// </remarks>
public sealed class EncryptedEntityBatchProcessor<TEntity> : IEncryptedEntityBatchProcessor
    where TEntity : class
{
    /// <inheritdoc />
    public async Task<List<object>> LoadBatchAsync(DbContext context, int skip, int take, CancellationToken ct)
    {
        var rows = await context.Set<TEntity>().Skip(skip).Take(take).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }
}
