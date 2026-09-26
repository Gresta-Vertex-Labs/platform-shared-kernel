using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Runs every analyzer that matches a SharedKernel type name, metadata name or namespace prefix
/// against the compiled kernel packages, with no stub copy of any kernel type in the fixture.
/// </summary>
/// <remarks>
/// <para>
/// The per-rule test files compile their fixtures against in-compilation stubs. A stub proves the
/// rule's logic, but not that its match string still names the shipped type: when a refactor moves a
/// type (<c>SharedKernel.Primitives.IClock</c> became <c>SharedKernel.Primitives.Clocks.IClock</c>),
/// the stub moves with the test and every stub test keeps passing while the rule silently stops
/// firing in real code. These tests reference the real assemblies, so a moved or renamed type
/// breaks a test here.
/// </para>
/// <para>
/// Built on a raw <see cref="CSharpCompilation"/> over the test host's trusted-platform-assemblies
/// list, the same technique as SK0035's real-assembly test: the testing package's
/// <c>ReferenceAssemblies</c> presets cannot supply net10.0 references, and the host's list contains
/// exactly the runtime plus every kernel and third-party assembly this project references.
/// </para>
/// <para>
/// A rule whose only kernel name is an exemption namespace cannot be proven by a diagnostic alone
/// (code inside the exemption produces nothing either way), so those tests also assert, by
/// reflection, that the shipped package's types really live under the exempted prefix.
/// </para>
/// <para>
/// Not covered here, because the rule matches no kernel name, or only third-party/BCL names that
/// the rule's own test file already compiles for real: SK0003, SK0004, SK0011, SK0014, SK0022,
/// SK0023, SK0025, SK0032, SK0033, SK0034, SK0704, SK0708. SK0002 and SK0035 already have
/// real-assembly tests in their own files. SK0013, SK0026 and the SK0020/SK0021 logging rules
/// name a kernel package only as an exemption prefix for a package this project does not
/// reference (Communication.Rest, AI.Qdrant/SemanticKernel, Testing).
/// </para>
/// </remarks>
public class RealKernelTypeNameTests
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(ResolveReferences);

    // ---------------------------------------------------------------------------
    // SK0001 — SharedKernel.Primitives exemption
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A service reading the real <c>IClock</c> is clean, a direct <c>DateTimeOffset.UtcNow</c> read
    /// beside it fires, and the real <c>SystemClock</c> (the one place allowed to read the wall
    /// clock) lives under the exempted <c>SharedKernel.Primitives</c> prefix.
    /// </summary>
    [Fact]
    public async Task Sk0001_RealIClockConsumer_FiresOnlyOnDirectRead()
    {
        Assert.StartsWith(
            "SharedKernel.Primitives",
            typeof(SharedKernel.Primitives.Clocks.SystemClock).Namespace,
            StringComparison.Ordinal
        );

        var diagnostics = await AnalyzeAsync(
            new DirectDateTimeUsageAnalyzer(),
            """
            using System;
            using SharedKernel.Primitives.Clocks;

            namespace Fixture
            {
                public sealed class SessionService
                {
                    private readonly IClock _clock;

                    public SessionService(IClock clock) => _clock = clock;

                    public bool HasExpired(DateTimeOffset expiresAt) => expiresAt <= _clock.UtcNow;

                    public bool HasExpiredWrongly(DateTimeOffset expiresAt) => expiresAt <= DateTimeOffset.UtcNow;
                }
            }

            namespace SharedKernel.Primitives.Clocks.Fixture
            {
                public sealed class WallClock
                {
                    public DateTimeOffset Now => DateTimeOffset.UtcNow;
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0001", "DateTimeOffset.UtcNow");
    }

    // ---------------------------------------------------------------------------
    // SK0005 — SharedKernelException (SharedKernel.Core.Exceptions)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0005_RealSharedKernelExceptionSubclass_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new StringOnlyExceptionConstructorAnalyzer(),
            """
            using System;
            using SharedKernel.Core.Exceptions;
            using SharedKernel.Primitives.Errors;

            namespace Fixture
            {
                public sealed class PaymentDeclinedException : SharedKernelException
                {
                    public PaymentDeclinedException(string message)
                        : base(message, Error.Unexpected("payment.declined", message)) { }
                }

                public static class Payments
                {
                    public static Exception Declined() => new PaymentDeclinedException("declined");

                    public static Exception Plain() => new InvalidOperationException("not a kernel exception");
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0005", "new PaymentDeclinedException(\"declined\")");
    }

    // ---------------------------------------------------------------------------
    // SK0006 — SharedKernel.Guards.IGuardClause / Guard.Throw
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0006_ExtensionOnRealIGuardClause_Fires()
    {
        Assert.Equal("SharedKernel.Guards.IGuardClause", typeof(SharedKernel.Guards.IGuardClause).FullName);
        Assert.NotNull(typeof(SharedKernel.Guards.Guard).GetNestedType("Throw"));

        var diagnostics = await AnalyzeAsync(
            new GuardClauseThrowAnalyzer(),
            """
            using System;
            using SharedKernel.Guards;

            namespace Fixture
            {
                public static class OrderGuards
                {
                    public static decimal NegativeTotal(this IGuardClause guard, decimal total)
                    {
                        if (total < 0)
                            throw new ArgumentOutOfRangeException(nameof(total));
                        return total;
                    }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0006", "throw new ArgumentOutOfRangeException(nameof(total));");
    }

    // ---------------------------------------------------------------------------
    // SK0007 — IRedisChannelService (SharedKernel.Caching.Redis.PubSub), SharedKernel.Caching exemption
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0007_RealRedisChannelServiceInEventContext_FiresOutsideCachingOnly()
    {
        Assert.StartsWith(
            "SharedKernel.Caching",
            typeof(SharedKernel.Caching.Redis.PubSub.IRedisChannelService).Namespace,
            StringComparison.Ordinal
        );

        var diagnostics = await AnalyzeAsync(
            new RedisChannelServiceMessagingSubstituteAnalyzer(),
            """
            using SharedKernel.Caching.Redis.PubSub;

            namespace Fixture.Orders
            {
                public sealed class OrderEventRelay
                {
                    public OrderEventRelay(IRedisChannelService channel) { }
                }
            }

            namespace SharedKernel.Caching.Redis.PubSub.Fixture
            {
                public sealed class CacheEventRelay
                {
                    public CacheEventRelay(IRedisChannelService channel) { }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0007", "IRedisChannelService");
        Assert.Contains("OrderEventRelay", diagnostics[0].GetMessage(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // SK0008 — IAggregateRoot (SharedKernel.Domain.Abstractions)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0008_RealAggregateRootInjectedIntoPublisher_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new AggregateRootDispatchCouplingAnalyzer(),
            """
            using System;
            using SharedKernel.Domain.Abstractions;

            namespace Fixture.Outbox
            {
                public sealed class OrderPublisher
                {
                    public OrderPublisher(IAggregateRoot<Guid> order) { }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0008", "IAggregateRoot<Guid>");
    }

    // ---------------------------------------------------------------------------
    // SK0009 — IDomainEvent / DomainEventVersionAttribute (SharedKernel.Domain.Events)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0009_RealDomainEventWithoutVersion_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new DomainEventMissingVersionAttributeAnalyzer(),
            """
            using System;
            using SharedKernel.Domain.Events;

            namespace Fixture
            {
                public sealed record OrderPlaced(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

                [DomainEventVersion(2)]
                public sealed record OrderShipped(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;
            }
            """
        );

        AssertFlagged(diagnostics, "SK0009", "OrderPlaced");
    }

    // ---------------------------------------------------------------------------
    // SK0010 — Specification<T>.ApplyOrderBy / ApplyOrderByDescending
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0010_RealSpecificationWithBothOrderings_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new SpecificationOrderingConflictAnalyzer(),
            """
            using System;
            using SharedKernel.Domain.Specifications;

            namespace Fixture
            {
                public sealed class Order
                {
                    public DateTimeOffset CreatedOn { get; init; }
                    public decimal Total { get; init; }
                }

                public sealed class ConflictingOrderSpec : Specification<Order>
                {
                    public ConflictingOrderSpec()
                    {
                        ApplyOrderBy(o => o.CreatedOn);
                        ApplyOrderByDescending(o => o.Total);
                    }
                }

                public sealed class RecentOrderSpec : Specification<Order>
                {
                    public RecentOrderSpec() => ApplyOrderByDescending(o => o.CreatedOn);
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0010", "ConflictingOrderSpec");
    }

    // ---------------------------------------------------------------------------
    // SK0016 — SharedKernel.Application namespace prefix (e.g. SharedKernel.Application.Pipeline)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0016_InsideRealPipelineNamespace_Fires()
    {
        // The rule's scope is the application packages' own namespaces. SharedKernel.Application.Pipeline
        // is not referenced here (it would only add its build warnings to this project); the fixture
        // declares code in its namespace, and the vocabulary package it builds on is checked for real.
        Assert.StartsWith(
            "SharedKernel.Application",
            typeof(SharedKernel.Application.Messaging.ICommandBase).Namespace,
            StringComparison.Ordinal
        );

        var diagnostics = await AnalyzeAsync(
            new RequestTypeShortNameUsageAnalyzer(),
            """
            using SharedKernel.Application.Messaging;

            namespace SharedKernel.Application.Pipeline.Fixture
            {
                public static class RequestTags
                {
                    public static string Short<TRequest>() => typeof(TRequest).Name;

                    public static string Safe<TRequest>() => typeof(TRequest).FullName ?? typeof(TRequest).Name;
                }
            }

            namespace Fixture
            {
                public static class ServiceTags
                {
                    public static string Short() => typeof(ICommandBase).Name;
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0016", "typeof(TRequest).Name");
    }

    // ---------------------------------------------------------------------------
    // SK0017 / SK0018 / SK0041 — ICommandBase, IQuery<T> (SharedKernel.Application.Messaging),
    // ICacheableQuery<T>, IInvalidatesCache (SharedKernel.Application.Caching)
    // ---------------------------------------------------------------------------

    private const string CacheableQueryMembers = """
                public CachePolicy CachePolicy => throw new NotImplementedException();
                public string CacheKey => "key";
        """;

    private const string InvalidatesCacheMembers = """
                public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => throw new NotImplementedException();
        """;

    [Fact]
    public async Task Sk0017_RealCommandImplementingRealCacheableQuery_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new CommandImplementsCacheableQueryAnalyzer(),
            $$"""
            using System;
            using SharedKernel.Application.Caching;
            using SharedKernel.Application.Messaging;
            using SharedKernel.Caching.Abstractions;

            namespace Fixture
            {
                public sealed record RenameProduct : ICommand<int>, ICacheableQuery<int>
                {
            {{CacheableQueryMembers}}
                }

                public sealed record GetProduct : ICacheableQuery<int>
                {
            {{CacheableQueryMembers}}
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0017", "RenameProduct");
    }

    [Fact]
    public async Task Sk0018_RealQueryImplementingRealInvalidatesCache_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new QueryImplementsInvalidatesCacheAnalyzer(),
            $$"""
            using System;
            using System.Collections.Generic;
            using SharedKernel.Application.Caching;
            using SharedKernel.Application.Messaging;

            namespace Fixture
            {
                public sealed record ListProducts : IQuery<int>, IInvalidatesCache
                {
            {{InvalidatesCacheMembers}}
                }

                public sealed record RenameProduct : ICommand, IInvalidatesCache
                {
            {{InvalidatesCacheMembers}}
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0018", "ListProducts");
    }

    [Fact]
    public async Task Sk0041_TwoRealCacheableQueriesSharingAName_FireTwice()
    {
        var diagnostics = await AnalyzeAsync(
            new DuplicateCacheableQueryNameAnalyzer(),
            $$"""
            using System;
            using SharedKernel.Application.Caching;
            using SharedKernel.Caching.Abstractions;

            namespace Fixture.Catalog
            {
                public sealed record GetProduct : ICacheableQuery<int>
                {
            {{CacheableQueryMembers}}
                }
            }

            namespace Fixture.Pricing
            {
                public sealed record GetProduct : ICacheableQuery<int>
                {
            {{CacheableQueryMembers}}
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0041", "GetProduct", "GetProduct");
    }

    // ---------------------------------------------------------------------------
    // SK0040 — IRequest<T> (SharedKernel.Application.Messaging), [RequirePermission]
    // (SharedKernel.Application.Authorization, inherited), IIdempotentRequest, Result (SharedKernel.Primitives.Results)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0040_RealMarkerOnNonResultRequest_Fires()
    {
        Assert.Equal(
            "SharedKernel.Application.Authorization",
            typeof(SharedKernel.Application.Authorization.RequirePermissionAttribute).Namespace
        );

        var diagnostics = await AnalyzeAsync(
            new PipelineMarkerResponseShapeMismatchAnalyzer(),
            """
            using SharedKernel.Application.Authorization;
            using SharedKernel.Application.Idempotency;
            using SharedKernel.Application.Messaging;

            namespace Fixture
            {
                [RequirePermission("reports.export")]
                public sealed record ExportReport : IRequest<string>;

                [RequirePermission("reports.read")]
                public abstract record ProtectedQuery : IRequest<string>;

                public sealed record ReadReport : ProtectedQuery;

                [RequirePermission("orders.place")]
                public sealed record PlaceOrder : ICommand<int>, IIdempotentRequest
                {
                    public string IdempotencyKey => "key";
                }

                public sealed record CancelOrder : ICommand, IIdempotentRequest
                {
                    public string IdempotencyKey => "key";
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0040", "ExportReport", "ReadReport");
        Assert.Contains("'string'", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("[RequirePermission]", diagnostics[0].GetMessage(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // SK0024 — IQueryBuilder / SearchQueryBuilder / SearchFilter (SharedKernel.Search.Abstractions)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0024_RealSearchQueryBuilderAndFilter_FireOnLiterals()
    {
        var diagnostics = await AnalyzeAsync(
            new RawSearchFieldNameLiteralAnalyzer(),
            """
            using SharedKernel.Search.Abstractions.Models;
            using SharedKernel.Search.Abstractions.Querying;

            namespace Fixture
            {
                public sealed class Product
                {
                    public string Title { get; init; } = "";
                    public string Status { get; init; } = "";
                }

                public static class ProductQueries
                {
                    public static IQueryBuilder Raw() =>
                        SearchQuery.New().OrderBy("title").SearchingIn("title", nameof(Product.Status));

                    public static IQueryBuilder Concrete() => new SearchQueryBuilder().OrderByDescending("status");

                    public static SearchFilter RawFilter() => SearchFilter.Eq("status", "active");

                    public static IQueryBuilder Named() => SearchQuery.New().OrderBy(nameof(Product.Title));

                    public static SearchFilter NamedFilter() => SearchFilter.Eq(nameof(Product.Status), "active");
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0024", "\"title\"", "\"title\"", "\"status\"", "\"status\"");
    }

    // ---------------------------------------------------------------------------
    // SK0027 — VectorFilter / IVectorCollectionProvisioner / VectorCollectionDefinitionBuilder
    // (SharedKernel.AI.Abstractions)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0027_RealVectorTypes_FireOnLiterals()
    {
        var diagnostics = await AnalyzeAsync(
            new RawIntelligenceIdentifierLiteralAnalyzer(),
            """
            using System.Threading.Tasks;
            using SharedKernel.AI.Abstractions.Abstractions;
            using SharedKernel.AI.Abstractions.Models;

            namespace Fixture
            {
                public static class Names
                {
                    public const string Collection = "docs";
                    public const string Model = "text-embedding-3-small";
                }

                public static class Usage
                {
                    public static VectorFilter RawFilter() => VectorFilter.Eq("category", "news");

                    public static Task RawExists(IVectorCollectionProvisioner provisioner) =>
                        provisioner.CollectionExistsAsync("docs");

                    public static VectorCollectionDefinitionBuilder RawModel() =>
                        new VectorCollectionDefinitionBuilder(Names.Collection).EmbeddingModel("text-embedding-3-small", 1536);

                    public static Task NamedExists(IVectorCollectionProvisioner provisioner) =>
                        provisioner.CollectionExistsAsync(Names.Collection);

                    public static VectorCollectionDefinitionBuilder NamedModel() =>
                        new VectorCollectionDefinitionBuilder(Names.Collection).EmbeddingModel(Names.Model, 1536);
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0027", "\"category\"", "\"docs\"", "\"text-embedding-3-small\"");
    }

    // ---------------------------------------------------------------------------
    // SK0028 — WorkflowBase / ActivityBase (SharedKernel.Workflows.Temporal.Authoring), IClock,
    // Temporalio.Workflows.WorkflowAttribute
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0028_RealWorkflowBaseAndWorkflowAttribute_FireAndRealActivityBaseIsExempt()
    {
        var diagnostics = await AnalyzeAsync(
            new NonDeterministicApiUsageInsideWorkflowAnalyzer(),
            """
            using System;
            using Microsoft.Extensions.Logging;
            using SharedKernel.Primitives.Clocks;
            using SharedKernel.Workflows.Temporal.Authoring;
            using Temporalio.Workflows;

            namespace Fixture
            {
                public class OrderWorkflow : WorkflowBase
                {
                    public OrderWorkflow(IClock clock) { }

                    public DateTime Started() => DateTime.UtcNow;
                }

                [Workflow]
                public class RefundWorkflow
                {
                    public Guid NewId() => Guid.NewGuid();
                }

                public sealed class ChargeActivity : ActivityBase
                {
                    public ChargeActivity(ILogger logger, IClock clock) : base(logger, clock) { }

                    public DateTime Started() => DateTime.UtcNow;

                    public Guid NewId() => Guid.NewGuid();
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0028", "IClock", "DateTime.UtcNow", "Guid.NewGuid()");
    }

    // ---------------------------------------------------------------------------
    // SK0029 — Temporalio client types, SharedKernel.Workflows.Temporal exemption
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0029_RealTemporalClientTypes_FireOutsideWorkflowsPackageOnly()
    {
        Assert.StartsWith(
            "SharedKernel.Workflows.Temporal",
            typeof(SharedKernel.Workflows.Temporal.Authoring.WorkflowBase).Namespace,
            StringComparison.Ordinal
        );

        var diagnostics = await AnalyzeAsync(
            new RawTemporalClientConstructorInjectionAnalyzer(),
            """
            using Temporalio.Client;
            using Temporalio.Worker;

            namespace Fixture
            {
                public sealed class OrderService
                {
                    public OrderService(ITemporalClient client, WorkflowHandle handle, TemporalWorker worker) { }
                }
            }

            namespace SharedKernel.Workflows.Temporal.Fixture
            {
                public sealed class Dispatcher
                {
                    public Dispatcher(ITemporalClient client) { }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0029", "ITemporalClient", "WorkflowHandle", "TemporalWorker");
    }

    // ---------------------------------------------------------------------------
    // SK0030 — IHasSuccessFlag / Result (SharedKernel.Primitives.Results)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0030_RealResultDiscarded_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new ResultOutcomeDiscardedAnalyzer(),
            """
            using System.Threading.Tasks;
            using SharedKernel.Primitives.Results;

            namespace Fixture
            {
                public static class Orders
                {
                    public static Result Cancel() => Result.Success();

                    public static Task<Result<int>> PlaceAsync() => Task.FromResult(Result<int>.Success(1));

                    public static async Task Run()
                    {
                        Cancel();
                        await PlaceAsync();
                        _ = Cancel();
                        var placed = await PlaceAsync();
                    }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0030", "Cancel()", "await PlaceAsync()");
    }

    // ---------------------------------------------------------------------------
    // SK0031 — IUserContext / IRequestContext in the message; Security.Oidc/.ApiKey exemptions
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0031_RealContextsAreClean_RawClaimsPrincipalFires_MessageNamesRealTypes()
    {
        Assert.StartsWith(
            "SharedKernel.Security.Oidc",
            typeof(SharedKernel.Security.Oidc.Extensions.OidcServiceCollectionExtensions).Namespace,
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "SharedKernel.Security.ApiKey",
            typeof(SharedKernel.Security.ApiKey.Extensions.ApiKeyServiceCollectionExtensions).Namespace,
            StringComparison.Ordinal
        );

        var diagnostics = await AnalyzeAsync(
            new RawSecurityContextConstructorInjectionAnalyzer(),
            """
            using System.Security.Claims;
            using SharedKernel.Execution.Context;
            using SharedKernel.Security.Abstractions;

            namespace Fixture
            {
                public sealed class CreateOrderHandler
                {
                    public CreateOrderHandler(IUserContext user, IRequestContext request) { }
                }

                public sealed class AuditHandler
                {
                    public AuditHandler(ClaimsPrincipal principal) { }
                }
            }

            namespace SharedKernel.Security.Oidc.Fixture
            {
                public sealed class PrincipalMapper
                {
                    public PrincipalMapper(ClaimsPrincipal principal) { }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0031", "ClaimsPrincipal");
        var message = diagnostics[0].GetMessage();
        Assert.Contains(typeof(SharedKernel.Security.Abstractions.IUserContext).FullName!, message, StringComparison.Ordinal);
        Assert.Contains(typeof(SharedKernel.Execution.Context.IRequestContext).FullName!, message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // SK0036 — SharedKernel.Presentation.Grpc exemption (every namespace of the real package) and
    // SharedKernel.Core's ThrowIfFailure()/GetValueOrThrow() in the message
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0036_RealRpcException_FiresOutsidePresentationGrpc_MessageNamesRealExtensions()
    {
        // The exemption prefix must cover every namespace of the real gRPC package, since its rich-status factory
        // and exception interceptor are the only sanctioned construction sites.
        var grpcNamespaces = System.Reflection.Assembly.Load("SharedKernel.Presentation.Grpc")
            .GetTypes()
            .Select(type => type.Namespace)
            .Where(ns => ns is not null)
            .Distinct()
            .ToList();
        Assert.NotEmpty(grpcNamespaces);
        Assert.All(grpcNamespaces, ns => Assert.StartsWith("SharedKernel.Presentation.Grpc", ns, StringComparison.Ordinal));

        var extensions = typeof(SharedKernel.Core.Extensions.ResultExtensions);

        var diagnostics = await AnalyzeAsync(
            new RawRpcExceptionConstructionAnalyzer(),
            """
            using System;
            using Grpc.Core;

            namespace Fixture
            {
                public static class Failures
                {
                    public static Exception NotFound() => new RpcException(new Status(StatusCode.NotFound, "missing"));
                }
            }

            namespace SharedKernel.Presentation.Grpc.Fixture
            {
                public static class Failures
                {
                    public static Exception NotFound() => new RpcException(new Status(StatusCode.NotFound, "missing"));
                }
            }
            """
        );

        AssertFlagged(
            diagnostics,
            "SK0036",
            "new RpcException(new Status(StatusCode.NotFound, \"missing\"))",
            "new Status(StatusCode.NotFound, \"missing\")"
        );
        var message = diagnostics[0].GetMessage();
        Assert.Contains(extensions.Namespace!, message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(SharedKernel.Core.Extensions.ResultExtensions.ThrowIfFailure)}()", message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(SharedKernel.Core.Extensions.ResultExtensions.GetValueOrThrow)}()", message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // SK0037 — ValueObject / SingleValueObject`1 (SharedKernel.Domain.ValueObjects)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0037_RealValueObjectWithoutEnsureValid_Fires_RealSingleValueObjectIsExempt()
    {
        var diagnostics = await AnalyzeAsync(
            new ValueObjectMissingEnsureValidAnalyzer(),
            """
            using System.Collections.Generic;
            using SharedKernel.Domain.ValueObjects;
            using SharedKernel.Primitives.Errors;

            namespace Fixture
            {
                public sealed class Sku : ValueObject
                {
                    public Sku(string value) { Value = value; }

                    public string Value { get; }

                    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }

                    protected override IEnumerable<Error>? Validate()
                    {
                        if (string.IsNullOrEmpty(Value))
                            yield return Error.Unexpected("sku.empty", "A SKU is required.");
                    }
                }

                public sealed class Barcode : ValueObject
                {
                    public Barcode(string value) { Value = value; EnsureValid(); }

                    public string Value { get; }

                    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }

                    protected override IEnumerable<Error>? Validate()
                    {
                        if (string.IsNullOrEmpty(Value))
                            yield return Error.Unexpected("barcode.empty", "A barcode is required.");
                    }
                }

                public sealed class Email : SingleValueObject<string>
                {
                    public Email(string value) : base(value) { }

                    protected override IEnumerable<Error>? Validate()
                    {
                        if (!Value.Contains('@'))
                            yield return Error.Unexpected("email.invalid", "Not an email address.");
                    }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0037", "Sku");
    }

    // ---------------------------------------------------------------------------
    // SK0038 / SK0039 — IIntegrationEvent / IntegrationEventAttribute (SharedKernel.Contracts.Events)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0038Sk0039_RealIntegrationEventTypes_Fire()
    {
        var diagnostics = await AnalyzeAsync(
            new IntegrationEventAttributeAnalyzer(),
            """
            using System;
            using SharedKernel.Contracts.Events;

            namespace Fixture
            {
                public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

                [IntegrationEvent("orders.order-shipped", Version = 1)]
                public sealed record OrderShipped(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

                [IntegrationEvent("Orders.Cancelled", Version = 0)]
                public sealed record OrderCancelled(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            }
            """
        );

        AssertFlagged(
            diagnostics,
            ["SK0038", "SK0039", "SK0039"],
            "OrderPlaced",
            "\"Orders.Cancelled\"",
            "Version = 0"
        );
    }

    // ---------------------------------------------------------------------------
    // SK0042 — SharedKernel.Persistence.Dapper.Sessions.IDbSession.Command, Dapper.SqlMapper
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0042_RealDbSessionCommandAndSqlMapper_FireOnNonConstantSql()
    {
        var diagnostics = await AnalyzeAsync(
            new NonConstantDapperSqlArgumentAnalyzer(),
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Dapper;
            using SharedKernel.Persistence.Dapper.Sessions;

            namespace Fixture
            {
                public sealed class OrderQueries
                {
                    private readonly IDbSessionFactory _sessions;

                    public OrderQueries(IDbSessionFactory sessions) => _sessions = sessions;

                    public async Task<IEnumerable<int>> FindAsync(string status)
                    {
                        await using var session = await _sessions.OpenReadOnlyAsync();
                        return await session.Connection.QueryAsync<int>(
                            session.Command($"SELECT id FROM orders WHERE status = '{status}'"));
                    }

                    public async Task<IEnumerable<int>> FindRawAsync(string table)
                    {
                        await using var session = await _sessions.OpenReadOnlyAsync();
                        return await session.Connection.QueryAsync<int>($"SELECT id FROM {table}");
                    }

                    public async Task<IEnumerable<int>> FindSafelyAsync(string status)
                    {
                        await using var session = await _sessions.OpenReadOnlyAsync();
                        return await session.Connection.QueryAsync<int>(
                            session.Command("SELECT id FROM orders WHERE status = @status", new { status }));
                    }
                }
            }
            """
        );

        AssertFlagged(
            diagnostics,
            "SK0042",
            "$\"SELECT id FROM orders WHERE status = '{status}'\"",
            "$\"SELECT id FROM {table}\""
        );
    }

    // ---------------------------------------------------------------------------
    // SK0201 — TenantedDbContext (SharedKernel.Persistence.EfCore.Context)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0201_RealTenantedDbContextWithoutBaseCall_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new TenantedDbContextOnModelCreatingAnalyzer(),
            """
            using Microsoft.EntityFrameworkCore;
            using SharedKernel.Persistence.EfCore.Context;

            namespace Fixture
            {
                public sealed class OrdersDbContext : TenantedDbContext
                {
                    public OrdersDbContext(DbContextOptions<OrdersDbContext> options, PersistenceContextDependencies dependencies)
                        : base(options, dependencies) { }

                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        modelBuilder.HasDefaultSchema("orders");
                    }
                }

                public sealed class BillingDbContext : TenantedDbContext
                {
                    public BillingDbContext(DbContextOptions<BillingDbContext> options, PersistenceContextDependencies dependencies)
                        : base(options, dependencies) { }

                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        base.OnModelCreating(modelBuilder);
                        modelBuilder.HasDefaultSchema("billing");
                    }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0201", "OnModelCreating");
        Assert.Contains("OrdersDbContext", diagnostics[0].GetMessage(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // SK0202 — TenantedRepository / SharedKernel.Persistence.EfCore exemption, real EF Core
    // IgnoreQueryFilters
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0202_RealIgnoreQueryFilters_FiresOutsideEfCorePackageOnly()
    {
        var tenantedRepository = typeof(SharedKernel.Persistence.EfCore.MultiTenancy.TenantedRepository<,>);
        Assert.StartsWith("SharedKernel.Persistence.EfCore", tenantedRepository.Namespace, StringComparison.Ordinal);
        Assert.Equal("TenantedRepository", tenantedRepository.Name.Split('`')[0]);

        var diagnostics = await AnalyzeAsync(
            new IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer(),
            """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            namespace Fixture
            {
                public sealed class Order
                {
                    public int Id { get; set; }
                }

                public static class Reports
                {
                    public static IQueryable<Order> All(DbContext db) => db.Set<Order>().IgnoreQueryFilters();

                    public static IQueryable<Order> WithDeleted(DbContext db) =>
                        db.Set<Order>().IgnoreQueryFilters(["soft_delete"]);
                }
            }

            namespace SharedKernel.Persistence.EfCore.Fixture
            {
                public static class Maintenance
                {
                    public static IQueryable<global::Fixture.Order> All(DbContext db) => db.Set<global::Fixture.Order>().IgnoreQueryFilters();
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0202", "db.Set<Order>().IgnoreQueryFilters()");
    }

    // ---------------------------------------------------------------------------
    // SK0703 / SK0705 — IMessageBus / IEventPublisher / IFaultConsumer<T>
    // (SharedKernel.Messaging.Abstractions)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Sk0703_RealMessageBusAndEventPublisherAsSingleton_Fire()
    {
        var diagnostics = await AnalyzeAsync(
            new MessageBusSingletonRegistrationAnalyzer(),
            """
            using Microsoft.Extensions.DependencyInjection;
            using SharedKernel.Messaging.Abstractions.EventPublisher;
            using SharedKernel.Messaging.Abstractions.MessageBus;

            namespace Fixture
            {
                public static class Registration
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddSingleton<IMessageBus>(_ => null!);
                        services.AddSingleton<IEventPublisher>(_ => null!);
                        services.AddScoped<IMessageBus>(_ => null!);
                    }
                }
            }
            """
        );

        AssertFlagged(
            diagnostics,
            "SK0703",
            "services.AddSingleton<IMessageBus>(_ => null!)",
            "services.AddSingleton<IEventPublisher>(_ => null!)"
        );
    }

    [Fact]
    public async Task Sk0705_RealFaultConsumerRegisteredDirectly_Fires()
    {
        var diagnostics = await AnalyzeAsync(
            new FaultConsumerDirectRegistrationAnalyzer(),
            """
            using Microsoft.Extensions.DependencyInjection;
            using SharedKernel.Messaging.Abstractions.Faults;

            namespace Fixture
            {
                public sealed class OrderPlaced
                {
                }

                public static class Registration
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<IFaultConsumer<OrderPlaced>>(_ => null!);
                    }
                }
            }
            """
        );

        AssertFlagged(diagnostics, "SK0705", "services.AddScoped<IFaultConsumer<OrderPlaced>>(_ => null!)");
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "RealKernelTypeNames",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references: References.Value,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var compileErrors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.True(compileErrors.Count == 0, "Fixture does not compile:\n" + string.Join("\n", compileErrors));

        var supportedIds = analyzer.SupportedDiagnostics.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        return
        [
            .. diagnostics
                .Where(d => supportedIds.Contains(d.Id))
                .OrderBy(d => d.Location.SourceSpan.Start),
        ];
    }

    /// <summary>Asserts exactly one <paramref name="id"/> diagnostic per expected span text, in source order.</summary>
    private static void AssertFlagged(ImmutableArray<Diagnostic> diagnostics, string id, params string[] expectedSpans) =>
        AssertFlagged(diagnostics, [.. expectedSpans.Select(_ => id)], expectedSpans);

    private static void AssertFlagged(ImmutableArray<Diagnostic> diagnostics, string[] ids, params string[] expectedSpans)
    {
        var actual = diagnostics
            .Select(d => $"{d.Id}: {d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)}")
            .ToList();
        var expected = ids.Zip(expectedSpans, (id, span) => $"{id}: {span}").ToList();
        Assert.Equal(expected, actual);
    }

    private static ImmutableArray<MetadataReference> ResolveReferences()
    {
        var paths = (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [])
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }
}
