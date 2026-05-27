using FluentAssertions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-26: P-053/WO-011 — DomainEventVersionAttribute and DomainEventVersionHelper tests.
/// </summary>
public class DomainEventVersionTests
{
    // --- Version 1 implicit (no attribute) ---

    private sealed record UnversionedEvent : DomainEvent
    {
        public string Data { get; init; } = string.Empty;
    }

    // --- Version 2 explicit ---

    [DomainEventVersion(2)]
    private sealed record EventV2 : DomainEvent
    {
        public string Data { get; init; } = string.Empty;
    }

    // --- Version 3 explicit ---

    [DomainEventVersion(3)]
    private sealed record EventV3 : DomainEvent
    {
        public string DataV3 { get; init; } = string.Empty;
    }

    [Fact]
    public void GetVersion_WithoutAttribute_Returns1()
    {
        var version = DomainEventVersionHelper.GetVersion(typeof(UnversionedEvent));

        version.Should().Be(1, "version 1 is the implicit default when attribute is absent");
    }

    [Fact]
    public void GetVersion_WithVersionAttribute2_Returns2()
    {
        var version = DomainEventVersionHelper.GetVersion(typeof(EventV2));

        version.Should().Be(2);
    }

    [Fact]
    public void GetVersion_WithVersionAttribute3_Returns3()
    {
        var version = DomainEventVersionHelper.GetVersion(typeof(EventV3));

        version.Should().Be(3);
    }

    [Fact]
    public void DomainEventVersionAttribute_VersionLessThan1_Throws()
    {
        var act = () => new DomainEventVersionAttribute(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("version");
    }

    [Fact]
    public void DomainEventVersionAttribute_NegativeVersion_Throws()
    {
        var act = () => new DomainEventVersionAttribute(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DomainEventVersionAttribute_Version1_IsValid()
    {
        var attr = new DomainEventVersionAttribute(1);
        attr.Version.Should().Be(1);
    }

    [Fact]
    public void DomainEventVersionAttribute_IsNonInherited()
    {
        var usage = typeof(DomainEventVersionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.Inherited.Should().BeFalse();
    }
}
