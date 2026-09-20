using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>Equality lookup helper for an encrypted property that opted into <c>.WithBlindIndex()</c>.</summary>
/// <remarks>
/// <para>
/// Querying an encrypted property's own value with a plain <c>.Where(x =&gt; x.Email == "...")</c> compiles, but
/// always returns zero rows — the column holds ciphertext, and <c>EncryptionInterceptor</c> only decrypts on full
/// entity materialization, never inside a translated SQL predicate. <see cref="EncryptedColumnEqualityGuardInterceptor"/>
/// detects the common shape of this mistake and throws instead of silently returning an empty result; this
/// extension is the supported alternative.
/// </para>
/// </remarks>
public static class EncryptedPropertyQueryExtensions
{
    /// <summary>Filters to rows whose blind-indexed encrypted property equals <paramref name="value"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The query to filter.</param>
    /// <param name="blindIndexService">The same <see cref="IBlindIndexService"/> the model's <c>.WithEncryption()</c> registered.</param>
    /// <param name="property">The encrypted, blind-indexed property, e.g. <c>x =&gt; x.Email</c>.</param>
    /// <param name="purpose">The exact purpose string passed to <c>.Encrypt(purpose)</c> for <paramref name="property"/>.</param>
    /// <param name="value">The plaintext value to look up.</param>
    /// <param name="normalize">
    /// The exact normalization delegate passed to <c>.WithBlindIndex(normalize)</c> for <paramref name="property"/>,
    /// or <see langword="null"/> if none was supplied. Must match, or the computed blind index will not equal what
    /// was stored.
    /// </param>
    /// <param name="tenantId">
    /// The tenant id, when the declaring entity implements <see cref="SharedKernel.Domain.Abstractions.IHasTenant"/>;
    /// otherwise <see langword="null"/>. The blind index is tenant-bound for every tenanted entity, regardless of
    /// whether the property also opted into <c>perTenantKey</c> (which affects only key derivation) — passing the
    /// wrong tenant id (or omitting it for a tenanted entity) computes a blind index that will not match.
    /// </param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="ArgumentException"><paramref name="property"/> is not a simple member-access expression.</exception>
    public static IQueryable<T> WhereBlindIndexEquals<T>(
        this IQueryable<T> query,
        IBlindIndexService blindIndexService,
        Expression<Func<T, string>> property,
        string purpose,
        string value,
        Func<string, string>? normalize = null,
        Guid? tenantId = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(blindIndexService);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(value);

        var shadowPropertyName = GetPropertyName(property) + "BlindIndex";
        var normalized = normalize is null ? value : normalize(value);
        var blindIndex = blindIndexService.Compute(purpose, normalized, tenantId);

        var parameter = property.Parameters[0];
        var shadowAccess = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(string)],
            parameter,
            Expression.Constant(shadowPropertyName));
        var predicate = Expression.Lambda<Func<T, bool>>(
            Expression.Equal(shadowAccess, Expression.Constant(blindIndex)),
            parameter);

        return query.Where(predicate);
    }

    private static string GetPropertyName<T>(Expression<Func<T, string>> property)
    {
        if (property.Body is MemberExpression { Member.Name: { } name })
            return name;

        throw new ArgumentException(
            $"'{nameof(property)}' must be a simple property access, e.g. 'x => x.Email'.", nameof(property));
    }
}
