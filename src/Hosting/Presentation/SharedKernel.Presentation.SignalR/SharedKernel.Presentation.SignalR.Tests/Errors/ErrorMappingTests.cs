using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SharedKernel.Localization;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Errors;

/// <summary>
/// Design D12: every error of a hub method becomes a HubException "{code}: {client message}", with the same
/// localization and server-error redaction as an HTTP problem response, over a live connection. The client receives
/// it behind SignalR's own sentence and reads it back with <see cref="HubErrorMessage.TryParse"/> (R34).
/// </summary>
public sealed class ErrorMappingTests
{
    private const string GenericUnexpected = "An unexpected error occurred.";

    private const string GenericUnavailable = "The service is temporarily unavailable. Try again later.";

    private const string GenericTimeout = "The operation did not complete in time.";

    private static readonly string MappingCategory = typeof(HubExceptionMappingFilter).FullName!;

    [Fact]
    public async Task SharedKernelException_ReachesTheClient_AsItsCodeAndMessage()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));

        error.Should().Be(new HubError("order.not_found", "Order 42 was not found."));
    }

    [Fact]
    public async Task ServerErrorException_OutsideDevelopment_KeepsItsCode_ButNotItsMessage()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowUnavailable));

        SignalRTestHost.ReadCodedError(failure, nameof(ErrorsHub.ThrowUnavailable))
            .Should().Be(new HubError("search.unreachable", GenericUnavailable));
        failure.Message.Should().NotContain("search.internal");
    }

    [Fact]
    public async Task ServerErrorException_InDevelopment_ShowsItsMessage()
    {
        await using var app = await StartAsync(SignalRTestHost.Development);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnavailable));

        error.Should().Be(new HubError("search.unreachable", HubMessages.InternalDetail));
    }

    [Fact]
    public async Task UnknownException_InProduction_IsTheGenericUnexpectedError()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowUnknown));

        SignalRTestHost.ReadCodedError(failure, nameof(ErrorsHub.ThrowUnknown))
            .Should().Be(new HubError(ErrorCodes.Unexpected.Default, GenericUnexpected));
        failure.Message.Should().NotContain("db.internal").And.NotContain("secret");
    }

    [Fact]
    public async Task UnknownException_InDevelopment_ShowsTheExceptionMessage()
    {
        await using var app = await StartAsync(SignalRTestHost.Development);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnknown));

        error.Should().Be(new HubError(ErrorCodes.Unexpected.Default, HubMessages.Secret));
    }

    [Theory]
    [InlineData(nameof(ErrorsHub.ThrowTimeout), typeof(TimeoutException))]
    [InlineData(nameof(ErrorsHub.ThrowInternalCancellation), typeof(TaskCanceledException))]
    public async Task TimeoutInsideTheService_IsTheTimeoutError_LoggedAsAServerError_AsOverHttp(string method, Type thrown)
    {
        // HTTP answers the same exceptions 504 timeout.default; the connection is open, so the client did not cause it.
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: loggerFactory);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var failure = await connection.InvokeExpectingFailureAsync(method);

        SignalRTestHost.ReadCodedError(failure, method).Should().Be(new HubError(ErrorCodes.Timeout.Default, GenericTimeout));
        failure.Message.Should().NotContain("search.internal");

        var records = loggerFactory.GetLogger(MappingCategory).Records;
        records.ShouldHaveLogged(new EventId(14103), LogLevel.Error).Exception.Should().BeOfType(thrown);
        records.ShouldNotHaveLogged(new EventId(14100));
    }

    [Fact]
    public async Task HubException_ThrownByTheHub_PassesUnchanged_AndHasNoCode()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowHubException));

        failure.Message.Should().Be(SignalRTestHost.InvocationFailure(nameof(ErrorsHub.ThrowHubException), "kept exactly as thrown"));
        HubErrorMessage.TryParse(failure.Message, out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task ValidationException_WithSeveralErrors_IsOneValidationError()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowValidation));

        error.Should().Be(new HubError(ErrorCodes.Validation.Failed, "2 validation errors occurred."));
    }

    [Fact]
    public async Task ValidationException_WithOneError_KeepsThatErrorsCode()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowSingleValidation));

        error.Should().Be(new HubError("name.required", "Name is required."));
    }

    [Fact]
    public async Task SuccessfulInvocation_IsUntouched()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var echoed = await connection.InvokeAsync<string>(nameof(ErrorsHub.Echo), "hello");

        echoed.Should().Be("hello");
    }

    [Fact]
    public async Task ServerErrors_AreLoggedAtError_ClientErrorsAtDebug()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: loggerFactory);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));
        await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnavailable));
        await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnknown));

        var records = loggerFactory.GetLogger(MappingCategory).Records;
        records.ShouldHaveLogged(new EventId(14104), LogLevel.Debug).Exception.Should().BeOfType<SharedKernel.Core.Exceptions.NotFoundException>();
        records.ShouldHaveLogged(new EventId(14103), LogLevel.Error).Exception.Should().NotBeNull();
        records.ShouldHaveLogged(new EventId(14100), LogLevel.Error).Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ErrorMessage_IsTranslatedIntoTheConnectionsCulture_WithItsArguments()
    {
        await using var app = await StartLocalizedAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.Errors,
            new Dictionary<string, string> { [HeaderNames.AcceptLanguage] = "tr-TR" });

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));

        error.Should().Be(new HubError("order.not_found", "42 numaralı sipariş bulunamadı."));
    }

    [Fact]
    public async Task ErrorMessage_WithoutATranslation_KeepsItsOwnText()
    {
        await using var app = await StartLocalizedAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.Errors,
            new Dictionary<string, string> { [HeaderNames.AcceptLanguage] = "en-US" });

        var error = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));

        error.Should().Be(new HubError("order.not_found", "Order 42 was not found."));
    }

    [Fact]
    public async Task ConnectionClosingDuringAnInvocation_IsLoggedAtDebug_NotAsAnUnhandledError()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: loggerFactory);
        var connection = await app.ConnectAsync(HubPaths.Errors);
        var counter = app.Services.GetRequiredService<InvocationCounter>();

        _ = connection.InvokeAsync<string>(nameof(ErrorsHub.WaitForever));
        await SignalRTestHost.WaitUntilAsync(() => counter.Count == 1);
        counter.Count.Should().Be(1, "the hub method must be running before the connection closes");
        await connection.DisposeAsync();

        var logger = loggerFactory.GetLogger(MappingCategory);
        await SignalRTestHost.WaitUntilAsync(() => logger.Records.Any(record => record.EventId.Id == 14106));

        logger.Records.ShouldHaveLogged(new EventId(14106), LogLevel.Debug);
        logger.Records.ShouldNotHaveLogged(new EventId(14100));
    }

    private static Task<WebApplication> StartAsync(
        string environment = SignalRTestHost.Production,
        InMemoryLoggerFactory? loggerFactory = null) =>
        SignalRTestHost.StartAsync(
            app => app.MapHub<ErrorsHub>(HubPaths.Errors),
            environment: environment,
            loggerFactory: loggerFactory);

    private static Task<WebApplication> StartLocalizedAsync() =>
        SignalRTestHost.StartAsync(
            app =>
            {
                app.UseRequestLocalization();
                app.MapHub<ErrorsHub>(HubPaths.Errors);
            },
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton<ILocalizationCatalog>(
                    new LocalizationCatalogBuilder()
                        .Add("order.not_found", new CultureInfo("tr-TR"), "{orderId} numaralı sipariş bulunamadı.")
                        .Build());
                builder.Services.AddRequestLocalization(options =>
                {
                    CultureInfo[] cultures = [new("en-US"), new("tr-TR")];
                    options.SupportedCultures = cultures;
                    options.SupportedUICultures = cultures;
                    options.SetDefaultCulture("en-US");
                });
            });
}
