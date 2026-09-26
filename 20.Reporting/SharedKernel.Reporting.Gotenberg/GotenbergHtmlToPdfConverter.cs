using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Gotenberg;

/// <summary>
/// Converts HTML with Gotenberg's Chromium route (<c>POST /forms/chromium/convert/html</c>), streaming the PDF from the
/// response to the destination.
/// </summary>
internal sealed class GotenbergHtmlToPdfConverter(
    ReportingDependencies dependencies,
    IHttpClientFactory httpClientFactory,
    IOptions<GotenbergOptions> options,
    IRequestContextAccessor? requestContext = null)
    : HtmlToPdfConverterBase(dependencies)
{
    public const string HttpClientName = "SharedKernel.Reporting.Gotenberg";

    internal const string ConvertPath = "forms/chromium/convert/html";
    internal const string TraceHeader = "Gotenberg-Trace";

    private const int MaxErrorBodyLength = 500;

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
            return ReportingErrors.ConverterUnavailable($"Gotenberg at {client.BaseAddress} could not be reached ({exception.HttpRequestError}).");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                return Result.Success();
            }

            string body = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    ReportingErrors.ConverterUnavailable("Gotenberg refused the credentials; check Username and Password."),
                HttpStatusCode.NotFound =>
                    ReportingErrors.ConverterUnavailable($"Gotenberg has no route {ConvertPath}; check BaseUrl ({client.BaseAddress})."),
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                    ReportingErrors.ConversionTimeout(_options.Timeout),
                HttpStatusCode.TooManyRequests => ReportingErrors.ConverterUnavailable($"Gotenberg is overloaded (429). {body}"),
                _ when (int)response.StatusCode >= 500 => ReportingErrors.ConverterUnavailable($"Gotenberg failed ({(int)response.StatusCode}). {body}"),
                _ => ReportingErrors.ConversionFailed($"Gotenberg rejected the document ({(int)response.StatusCode}). {body}"),
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

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        body = body.Trim();
        return body.Length > MaxErrorBodyLength ? body[..MaxErrorBodyLength] + "…" : body;
    }
}
