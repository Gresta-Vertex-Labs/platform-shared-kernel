using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>
/// Loads a page of rows for a specific encrypted entity type during key rotation.
/// </summary>
/// <remarks>
/// One closed-generic implementation (<see cref="EncryptedEntityBatchProcessor{TEntity}"/>) exists
/// per encrypted entity type, registered in <see cref="EncryptedEntityBatchProcessorRegistry"/> at
/// startup. This interface allows <see cref="EncryptionRotationService{TContext}"/> to dispatch by
/// CLR type without reflection (no <c>GetMethod</c>/<c>MakeGenericMethod</c>/<c>Invoke</c> in the
/// rotation hot path).
/// </remarks>
public interface IEncryptedEntityBatchProcessor
{
    /// <summary>
    /// Loads up to <paramref name="take"/> rows, skipping the first <paramref name="skip"/>, from
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to query.</param>
    /// <param name="skip">The number of rows to skip.</param>
    /// <param name="take">The maximum number of rows to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The loaded rows boxed as <see cref="object"/>, so the caller can iterate with
    /// <c>context.Entry(entity)</c> regardless of CLR type.
    /// </returns>
    Task<List<object>> LoadBatchAsync(DbContext context, int skip, int take, CancellationToken ct);
}
