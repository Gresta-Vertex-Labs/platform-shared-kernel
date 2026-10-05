namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// A keyset position decoded from a cursor: the sort key and identity key of the last item on the previous page.
/// </summary>
/// <typeparam name="TKey">The type of the sort key.</typeparam>
/// <typeparam name="TId">The type of the identity key.</typeparam>
/// <param name="Key">The sort key of the last item on the previous page.</param>
/// <param name="Id">The identity key of the last item on the previous page, which breaks ties between equal keys.</param>
public readonly record struct CursorPosition<TKey, TId>(TKey Key, TId Id);
