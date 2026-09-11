using SharedKernel.Primitives.Enums;
using Xunit;

namespace SharedKernel.Primitives.Tests.Enums;

/// <summary>
/// Covers <see cref="SmartEnum{TEnum, TValue}"/> ordering, argument handling, and the
/// duplicate-member diagnostic.
/// </summary>
/// <remarks>
/// Each enum type below is used by exactly one test, on purpose. The lookup dictionaries are
/// per-closed-generic-type and built once, so a type whose first lookup is expected to throw
/// cannot be shared with a test that expects a successful lookup — the outcome would then depend
/// on test execution order.
/// </remarks>
public sealed class SmartEnumOrderingAndDiagnosticsTests
{
    // ---- Ordering ----

    [Fact]
    public void Sort_OrdersByUnderlyingValue()
    {
        // Previously threw InvalidOperationException ("Failed to compare two elements in the
        // array") because the base type implemented no IComparable at all.
        var members = new List<OrderedEnum>
        {
            OrderedEnum.Third,
            OrderedEnum.First,
            OrderedEnum.Second,
        };

        members.Sort();

        Assert.Equal([OrderedEnum.First, OrderedEnum.Second, OrderedEnum.Third], members);
    }

    [Fact]
    public void CompareTo_ReflectsValueOrder()
    {
        Assert.True(OrderedEnum.First.CompareTo(OrderedEnum.Second) < 0);
        Assert.True(OrderedEnum.Second.CompareTo(OrderedEnum.First) > 0);
        Assert.Equal(0, OrderedEnum.First.CompareTo(OrderedEnum.First));
    }

    [Fact]
    public void CompareTo_Null_SortsNullFirst()
    {
        Assert.True(OrderedEnum.First.CompareTo(null) > 0);
    }

    [Fact]
    public void CompareTo_Object_Null_SortsNullFirst()
    {
        Assert.True(OrderedEnum.First.CompareTo((object?)null) > 0);
    }

    [Fact]
    public void CompareTo_Object_WrongType_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => OrderedEnum.First.CompareTo("not an OrderedEnum")
        );

        Assert.Contains(nameof(OrderedEnum), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderBy_Works_ViaDefaultComparer()
    {
        // Confirms Comparer<T>.Default picks up IComparable<TEnum> through the generic base, which
        // is what every LINQ ordering operator and SortedSet<T> rely on.
        var ordered = new[] { OrderedEnum.Third, OrderedEnum.First }.OrderBy(member => member);

        Assert.Equal([OrderedEnum.First, OrderedEnum.Third], ordered);
    }

    // ---- Null argument handling ----

    [Fact]
    public void TryFromValue_Null_ReturnsFalseAndDoesNotThrow()
    {
        // A Try-shaped member must never throw for a missing key. This previously leaked
        // ArgumentNullException ("Parameter: key") straight out of Dictionary internals.
        var found = NullableValueEnum.TryFromValue(null, out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void TryFromName_Null_ReturnsFalseAndDoesNotThrow()
    {
        var found = NullableValueEnum.TryFromName(null, out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void FromValue_Null_ThrowsArgumentNullNamingTheParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => NullableValueEnum.FromValue(null!)
        );

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void FromName_Null_ThrowsArgumentNullNamingTheParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => NullableValueEnum.FromName(null!)
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void TryFromValue_KnownValue_StillResolves()
    {
        Assert.True(NullableValueEnum.TryFromValue("alpha", out var result));
        Assert.Same(NullableValueEnum.Alpha, result);
    }

    [Fact]
    public void TryFromName_KnownName_StillResolves()
    {
        Assert.True(NullableValueEnum.TryFromName(nameof(NullableValueEnum.Alpha), out var result));
        Assert.Same(NullableValueEnum.Alpha, result);
    }

    // ---- Duplicate-member diagnostics ----

    [Fact]
    public void DuplicateValue_ThrowsNamingTypeKeyAndBothMembers()
    {
        // Previously surfaced as ArgumentException "An item with the same key has already been
        // added. Key: 1" from inside a Lazy — no type, no member names, no hint that a SmartEnum
        // declaration was at fault.
        var exception = Assert.Throws<InvalidOperationException>(
            () => DuplicateValueEnum.FromValue(1)
        );

        Assert.Contains(nameof(DuplicateValueEnum), exception.Message, StringComparison.Ordinal);
        Assert.Contains("value", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DuplicateValueEnum.First), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DuplicateValueEnum.Second), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateName_ThrowsNamingTypeAndKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DuplicateNameEnum.FromName("Same")
        );

        Assert.Contains(nameof(DuplicateNameEnum), exception.Message, StringComparison.Ordinal);
        Assert.Contains("name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownValue_ThrowsNamingTheType()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => UnknownLookupEnum.FromValue(999)
        );

        Assert.Contains(nameof(UnknownLookupEnum), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownName_ThrowsNamingTheType()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => UnknownLookupEnum.FromName("Absent")
        );

        Assert.Contains(nameof(UnknownLookupEnum), exception.Message, StringComparison.Ordinal);
    }

    // ---- Fixtures ----

    private sealed class OrderedEnum : SmartEnum<OrderedEnum, int>
    {
        public static readonly OrderedEnum First = new(nameof(First), 1);
        public static readonly OrderedEnum Second = new(nameof(Second), 2);
        public static readonly OrderedEnum Third = new(nameof(Third), 3);

        private OrderedEnum(string name, int value)
            : base(name, value) { }
    }

    private sealed class NullableValueEnum : SmartEnum<NullableValueEnum, string>
    {
        public static readonly NullableValueEnum Alpha = new(nameof(Alpha), "alpha");

        private NullableValueEnum(string name, string value)
            : base(name, value) { }
    }

    private sealed class DuplicateValueEnum : SmartEnum<DuplicateValueEnum, int>
    {
        public static readonly DuplicateValueEnum First = new(nameof(First), 1);
        public static readonly DuplicateValueEnum Second = new(nameof(Second), 1);

        private DuplicateValueEnum(string name, int value)
            : base(name, value) { }
    }

    private sealed class DuplicateNameEnum : SmartEnum<DuplicateNameEnum, int>
    {
        public static readonly DuplicateNameEnum One = new("Same", 1);
        public static readonly DuplicateNameEnum Two = new("Same", 2);

        private DuplicateNameEnum(string name, int value)
            : base(name, value) { }
    }

    private sealed class UnknownLookupEnum : SmartEnum<UnknownLookupEnum, int>
    {
        public static readonly UnknownLookupEnum Only = new(nameof(Only), 1);

        private UnknownLookupEnum(string name, int value)
            : base(name, value) { }
    }
}
