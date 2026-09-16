using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0040 <see cref="PipelineMarkerResponseShapeMismatchAnalyzer"/>.</summary>
/// <remarks>
/// Fire path: a type implementing <c>IAuthorizeRequest</c> or <c>IIdempotentRequest</c> plus
/// <c>MediatR.IRequest&lt;TResponse&gt;</c> where <c>TResponse</c> is a plain DTO — the exact
/// runtime shape that makes <c>FailureResponse.Create&lt;TResponse&gt;</c> throw
/// <see cref="InvalidOperationException"/> the first time the behavior short-circuits.
/// Pass path: a <c>Result</c>/closed <c>Result&lt;T&gt;</c> response, no <c>IRequest&lt;&gt;</c>
/// at all, an open/generic response type, and — the two markers whose behaviors were read and
/// found NOT to call <c>FailureResponse.Create</c> — <c>IAuditableRequest&lt;TResponse&gt;</c>
/// and <c>ILoggableRequest&lt;TResponse&gt;</c>, which must never fire this rule.
/// </remarks>
public class SK0040_PipelineMarkerResponseShapeMismatchAnalyzerTests
{
    private const string Stubs = """
        namespace MediatR
        {
            public interface IRequest<out TResponse> { }
        }

        namespace SharedKernel.Primitives.Results
        {
            public readonly struct Result
            {
                public static Result Failure(object error) => default;
            }

            public readonly struct Result<T>
            {
                public static Result<T> Failure(object error) => default;
            }
        }

        namespace SharedKernel.Application.Behaviors.Authorization
        {
            public interface IAuthorizeRequest { }
        }

        namespace SharedKernel.Application.Behaviors.Idempotency
        {
            public interface IIdempotentRequest { }
        }

        namespace SharedKernel.Application.Behaviors.Auditing
        {
            public interface IAuditableRequest<TResponse> { }
        }

        namespace SharedKernel.Application.Behaviors.Logging
        {
            public interface ILoggableRequest<TResponse> { }
        }

        """;

    // ---------------------------------------------------------------------------
    // Fire path
    // ---------------------------------------------------------------------------

    /// <summary>A plain-DTO-response command implementing <c>IAuthorizeRequest</c> must fire.</summary>
    [Fact]
    public async Task FirePath_AuthorizeRequestWithDtoResponse_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Authorization;

                    public sealed class OrderDto { }

                    public sealed class {|SK0040:BadAuthorizeCommand|} : IAuthorizeRequest, IRequest<OrderDto>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A plain-DTO-response command implementing <c>IIdempotentRequest</c> must fire.</summary>
    [Fact]
    public async Task FirePath_IdempotentRequestWithDtoResponse_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Idempotency;

                    public sealed class OrderDto { }

                    public sealed class {|SK0040:BadIdempotentCommand|} : IIdempotentRequest, IRequest<OrderDto>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A type implementing BOTH markers with a bad response must still fire exactly once, naming
    /// both markers in the message.
    /// </summary>
    [Fact]
    public async Task FirePath_BothMarkersWithDtoResponse_ReportsSingleDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Authorization;
                    using SharedKernel.Application.Behaviors.Idempotency;

                    public sealed class OrderDto { }

                    public sealed class {|SK0040:BadBothMarkersCommand|}
                        : IAuthorizeRequest, IIdempotentRequest, IRequest<OrderDto>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Pass path
    // ---------------------------------------------------------------------------

    /// <summary>A non-generic <c>Result</c> response must NOT fire.</summary>
    [Fact]
    public async Task PassPath_AuthorizeRequestWithResultResponse_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Authorization;
                    using SharedKernel.Primitives.Results;

                    public sealed class GoodAuthorizeCommand : IAuthorizeRequest, IRequest<Result>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A closed <c>Result&lt;T&gt;</c> response must NOT fire.</summary>
    [Fact]
    public async Task PassPath_IdempotentRequestWithClosedResultOfTResponse_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Idempotency;
                    using SharedKernel.Primitives.Results;

                    public sealed class GoodIdempotentCommand : IIdempotentRequest, IRequest<Result<int>>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A marker implemented with no <c>IRequest&lt;TResponse&gt;</c> at all — so no behavior can
    /// ever resolve into its pipeline — must NOT fire.
    /// </summary>
    [Fact]
    public async Task PassPath_AuthorizeRequestWithNoMediatRRequestInterface_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application.Behaviors.Authorization;

                    public sealed class NotEvenARequest : IAuthorizeRequest
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// <c>IAuditableRequest&lt;TResponse&gt;</c> — read <c>AuditingBehavior</c>'s source and
    /// confirmed it never calls <c>FailureResponse.Create</c>; it only forwards the response
    /// <c>next()</c> already produced and reads it through <c>ResponseOutcome.TryGetError</c>,
    /// which degrades gracefully for a non-Result response. Must NOT fire even with a plain DTO
    /// response.
    /// </summary>
    [Fact]
    public async Task PassPath_AuditableRequestWithDtoResponse_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Auditing;

                    public sealed class OrderDto { }

                    public sealed class AuditedCommand : IAuditableRequest<OrderDto>, IRequest<OrderDto>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// <c>ILoggableRequest&lt;TResponse&gt;</c> — read <c>LoggingBehavior</c>'s source and
    /// confirmed it never calls <c>FailureResponse.Create</c> either; it only reads
    /// <c>LoggableRequestFields</c>/<c>GetLoggableResponseFields</c> and classifies the response
    /// via the same graceful <c>ResponseOutcome</c> helper. Must NOT fire even with a plain DTO
    /// response.
    /// </summary>
    [Fact]
    public async Task PassPath_LoggableRequestWithDtoResponse_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Logging;

                    public sealed class OrderDto { }

                    public sealed class LoggedQuery : ILoggableRequest<OrderDto>, IRequest<OrderDto>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A generic request class declaring <c>IRequest&lt;TResult&gt;</c> against its own open type
    /// parameter cannot be resolved to a concrete shape at the declaration site — must NOT fire.
    /// </summary>
    [Fact]
    public async Task PassPath_OpenGenericResponseTypeParameter_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Authorization;

                    public sealed class GenericCommand<TResult> : IAuthorizeRequest, IRequest<TResult>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A closed <c>Result&lt;T&gt;</c> whose own type argument is still an open type parameter of
    /// the declaring generic class is still Result-shaped at the outer level — must NOT fire.
    /// </summary>
    [Fact]
    public async Task PassPath_ClosedResultOfOpenTypeParameter_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PipelineMarkerResponseShapeMismatchAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + """
                namespace Fixture.Requests
                {
                    using MediatR;
                    using SharedKernel.Application.Behaviors.Idempotency;
                    using SharedKernel.Primitives.Results;

                    public sealed class GenericIdempotentCommand<T> : IIdempotentRequest, IRequest<Result<T>>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
