using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="PresentationLayeringRules"/> — introduced by WO-031 P-199.
/// </summary>
/// <remarks>
/// <para>
/// T-143/T-144 cover <see cref="PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi"/>.
/// T-145/T-146 cover <see cref="PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>.
/// </para>
/// <para>
/// Per the phase's Dependencies section, design/implementation proceeds against contrived
/// in-memory fixtures built via <see cref="CSharpCompilation"/> +
/// <see cref="MetadataReference.CreateFromFile(string)"/> — the same technique used by
/// <c>RedisTopologyRulesTests</c>, <c>ServiceDefaultsGovernanceRulesTests</c>, and
/// <c>HealthCheckConstantsUsageRulesTests</c>. Real ASP.NET Core types
/// (<c>Microsoft.AspNetCore.Mvc.ProblemDetails</c>, <c>Microsoft.AspNetCore.Http.IResult</c>,
/// etc.) are stubbed locally in each fixture's source under the matching namespace/name so the
/// predicates' exact <c>FullName</c>/<c>Name</c> matching resolves correctly without requiring an
/// ASP.NET Core framework reference in this test project. A real-assembly re-verification pass
/// against <c>SharedKernel.Presentation.WebApi</c> is a tracked, non-blocking follow-up gated on
/// P-194 per the phase's Dependencies section.
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
    // return, with no ToProblemDetailsResult call
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-145: A contrived fixture method reading <c>Result.IsSuccess</c>/<c>IsFailure</c> and
    /// returning <c>IResult</c>/<c>ActionResult</c>/<c>ActionResult&lt;T&gt;</c> with no
    /// <c>ToProblemDetailsResult</c> call must fail
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
            because: "Handle reads Result.IsSuccess and returns IResult with no " +
                     "ToProblemDetailsResult call in the same method");

        result.FailingTypeNames.Should().Contain(
            "Application.Endpoints.OrderEndpoints",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-146 — Pass path: same two signals, but also calls ToProblemDetailsResult;
    // companion vacuous-pass path: no IsSuccess/IsFailure usage at all
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-146: A contrived fixture exhibiting the same two signals as T-145, but the method also
    /// calls a member named <c>ToProblemDetailsResult</c>, must pass
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
                    public static IResult ToProblemDetailsResult(this Result result)
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
                        return result.ToProblemDetailsResult();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.InlineResultBranch.EscapeHatchPresent", source);

        var result = PresentationLayeringRules
            .NoInlineResultBranchBeforeHttpResultOutsideWebApi(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Handle calls ToProblemDetailsResult in the same method, suppressing the " +
                     "violation despite the IsSuccess read and IResult return type both being present");
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
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// Follows the established pattern from <c>RedisTopologyRulesTests</c> and
    /// <c>HealthCheckConstantsUsageRulesTests</c>.
    /// </summary>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
        };

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
