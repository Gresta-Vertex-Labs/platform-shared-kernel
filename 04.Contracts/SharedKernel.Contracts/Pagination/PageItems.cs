using System.Collections.ObjectModel;

namespace SharedKernel.Contracts.Pagination;

/// <summary>Copying, projection and value equality for the item lists the paged results carry.</summary>
internal static class PageItems
{
    public static ReadOnlyCollection<T> Snapshot<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? ReadOnlyCollection<T>.Empty : Array.AsReadOnly(items.ToArray());

    public static ReadOnlyCollection<TResult> Map<T, TResult>(IReadOnlyList<T> items, Func<T, TResult> selector)
    {
        if (items.Count == 0)
            return ReadOnlyCollection<TResult>.Empty;

        var mapped = new TResult[items.Count];
        for (var i = 0; i < items.Count; i++)
            mapped[i] = selector(items[i]);

        return Array.AsReadOnly(mapped);
    }

    public static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Count; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public static int SequenceHash<T>(IReadOnlyList<T> items)
    {
        var hash = new HashCode();
        hash.Add(items.Count);
        foreach (var item in items)
            hash.Add(item);

        return hash.ToHashCode();
    }
}
