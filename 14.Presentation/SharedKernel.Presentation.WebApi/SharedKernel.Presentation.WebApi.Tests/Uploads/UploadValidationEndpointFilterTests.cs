using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel.Presentation.WebApi.Tests.Authorization;
using SharedKernel.Presentation.WebApi.Uploads;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Uploads;

/// <summary>
/// Tests for <see cref="UploadValidationEndpointFilter"/> (WO-063, P-416).
/// </summary>
public class UploadValidationEndpointFilterTests
{
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    [Fact]
    public async Task InvokeAsync_AttributeAbsent_NoOps_PassesThroughUnchanged()
    {
        // T-60 (regression): an endpoint that does not apply [RequireValidatedUpload] behaves
        // exactly as today — no size/type check is performed at all, even with an oversized
        // Content-Length present.
        var filter = new UploadValidationEndpointFilter();
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null);
        context.HttpContext.Request.ContentLength = long.MaxValue;

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_ContentLengthExceedsMaxSize_ShortCircuitsWith413_HandlerNeverInvoked()
    {
        // T-58: a marker/counter (RecordingNext.CallCount) proves the handler is never reached —
        // rejection happens on the declared Content-Length alone, before any body buffering/read.
        var filter = new UploadValidationEndpointFilter(new UploadValidationOptions { MaxSizeBytes = 100 });
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireValidatedUploadAttribute());
        context.HttpContext.Request.ContentLength = 1000;

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0, "the handler must never be invoked when the declared body size exceeds the configured limit");
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(413);
    }

    [Fact]
    public async Task InvokeAsync_ContentLengthWithinMaxSize_AllowsNextExactlyOnce()
    {
        var filter = new UploadValidationEndpointFilter(new UploadValidationOptions { MaxSizeBytes = 1000 });
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireValidatedUploadAttribute());
        context.HttpContext.Request.ContentLength = 100;

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_ContentTypeNotInAllowList_ShortCircuitsWith415()
    {
        // T-59 (content-type half).
        var filter = new UploadValidationEndpointFilter(new UploadValidationOptions());
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext: null,
            new RequireValidatedUploadAttribute(allowedContentTypes: "application/pdf"));
        context.HttpContext.Request.ContentType = "image/png";

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(415);
    }

    [Fact]
    public async Task InvokeAsync_ContentTypeCarriesCharsetParameter_StillMatchesAllowList()
    {
        var filter = new UploadValidationEndpointFilter(new UploadValidationOptions());
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext: null,
            new RequireValidatedUploadAttribute(allowedContentTypes: "text/plain"));
        context.HttpContext.Request.ContentType = "text/plain; charset=utf-8";

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_MagicByteSignatureMismatch_ShortCircuitsWith400()
    {
        // T-59 (magic-byte half): declared Content-Type says "application/pdf", but the leading
        // bytes do not match the configured PDF signature.
        var options = new UploadValidationOptions();
        options.AllowedMagicBytes["application/pdf"] = PdfMagicBytes;
        var filter = new UploadValidationEndpointFilter(options);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext: null,
            new RequireValidatedUploadAttribute(allowedContentTypes: "application/pdf"));
        context.HttpContext.Request.ContentType = "application/pdf";
        context.HttpContext.Request.Body = new MemoryStream([0x00, 0x00, 0x00, 0x00, 0x00]);

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_MagicByteSignatureMatches_AllowsNextExactlyOnce_BodyStillReadableByHandler()
    {
        var options = new UploadValidationOptions();
        options.AllowedMagicBytes["application/pdf"] = PdfMagicBytes;
        var filter = new UploadValidationEndpointFilter(options);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext: null,
            new RequireValidatedUploadAttribute(allowedContentTypes: "application/pdf"));
        context.HttpContext.Request.ContentType = "application/pdf";
        var bodyBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // "%PDF-1.4"
        context.HttpContext.Request.Body = new MemoryStream(bodyBytes);

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);

        // The stream position is reset to 0 so the endpoint handler still sees the full body.
        context.HttpContext.Request.Body.Position.Should().Be(0);
        using var reader = new StreamReader(context.HttpContext.Request.Body, leaveOpen: true);
        var fullBody = await reader.ReadToEndAsync();
        fullBody.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task InvokeAsync_PerEndpointOverrides_TakePrecedenceOverGlobalDefaults()
    {
        // T-60: per-endpoint override args (maxSizeBytes/allowedContentTypes) take precedence over
        // the global UploadValidationOptions defaults when supplied.
        var globalDefaults = new UploadValidationOptions { MaxSizeBytes = 10_000_000 };
        globalDefaults.AllowedContentTypes.Add("image/png");
        var filter = new UploadValidationEndpointFilter(globalDefaults);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        // Endpoint-level override: a much smaller max size and a different allowed content type.
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext: null,
            new RequireValidatedUploadAttribute(maxSizeBytes: 100, allowedContentTypes: "application/pdf"));
        context.HttpContext.Request.ContentLength = 500; // Exceeds the endpoint override, not the global default.
        context.HttpContext.Request.ContentType = "application/pdf";

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0, "the smaller per-endpoint MaxSizeBytes override must win over the larger global default");
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(413);
    }
}
