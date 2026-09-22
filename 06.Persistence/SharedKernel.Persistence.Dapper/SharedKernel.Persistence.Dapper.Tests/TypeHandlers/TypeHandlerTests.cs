using System.Data;
using System.Text.Json.Serialization;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using NSubstitute;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Dapper.TypeHandlers;
using SharedKernel.Primitives.Enums;

namespace SharedKernel.Persistence.Dapper.Tests.TypeHandlers;

public sealed record TestOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static TestOrderId New() => new(Guid.NewGuid());
}

public sealed record TestSequenceId : StronglyTypedId<long>
{
    private TestSequenceId(long value) : base(value)
    {
    }

    public static TestSequenceId From(long value) => new(value);
}

public sealed class TestStatus : SmartEnum<TestStatus, int>
{
    public static readonly TestStatus Active = new(nameof(Active), 1);
    public static readonly TestStatus Inactive = new(nameof(Inactive), 2);

    private TestStatus(string name, int value) : base(name, value) { }
}

public sealed record TestAddress(string Street, string City);

[JsonSerializable(typeof(TestAddress))]
public sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>The generic Dapper type handlers and <see cref="DapperConfiguration"/>.</summary>
[Collection(DapperConfigurationCollection.Name)]
public sealed class TypeHandlerTests
{
    [Fact]
    public void StronglyTypedId_DefaultFactory_UsesThePublicConstructor()
    {
        var handler = new StronglyTypedIdTypeHandler<TestOrderId, Guid>();
        var value = Guid.NewGuid();

        handler.Parse(value).Should().Be(new TestOrderId(value));
    }

    [Fact]
    public void StronglyTypedId_WithoutAPublicConstructor_RequiresAFactory()
    {
        var act = () => new StronglyTypedIdTypeHandler<TestSequenceId, long>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*factory*");
        new StronglyTypedIdTypeHandler<TestSequenceId, long>(TestSequenceId.From).Parse(7).Value.Should().Be(7);
    }

    [Fact]
    public void StronglyTypedId_ConvertsAWiderProviderValue_AndWritesTheUnderlyingValue()
    {
        var handler = new StronglyTypedIdTypeHandler<TestSequenceId, long>(TestSequenceId.From);
        handler.Parse(7).Value.Should().Be(7L);

        var parameter = Substitute.For<IDbDataParameter>();
        handler.SetValue(parameter, TestSequenceId.From(9));
        parameter.Received(1).Value = 9L;

        handler.SetValue(parameter, null);
        parameter.Received(1).Value = DBNull.Value;
    }

    [Fact]
    public void SmartEnum_RoundTrips_AndRejectsUnknownValues()
    {
        var handler = new SmartEnumTypeHandler<TestStatus, int>();
        var parameter = Substitute.For<IDbDataParameter>();

        handler.SetValue(parameter, TestStatus.Active);
        parameter.Received(1).Value = 1;
        handler.Parse(2).Should().Be(TestStatus.Inactive);
        handler.Parse(2L).Should().Be(TestStatus.Inactive);

        var act = () => handler.Parse(99);
        act.Should().Throw<InvalidOperationException>().WithMessage("*TestStatus*99*");
    }

    [Fact]
    public void Jsonb_WritesJsonbTypedSourceGeneratedJson_AndReadsItBack()
    {
        var handler = new JsonbTypeHandler<TestAddress>(TestJsonContext.Default.TestAddress);
        var parameter = new NpgsqlParameter();

        handler.SetValue(parameter, new TestAddress("Main 1", "Izmir"));

        parameter.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
        parameter.Value.Should().Be("""{"Street":"Main 1","City":"Izmir"}""");
        handler.Parse("""{"Street":"Main 1","City":"Izmir"}""").Should().Be(new TestAddress("Main 1", "Izmir"));
    }

    [Fact]
    public void Configuration_MatchNamesWithUnderscores_IsOnByDefault_AndCanBeTurnedOff()
    {
        try
        {
            DapperConfiguration.Apply(b => b.MatchNamesWithUnderscores(false));
            global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores.Should().BeFalse();
        }
        finally
        {
            DapperConfiguration.Apply();
        }

        global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores.Should().BeTrue();
    }
}
