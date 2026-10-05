using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0013 <see cref="RawHttpClientConstructorInjectionAnalyzer"/>.</summary>
/// <remarks>
/// T-124: Fire path — raw <c>HttpClient</c> parameter outside exempted context fires SK0013.
/// T-125: Pass path — <c>DelegatingHandler</c> subclass with <c>HttpClient</c> parameter does NOT fire.
/// T-126: Pass path — constructor with <c>IHttpClientFactory</c> parameter does NOT fire.
/// Additional: SharedKernel.Communication.Rest namespace exemption.
/// </remarks>
public class SK0013_RawHttpClientConstructorInjectionAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-124 — Fire path: HttpClient parameter not in exempted context fires SK0013
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-124: A constructor with a parameter typed as <c>HttpClient</c> in a plain application
    /// class (not a <c>DelegatingHandler</c> subclass, not inside
    /// <c>SharedKernel.Communication.Rest</c>) must trigger SK0013 on the parameter's type token.
    /// </summary>
    [Fact]
    public async Task FirePath_HttpClientParameterInApplicationClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                namespace Application.Services
                {
                    public interface IOrderRepository { }

                    public class OrderHandler
                    {
                        public OrderHandler({|SK0013:HttpClient|} client, IOrderRepository repo) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: class with only an <c>HttpClient</c> constructor parameter fires SK0013.
    /// </summary>
    [Fact]
    public async Task FirePath_HttpClientOnlyParameter_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                namespace Infrastructure.Clients
                {
                    public class ExternalApiClient
                    {
                        public ExternalApiClient({|SK0013:HttpClient|} httpClient) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-125 — Pass path: DelegatingHandler subclass with HttpClient does NOT fire
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-125: A class that extends <c>DelegatingHandler</c> with a constructor accepting
    /// <c>HttpClient</c> must NOT trigger SK0013 — delegating handlers legitimately receive the
    /// inner handler as part of the chain.
    /// </summary>
    [Fact]
    public async Task PassPath_DelegatingHandlerSubclassWithHttpClient_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                namespace SharedKernel.Communication.Rest
                {
                    public class TenantIdDelegatingHandler : DelegatingHandler
                    {
                        public TenantIdDelegatingHandler(HttpClient client) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>DelegatingHandler</c> subclass outside the exempted namespace also passes
    /// — the base-class exemption applies regardless of namespace.
    /// </summary>
    [Fact]
    public async Task PassPath_DelegatingHandlerSubclassOutsideRestNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                namespace Infrastructure.Http
                {
                    public class LoggingDelegatingHandler : DelegatingHandler
                    {
                        public LoggingDelegatingHandler(HttpClient client) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-126 — Pass path: IHttpClientFactory parameter does NOT fire SK0013
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-126: A constructor whose parameters include only <c>IHttpClientFactory</c> (not
    /// <c>HttpClient</c>) must NOT trigger SK0013. The interface is stubbed inline since the
    /// test compilation context does not reference Microsoft.Extensions.Http.
    /// </summary>
    [Fact]
    public async Task PassPath_IHttpClientFactoryParameter_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                // Stub IHttpClientFactory for compilation — the rule is syntax-only,
                // so we only need the identifier text "IHttpClientFactory" to differ from "HttpClient".
                public interface IHttpClientFactory
                {
                    HttpClient CreateClient(string name);
                }

                namespace Application.Services
                {
                    public class OrderServiceClient
                    {
                        public OrderServiceClient(IHttpClientFactory factory) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Additional — SharedKernel.Communication.Rest namespace exemption
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass path: a class inside the <c>SharedKernel.Communication.Rest</c> namespace with an
    /// <c>HttpClient</c> constructor parameter must NOT trigger SK0013 — the REST typed-client
    /// package legitimately manages <c>HttpClient</c> internally.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelCommunicationRestNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                namespace SharedKernel.Communication.Rest
                {
                    public class TypedRestClient
                    {
                        public TypedRestClient(HttpClient client) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a class inside a sub-namespace of <c>SharedKernel.Communication.Rest</c> with
    /// an <c>HttpClient</c> constructor parameter must NOT trigger SK0013.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelCommunicationRestSubNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Net.Http;

                namespace SharedKernel.Communication.Rest.Internals
                {
                    public class PollyPipelineFactory
                    {
                        public PollyPipelineFactory(HttpClient client) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a constructor with no parameters does not trigger SK0013.
    /// </summary>
    [Fact]
    public async Task PassPath_ConstructorWithNoParameters_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawHttpClientConstructorInjectionAnalyzer, DefaultVerifier>
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
