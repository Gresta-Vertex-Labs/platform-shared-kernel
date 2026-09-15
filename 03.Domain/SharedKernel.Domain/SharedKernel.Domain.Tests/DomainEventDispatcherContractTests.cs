using System.Reflection;
using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-29 — ContractShapeTests for IDomainEventDispatcher (P-081/WO-014).
/// Verifies the interface shape via reflection without requiring an implementation.
/// </summary>
public class DomainEventDispatcherContractTests
{
    private static readonly Assembly DomainAssembly =
        typeof(IDomainEventDispatcher).Assembly;

    // (1) Interface exists in the SharedKernel.Domain assembly under namespace SharedKernel.Domain

    [Fact]
    public void IDomainEventDispatcher_ExistsInAssembly()
    {
        var type = DomainAssembly.GetType("SharedKernel.Domain.Abstractions.IDomainEventDispatcher");
        type.Should().NotBeNull("IDomainEventDispatcher must be exported from SharedKernel.Domain");
    }

    [Fact]
    public void IDomainEventDispatcher_IsInTheAbstractionsNamespace()
    {
        typeof(IDomainEventDispatcher).Namespace.Should().Be("SharedKernel.Domain.Abstractions");
    }

    // (2) Has exactly one method: DispatchAsync

    [Fact]
    public void IDomainEventDispatcher_HasExactlyOneMethod()
    {
        var methods = typeof(IDomainEventDispatcher).GetMethods();
        methods.Should().HaveCount(1, "IDomainEventDispatcher defines a single method: DispatchAsync");
        methods[0].Name.Should().Be("DispatchAsync");
    }

    // (3) Method signature: Task DispatchAsync(IReadOnlyList<IDomainEvent>, CancellationToken)

    [Fact]
    public void DispatchAsync_ReturnType_IsTask()
    {
        var method = typeof(IDomainEventDispatcher).GetMethod("DispatchAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void DispatchAsync_FirstParameter_IsIReadOnlyListOfIDomainEvent()
    {
        var method = typeof(IDomainEventDispatcher).GetMethod("DispatchAsync")!;
        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);

        var firstParam = parameters[0];
        firstParam.ParameterType.Should().Be(typeof(IReadOnlyList<IDomainEvent>));
    }

    [Fact]
    public void DispatchAsync_SecondParameter_IsCancellationToken()
    {
        var method = typeof(IDomainEventDispatcher).GetMethod("DispatchAsync")!;
        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);

        var secondParam = parameters[1];
        secondParam.ParameterType.Should().Be(typeof(CancellationToken));
    }

    // (4) Interface is public

    [Fact]
    public void IDomainEventDispatcher_IsPublic()
    {
        typeof(IDomainEventDispatcher).IsPublic.Should().BeTrue();
    }

    // (5) IDomainEvent parameter type is the existing interface from the same package — no external references

    [Fact]
    public void DispatchAsync_IDomainEvent_ParameterType_IsFromSameAssembly()
    {
        var method = typeof(IDomainEventDispatcher).GetMethod("DispatchAsync")!;
        // IReadOnlyList<IDomainEvent> — the generic argument is IDomainEvent
        var listType = method.GetParameters()[0].ParameterType;
        var domainEventType = listType.GetGenericArguments()[0];

        domainEventType.Should().Be(typeof(IDomainEvent));
        (domainEventType.Assembly == DomainAssembly).Should().BeTrue(
            "IDomainEvent referenced in the parameter must come from the same SharedKernel.Domain assembly");
    }

    // (6) Interface is an interface (not a class or abstract class)

    [Fact]
    public void IDomainEventDispatcher_IsAnInterface()
    {
        typeof(IDomainEventDispatcher).IsInterface.Should().BeTrue();
    }
}
