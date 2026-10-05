using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Reporting.Gotenberg.Tests;

/// <summary>The converter against a stub Gotenberg: the request it sends and how it maps every answer.</summary>
public sealed class GotenbergConverterTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%stub\n%%EOF\n");

    [Fact]
    public async Task SendsTheDocumentHeaderFooterAssetsAndLayout_InInvariantCulture()
    {
        var handler = new StubHandler(_ => Ok());
        using IHost host = Host(handler);
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

        using var stream = new MemoryStream();
        Result<long> result;
        try
        {
            result = await Convert(host);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Task<Result<long>> Convert(IHost h) => Converter(h).ConvertToStreamAsync(
            "<h1>Fatura</h1>",
            stream,
            new HtmlToPdfOptions
            {
                PageSize = PdfPageSize.Letter,
                Landscape = true,
                Margins = new PdfMargins(20, 10.5, 20, 10.5),
                Scale = 0.85,
                FooterHtml = HtmlToPdfOptions.PageNumberFooter,
                Assets = [new HtmlAsset("logo.png", new byte[] { 1, 2, 3 })],
            });

        result.Value.Should().Be(Pdf.Length);
        stream.ToArray().Should().Equal(Pdf);

        StubHandler.Captured request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://gotenberg.test/forms/chromium/convert/html"));
        request.Body.Should().Contain("filename=index.html").And.Contain("<h1>Fatura</h1>");
        request.Body.Should().Contain("filename=footer.html").And.Contain("class=\"pageNumber\"");
        request.Body.Should().Contain("filename=logo.png");
        request.Body.Should().NotContain("filename=header.html");
        Field(request, "paperWidth").Should().Be("215.9mm");
        Field(request, "paperHeight").Should().Be("279.4mm");
        Field(request, "marginRight").Should().Be("10.5mm");
        Field(request, "landscape").Should().Be("true");
        Field(request, "printBackground").Should().Be("true");
        Field(request, "scale").Should().Be("0.85");
    }

    [Fact]
    public async Task SendsTheCorrelationIdAsGotenbergTrace_AndBasicCredentials()
    {
        var handler = new StubHandler(_ => Ok());
        using IHost host = Host(
            handler,
            new() { ["SharedKernel:Reporting:Gotenberg:Username"] = "reports", ["SharedKernel:Reporting:Gotenberg:Password"] = "s3cret" },
            services => services.AddSingleton<IRequestContextAccessor>(new FixedContextAccessor("corr-42")));

        (await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null)).IsSuccess.Should().BeTrue();

        StubHandler.Captured request = handler.Requests.Single();
        request.Headers["Gotenberg-Trace"].Should().Be("corr-42");
        request.Headers["Authorization"].Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("reports:s3cret")));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ReportingErrorCodes.ConversionFailed, "form field 'scale' is invalid")]
    [InlineData(HttpStatusCode.NotFound, ReportingErrorCodes.ConverterUnavailable, "BaseUrl")]
    [InlineData(HttpStatusCode.Unauthorized, ReportingErrorCodes.ConverterUnavailable, "credentials")]
    [InlineData(HttpStatusCode.ServiceUnavailable, ReportingErrorCodes.ConverterUnavailable, "503")]
    [InlineData(HttpStatusCode.GatewayTimeout, ReportingErrorCodes.ConversionTimeout, "did not finish")]
    public async Task MapsGotenbergFailures(HttpStatusCode status, string code, string mentioned)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("Invalid form data: form field 'scale' is invalid"),
        });
        using IHost host = Host(handler, new() { ["SharedKernel:Reporting:Gotenberg:MaxRetryAttempts"] = "0" });
        using var stream = new MemoryStream();

        Result<long> result = await Converter(host).ConvertToStreamAsync("<p/>", stream);

        result.Error.Code.Should().Be(code);
        result.Error.Message.Should().Contain(mentioned);
        stream.Length.Should().Be(0, "nothing is written when the conversion fails");
    }

    [Fact]
    public async Task AnUnreachableGotenberg_IsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        using IHost host = Host(handler, new() { ["SharedKernel:Reporting:Gotenberg:MaxRetryAttempts"] = "0" });

        Result<long> result = await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null);

        result.Error.Code.Should().Be(ReportingErrorCodes.ConverterUnavailable);
        result.Error.Message.Should().Contain("ConnectionError");
    }

    [Fact]
    public async Task ASlowGotenberg_TimesOut()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok();
        });
        using IHost host = Host(handler, new()
        {
            ["SharedKernel:Reporting:Gotenberg:MaxRetryAttempts"] = "0",
            ["SharedKernel:Reporting:Gotenberg:Timeout"] = "00:00:01",
        });

        Result<long> result = await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null);

        result.Error.Code.Should().Be(ReportingErrorCodes.ConversionTimeout);
    }

    [Fact]
    public async Task ATransientFailure_IsRetried()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok());
        using IHost host = Host(handler, new() { ["SharedKernel:Reporting:Gotenberg:MaxRetryAttempts"] = "1" });

        Result<long> result = await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null);

        result.IsSuccess.Should().BeTrue();
        calls.Should().Be(2);
        handler.Requests[1].Body.Should().Contain("filename=index.html", "the form is sent again in full");
    }

    [Fact]
    public async Task ReadinessProbe_ReportsGotenbergHealth()
    {
        var healthy = true;
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/health"
            ? new HttpResponseMessage(healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
            : Ok());
        using IHost host = Host(handler, new() { ["SharedKernel:Reporting:Gotenberg:MaxRetryAttempts"] = "0" });
        IReadinessProbe probe = host.Services.GetRequiredReadinessProbe("gotenberg");

        (await probe.ProbeAsync()).Status.Should().Be(ReadinessStatus.Healthy);
        healthy = false;
        (await probe.ProbeAsync()).Status.Should().Be(ReadinessStatus.Unhealthy);
    }

    [Theory]
    [InlineData(null, null, null, "BaseUrl")]
    [InlineData("ftp://gotenberg", null, null, "BaseUrl")]
    [InlineData("http://gotenberg:3000", "user", null, "Username")]
    public async Task InvalidOptions_FailAtStartup(string? baseUrl, string? username, string? password, string mentioned)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Reporting:Gotenberg:BaseUrl"] = baseUrl,
            ["SharedKernel:Reporting:Gotenberg:Username"] = username,
            ["SharedKernel:Reporting:Gotenberg:Password"] = password,
        });
        builder.Services.AddSharedKernelReporting().AddGotenberg(builder.Configuration);
        using IHost host = builder.Build();

        var start = () => host.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage($"*{mentioned}*");
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf) };

    private static string Field(StubHandler.Captured request, string name)
    {
        string marker = $"name={name}\r\n\r\n";
        int start = request.Body.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"the form has the field {name}");
        start += marker.Length;
        return request.Body[start..request.Body.IndexOf("\r\n", start, StringComparison.Ordinal)];
    }

    private static IHtmlToPdfConverter Converter(IHost host) => host.Services.GetRequiredService<IHtmlToPdfConverter>();

    private static IHost Host(StubHandler handler, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Reporting:Gotenberg:BaseUrl"] = "http://gotenberg.test",
        });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddSharedKernelReporting().AddGotenberg(builder.Configuration);
        builder.Services.AddHttpClient(GotenbergHtmlToPdfConverter.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    private sealed class FixedContextAccessor(string correlationId) : IRequestContextAccessor
    {
        public IRequestContext? Current { get; } = new SystemRequestContext([], "test", correlationId: correlationId);
    }
}

/// <summary>Answers HTTP requests from a function and records what was sent.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    public List<Captured> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (Requests)
        {
            Requests.Add(new Captured(request.Method, request.RequestUri!, headers, body));
        }

        return await _respond(request, cancellationToken);
    }

    internal sealed record Captured(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);
}
