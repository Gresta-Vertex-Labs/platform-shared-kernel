using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Design D13/D16: every failure — thrown, or a result ended with <c>SharedKernel.Core</c>'s
/// <c>GetValueOrThrow()</c>/<c>ThrowIfFailure()</c> — reaches a real gRPC client as a <c>google.rpc.Status</c> with a
/// mapped code, the client message, an <c>ErrorInfo</c> (code, domain, trace and correlation ids) and, for field
/// errors, a <c>BadRequest</c> with the violations (capped, P-562 R31). An <see cref="RpcException"/> of the service
/// keeps its code but is rebuilt as this service's (R30).
/// </summary>
public sealed class RichStatusTests
{
    [Fact]
    public async Task ThrownError_CarriesItsCodeMessageAndErrorInfo()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, Failures.NotFoundException, correlationId: "flow-1");

        var status = exception.ShouldHaveRichStatus(StatusCode.NotFound);
        status.Message.Should().Be(Failures.OrderNotFoundMessage);
        var errorInfo = status.ErrorInfo();
        errorInfo.Reason.Should().Be("order.not_found");
        errorInfo.Domain.Should().Be(app.Environment.ApplicationName);
        errorInfo.Metadata.Should().ContainKey(ProblemDetailsExtensionNames.TraceId).WhoseValue.Should().NotBeNullOrWhiteSpace();
        errorInfo.Metadata.Should().Contain(ProblemDetailsExtensionNames.CorrelationId, "flow-1");
        status.GetDetail<Google.Rpc.BadRequest>().Should().BeNull("an error without field errors has no BadRequest detail");
    }

    [Fact]
    public async Task B11_ThrownValidationException_ReportsTheCodeAndEveryFieldViolation()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, Failures.ValidationException);

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.Message.Should().Be("3 validation errors occurred.");
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Validation.Failed);
        status.FieldViolations().Should().Equal(
            ("customer.name", "Name is required.", "customer.name_required"),
            ("customer.email", "Email is invalid.", "customer.email_invalid"),
            ("order.lines_empty", "An order needs at least one line.", "order.lines_empty"));
    }

    [Fact]
    public async Task B11_ReturnedValidationResult_ReportsTheCodeAndEveryFieldViolation()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, Failures.ValidationResult);

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Validation.Failed);
        status.FieldViolations().Should().Equal(
            ("customer.name", "Name is required.", "customer.name_required"),
            ("customer.email", "Email is invalid.", "customer.email_invalid"),
            ("order.lines_empty", "An order needs at least one line.", "order.lines_empty"));
    }

    [Fact]
    public async Task ThrownValidationException_WithOneError_KeepsItsOwnCodeAndMessage()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, Failures.SingleValidationException);

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.Message.Should().Be("Name is required.");
        status.ErrorInfo().Reason.Should().Be("customer.name_required");
        status.GetDetail<Google.Rpc.BadRequest>().Should().BeNull(
            "one error without field errors of its own is presented as a returned one, as over HTTP (P-562 R9)");
    }

    [Theory]
    [InlineData(Failures.SingleValidationException, Failures.SingleValidationResult)]
    [InlineData(Failures.ValidationException, Failures.ValidationResult)]
    public async Task ThrownValidationException_GetsTheStatusOfTheReturnedError(string thrown, string returned)
    {
        await using var app = await GrpcTestHost.StartAsync();

        var fromThrown = (await FailAsync(app, thrown)).ShouldHaveRichStatus(StatusCode.InvalidArgument);
        var fromReturned = (await FailAsync(app, returned)).ShouldHaveRichStatus(StatusCode.InvalidArgument);

        fromThrown.WithoutRequestIds().Should().Be(fromReturned.WithoutRequestIds());
    }

    [Theory]
    [InlineData(Failures.ConflictException, StatusCode.Aborted, "order.version_conflict")]
    [InlineData(Failures.ConflictTaskResult, StatusCode.Aborted, "order.version_conflict")]
    [InlineData(Failures.UnavailableException, StatusCode.Unavailable, "search.unreachable")]
    [InlineData(Failures.UnavailableTaskResult, StatusCode.Unavailable, "search.unreachable")]
    [InlineData(Failures.TimeoutResult, StatusCode.DeadlineExceeded, "search.timeout")]
    public async Task ErrorType_MapsToItsStatusCode(string failure, StatusCode expected, string code)
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, failure);

        exception.ShouldHaveRichStatus(expected).ErrorInfo().Reason.Should().Be(code);
    }

    [Theory]
    [InlineData(Failures.UnavailableException, StatusCode.Unavailable, "The service is temporarily unavailable. Try again later.", Failures.UnavailableDetail)]
    [InlineData(Failures.UnavailableTaskResult, StatusCode.Unavailable, "The service is temporarily unavailable. Try again later.", Failures.UnavailableDetail)]
    [InlineData(Failures.TimeoutResult, StatusCode.DeadlineExceeded, "The operation did not complete in time.", Failures.TimeoutDetail)]
    public async Task ServerError_OutsideDevelopment_IsRedacted(string failure, StatusCode expected, string generic, string internals)
    {
        await using var app = await GrpcTestHost.StartAsync(GrpcTestHost.Production);

        var exception = await FailAsync(app, failure);

        var status = exception.ShouldHaveRichStatus(expected);
        status.Message.Should().Be(generic);
        status.ToString().Should().NotContain(internals);
    }

    [Theory]
    [InlineData(Failures.UnavailableException, Failures.UnavailableDetail)]
    [InlineData(Failures.TimeoutResult, Failures.TimeoutDetail)]
    public async Task ServerError_InDevelopment_ShowsItsMessage(string failure, string message)
    {
        await using var app = await GrpcTestHost.StartAsync(GrpcTestHost.Development);

        var exception = await FailAsync(app, failure);

        exception.Status.Detail.Should().Be(message);
    }

    [Fact]
    public async Task UnknownException_OutsideDevelopment_IsInternal_WithAGenericMessage()
    {
        await using var app = await GrpcTestHost.StartAsync(GrpcTestHost.Production);

        var exception = await FailAsync(app, Failures.Unknown);

        var status = exception.ShouldHaveRichStatus(StatusCode.Internal);
        status.Message.Should().Be("An unexpected error occurred.");
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Unexpected.Default);
        status.ToString().Should().NotContain("Password");
    }

    [Fact]
    public async Task UnknownException_InDevelopment_ShowsTheExceptionMessage()
    {
        await using var app = await GrpcTestHost.StartAsync(GrpcTestHost.Development);

        var exception = await FailAsync(app, Failures.Unknown);

        var status = exception.ShouldHaveRichStatus(StatusCode.Internal);
        status.Message.Should().Be(Failures.UnknownDetail);
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Unexpected.Default);
    }

    [Fact]
    public async Task R30_RpcExceptionOfTheService_KeepsItsCodeAndDetail_WithThisServicesErrorInfo()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, Failures.Rpc, correlationId: "flow-4");

        // A client category: the service's own answer, shown as it wrote it.
        var status = exception.ShouldHaveRichStatus(StatusCode.ResourceExhausted);
        status.Message.Should().Be(Failures.RpcDetail);
        status.Details.Should().ContainSingle("only this service's ErrorInfo is added");
        var errorInfo = status.ErrorInfo();
        errorInfo.Reason.Should().Be(GrpcErrorCodes.ForStatus(StatusCode.ResourceExhausted)).And.Be("grpc.resource_exhausted");
        errorInfo.Domain.Should().Be(app.Environment.ApplicationName);
        errorInfo.Metadata.Should().ContainKey(ProblemDetailsExtensionNames.TraceId);
        errorInfo.Metadata.Should().Contain(ProblemDetailsExtensionNames.CorrelationId, "flow-4");
    }

    [Fact]
    public async Task R30_ServerCategoryRpcExceptionOfTheService_OutsideDevelopment_IsRedacted_WithoutItsTrailers()
    {
        await using var app = await GrpcTestHost.StartAsync(GrpcTestHost.Production);

        var exception = await FailAsync(app, Failures.InternalRpcWithTrailers);

        var status = exception.ShouldHaveRichStatus(StatusCode.Internal);
        status.Message.Should().Be("An unexpected error occurred.");
        status.ErrorInfo().Reason.Should().Be("grpc.internal");
        exception.Trailers.Get(Failures.InternalTrailerKey).Should().BeNull("the original trailers are never sent");
        exception.EverythingTheClientSees().Should().NotContain(Failures.InternalRpcDetail).And.NotContain(Failures.InternalTrailerValue);
    }

    [Fact]
    public async Task R30_ServerCategoryRpcExceptionOfTheService_InDevelopment_ShowsItsDetail_StillWithoutItsTrailers()
    {
        await using var app = await GrpcTestHost.StartAsync(GrpcTestHost.Development);

        var exception = await FailAsync(app, Failures.InternalRpcWithTrailers);

        exception.ShouldHaveRichStatus(StatusCode.Internal).Message.Should().Be(Failures.InternalRpcDetail);
        exception.Trailers.Get(Failures.InternalTrailerKey).Should().BeNull();
    }

    [Theory]
    [InlineData(Failures.ManyViolations, 400)]
    [InlineData(Failures.ManyViolations, 3000)]
    [InlineData(Failures.ManyViolationsResult, 400)]
    [InlineData(Failures.ManyViolationsResult, 3000)]
    public async Task R31_ManyFieldErrors_ArriveAsInvalidArgument_WithTheViolationsCapped(string failure, int count)
    {
        await using var app = await GrpcTestHost.StartAsync();

        var exception = await FailAsync(app, failure + count);

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.Message.Should().Be($"{count} validation errors occurred.");
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Validation.Failed);

        var violations = status.FieldViolations();
        violations.Should().HaveCount(RpcStatusFactory.MaxFieldViolations + 1);
        violations.Take(RpcStatusFactory.MaxFieldViolations).Should().Equal(
            Enumerable.Range(0, RpcStatusFactory.MaxFieldViolations)
                .Select(index => ($"items[{index}].name", "Name is required.", "item.name_required")));
        violations[^1].Should().Be((
            GrpcErrorCodes.MoreFieldViolations,
            $"… and {count - RpcStatusFactory.MaxFieldViolations} more.",
            GrpcErrorCodes.MoreFieldViolations));

        // Many gRPC clients refuse more than 8 KB of headers or trailers by default; the status stays well under.
        exception.StatusBytesOnTheWire().Should().BeLessThan(6 * 1024);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(3000)]
    public async Task R31_OverRealHttp2_ManyFieldErrors_ReachAClientThatAcceptsOnly8KBOfHeaders(int count)
    {
        // The in-memory test server enforces no header limit; a real connection with a client capped like many gRPC
        // clients are by default does. Uncapped, 400 violations are about 30 KB of trailer and 3000 about 230 KB.
        await using var app = await GrpcTestHost.StartAsync(overSockets: true);
        var client = app.CreateSocketClient(maxResponseHeadersKilobytes: 8);

        var act = async () => await client.FailAsync(new EchoRequest { Value = Failures.ManyViolations + count });

        var status = (await act.Should().ThrowAsync<RpcException>()).Which.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Validation.Failed);
        status.FieldViolations().Should().HaveCount(RpcStatusFactory.MaxFieldViolations + 1)
            .And.EndWith((GrpcErrorCodes.MoreFieldViolations, $"… and {count - RpcStatusFactory.MaxFieldViolations} more.", GrpcErrorCodes.MoreFieldViolations));
    }

    [Fact]
    public async Task ServerStreaming_FailureAfterAMessage_CarriesTheRichStatus()
    {
        await using var app = await GrpcTestHost.StartAsync();
        using var call = app.CreateClient().StreamFail(new EchoRequest { Value = Failures.ValidationException });

        (await call.ResponseStream.MoveNext(CancellationToken.None)).Should().BeTrue();
        call.ResponseStream.Current.Value.Should().Be("first");
        var act = async () => await call.ResponseStream.MoveNext(CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.ShouldHaveRichStatus(StatusCode.InvalidArgument).FieldViolations().Should().HaveCount(3);
    }

    [Fact]
    public async Task ErrorDomain_IsBoundFromConfiguration()
    {
        await using var app = await GrpcTestHost.StartAsync(configuration: new Dictionary<string, string?>
        {
            ["SharedKernel:Presentation:Grpc:ErrorDomain"] = "orders.example.com",
        });

        var exception = await FailAsync(app, Failures.NotFoundException);

        exception.ShouldHaveRichStatus(StatusCode.NotFound).ErrorInfo().Domain.Should().Be("orders.example.com");
    }

    [Fact]
    public async Task ErrorDomain_FromConfigure_ReachesResultFailuresToo()
    {
        await using var app = await GrpcTestHost.StartAsync(configureGrpc: options => options.ErrorDomain = "billing.example.com");

        var exception = await FailAsync(app, Failures.ValidationResult, correlationId: "flow-2");

        var errorInfo = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument).ErrorInfo();
        errorInfo.Domain.Should().Be("billing.example.com");
        errorInfo.Metadata.Should().Contain(ProblemDetailsExtensionNames.CorrelationId, "flow-2");
    }

    [Fact]
    public async Task GrpcOnlyHost_WithoutTheWebApiPipeline_StillCarriesTheRichStatus_AndTheCorrelationId()
    {
        await using var app = await GrpcTestHost.StartAsync(useWebApi: false);

        var exception = await FailAsync(app, Failures.ValidationException, correlationId: "flow-3");

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.FieldViolations().Should().HaveCount(3);
        // The correlation id belongs to UseSharedKernelRequestContext(), not to the WebApi pipeline (P-579).
        status.ErrorInfo().Metadata[ProblemDetailsExtensionNames.CorrelationId].Should().Be("flow-3");
    }

    [Fact]
    public async Task ServerErrors_AreLoggedAtError_ClientErrorsAtDebug_WhetherThrownOrReturned()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GrpcTestHost.StartAsync(loggerFactory: logs);
        var records = logs.GetLogger(typeof(GrpcExceptionInterceptor).FullName!);

        await FailAsync(app, Failures.UnavailableException);
        await FailAsync(app, Failures.NotFoundException);
        await FailAsync(app, Failures.Unknown);
        await FailAsync(app, Failures.TimeoutResult);
        await FailAsync(app, Failures.Rpc);
        await FailAsync(app, Failures.InternalRpcWithTrailers);

        // A failed result arrives as the exception Core's GetValueOrThrow/ThrowIfFailure throw, logged like any other.
        records.Records.Select(record => (record.EventId.Id, record.LogLevel)).Should().Equal(
            (14202, LogLevel.Error),
            (14203, LogLevel.Debug),
            (14200, LogLevel.Error),
            (14202, LogLevel.Error),
            (14203, LogLevel.Debug),
            (14202, LogLevel.Error));
        records.Records.Should().OnlyContain(record => record.Exception != null);
        records.Records.Select(record => record.TryGetProperty("ErrorCode", out var code) ? code : null).Should().Equal(
            "search.unreachable",
            "order.not_found",
            null,
            "search.timeout",
            "grpc.resource_exhausted",
            "grpc.internal");
    }

    private static async Task<RpcException> FailAsync(WebApplication app, string failure, string? correlationId = null)
    {
        var headers = new Metadata();
        if (correlationId is not null)
        {
            headers.Add(WellKnownHeaders.CorrelationId, correlationId);
        }

        var act = async () => await app.CreateClient().FailAsync(new EchoRequest { Value = failure }, headers);

        return (await act.Should().ThrowAsync<RpcException>()).Which;
    }
}
