using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0030 <see cref="ResultOutcomeDiscardedAnalyzer"/> — WO-049 P-299.</summary>
/// <remarks>
/// <para>
/// T-253–T-257: fire-path tests (bare invocation of a <c>Result</c>-returning method, bare
/// invocation of a <c>Result&lt;T&gt;</c>-returning method, bare <c>await</c> of a
/// <c>Task&lt;Result&lt;T&gt;&gt;</c>-returning method, bare <c>await</c> of a
/// <c>ValueTask&lt;Result&lt;T&gt;&gt;</c>-returning method, and a fluent chain whose OUTERMOST
/// call still resolves to <c>Result&lt;T&gt;</c>). T-258–T-265: pass-path tests (local
/// declaration, re-assignment, field assignment, return, argument, void-terminal member-access
/// chain, explicit discard, and a non-<c>IHasSuccessFlag</c> baseline). T-266–T-269:
/// real-pattern-audit pass-path fixtures, each modeled on (paraphrased from, never literally
/// copy-pasted) an actual shipped consumption shape found in
/// <c>SharedKernel.Application.Pipeline</c> (audited as <c>05.Application.Behaviors</c>)/<c>06.Persistence.EfCore</c>/
/// <c>07.Messaging.MassTransit</c>/<c>17.Workflows.Temporal</c>. T-270 is the real-source audit
/// record — see its own XML doc below for the full catalog and the one genuine finding.
/// </para>
/// <para>
/// Fixture stubs are self-contained in-compilation stand-ins for
/// <c>SharedKernel.Primitives.Results.IHasSuccessFlag</c>/<c>Result</c>/<c>Result&lt;T&gt;</c>
/// (no <c>ProjectReference</c> to the real <c>SharedKernel.Primitives</c> assembly) — the same
/// in-compilation-stub technique already established by SK0013/SK0017/SK0026/SK0029. The stub
/// namespace (<c>SharedKernel.Primitives.Results</c>) and the marker interface's simple name
/// (<c>IHasSuccessFlag</c>) intentionally match the real shipped type exactly, per SK0017–SK0019's
/// "simple name + namespace prefix" discriminator.
/// </para>
/// </remarks>
public class SK0030_ResultOutcomeDiscardedAnalyzerTests
{
    private const string ResultStubs = """
        namespace SharedKernel.Primitives.Errors
        {
            public sealed class Error
            {
                public static readonly Error None = new();
            }
        }

        namespace SharedKernel.Primitives.Results
        {
            public interface IHasSuccessFlag
            {
                bool IsSuccess { get; }
            }

            public readonly struct Result : IHasSuccessFlag
            {
                private Result(bool isSuccess) => IsSuccess = isSuccess;

                public bool IsSuccess { get; }

                public bool IsFailure => !IsSuccess;

                public static Result Success() => new(true);

                public static Result Failure() => new(false);
            }

            public sealed class Result<T> : IHasSuccessFlag
            {
                private Result(T value, bool isSuccess)
                {
                    Value = value;
                    IsSuccess = isSuccess;
                }

                public bool IsSuccess { get; }

                public bool IsFailure => !IsSuccess;

                public T Value { get; }

                public static Result<T> Success(T value) => new(value, true);

                public static Result<T> Failure() => new(default!, false);

                public static implicit operator Result<T>(SharedKernel.Primitives.Errors.Error error) =>
                    new(default!, false);
            }

            public static class ResultFixtureExtensions
            {
                public static Result<TOut> Map<T, TOut>(this Result<T> result, System.Func<T, TOut> map) =>
                    Result<TOut>.Success(map(result.Value));

                public static void Match<T>(
                    this Result<T> result,
                    System.Action<T> onSuccess,
                    System.Action onFailure)
                {
                }
            }
        }

        """;

    private static CSharpAnalyzerTest<ResultOutcomeDiscardedAnalyzer, DefaultVerifier> CreateTest(string source) =>
        new() { TestCode = ResultStubs + source };

