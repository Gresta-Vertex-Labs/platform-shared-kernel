using System.Data;
using Dapper;
using FluentAssertions;
using NSubstitute;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Dapper.TypeHandlers;
using SharedKernel.Primitives.Enums;

namespace SharedKernel.Persistence.Dapper.Tests.TypeHandlers;

// ---------------------------------------------------------------------------
// Test strongly-typed ID
// ---------------------------------------------------------------------------

public sealed record TestOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static TestOrderId New() => new(Guid.NewGuid());
}

public sealed class TestOrderIdHandler : StronglyTypedIdTypeHandler<TestOrderId, Guid>
{
    protected override TestOrderId FromValue(Guid value) => new(value);
}

// ---------------------------------------------------------------------------
// Test SmartEnum
// ---------------------------------------------------------------------------

public sealed class TestStatus : SmartEnum<TestStatus, int>
{
    public static readonly TestStatus Active = new(nameof(Active), 1);
    public static readonly TestStatus Inactive = new(nameof(Inactive), 2);

    private TestStatus(string name, int value) : base(name, value) { }
}

public sealed class TestStatusHandler : SmartEnumTypeHandler<TestStatus, int> { }

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

/// <summary>
/// T-39(5-6): StronglyTypedIdTypeHandler and SmartEnumTypeHandler unit tests.
/// </summary>
public sealed class TypeHandlerTests
{
    // -----------------------------------------------------------------------
    // StronglyTypedIdTypeHandler
    // -----------------------------------------------------------------------

    [Fact]
    public void StronglyTypedIdTypeHandler_SetValue_Writes_UnderlyingValue()
    {
        var handler = new TestOrderIdHandler();
        var parameter = Substitute.For<IDbDataParameter>();
        var id = new TestOrderId(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

        handler.SetValue(parameter, id);

        parameter.Received(1).Value = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    [Fact]
    public void StronglyTypedIdTypeHandler_Parse_Returns_CorrectId()
    {
        var handler = new TestOrderIdHandler();
        var expectedGuid = Guid.NewGuid();

        var result = handler.Parse(expectedGuid);

        result.Should().BeOfType<TestOrderId>();
        result.Value.Should().Be(expectedGuid);
    }

    // -----------------------------------------------------------------------
    // SmartEnumTypeHandler
    // -----------------------------------------------------------------------

    [Fact]
    public void SmartEnumTypeHandler_SetValue_Writes_UnderlyingValue()
    {
        var handler = new TestStatusHandler();
        var parameter = Substitute.For<IDbDataParameter>();

        handler.SetValue(parameter, TestStatus.Active);

        parameter.Received(1).Value = (object)1;
    }

    [Fact]
    public void SmartEnumTypeHandler_Parse_Returns_CorrectMember()
    {
        var handler = new TestStatusHandler();

        var result = handler.Parse(2);

        result.Should().NotBeNull();
        result.Name.Should().Be("Inactive");
        result.Value.Should().Be(2);
    }

    [Fact]
    public void SmartEnumTypeHandler_Parse_UnknownValue_Throws()
    {
        var handler = new TestStatusHandler();

        var act = () => handler.Parse(99);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestStatus*99*");
    }

    // -----------------------------------------------------------------------
    // DapperTypeHandlers
    // -----------------------------------------------------------------------

    [Fact]
    public void DapperTypeHandlers_Register_IsIdempotent()
    {
        // Should not throw on repeated calls
        DapperTypeHandlers.Register();
        DapperTypeHandlers.Register();
        DapperTypeHandlers.Register();
    }
}
