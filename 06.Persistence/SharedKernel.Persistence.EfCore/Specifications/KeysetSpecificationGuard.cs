using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// Rejects a <see cref="KeysetSpecification{T, TKey}"/> passed to a repository method that does not
/// honor its cursor.
/// </summary>
/// <remarks>
/// <c>ISpecificationEvaluator{T}.GetQuery</c> silently ignores
/// <c>KeysetSpecification{T,TKey}.AfterKey</c>/<c>AfterId</c> and always returns the first page — by
/// design, only <c>GetKeysetQuery</c> honors the cursor. Passing a keyset specification to
/// <c>GetBySpecAsync</c>, <c>ListAsync</c>, <c>CountAsync</c>, or <c>AnyAsync</c> previously compiled
/// and ran without error, silently returning the wrong page. This guard makes that a loud,
/// immediate <see cref="InvalidOperationException"/> instead.
/// </remarks>
internal static class KeysetSpecificationGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="spec"/> is (or derives
    /// from) a closed <see cref="KeysetSpecification{T, TKey}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="spec">The specification to check.</param>
    /// <param name="methodName">The calling repository method's name, for the exception message.</param>
    public static void EnsureNotKeyset<T>(ISpecification<T> spec, string methodName)
    {
        for (var type = spec.GetType(); type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeysetSpecification<,>))
            {
                throw new InvalidOperationException(
                    $"'{spec.GetType().Name}' is a KeysetSpecification<T,TKey> — its cursor " +
                    $"(AfterKey/AfterId) is silently ignored by '{methodName}'. Use " +
                    "'ListKeysetAsync'/'ListKeysetProjectedAsync' instead, the only methods that " +
                    "honor the cursor.");
            }
        }
    }
}
