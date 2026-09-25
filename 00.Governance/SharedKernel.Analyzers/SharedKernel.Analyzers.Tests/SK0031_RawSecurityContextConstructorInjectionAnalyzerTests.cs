using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0031 <see cref="RawSecurityContextConstructorInjectionAnalyzer"/>.</summary>
/// <remarks>
/// T-281: Fire path — <c>IHttpContextAccessor</c> constructor parameter outside any exempted
/// namespace fires SK0031.
/// T-282: Fire path — <c>ClaimsPrincipal</c> constructor parameter outside any exempted namespace
/// fires SK0031.
/// T-283: Fire path — <c>HttpContext</c> constructor parameter outside any exempted namespace
/// fires SK0031.
/// T-284: Pass path — the identical <c>IHttpContextAccessor</c> constructor shape inside a
/// <c>SharedKernel.Security.Oidc</c>-namespaced type does NOT fire.
/// T-285: Pass path — a constructor injecting <c>IUserContext</c>/<c>IRequestContext</c> instead
/// does NOT fire.
/// </remarks>
public class SK0031_RawSecurityContextConstructorInjectionAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-281 — Fire path: IHttpContextAccessor parameter outside exempted namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-281: A constructor with a parameter typed as <c>IHttpContextAccessor</c> in a plain
    /// application class (not inside <c>SharedKernel.Security.Oidc</c>/<c>.ApiKey</c>) must
    /// trigger SK0031 on the parameter's type token.
    /// </summary>
    [Fact]
    public async Task FirePath_IHttpContextAccessorParameterInApplicationClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                // Stub IHttpContextAccessor for compilation — the rule is syntax-only, so we only
                // need the identifier text "IHttpContextAccessor" to be present.
                public interface IHttpContextAccessor { }

                namespace Application.Services
                {
                    public class OrderPolicyEvaluator
                    {
                        public OrderPolicyEvaluator({|SK0031:IHttpContextAccessor|} accessor) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-282 — Fire path: ClaimsPrincipal parameter outside exempted namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-282: A constructor with a parameter typed as <c>ClaimsPrincipal</c> in a plain
    /// application class must trigger SK0031 on the parameter's type token.
    /// </summary>
    [Fact]
    public async Task FirePath_ClaimsPrincipalParameterInApplicationClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Security.Claims;

                namespace Application.Handlers
                {
                    public class AuditRequestHandler
                    {
                        public AuditRequestHandler({|SK0031:ClaimsPrincipal|} principal) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-283 — Fire path: HttpContext parameter outside exempted namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-283: A constructor with a parameter typed as <c>HttpContext</c> in a plain application
    /// class must trigger SK0031 on the parameter's type token.
    /// </summary>
    [Fact]
    public async Task FirePath_HttpContextParameterInApplicationClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                // Stub HttpContext for compilation — the rule is syntax-only, so we only need the
                // identifier text "HttpContext" to be present.
                public class HttpContext { }

                namespace Application.Middleware
                {
                    public class RequestAuditLogger
                    {
                        public RequestAuditLogger({|SK0031:HttpContext|} context) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-284 — Pass path: identical IHttpContextAccessor shape inside SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-284: A class inside the <c>SharedKernel.Security.Oidc</c> namespace with an
    /// <c>IHttpContextAccessor</c> constructor parameter must NOT trigger SK0031 — the OIDC
    /// implementation package legitimately constructs <c>IUserContext</c>
    /// from raw <c>HttpContext</c>-family types.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelSecurityOidcNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IHttpContextAccessor { }

                namespace SharedKernel.Security.Oidc.Mapping
                {
                    public class RequestScopedUserContextFactory
                    {
                        public RequestScopedUserContextFactory(IHttpContextAccessor accessor) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a class inside the forward-looking, currently-vacuous
    /// <c>SharedKernel.Security.ApiKey</c> exemption namespace with an
    /// <c>IHttpContextAccessor</c> constructor parameter must NOT trigger SK0031.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelSecurityApiKeyNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IHttpContextAccessor { }

                namespace SharedKernel.Security.ApiKey.Validation
                {
                    public class ApiKeyContextFactory
                    {
                        public ApiKeyContextFactory(IHttpContextAccessor accessor) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-285 — Pass path: compliant alternative — IUserContext/IRequestContext injection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-285: A constructor injecting <c>IUserContext</c>/<c>IRequestContext</c> instead of a raw
    /// <c>HttpContext</c>-family type must NOT trigger SK0031.
    /// </summary>
    [Fact]
    public async Task PassPath_UserContextAndRequestContextParameters_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Security.Abstractions
                {
                    public interface IUserContext { }
                }

                namespace SharedKernel.Execution.Context
                {
                    public interface IRequestContext { }
                }

                namespace Application.Handlers
                {
                    using SharedKernel.Execution.Context;
                    using SharedKernel.Security.Abstractions;

                    public class CreateOrderHandler
                    {
                        public CreateOrderHandler(IUserContext userContext, IRequestContext requestContext) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a constructor with no parameters does not trigger SK0031.
    /// </summary>
    [Fact]
    public async Task PassPath_ConstructorWithNoParameters_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSecurityContextConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Application.Services
                {
                    public class SimpleService
                    {
                        public SimpleService() { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
