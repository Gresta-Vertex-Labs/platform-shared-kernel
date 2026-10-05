using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharedKernel.Presentation.WebApi.Generators;

/// <summary>Why a module cannot be mapped; <see cref="None"/> when it can.</summary>
internal enum ModuleIssue
{
    None,
    Abstract,
    Generic,
    NestedInGeneric,
    Inaccessible,
    InheritsMap,
}

/// <summary>
/// One type that implements <c>IEndpointModule</c>, reduced to strings and value types so the pipeline caches it: no
/// <see cref="ISymbol"/> and no <see cref="Location"/> leave the transform.
/// </summary>
/// <param name="FullName">The fully qualified name with the <c>global::</c> alias, as it is written in generated code.</param>
/// <param name="CallsExplicitImplementation">Whether <c>Map</c> is implemented explicitly, so a plain <c>Type.Map</c> call would not compile.</param>
/// <param name="Issue">Why it cannot be mapped.</param>
/// <param name="IssueDetail">The type named by the issue, or the accessibility that makes the module unreachable.</param>
/// <param name="Location">Where the module is declared.</param>
internal sealed record EndpointModuleCandidate(
    string FullName,
    bool CallsExplicitImplementation,
    ModuleIssue Issue,
    string? IssueDetail,
    LocationInfo Location)
{
    /// <summary>The ordering key: the full name without the <c>global::</c> alias.</summary>
    public string SortKey => FullName.StartsWith("global::", StringComparison.Ordinal) ? FullName.Substring(8) : FullName;
}

/// <summary>An equatable copy of a source location.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo From(Location location) =>
        new(location.SourceTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

/// <summary>An immutable array compared by its elements, so a collected pipeline value caches.</summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Length => Items.Length;

    public bool Equals(EquatableArray<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