    // ---------------------------------------------------------------------------
    // T-253 — Fire path: bare-statement invocation of a method returning Result (non-generic)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_BareInvocationReturningNonGenericResult_ReportsSk0030()
    {
        var test = CreateTest(
            """
            namespace Fixture.T253
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result Foo() => Result.Success();

                    public void Bar()
                    {
                        {|SK0030:Foo()|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-254 — Fire path: bare-statement invocation of a method returning Result<T>
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_BareInvocationReturningGenericResult_ReportsSk0030()
    {
        var test = CreateTest(
            """
            namespace Fixture.T254
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public void Bar()
                    {
                        {|SK0030:Foo()|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-255 — Fire path: bare "await FooAsync();" where FooAsync returns Task<Result<T>>
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_BareAwaitOfTaskOfGenericResult_ReportsSk0030()
    {
        var test = CreateTest(
            """
            namespace Fixture.T255
            {
                using SharedKernel.Primitives.Results;
                using System.Threading.Tasks;

                public class Sample
                {
                    public static Task<Result<int>> FooAsync() => Task.FromResult(Result<int>.Success(1));

                    public async Task Bar()
                    {
                        {|SK0030:await FooAsync()|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-256 — Fire path: bare "await FooAsync();" where FooAsync returns ValueTask<Result<T>>
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_BareAwaitOfValueTaskOfGenericResult_ReportsSk0030()
    {
        var test = CreateTest(
            """
            namespace Fixture.T256
            {
                using SharedKernel.Primitives.Results;
                using System.Threading.Tasks;

                public class Sample
                {
                    public static ValueTask<Result<int>> FooAsync() => new(Result<int>.Success(1));

                    public async Task Bar()
                    {
                        {|SK0030:await FooAsync()|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-257 — Fire path: fluent-chain bare statement whose outermost call still returns Result<T>
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_FluentChainOutermostCallStillReturnsResult_ReportsSk0030()
    {
        var test = CreateTest(
            """
            namespace Fixture.T257
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public void Bar()
                    {
                        {|SK0030:Foo().Map(x => x + 1)|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-258 — Pass path: "var result = Foo();" is a LocalDeclarationStatementSyntax, not
    // an ExpressionStatementSyntax
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_LocalDeclarationAssignment_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T258
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public bool Bar()
                    {
                        var result = Foo();
                        return result.IsSuccess;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-259 — Pass path: "result = Foo();" re-assigning an existing local
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ReassignmentOfExistingLocal_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T259
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public bool Bar()
                    {
                        Result<int> result = Foo();
                        result = Foo();
                        return result.IsSuccess;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-260 — Pass path: "_field = Foo();" assigning to a field
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_FieldAssignment_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T260
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    private Result<int> _field;

                    public static Result<int> Foo() => Result<int>.Success(1);

                    public void Bar()
                    {
                        _field = Foo();
                    }

                    public bool IsFieldSuccess() => _field.IsSuccess;
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-261 — Pass path: "return Foo();"
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ReturnStatement_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T261
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public Result<int> Bar()
                    {
                        return Foo();
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-262 — Pass path: "Bar(Foo());" — Foo()'s Result passed as an argument to a void-returning
    // method; the OUTERMOST call (Bar) resolves to void, not IHasSuccessFlag
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ResultPassedAsArgumentToVoidMethod_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T262
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public static void Consume(Result<int> result)
                    {
                    }

                    public void Bar()
                    {
                        Consume(Foo());
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-263 — Pass path: "Foo().Match(onSuccess, onFailure);" — the terminal Match overload
    // returns void, mirroring the real SharedKernel.Core ResultExtensions.Match(this Result, ...)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_VoidTerminalMatchChain_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T263
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public void Bar()
                    {
                        Foo().Match(x => { }, () => { });
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-264 — Pass path: "_ = Foo();" explicit discard assignment
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ExplicitDiscardAssignment_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T264
            {
                using SharedKernel.Primitives.Results;

                public class Sample
                {
                    public static Result<int> Foo() => Result<int>.Success(1);

                    public void Bar()
                    {
                        _ = Foo();
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-265 — Pass path, baseline: a bare-statement invocation of a method NOT implementing
    // IHasSuccessFlag (a void-returning method, and a bare-awaited plain Task-returning method)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_BaselineNonResultVoidMethodAndPlainTask_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T265
            {
                using System.Threading.Tasks;

                public class Sample
                {
                    public static void Foo()
                    {
                    }

                    public static Task BarAsync() => Task.CompletedTask;

                    public void Baz()
                    {
                        Foo();
                    }

                    public async Task QuxAsync()
                    {
                        await BarAsync();
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-266 — Real-pattern audit, pass path: modeled on SharedKernel.Application.Pipeline's (then 05.Application.Behaviors) actual
    // pipeline-behavior shape (e.g. CacheInvalidationBehavior / ResponseOutcomeClassifier) —
    // "var result = await next(); if (result is IHasSuccessFlag f && !f.IsSuccess) { ... }
    // return result;"
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RealSourceAudit_PipelineBehaviorNextThenIsSuccessFlagPatternMatch_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T266
            {
                using SharedKernel.Primitives.Results;
                using System.Threading.Tasks;

                public delegate Task<TResponse> RequestHandlerDelegateFixture<TResponse>();

                public class PipelineBehaviorFixture<TResponse>
                {
                    public async Task<TResponse> HandleAsync(RequestHandlerDelegateFixture<TResponse> next)
                    {
                        var result = await next();

                        if (result is IHasSuccessFlag flag && !flag.IsSuccess)
                        {
                            // A real behavior short-circuits, logs, or tags metrics here.
                        }

                        return result;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-267 — Real-pattern audit, pass path: modeled on 06.Persistence.EfCore's actual
    // repository-method shape — a method whose final statements are "return
    // Result<T>.Success(entity);" / "return error;" (implicit Result<T> conversion)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RealSourceAudit_RepositoryReturnSuccessOrImplicitErrorConversion_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T267
            {
                using SharedKernel.Primitives.Errors;
                using SharedKernel.Primitives.Results;

                public class EntityFixture
                {
                }

                public class RepositoryFixture
                {
                    public Result<EntityFixture> GetById(bool found, EntityFixture entity, Error error)
                    {
                        if (!found)
                        {
                            return error;
                        }

                        return Result<EntityFixture>.Success(entity);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-268 — Real-pattern audit, pass path: modeled on 07.Messaging.MassTransit's actual
    // consumer shape — "var result = await _mediator.Send(command, context.CancellationToken);
    // if (result.IsFailure) { throw new ConsumerException(...); }"
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RealSourceAudit_ConsumerAwaitedSendThenThrowOnFailure_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T268
            {
                using SharedKernel.Primitives.Results;
                using System.Threading;
                using System.Threading.Tasks;

                public class ConsumerExceptionFixture : System.Exception
                {
                    public ConsumerExceptionFixture(string message)
                        : base(message)
                    {
                    }
                }

                public interface IMediatorFixture
                {
                    Task<Result> Send(object command, CancellationToken ct);
                }

                public class ConsumerFixture
                {
                    public async Task Consume(IMediatorFixture mediator, object command, CancellationToken ct)
                    {
                        var result = await mediator.Send(command, ct);

                        if (result.IsFailure)
                        {
                            throw new ConsumerExceptionFixture("command failed");
                        }
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-269 — Real-pattern audit, pass path: modeled on 17.Workflows.Temporal's
    // CommandActivity<TCommand>/WorkflowFailureMapper shape — a
    // "return WorkflowFailureMapper.ToApplicationFailureAsync(result);"-style mapping return
    // statement (a ReturnStatementSyntax, never registered regardless of the wrapped await)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RealSourceAudit_CommandActivityReturnAwaitedFailureMapper_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.T269
            {
                using SharedKernel.Primitives.Results;
                using System.Threading.Tasks;

                public class WorkflowFailureMapperFixture
                {
                    public static Task<int> ToApplicationFailureAsync(Result result) => Task.FromResult(0);
                }

                public class CommandActivityFixture
                {
                    public async Task<int> ExecuteAsync(Result result)
                    {
                        if (result.IsFailure)
                        {
                            return await WorkflowFailureMapperFixture.ToApplicationFailureAsync(result);
                        }

                        return 0;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-270 — Real-source audit record
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Real-source audit record (T-270) — a manual grep/read across the shipped production
    /// source (excluding <c>bin/</c>, <c>obj/</c>, and every <c>*.Tests</c> project/directory) of
    /// <c>05.Application.Behaviors</c> (plus <c>05.Application</c> where it declares
    /// <c>Result</c>-returning contracts), <c>06.Persistence.EfCore</c>,
    /// <c>07.Messaging.MassTransit</c>, and <c>17.Workflows.Temporal</c> — all four already
    /// Published as of this phase's authoring (2026-07-27) — performed per this domain's own
    /// Test Rule ("never test analyzers by compiling real source files manually"): every distinct
    /// shape is encoded as a representative, paraphrased fixture in T-266–T-269 above, never a
    /// literal copy-paste of the real <c>.cs</c> files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Historical record.</b> The paths below name the packages as they were audited. Since
    /// WO-086, <c>05.Application.Behaviors</c> is <c>SharedKernel.Application.Pipeline</c> and the
    /// sender is the kernel's own <c>SharedKernel.Application.Messaging.ISender</c> (MediatR sits
    /// behind <c>SharedKernel.Application.Mediator.MediatR</c>); the fire-and-forget consumer named
    /// in the finding was removed before first publish (P-544). The fixtures still model the
    /// shapes, which is what the rule checks.
    /// </para>
    /// <para><b>Distinct consumption shapes found (cross-referenced to their fixture):</b></para>
    /// <list type="number">
    /// <item>Awaited, assigned to a typed local, branched on <c>.IsFailure</c>, then
    /// <c>.Error</c>/<c>.Value</c> accessed to build a different return shape —
    /// <c>17.Workflows.Temporal/Authoring/CommandActivity.cs</c>,
    /// <c>Dispatch/WorkflowDispatcher.cs</c>. Modeled by T-268/T-269.</item>
    /// <item>Returned directly as the tail of an async method (<c>return await next();</c>), no
    /// local variable — <c>05.Application.Behaviors/Validation/ValidationBehavior.cs</c>,
    /// <c>Tracing/TracingBehavior.cs</c>. A <c>ReturnStatementSyntax</c>, structurally identical
    /// to T-261/T-269's pass path (never an <c>ExpressionStatementSyntax</c> regardless of the
    /// wrapped <c>await</c>).</item>
    /// <item>Assigned to a generic <c>var response</c>/<c>TResponse</c> local, then returned
    /// unconditionally — <c>Transaction/TransactionBehavior.cs</c>,
    /// <c>CacheInvalidation/CacheInvalidationBehavior.cs</c>. Modeled by T-266.</item>
    /// <item>Runtime <c>is IHasSuccessFlag flag</c> pattern-match on a generic response, then
    /// <c>.IsSuccess</c> branch — <c>CacheInvalidationBehavior.cs</c>,
    /// <c>Shared/ResponseOutcomeClassifier.cs</c>. Modeled by T-266.</item>
    /// <item>Passed as a method argument to a classification helper —
    /// <c>Metrics/MetricsBehavior.cs</c>, <c>Logging/LoggingBehavior.cs</c>. Modeled by T-262.</item>
    /// <item><c>.Error</c>/<c>.Value</c> member access after an <c>IsFailure</c> check, feeding a
    /// different return shape (exception construction or unwrapped payload) —
    /// <c>CommandActivity.cs</c>, <c>Failures/WorkflowFailureMapper.cs</c>,
    /// <c>06.Persistence.EfCore/Encryption/EncryptedValueConverter.cs</c>. Modeled by T-267/T-268.</item>
    /// <item>Constructed via a static factory and returned directly as a short-circuit failure —
    /// <c>Authorization/AuthorizationBehavior.cs</c>, <c>Idempotency/IdempotentCommandBehavior.cs</c>.
    /// A <c>ReturnStatementSyntax</c>, modeled by T-261.</item>
    /// <item>Returned directly from a static deserialization helper, no local assignment —
    /// <c>IdempotentCommandBehavior.cs</c>. Modeled by T-261.</item>
    /// <item>Implicit <c>Error</c>-to-<c>Result&lt;T&gt;</c> conversion returned directly —
    /// <c>06.Persistence.EfCore</c> repository methods. Modeled by T-267.</item>
    /// </list>
    /// <para>
    /// <b>07.Messaging.MassTransit correction:</b> that project's own production code never
    /// touches <c>SharedKernel.Primitives.Results.Result</c> at all — its
    /// <c>MessagingOptionsValidator</c> returns the unrelated
    /// <c>Microsoft.Extensions.Options.ValidateOptionsResult</c>. T-268's fixture models the
    /// architecturally-analogous consumer shape (awaited send, branch on failure, throw) that
    /// WOULD apply the moment a MassTransit consumer in this platform dispatches a
    /// <c>Result</c>-returning command, per this phase's own acceptance criteria.
    /// </para>
    /// <para>
    /// <b>GENUINE FINDING — one confirmed bare-statement discard, T-270's core result.</b>
    /// <c>05.Application.Behaviors/FireAndForget/FireAndForgetBackgroundConsumer.cs</c>, the
    /// statement <c>await sender.Send(command, stoppingToken).ConfigureAwait(false);</c> (as of
    /// this phase's authoring, line 57). <c>command</c> is statically typed
    /// <c>IFireAndForgetCommand</c>, which extends <c>ICommand : ICommandBase,
    /// IRequest&lt;Result&gt;</c> — the generic <c>ISender.Send&lt;TResponse&gt;</c> (then MediatR's; now the kernel's <c>SharedKernel.Application.Messaging.ISender</c>)
    /// overload is selected at compile time with <c>TResponse</c> inferred as
    /// <c>SharedKernel.Primitives.Results.Result</c>, so the awaited expression's resolved type
    /// is <c>Result</c>, which implements <c>IHasSuccessFlag</c> — SK0030's exact fire condition.
    /// The outcome is never assigned, returned, passed as an argument, or explicitly discarded
    /// via <c>_ = ...</c>. The class's own XML doc states "the caller already relinquished
    /// result observation" — the discard is a deliberate DESIGN choice (a faulted handler is
    /// already caught and logged by the surrounding <c>try/catch</c> at the exception level), but
    /// it was never written as an EXPLICIT <c>_ = await ...</c> discard, which is precisely the
    /// ambiguity SK0030 exists to force into the open: a reader cannot distinguish "nobody
    /// remembered to check this" from "this is intentionally fire-and-forget" without re-reading
    /// the whole method's surrounding doc comment. This is a genuine, previously-invisible defect
    /// in <c>05.Application</c>'s own shipped code — NOT a false positive, and per this
    /// analyzer's own design rules (Implementation Rule 7 / this phase's Test Rules), it is
    /// recorded here rather than "fixed" by narrowing SK0030's trigger condition.
    /// <see cref="RealSourceAudit_FireAndForgetBackgroundConsumerShape_ReportsSk0030"/> reproduces
    /// this exact shape (paraphrased into a fixture, never the literal file) as a FIRE-path test,
    /// proving SK0030 mechanically catches this real, already-shipped pattern.
    /// </para>
    /// <para>
    /// <b>Escalation:</b> recorded as a candidate follow-up work order for <c>05.Application</c>
    /// — rewrite the call site as
    /// <c>_ = await sender.Send(command, stoppingToken).ConfigureAwait(false);</c> (zero
    /// behavior change; the handler-fault <c>try/catch</c> around the loop already provides the
    /// only observation this dispatch path is designed to have). <c>00.Governance</c> does not
    /// implement this fix — production code in another domain is outside this agent's
    /// jurisdiction; see the closeout Changelog entry in <c>00.Governance/CLAUDE.md</c> for the
    /// full record.
    /// </para>
    /// <para>No other bare-statement discard of a <c>Result</c>/<c>Result&lt;T&gt;</c>/
    /// <c>Task&lt;Result&lt;T&gt;&gt;</c>/<c>ValueTask&lt;Result&lt;T&gt;&gt;</c>-returning call
    /// was found anywhere else in the four audited projects' production code.</para>
    /// </remarks>
    [Fact]
    public async Task RealSourceAudit_FireAndForgetBackgroundConsumerShape_ReportsSk0030()
    {
        var test = CreateTest(
            """
            namespace Fixture.T270
            {
                using SharedKernel.Primitives.Results;
                using System.Threading;
                using System.Threading.Tasks;

                public interface IFireAndForgetCommandFixture
                {
                }

                public interface ISenderFixture
                {
                    Task<Result> Send(IFireAndForgetCommandFixture command, CancellationToken ct);
                }

                public class FireAndForgetBackgroundConsumerFixture
                {
                    public async Task ExecuteAsync(
                        ISenderFixture sender,
                        IFireAndForgetCommandFixture command,
                        CancellationToken stoppingToken)
                    {
                        {|SK0030:await sender.Send(command, stoppingToken).ConfigureAwait(false)|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }
}
