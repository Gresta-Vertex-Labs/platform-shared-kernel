using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Gotenberg.Diagnostics;

namespace SharedKernel.Reporting.Gotenberg;

/// <summary>
/// Converts HTML with Gotenberg's Chromium route (<c>POST /forms/chromium/convert/html</c>), streaming the PDF from the
/// response to the destination.
/// </summary>
internal sealed class GotenbergHtmlToPdfConverter(
    ReportingDependencies dependencies,
    IHttpClientFactory httpClientFactory,
    IOptions<GotenbergOptions> options,
    ILogger<GotenbergHtmlToPdfConverter> logger,
    IRequestContextAccessor? requestContext = null)
    : HtmlToPdfConverterBase(dependencies)
{
    public const string HttpClientName = "SharedKernel.Reporting.Gotenberg";

    internal const string ConvertPath = "forms/chromium/convert/html";
    internal const string TraceHeader = "Gotenberg-Trace";

    private readonly GotenbergOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    protected override async Task<Result> RenderAsync(
        string html,
        HtmlToPdfOptions options,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ConvertPath) { Content = Form(html, options) };
        if (requestContext?.Current?.CorrelationId is { Length: > 0 } correlationId)
        {
            request.Headers.TryAddWithoutValidation(TraceHeader, correlationId);
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutRejectedException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return ReportingErrors.ConversionTimeout(_options.Timeout);
        }
        catch (HttpRequestException exception)
        {
            GotenbergLog.Unreachable(logger, exception, exception.HttpRequestError);
            return ReportingErrors.ConverterUnavailable();
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                return Result.Success();
            }

            // The status code is the operator's diagnostic; the error body is engine output that may echo the document.
            GotenbergLog.Rejected(logger, (int)response.StatusCode);
            return response.StatusCode switch
            {
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ReportingErrors.ConversionTimeout(_options.Timeout),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests =>
                    ReportingErrors.ConverterUnavailable(),
                _ when (int)response.StatusCode >= 500 => ReportingErrors.ConverterUnavailable(),
                _ => ReportingErrors.ConversionFailed(),
            };
        }
    }

    internal static MultipartFormDataContent Form(string html, HtmlToPdfOptions options)
    {
        var form = new MultipartFormDataContent();
        form.Add(Html(html), "files", "index.html");

        if (options.HeaderHtml is { Length: > 0 } header)
        {
            form.Add(Html(header), "files", "header.html");
        }

        if (options.FooterHtml is { Length: > 0 } footer)
        {
            form.Add(Html(footer), "files", "footer.html");
        }

        foreach (HtmlAsset asset in options.Assets)
        {
            var content = new ReadOnlyMemoryContent(asset.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", asset.FileName);
        }

        Field(form, "paperWidth", Millimeters(options.PageSize.WidthMillimeters));
        Field(form, "paperHeight", Millimeters(options.PageSize.HeightMillimeters));
        Field(form, "marginTop", Millimeters(options.Margins.Top));
        Field(form, "marginRight", Millimeters(options.Margins.Right));
        Field(form, "marginBottom", Millimeters(options.Margins.Bottom));
        Field(form, "marginLeft", Millimeters(options.Margins.Left));
        Field(form, "landscape", Boolean(options.Landscape));
        Field(form, "printBackground", Boolean(options.PrintBackground));
        Field(form, "preferCssPageSize", Boolean(options.PreferCssPageSize));
        Field(form, "scale", options.Scale.ToString("0.###", CultureInfo.InvariantCulture));
        return form;
    }

    private static StringContent Html(string html) => new(html, Encoding.UTF8, "text/html");

    private static void Field(MultipartFormDataContent form, string name, string value) => form.Add(new StringContent(value), name);

    private static string Millimeters(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "mm";

    private static string Boolean(bool value) => value ? "true" : "false";
}
