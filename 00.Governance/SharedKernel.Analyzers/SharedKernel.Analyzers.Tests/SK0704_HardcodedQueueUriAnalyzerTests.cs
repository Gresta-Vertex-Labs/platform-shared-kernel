using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0704 <see cref="HardcodedQueueUriAnalyzer"/>.
/// </summary>
public class SK0704_HardcodedQueueUriAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-94 — Fire path: GetSendEndpoint with literal queue:/exchange: URI triggers SK0704
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-94a: <c>provider.GetSendEndpoint(new Uri("queue:order-commands"))</c>
    /// triggers SK0704 — hardcoded queue URI is not allowed.
    /// </summary>
    [Fact]
    public async Task FirePath_GetSendEndpointWithQueueUri_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<HardcodedQueueUriAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                using System.Threading.Tasks;

                public interface ISendEndpointProvider
                {
                    Task<ISendEndpoint> GetSendEndpoint(Uri address);
                }
                public interface ISendEndpoint { }

                public class OrderProducer
                {
                    private readonly ISendEndpointProvider _provider;
                    public OrderProducer(ISendEndpointProvider provider) => _provider = provider;

                    public async Task SendAsync()
                    {
                        // SK0704: hardcoded "queue:" URI
                        var endpoint = await _provider.GetSendEndpoint({|SK0704:new Uri("queue:order-commands")|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-94b: <c>provider.GetSendEndpoint(new Uri("exchange:order-events"))</c>
    /// triggers SK0704 — hardcoded exchange URI is not allowed.
    /// </summary>
    [Fact]
    public async Task FirePath_GetSendEndpointWithExchangeUri_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<HardcodedQueueUriAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                using System.Threading.Tasks;

                public interface ISendEndpointProvider
                {
                    Task<ISendEndpoint> GetSendEndpoint(Uri address);
                }
                public interface ISendEndpoint { }

                public class OrderProducer
                {
                    private readonly ISendEndpointProvider _provider;
                    public OrderProducer(ISendEndpointProvider provider) => _provider = provider;

                    public async Task SendAsync()
                    {
                        // SK0704: hardcoded "exchange:" URI
                        var endpoint = await _provider.GetSendEndpoint({|SK0704:new Uri("exchange:order-events")|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-95 — Pass path: convention-based or non-literal Uri does not trigger SK0704
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-95a: <c>provider.GetSendEndpoint(formatter.GetDestinationAddress&lt;OrderCommand&gt;())</c>
    /// does not trigger SK0704 — convention-based resolution does not use a literal Uri.
    /// </summary>
    [Fact]
    public async Task PassPath_GetSendEndpointWithConventionAddress_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<HardcodedQueueUriAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                using System.Threading.Tasks;

                public interface ISendEndpointProvider
                {
                    Task<ISendEndpoint> GetSendEndpoint(Uri address);
                }
                public interface ISendEndpoint { }

                public interface IEndpointNameFormatter
                {
                    Uri GetDestinationAddress<T>();
                }

                public class OrderCommand { }

                public class OrderProducer
                {
                    private readonly ISendEndpointProvider _provider;
                    private readonly IEndpointNameFormatter _formatter;

                    public OrderProducer(ISendEndpointProvider provider, IEndpointNameFormatter formatter)
                    {
                        _provider = provider;
                        _formatter = formatter;
                    }

                    public async Task SendAsync()
                    {
                        // Compliant: convention-based endpoint resolution via formatter
                        var endpoint = await _provider.GetSendEndpoint(
                            _formatter.GetDestinationAddress<OrderCommand>());
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-95b: <c>provider.GetSendEndpoint(new Uri(someVariable))</c>
    /// does not trigger SK0704 — the argument is not a string literal.
    /// </summary>
    [Fact]
    public async Task PassPath_GetSendEndpointWithVariableUri_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<HardcodedQueueUriAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                using System.Threading.Tasks;

                public interface ISendEndpointProvider
                {
                    Task<ISendEndpoint> GetSendEndpoint(Uri address);
                }
                public interface ISendEndpoint { }

                public class OrderProducer
                {
                    private readonly ISendEndpointProvider _provider;
                    public OrderProducer(ISendEndpointProvider provider) => _provider = provider;

                    public async Task SendAsync(string queueAddress)
                    {
                        // Compliant: non-literal URI argument — no string literal to check
                        var endpoint = await _provider.GetSendEndpoint(new Uri(queueAddress));
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>new Uri("https://...")</c> does not trigger SK0704 —
    /// scheme is not "queue:" or "exchange:".
    /// </summary>
    [Fact]
    public async Task PassPath_GetSendEndpointWithHttpsUri_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<HardcodedQueueUriAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                using System.Threading.Tasks;

                public interface ISendEndpointProvider
                {
                    Task<ISendEndpoint> GetSendEndpoint(Uri address);
                }
                public interface ISendEndpoint { }

                public class OrderProducer
                {
                    private readonly ISendEndpointProvider _provider;
                    public OrderProducer(ISendEndpointProvider provider) => _provider = provider;

                    public async Task SendAsync()
                    {
                        // Compliant: https:// scheme is not a forbidden queue/exchange URI
                        var endpoint = await _provider.GetSendEndpoint(new Uri("https://api.example.com/endpoint"));
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
