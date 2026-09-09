using SharedKernel.Primitives.Enums;
using Xunit;

namespace SharedKernel.Primitives.Tests.Enums;

public sealed class SmartEnumTests
{
    [Fact]
    public void List_ContainsAllDeclaredMembers()
    {
        var list = TestStatus.List;
        Assert.Equal(3, list.Count);
        Assert.Contains(TestStatus.Active, list);
        Assert.Contains(TestStatus.Inactive, list);
        Assert.Contains(TestStatus.Pending, list);
    }

    [Fact]
    public void FromValue_Hit_ReturnsCorrectMember()
    {
        var result = TestStatus.FromValue(1);
        Assert.Equal(TestStatus.Active, result);
    }

    [Fact]
    public void FromValue_Miss_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TestStatus.FromValue(999));
    }

    [Fact]
    public void TryFromValue_Hit_ReturnsTrueAndMember()
    {
        var found = TestStatus.TryFromValue(2, out var member);
        Assert.True(found);
        Assert.Equal(TestStatus.Inactive, member);
    }

    [Fact]
    public void TryFromValue_Miss_ReturnsFalseAndNull()
    {
        var found = TestStatus.TryFromValue(999, out var member);
        Assert.False(found);
        Assert.Null(member);
    }

    [Fact]
    public void FromName_Hit_ReturnsCorrectMember()
    {
        var result = TestStatus.FromName("Pending");
        Assert.Equal(TestStatus.Pending, result);
    }

    [Fact]
    public void FromName_Miss_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TestStatus.FromName("DoesNotExist"));
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        Assert.Equal("Active", TestStatus.Active.ToString());
    }

    [Fact]
    public void Name_AndValue_AreCorrect()
    {
        Assert.Equal("Inactive", TestStatus.Inactive.Name);
        Assert.Equal(2, TestStatus.Inactive.Value);
    }

    // ---- SK.01.P515: SmartEnum static-initialization trap regression coverage ----

    [Fact]
    public void FromValue_FirstTouchThroughInheritedStaticMember_ResolvesCorrectly()
    {
        // Regression test for SK.01.P515. `FirstTouchOnlyEnumForFromValue.FromValue(...)`
        // resolves, at the CLR level, to the INHERITED SmartEnum<FirstTouchOnlyEnumForFromValue,int>.FromValue
        // — a member physically declared on the base closed generic type, not on
        // FirstTouchOnlyEnumForFromValue itself. Reaching it here, as the very first touch of this
        // type anywhere in the process, does NOT by itself guarantee
        // FirstTouchOnlyEnumForFromValue's own static constructor (the one whose field initializers
        // populate `_list` via the base instance constructor) has already run — that is exactly the
        // trap this phase's fix (a force-cctor field on SmartEnum<TEnum,TValue> itself) closes.
        // Before the fix, this exact call shape could observe an empty `_byValue` dictionary built
        // and cached forever against an empty `_list`.
        var member = FirstTouchOnlyEnumForFromValue.FromValue(10);

        Assert.Equal("Alpha", member.Name);
    }

    [Fact]
    public void TryFromValue_FirstTouchThroughInheritedStaticMember_ResolvesCorrectly()
    {
        // Same trap as above, exercised via TryFromValue instead of FromValue — a distinct
        // one-shot Lazy<Dictionary<...>> field (_byValue) with no invalidation hook of its own.
        var found = FirstTouchOnlyEnumForTryFromValue.TryFromValue(21, out var member);

        Assert.True(found);
        Assert.Equal("Beta", member!.Name);
    }

    [Fact]
    public void FromName_FirstTouchThroughInheritedStaticMember_ResolvesCorrectly()
    {
        // Same trap as above, exercised via FromName — the _byName Lazy<Dictionary<...>> field.
        var member = FirstTouchOnlyEnumForFromName.FromName("Gamma");

        Assert.Equal(32, member.Value);
    }

    [Fact]
    public void List_FirstTouchThroughInheritedStaticMember_ContainsAllDeclaredMembers()
    {
        // List's own _readOnlyList ??= _list.AsReadOnly() self-heals once the type's cctor
        // eventually runs (every instance constructor resets _readOnlyList to null), so this
        // case was never actually vulnerable to the trap the way FromValue/TryFromValue/FromName
        // are — but it is included per T-83's own acceptance criteria (List named as one of the
        // four inherited static members to prove) and to lock in that self-healing behavior as a
        // regression guard in its own right.
        var members = FirstTouchOnlyEnumForList.List;

        Assert.Equal(3, members.Count);
        Assert.Contains(members, m => m.Name == "Uno");
        Assert.Contains(members, m => m.Name == "Dos");
        Assert.Contains(members, m => m.Name == "Tres");
    }

    // Each of the four tests above gets its OWN dedicated, never-elsewhere-referenced SmartEnum
    // type. This is deliberate: a shared type touched by more than one test would let whichever
    // test runs first "warm" the type's cctor for every subsequent test, defeating the entire
    // point of proving first-touch-via-inherited-static-member behavior. None of these types'
    // named static instances (Alpha/Beta/Gamma/Uno/Dos/Tres) are ever referenced by the test
    // bodies above — only the inherited static members (FromValue/TryFromValue/FromName/List) are.

    private sealed class FirstTouchOnlyEnumForFromValue : SmartEnum<FirstTouchOnlyEnumForFromValue, int>
    {
        public static readonly FirstTouchOnlyEnumForFromValue Alpha = new(nameof(Alpha), 10);
        public static readonly FirstTouchOnlyEnumForFromValue Beta  = new(nameof(Beta),  11);
        public static readonly FirstTouchOnlyEnumForFromValue Gamma = new(nameof(Gamma), 12);

        private FirstTouchOnlyEnumForFromValue(string name, int value) : base(name, value) { }
    }

    private sealed class FirstTouchOnlyEnumForTryFromValue : SmartEnum<FirstTouchOnlyEnumForTryFromValue, int>
    {
        public static readonly FirstTouchOnlyEnumForTryFromValue Alpha = new(nameof(Alpha), 20);
        public static readonly FirstTouchOnlyEnumForTryFromValue Beta  = new(nameof(Beta),  21);
        public static readonly FirstTouchOnlyEnumForTryFromValue Gamma = new(nameof(Gamma), 22);

        private FirstTouchOnlyEnumForTryFromValue(string name, int value) : base(name, value) { }
    }

    private sealed class FirstTouchOnlyEnumForFromName : SmartEnum<FirstTouchOnlyEnumForFromName, int>
    {
        public static readonly FirstTouchOnlyEnumForFromName Alpha = new(nameof(Alpha), 30);
        public static readonly FirstTouchOnlyEnumForFromName Beta  = new(nameof(Beta),  31);
        public static readonly FirstTouchOnlyEnumForFromName Gamma = new(nameof(Gamma), 32);

        private FirstTouchOnlyEnumForFromName(string name, int value) : base(name, value) { }
    }

    private sealed class FirstTouchOnlyEnumForList : SmartEnum<FirstTouchOnlyEnumForList, int>
    {
        public static readonly FirstTouchOnlyEnumForList Uno  = new(nameof(Uno),  1);
        public static readonly FirstTouchOnlyEnumForList Dos  = new(nameof(Dos),  2);
        public static readonly FirstTouchOnlyEnumForList Tres = new(nameof(Tres), 3);

        private FirstTouchOnlyEnumForList(string name, int value) : base(name, value) { }
    }

    // ---- Test SmartEnum definition ----
    private sealed class TestStatus : SmartEnum<TestStatus, int>
    {
        public static readonly TestStatus Active   = new(nameof(Active),   1);
        public static readonly TestStatus Inactive = new(nameof(Inactive), 2);
        public static readonly TestStatus Pending  = new(nameof(Pending),  3);

        private TestStatus(string name, int value) : base(name, value) { }
    }
}
