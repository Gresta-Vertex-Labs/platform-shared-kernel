using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Two in-memory service assemblies, compiled against the real kernel, MassTransit, EF Core and ASP.NET Core assemblies
/// the test project references, so the placement rules recognise the real types by metadata name. The names
/// (<c>Fixture.Good</c>, <c>Fixture.Bad</c>) collide with no loaded assembly (rule 15).
/// </summary>
internal static class ServicePlacementFixtures
{
    internal const string GoodService = "Fixture.Good";
    internal const string BadService = "Fixture.Bad";

    private static readonly Lazy<Assembly> GoodAssembly = new(() => Compile("Fixture.Good.Service", GoodSource));
    private static readonly Lazy<Assembly> BadAssembly = new(() => Compile("Fixture.Bad.Service", BadSource));
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);

    /// <summary>Every kind of type, in the folder the rules expect.</summary>
    internal static Assembly Good => GoodAssembly.Value;

    /// <summary>One or more misplaced types per rule.</summary>
    internal static Assembly Bad => BadAssembly.Value;

    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var references = new List<MetadataReference>();
        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception)
            {
                // A native library on the trusted-assembly list is not metadata; skip it.
            }
        }

        return references;
    }

    private static Assembly Compile(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' did not compile:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            );
        }

        // Cecil (NetArchTest) reads the module from disk, so the fixture is written out with a unique name.
        string path = Path.Combine(Path.GetTempPath(), $"{assemblyName}.{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, stream.ToArray());
        return Assembly.LoadFrom(path);
    }

    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using MassTransit;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using SharedKernel.Application.DomainEvents;
        using SharedKernel.Application.Messaging;
        using SharedKernel.Application.Validation;
        using SharedKernel.Core.Exceptions;
        using SharedKernel.Domain.Aggregates;
        using SharedKernel.Domain.BusinessRules;
        using SharedKernel.Domain.Events;
        using SharedKernel.Domain.Specifications;
        using SharedKernel.Presentation.WebApi;
        using SharedKernel.Primitives.Errors;
        using SharedKernel.Primitives.Results;

        """;

    private const string GoodSource = Usings + """
        namespace Fixture.Good.Domain.Orders
        {
            public sealed class Order : AggregateRoot<Guid> { public Order() { } }
        }
        namespace Fixture.Good.Domain.Orders.Rules
        {
            public sealed class OrderIsOpen : IBusinessRule
            {
                public string Code => "order.open";
                public string Message => "The order is not open.";
                public bool IsBroken() => false;
            }
        }
        namespace Fixture.Good.Domain.Orders.Specifications
        {
            public sealed class OpenOrders : Specification<Fixture.Good.Domain.Orders.Order>
            {
                public OpenOrders() { AddCriteria(order => true); }
            }
        }
        namespace Fixture.Good.Domain.Orders.Events
        {
            public sealed record OrderPlaced : DomainEvent;
        }
        namespace Fixture.Good.Domain.Common
        {
            public static class Clock { }
        }
        namespace Fixture.Good.Application.Orders.Commands.PlaceOrder
        {
            public sealed record PlaceOrderCommand : ICommand<Guid>;
            public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
            {
                public Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
            public sealed class PlaceOrderCommandValidator : IRequestValidator<PlaceOrderCommand>
            {
                public ValueTask<IReadOnlyList<Error>> ValidateAsync(PlaceOrderCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
        }
        namespace Fixture.Good.Application.Orders.Queries.GetOrder
        {
            public sealed record GetOrderQuery : IQuery<string>;
            public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, string>
            {
                public Task<Result<string>> Handle(GetOrderQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
        }
        namespace Fixture.Good.Application.Orders.EventHandlers
        {
            public sealed class OrderPlacedHandler : IDomainEventHandler<Fixture.Good.Domain.Orders.Events.OrderPlaced>
            {
                public Task Handle(Fixture.Good.Domain.Orders.Events.OrderPlaced domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }
        }
        namespace Fixture.Good.Infrastructure.Persistence.Configurations
        {
            public sealed class OrderConfiguration : IEntityTypeConfiguration<Fixture.Good.Domain.Orders.Order>
            {
                public void Configure(EntityTypeBuilder<Fixture.Good.Domain.Orders.Order> builder) { }
            }
        }
        namespace Fixture.Good.Infrastructure.Messaging.Consumers
        {
            public sealed class OrderMessage { }
            public sealed class OrderConsumer : IConsumer<OrderMessage>
            {
                public Task Consume(ConsumeContext<OrderMessage> context) => Task.CompletedTask;
            }
        }
        namespace Fixture.Good.Api.Endpoints
        {
            public sealed class OrderEndpoints : IEndpointModule
            {
                public static void Map(IEndpointRouteBuilder app) { }
            }
        }
        namespace Fixture.Good.Api.Middleware
        {
            public sealed class TimingMiddleware : IMiddleware
            {
                public Task InvokeAsync(HttpContext context, RequestDelegate next) => next(context);
            }
            public sealed class ConventionMiddleware
            {
                public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
            }
        }
        namespace Fixture.Good.Api.Filters
        {
            public sealed class AuditFilter : IEndpointFilter
            {
                public ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }
        }
        """;

    private const string BadSource = Usings + """
        namespace Fixture.Bad.Domain
        {
            public sealed class LooseAggregate : AggregateRoot<Guid> { public LooseAggregate() { } }
        }
        namespace Fixture.Bad.Domain.Common
        {
            public sealed class SharedKindAggregate : AggregateRoot<Guid> { public SharedKindAggregate() { } }
        }
        namespace Fixture.Bad.Domain.Orders
        {
            public sealed class OrderAggregate : AggregateRoot<Guid> { public OrderAggregate() { } }
            public sealed class MisplacedRule : IBusinessRule
            {
                public string Code => "x";
                public string Message => "x";
                public bool IsBroken() => false;
            }
            public sealed class MisplacedSpecification : Specification<OrderAggregate> { }
            public sealed record MisplacedEvent : DomainEvent;
        }
        namespace Fixture.Bad.Domain.Orders.RulesEngine
        {
            public sealed class LookalikeRule : IBusinessRule
            {
                public string Code => "x";
                public string Message => "x";
                public bool IsBroken() => false;
            }
        }
        namespace Fixture.Bad.Domain.Orders.Rules
        {
            public sealed class WellPlacedRule : IBusinessRule
            {
                public string Code => "x";
                public string Message => "x";
                public bool IsBroken() => false;
            }
        }
        namespace Fixture.Bad.Domain.Helpers
        {
            public sealed class Thing { }
        }
        namespace Fixture.Bad.Application.Utils
        {
            public interface IUtil { }
        }
        namespace Fixture.Bad.Application.Orders.Commands
        {
            public sealed record WrongPlaceCommand : ICommand<Guid>;
        }
        namespace Fixture.Bad.Application.Orders.Commands.ShipOrder
        {
            public sealed record ShipOrderCommand : ICommand;
            public sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
            {
                public Task<Result> Handle(ShipOrderCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
        }
        namespace Fixture.Bad.Application.Orders.Commands.Cancel
        {
            public sealed record CancelOrderCommand : ICommand;
            public sealed record RefundOrderCommand : ICommand;
        }
        namespace Fixture.Bad.Application.Orders.Queries.Find
        {
            public sealed record FindOrder : IQuery<string>;
        }
        namespace Fixture.Bad.Application.Orders.Handlers
        {
            public sealed class CancelOrderHandler : ICommandHandler<Fixture.Bad.Application.Orders.Commands.Cancel.CancelOrderCommand>
            {
                public Task<Result> Handle(Fixture.Bad.Application.Orders.Commands.Cancel.CancelOrderCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
            public sealed class ShipOrderValidator : IRequestValidator<Fixture.Bad.Application.Orders.Commands.ShipOrder.ShipOrderCommand>
            {
                public ValueTask<IReadOnlyList<Error>> ValidateAsync(Fixture.Bad.Application.Orders.Commands.ShipOrder.ShipOrderCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
            }
            public sealed class StrayEventHandler : IDomainEventHandler<Fixture.Bad.Domain.Orders.MisplacedEvent>
            {
                public Task Handle(Fixture.Bad.Domain.Orders.MisplacedEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
            }
        }
        namespace Fixture.Bad.Infrastructure.Data
        {
            public sealed class StrayConfiguration : IEntityTypeConfiguration<Fixture.Bad.Domain.Orders.OrderAggregate>
            {
                public void Configure(EntityTypeBuilder<Fixture.Bad.Domain.Orders.OrderAggregate> builder) { }
            }
        }
        namespace Fixture.Bad.Infrastructure.Consumers
        {
            public sealed class StrayMessage { }
            public sealed class StrayConsumer : IConsumer<StrayMessage>
            {
                public Task Consume(ConsumeContext<StrayMessage> context) => Task.CompletedTask;
            }
        }
        namespace Fixture.Bad.Api.Modules
        {
            public sealed class StrayEndpoints : IEndpointModule
            {
                public static void Map(IEndpointRouteBuilder app) { }
            }
            public sealed class StrayMiddleware : IMiddleware
            {
                public Task InvokeAsync(HttpContext context, RequestDelegate next) => next(context);
            }
            public sealed class StrayConventionMiddleware
            {
                public Task Invoke(HttpContext context) => Task.CompletedTask;
            }
            public sealed class StrayFilter : IEndpointFilter
            {
                public ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }
        }
        namespace Fixture.Bad.Errors
        {
            public sealed class PlainFailure : Exception { }
            public sealed class ClassifiedFailure : SharedKernelException
            {
                public ClassifiedFailure(Error error) : base(error) { }
            }
        }
        """;
}
