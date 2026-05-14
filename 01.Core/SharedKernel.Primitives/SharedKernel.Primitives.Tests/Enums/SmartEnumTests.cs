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

    // ---- Test SmartEnum definition ----
    private sealed class TestStatus : SmartEnum<TestStatus, int>
    {
        public static readonly TestStatus Active   = new(nameof(Active),   1);
        public static readonly TestStatus Inactive = new(nameof(Inactive), 2);
        public static readonly TestStatus Pending  = new(nameof(Pending),  3);

        private TestStatus(string name, int value) : base(name, value) { }
    }
}
