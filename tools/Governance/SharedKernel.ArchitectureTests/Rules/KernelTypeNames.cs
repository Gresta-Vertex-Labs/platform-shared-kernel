namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Metadata full names of the kernel and third-party types the service placement rules recognise. They are names, not
/// types, so this package references no kernel, MassTransit, EF Core or ASP.NET Core assembly; a generic type is named
/// in its open form (<c>Name`arity</c>). Each name is the default of the matching <see cref="ServicePlacementOptions"/>
/// property, so a caller whose service uses another anchor overrides the property.
/// </summary>
public static class KernelTypeNames
{
    /// <summary><c>SharedKernel.Domain.Abstractions.IAggregateRoot&lt;TId&gt;</c>.</summary>
    public const string AggregateRoot = "SharedKernel.Domain.Abstractions.IAggregateRoot`1";

    /// <summary><c>SharedKernel.Domain.BusinessRules.IBusinessRule</c>.</summary>
    public const string BusinessRule = "SharedKernel.Domain.BusinessRules.IBusinessRule";

    /// <summary><c>SharedKernel.Domain.Specifications.ISpecification&lt;T&gt;</c>.</summary>
    public const string Specification = "SharedKernel.Domain.Specifications.ISpecification`1";

    /// <summary><c>SharedKernel.Domain.Events.IDomainEvent</c>.</summary>
    public const string DomainEvent = "SharedKernel.Domain.Events.IDomainEvent";

    /// <summary><c>SharedKernel.Application.Messaging.ICommandBase</c>, the marker of every command.</summary>
    public const string Command = "SharedKernel.Application.Messaging.ICommandBase";

    /// <summary><c>SharedKernel.Application.Messaging.IQueryBase</c>, the marker of every query.</summary>
    public const string Query = "SharedKernel.Application.Messaging.IQueryBase";

    /// <summary><c>SharedKernel.Application.Messaging.IRequestHandler&lt;TRequest, TResponse&gt;</c>.</summary>
    public const string RequestHandler = "SharedKernel.Application.Messaging.IRequestHandler`2";

    /// <summary><c>SharedKernel.Application.Validation.IRequestValidator&lt;TRequest&gt;</c>.</summary>
    public const string RequestValidator = "SharedKernel.Application.Validation.IRequestValidator`1";

    /// <summary><c>SharedKernel.Application.DomainEvents.IDomainEventHandler&lt;TEvent&gt;</c>.</summary>
    public const string DomainEventHandler = "SharedKernel.Application.DomainEvents.IDomainEventHandler`1";

    /// <summary><c>SharedKernel.Presentation.WebApi.IEndpointModule</c>.</summary>
    public const string EndpointModule = "SharedKernel.Presentation.WebApi.IEndpointModule";

    /// <summary><c>Microsoft.EntityFrameworkCore.IEntityTypeConfiguration&lt;TEntity&gt;</c>.</summary>
    public const string EntityTypeConfiguration = "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1";

    /// <summary><c>MassTransit.IConsumer&lt;TMessage&gt;</c>.</summary>
    public const string Consumer = "MassTransit.IConsumer`1";

    /// <summary><c>Microsoft.AspNetCore.Http.IMiddleware</c>.</summary>
    public const string Middleware = "Microsoft.AspNetCore.Http.IMiddleware";

    /// <summary><c>Microsoft.AspNetCore.Http.IEndpointFilter</c>.</summary>
    public const string EndpointFilter = "Microsoft.AspNetCore.Http.IEndpointFilter";

    /// <summary>
    /// <c>Microsoft.AspNetCore.Http.HttpContext</c>: a public <c>Invoke</c>/<c>InvokeAsync</c> method whose first
    /// parameter is this type marks a conventional (non-<c>IMiddleware</c>) middleware.
    /// </summary>
    public const string HttpContext = "Microsoft.AspNetCore.Http.HttpContext";

    /// <summary><c>System.Exception</c>.</summary>
    public const string Exception = "System.Exception";
}
