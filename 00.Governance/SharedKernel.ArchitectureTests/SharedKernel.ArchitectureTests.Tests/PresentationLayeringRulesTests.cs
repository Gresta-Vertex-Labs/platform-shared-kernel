using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="PresentationLayeringRules"/> — introduced by WO-031 P-199, adjusted for the P-562 redesign.
/// </summary>
/// <remarks>
/// <para>
/// T-143/T-144 cover <see cref="PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi"/>.
/// T-145/T-146 cover <see cref="PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>.
/// T-360/T-361 cover <see cref="PresentationLayeringRules.GrpcNeverReferencesContracts"/>.
/// </para>
/// <para>
/// Contrived in-memory fixtures are built via <see cref="CSharpCompilation"/> +
/// <see cref="MetadataReference.CreateFromFile(string)"/> — the same technique used by
/// <c>RedisTopologyRulesTests</c>, <c>ServiceDefaultsGovernanceRulesTests</c>, and
/// <c>HealthCheckConstantsUsageRulesTests</c>. Real ASP.NET Core types
/// (<c>Microsoft.AspNetCore.Mvc.ProblemDetails</c>, <c>Microsoft.AspNetCore.Http.IResult</c>,
/// etc.) are stubbed locally in each fixture's source under the matching namespace/name so the
/// predicates' exact <c>FullName</c>/<c>Name</c> matching resolves correctly without requiring an
/// ASP.NET Core framework reference in this test project.
/// </para>
/// <para>
/// <strong>P-562.</strong> The fixtures follow the redesigned WebApi surface: typed results (<c>ToOk</c>,
/// <c>ToErrorResult</c>, <c>ErrorHttpResult</c>) instead of the deleted <c>ToProblemDetailsResult</c>, and the
/// typed-results union and <c>IActionResult</c> return types the rule now recognizes. The real-assembly tests pass
/// all three sibling packages — <c>.OpenApi</c>, <c>.SignalR</c>, <c>.Grpc</c> — through the WebApi-exclusion rules
/// (only <c>SharedKernel.Presentation.WebApi</c> is exempt) and lock the OpenAPI stack inside the add-on.
/// </para>
/// </remarks>
public class PresentationLayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // T-143 — Fire path: direct construction of ProblemDetails via newobj
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-143: A contrived fixture with a type directly constructing
    /// <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> via <c>newobj</c> must fail
    /// <see cref="PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi"/>,
    /// naming the offending type/method.
    /// </summary>
    [Fact]
    public void NoDirectProblemDetailsConstructionOutsideWebApi_DirectConstruction_RuleFails()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class ProblemDetails
                {
                    public string? Title { get; set; }
                    public int? Status { get; set; }
                }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Mvc;

                public static class OrderEndpoints
                {
                    public static ProblemDetails BuildErrorBody()
                    {
                        return new ProblemDetails { Title = "Bad request", Status = 400 };
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.ProblemDetails.DirectConstruction", source);

        var result = PresentationLayeringRules
            .NoDirectProblemDetailsConstructionOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "BuildErrorBody directly constructs Microsoft.AspNetCore.Mvc.ProblemDetails via newobj");

        result.FailingTypeNames.Should().Contain(
            "Application.Endpoints.OrderEndpoints",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-144 — Pass path: no direct construction (factory method only); covers
    // HttpValidationProblemDetails fire path as a companion case
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-144: A contrived fixture with no direct <c>ProblemDetails</c> /
    /// <c>HttpValidationProblemDetails</c> construction (constructs via a factory method only)
    /// must pass
    /// <see cref="PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi"/>.
    /// </summary>
    [Fact]
    public void NoDirectProblemDetailsConstructionOutsideWebApi_FactoryMethodOnly_RulePasses()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class ProblemDetails
                {
                    public string? Title { get; set; }
                    public int? Status { get; set; }
                }
            }

            namespace ExternalFactory
            {
                using Microsoft.AspNetCore.Mvc;

                // Simulates a factory living in a different assembly (e.g. the real
                // SharedKernel.Presentation.WebApi, which the caller never passes to this
                // rule). This fixture assembly's own types perform no ProblemDetails
                // construction at all.
                public static class ErrorProblemDetailsExtensions
                {
                    public static ProblemDetails ToProblemDetails(string title, int status) =>
                        throw new System.NotImplementedException();
                }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Mvc;
                using ExternalFactory;

                public static class OrderEndpoints
                {
                    public static ProblemDetails BuildErrorBody()
                    {
                        return ErrorProblemDetailsExtensions.ToProblemDetails("Bad request", 400);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.ProblemDetails.FactoryMethodOnly", source);

        var result = PresentationLayeringRules
            .NoDirectProblemDetailsConstructionOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "BuildErrorBody routes through a factory method call rather than directly " +
                     "constructing ProblemDetails — no type in this fixture assembly contains a " +
                     "newobj ProblemDetails instruction");
    }

    /// <summary>
    /// Companion fire-path case: a contrived fixture with a type directly constructing
    /// <c>Microsoft.AspNetCore.Http.HttpValidationProblemDetails</c> via <c>newobj</c> must also
    /// fail
    /// <see cref="PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi"/>.
    /// </summary>
    [Fact]
    public void NoDirectProblemDetailsConstructionOutsideWebApi_DirectHttpValidationProblemDetailsConstruction_RuleFails()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Http
            {
                public class HttpValidationProblemDetails
                {
                    public string? Title { get; set; }
                }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Http;

                public static class ValidationEndpoints
                {
                    public static HttpValidationProblemDetails BuildValidationErrorBody()
                    {
                        return new HttpValidationProblemDetails { Title = "Invalid" };
                    }
                }
            }
            """;

        var assembly = CompileInMemory(
            "Fixture.ProblemDetails.DirectHttpValidationConstruction",
            source);

        var result = PresentationLayeringRules
            .NoDirectProblemDetailsConstructionOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "BuildValidationErrorBody directly constructs " +
                     "Microsoft.AspNetCore.Http.HttpValidationProblemDetails via newobj");

        result.FailingTypeNames.Should().Contain(
            "Application.Endpoints.ValidationEndpoints",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-145 — Fire path: inline IsSuccess/IsFailure branch before IResult/ActionResult
    // return, with no mapping through the WebApi core
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-145: A contrived fixture method reading <c>Result.IsSuccess</c>/<c>IsFailure</c> and
    /// returning <c>IResult</c>/<c>ActionResult</c>/<c>ActionResult&lt;T&gt;</c> without mapping through the WebApi
    /// core must fail
    /// <see cref="PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>,
    /// naming the offending type/method.
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_InlineBranchNoEscapeHatch_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Primitives
            {
                public class Result
                {
                    public bool IsSuccess { get; }
                    public bool IsFailure => !IsSuccess;

                    public Result(bool isSuccess)
                    {
                        IsSuccess = isSuccess;
                    }
                }
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }

                public sealed class OkResult : IResult { }
                public sealed class ProblemResult : IResult { }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Http;
                using SharedKernel.Primitives;

                public static class OrderEndpoints
                {
                    public static IResult Handle(Result result)
                    {
                        if (result.IsSuccess)
                        {
                            return new OkResult();
                        }

                        return new ProblemResult();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.NoEscapeHatch", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "Handle reads Result.IsSuccess and returns IResult without mapping " +
                     "through the WebApi core in the same method");

        result.FailingTypeNames.Should().Contain(
            "Application.Endpoints.OrderEndpoints",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-146 — Pass path: same two signals, but the method maps through the WebApi
    // core's typed results; companion vacuous-pass path: no IsSuccess/IsFailure usage
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-146: A contrived fixture exhibiting the same two signals as T-145, but the method also
    /// calls <c>SharedKernel.Presentation.WebApi.ResultHttpExtensions.ToOk</c>, must pass
    /// <see cref="PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>.
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_EscapeHatchPresent_RulePasses()
    {
        const string source = """
            namespace SharedKernel.Primitives
            {
                public class Result
                {
                    public bool IsSuccess { get; }
                    public bool IsFailure => !IsSuccess;

                    public Result(bool isSuccess)
                    {
                        IsSuccess = isSuccess;
                    }
                }
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }

                public sealed class OkResult : IResult { }
            }

            namespace SharedKernel.Presentation.WebApi
            {
                using Microsoft.AspNetCore.Http;
                using SharedKernel.Primitives;

                public static class ResultHttpExtensions
                {
                    public static IResult ToOk(this Result result)
                    {
                        return new OkResult();
                    }
                }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Http;
                using SharedKernel.Presentation.WebApi;
                using SharedKernel.Primitives;

                public static class OrderEndpoints
                {
                    public static IResult Handle(Result result)
                    {
                        var diagnosticCheck = result.IsSuccess;
                        System.Console.WriteLine(diagnosticCheck);
                        return result.ToOk();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.EscapeHatchPresent", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Handle maps through ResultHttpExtensions.ToOk in the same method, suppressing the " +
                     "violation despite the IsSuccess read and IResult return type both being present");
    }

    // ---------------------------------------------------------------------------
    // P-562 — the rule after the redesign: typed-results unions and IActionResult are HTTP
    // results; the failure branch may route through ToErrorResult or ErrorHttpResult; a
    // same-named method on another type is not the platform mapping
    // ---------------------------------------------------------------------------

    /// <summary>
    /// P-562: typed results made <c>Results&lt;T1, T2&gt;</c> the everyday return type, so a hand-rolled branch
    /// returning a typed-results union must fail — the redesign's most likely violation.
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_TypedResultsUnionReturn_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Primitives
            {
                public class Result<T>
                {
                    public bool IsSuccess { get; set; }
                    public T Value { get; set; } = default!;
                }
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }
            }

            namespace Microsoft.AspNetCore.Http.HttpResults
            {
                public sealed class Ok<T> : Microsoft.AspNetCore.Http.IResult { }
                public sealed class ProblemHttpResult : Microsoft.AspNetCore.Http.IResult { }
                public sealed class Results<T1, T2> : Microsoft.AspNetCore.Http.IResult
                {
                    public static Results<T1, T2> From(object result) => new Results<T1, T2>();
                }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Http.HttpResults;
                using SharedKernel.Primitives;

                public static class OrderEndpoints
                {
                    public static Results<Ok<int>, ProblemHttpResult> Handle(Result<int> result)
                    {
                        if (result.IsSuccess)
                        {
                            return Results<Ok<int>, ProblemHttpResult>.From(new Ok<int>());
                        }

                        return Results<Ok<int>, ProblemHttpResult>.From(new ProblemHttpResult());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.TypedResultsUnion", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "Handle reads Result<T>.IsSuccess and returns a typed-results union built by hand");
        result.FailingTypeNames.Should().Contain("Application.Endpoints.OrderEndpoints");
    }

    /// <summary>
    /// P-562: MVC's everyday action return type, <c>IActionResult</c>, is an HTTP result too (the core maps
    /// <c>Result</c> to it with <c>ToActionResult</c>).
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_IActionResultReturn_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Primitives
            {
                public class Result
                {
                    public bool IsFailure { get; set; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public sealed class OkResult : IActionResult { }
                public sealed class NotFoundResult : IActionResult { }
            }

            namespace Application.Controllers
            {
                using Microsoft.AspNetCore.Mvc;
                using SharedKernel.Primitives;

                public sealed class OrdersController
                {
                    public IActionResult Cancel(Result result)
                    {
                        if (result.IsFailure)
                        {
                            return new NotFoundResult();
                        }

                        return new OkResult();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.IActionResult", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "Cancel reads Result.IsFailure and returns an IActionResult it built by hand");
        result.FailingTypeNames.Should().Contain("Application.Controllers.OrdersController");
    }

    /// <summary>
    /// P-562: a failure branch written out but routed through the core — <c>error.ToErrorResult()</c> or
    /// <c>new ErrorHttpResult(error)</c> — carries the platform's status, code, localization and redaction, so it
    /// passes.
    /// </summary>
    [Theory]
    [InlineData("result.Error.ToErrorResult()")]
    [InlineData("new ErrorHttpResult(result.Error)")]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_FailureBranchThroughTheCore_RulePasses(string failureBranch)
    {
        var source = $$"""
            namespace SharedKernel.Primitives
            {
                public sealed class Error { }

                public class Result<T>
                {
                    public bool IsFailure { get; set; }
                    public T Value { get; set; } = default!;
                    public Error Error { get; set; } = new Error();
                }
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }
                public sealed class OkResult : IResult { }
            }

            namespace SharedKernel.Presentation.WebApi.Errors
            {
                public sealed class ErrorHttpResult : Microsoft.AspNetCore.Http.IResult
                {
                    public ErrorHttpResult(SharedKernel.Primitives.Error error) { }
                }
            }

            namespace SharedKernel.Presentation.WebApi
            {
                using SharedKernel.Presentation.WebApi.Errors;
                using SharedKernel.Primitives;

                public static class ResultHttpExtensions
                {
                    public static ErrorHttpResult ToErrorResult(this Error error) => new ErrorHttpResult(error);
                }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Http;
                using SharedKernel.Presentation.WebApi;
                using SharedKernel.Presentation.WebApi.Errors;
                using SharedKernel.Primitives;

                public static class OrderEndpoints
                {
                    public static IResult Handle(Result<int> result)
                    {
                        if (result.IsFailure)
                        {
                            return {{failureBranch}};
                        }

                        return new OkResult();
                    }
                }
            }
            """;

        var assembly = CompileInMemory($"Fixture.InlineResultBranch.ThroughTheCore.{failureBranch.Length}", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Handle's failure branch goes through the WebApi core ({failureBranch})");
    }

    /// <summary>
    /// P-562: the escape hatch is matched by declaring type, not by name — a service's own extension method named
    /// <c>ToOk</c> is a hand-rolled mapping, not the platform's.
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_SameNamedMethodOnAnotherType_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Primitives
            {
                public class Result
                {
                    public bool IsSuccess { get; set; }
                }
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }
                public sealed class OkResult : IResult { }
                public sealed class BadRequestResult : IResult { }
            }

            namespace Application.Http
            {
                using Microsoft.AspNetCore.Http;
                using SharedKernel.Primitives;

                public static class HomeGrownResultExtensions
                {
                    public static IResult ToOk(this Result result) => new OkResult();
                }
            }

            namespace Application.Endpoints
            {
                using Application.Http;
                using Microsoft.AspNetCore.Http;
                using SharedKernel.Primitives;

                public static class OrderEndpoints
                {
                    public static IResult Handle(Result result)
                    {
                        if (!result.IsSuccess)
                        {
                            return new BadRequestResult();
                        }

                        return result.ToOk();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.HomeGrownToOk", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "a ToOk declared outside SharedKernel.Presentation.WebApi is not the platform's mapping");
        result.FailingTypeNames.Should().Contain("Application.Endpoints.OrderEndpoints");
    }

    /// <summary>
    /// Companion vacuous-pass case: a contrived fixture with no <c>IsSuccess</c>/<c>IsFailure</c>
    /// usage at all must pass
    /// <see cref="PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>.
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_NoIsSuccessOrIsFailureUsage_RulePassesVacuously()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }

                public sealed class OkResult : IResult { }
            }

            namespace Application.Endpoints
            {
                using Microsoft.AspNetCore.Http;

                public static class PingEndpoints
                {
                    public static IResult Handle()
                    {
                        return new OkResult();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.NoIsSuccessUsage", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Handle never reads Result.IsSuccess/IsFailure — there is nothing yet to enforce");
    }

    // ---------------------------------------------------------------------------
    // T-360 — Fire path: contrived assembly shaped like SharedKernel.Presentation.Grpc
    // references SharedKernel.Contracts
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-360 (coordinator-directed extension, WO-074, folded into P-469): a contrived assembly
    /// shaped like <c>SharedKernel.Presentation.Grpc</c> whose type references
    /// <c>SharedKernel.Contracts</c> must fail
    /// <see cref="PresentationLayeringRules.GrpcNeverReferencesContracts"/> — mirrors
    /// <c>CommunicationLayeringRulesTests.GrpcNeverReferencesContracts_ContractsReference_RuleFails</c>
    /// (T-127) for the sibling <c>11.Communication</c> rule.
    /// </summary>
    [Fact]
    public void GrpcNeverReferencesContracts_ContractsReference_RuleFails()
    {
        const string contractsStubSource = """
            namespace SharedKernel.Contracts
            {
                public class PagedList<T> { }
            }
            """;

        const string grpcSource = """
            namespace SharedKernel.Presentation.Grpc
            {
                public class GrpcResponseMapper
                {
                    // Violation: a direct use of SharedKernel.Contracts, exactly the gap this
                    // rule closes despite SharedKernel.Presentation.Grpc's legitimate transitive
                    // reference chain through SharedKernel.Presentation.WebApi.
                    private readonly SharedKernel.Contracts.PagedList<object> _paged;

                    public GrpcResponseMapper(SharedKernel.Contracts.PagedList<object> paged)
                    {
                        _paged = paged;
                    }
                }
            }
            """;

        var contractsAssembly = CompileInMemory(
            "Fixture.PresentationGrpcContracts.SharedKernel.Contracts.Stub",
            contractsStubSource);

        var grpcAssembly = CompileInMemory(
            "Fixture.PresentationGrpcContracts.ViolatingGrpc",
            grpcSource,
            extraReferences: new[] { contractsAssembly });

        var result = PresentationLayeringRules
            .GrpcNeverReferencesContracts(grpcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "GrpcResponseMapper depends on SharedKernel.Contracts — forbidden for " +
                     "SharedKernel.Presentation.Grpc per the root CLAUDE.md Hard rule");
    }

    // ---------------------------------------------------------------------------
    // T-361 — Pass path: the real SharedKernel.Presentation.Grpc assembly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-361 (coordinator-directed extension, WO-074, folded into P-469): the real, currently-built
    /// <c>SharedKernel.Presentation.Grpc</c> assembly must pass
    /// <see cref="PresentationLayeringRules.GrpcNeverReferencesContracts"/> with zero violations —
    /// this is the empirical proof that NetArchTest's <c>NotHaveDependencyOn</c> correctly
    /// distinguishes "reachable via the reference closure" (true today, because of the deliberate
    /// <c>SharedKernel.Presentation.WebApi</c> reference) from "actually used by a type in this
    /// assembly" (false today — no type does), so this rule is a sufficient mechanical lock without
    /// requiring the <c>SharedKernel.Presentation.WebApi</c> reference itself to be removed.
    /// </summary>
    [Fact]
    public void GrpcNeverReferencesContracts_RealGrpcAssembly_RulePasses()
    {
        var grpcAssembly = typeof(SharedKernel.Presentation.Grpc.GrpcResultExtensions).Assembly;

        var result = PresentationLayeringRules
            .GrpcNeverReferencesContracts(grpcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "no type in the real SharedKernel.Presentation.Grpc assembly actually uses a " +
                     "SharedKernel.Contracts type; neither it nor SharedKernel.Presentation.WebApi references Contracts");
    }

    // ---------------------------------------------------------------------------
    // P-562 — the real presentation assemblies: only the WebApi core is exempt
    // ---------------------------------------------------------------------------

    /// <summary>
    /// P-562: the OpenAPI add-on, SignalR and gRPC packages are not legitimate <c>ProblemDetails</c> construction
    /// sites — SignalR and gRPC present errors through the core's <c>ErrorPresentation</c>, and the add-on describes
    /// the problem shape as an <c>OpenApiSchema</c> — so the real assemblies pass the rule unexempted.
    /// </summary>
    [Fact]
    public void NoDirectProblemDetailsConstructionOutsideWebApi_RealOpenApiSignalRAndGrpcAssemblies_RulePasses()
    {
        var result = PresentationLayeringRules
            .NoDirectProblemDetailsConstructionOutsideWebApi(PresentationSiblingAssemblies())
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "only SharedKernel.Presentation.WebApi builds ProblemDetails; failing types: " +
                     string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// P-562 control: the real WebApi core does construct <c>ProblemDetails</c> (its problem factory), so the rule is
    /// not vacuous against shipped code — and this is exactly why callers must exclude that one assembly.
    /// </summary>
    [Fact]
    public void NoDirectProblemDetailsConstructionOutsideWebApi_RealWebApiAssembly_RuleFails_WhichIsWhyItIsExcluded()
    {
        var result = PresentationLayeringRules
            .NoDirectProblemDetailsConstructionOutsideWebApi(WebApiAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "the WebApi core is the one place that shapes ProblemDetails, so it must never be passed to the rule");
    }

    /// <summary>
    /// P-562: SignalR's and gRPC's <c>Result</c> handling (hub method results, <c>ThrowIfFailure</c>,
    /// <c>GetValueOrThrow</c>) returns no HTTP result type, and the add-on reads no <c>Result</c> — so the real
    /// sibling assemblies pass the inline-branch rule unexempted.
    /// </summary>
    [Fact]
    public void NoInlineResultBranchBeforeHttpResultOutsideWebApi_RealOpenApiSignalRAndGrpcAssemblies_RulePasses()
    {
        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(PresentationSiblingAssemblies())
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "no sibling package maps a Result to an HTTP response by hand; failing types: " +
                     string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---------------------------------------------------------------------------
    // P-562 — NoOpenApiStackDependencyOutsideOpenApiAddOn
    // ---------------------------------------------------------------------------

    /// <summary>
    /// P-562: the WebApi core, SignalR and gRPC packages carry no dependency on the API versioning, OpenAPI or Scalar
    /// stack — every one of those lives in <c>SharedKernel.Presentation.OpenApi</c>.
    /// </summary>
    [Fact]
    public void NoOpenApiStackDependencyOutsideOpenApiAddOn_RealWebApiSignalRAndGrpcAssemblies_RulePasses()
    {
        var result = PresentationLayeringRules
            .NoOpenApiStackDependencyOutsideOpenApiAddOn(
                WebApiAssembly,
                typeof(SharedKernel.Presentation.SignalR.SignalRHostBuilderExtensions).Assembly,
                typeof(SharedKernel.Presentation.Grpc.GrpcHostBuilderExtensions).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the core and the protocol packages stay free of the OpenAPI stack; failing types: " +
                     string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// P-562 control: the real add-on does depend on the stack, so the rule detects a real dependency rather than
    /// passing vacuously — and the add-on is the one assembly callers never pass.
    /// </summary>
    [Fact]
    public void NoOpenApiStackDependencyOutsideOpenApiAddOn_RealOpenApiAddOn_RuleFails_WhichIsWhyItIsExcluded()
    {
        var result = PresentationLayeringRules
            .NoOpenApiStackDependencyOutsideOpenApiAddOn(OpenApiAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "SharedKernel.Presentation.OpenApi is where Asp.Versioning, Microsoft.OpenApi and Scalar belong");
    }

    /// <summary>
    /// P-562 fire path, once per namespace of the stack (so every chained condition is exercised, not only the first):
    /// an assembly shaped like the WebApi core that reaches for one of them fails the rule.
    /// </summary>
    [Theory]
    [InlineData("Asp.Versioning")]
    [InlineData("Microsoft.AspNetCore.OpenApi")]
    [InlineData("Microsoft.OpenApi")]
    [InlineData("Scalar.AspNetCore")]
    public void NoOpenApiStackDependencyOutsideOpenApiAddOn_CoreUsingTheStack_RuleFails(string stackNamespace)
    {
        var stackStubSource = $$"""
            namespace {{stackNamespace}}
            {
                public sealed class StackType { }
            }
            """;

        var coreSource = $$"""
            namespace SharedKernel.Presentation.WebApi
            {
                public static class DocumentAwareProblems
                {
                    // Violation: the core reaching for the stack the OpenAPI add-on owns.
                    public static object Describe() => new {{stackNamespace}}.StackType();
                }
            }
            """;

        var stackAssembly = CompileInMemory($"Fixture.PresentationOpenApiStack.{stackNamespace}.Stub", stackStubSource);
        var coreAssembly = CompileInMemory(
            $"Fixture.PresentationOpenApiStack.ViolatingCore.{stackNamespace}",
            coreSource,
            extraReferences: new[] { stackAssembly });

        var result = PresentationLayeringRules
            .NoOpenApiStackDependencyOutsideOpenApiAddOn(coreAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: $"DocumentAwareProblems depends on {stackNamespace}, which only the OpenAPI add-on may reference");
        result.FailingTypeNames.Should().Contain("SharedKernel.Presentation.WebApi.DocumentAwareProblems");
    }

    private static Assembly WebApiAssembly => typeof(SharedKernel.Presentation.WebApi.WebApiHostBuilderExtensions).Assembly;

    private static Assembly OpenApiAssembly => typeof(SharedKernel.Presentation.OpenApi.OpenApiHostBuilderExtensions).Assembly;

    /// <summary>The presentation packages that build on the WebApi core — every one checked, none exempt.</summary>
    private static Assembly[] PresentationSiblingAssemblies() =>
    [
        OpenApiAssembly,
        typeof(SharedKernel.Presentation.SignalR.SignalRHostBuilderExtensions).Assembly,
        typeof(SharedKernel.Presentation.Grpc.GrpcHostBuilderExtensions).Assembly,
    ];

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// Follows the established pattern from <c>RedisTopologyRulesTests</c> and
    /// <c>HealthCheckConstantsUsageRulesTests</c>.
    /// </summary>
    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        Assembly[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                references.Add(
                    MetadataReference.CreateFromImage(
                        System.Collections.Immutable.ImmutableArray.Create(
                            File.ReadAllBytes(extraReference.Location))));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));

            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
