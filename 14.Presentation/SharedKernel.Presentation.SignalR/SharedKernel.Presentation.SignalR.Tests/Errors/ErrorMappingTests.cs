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
/// Design D12: every error of a hub method reaches the client as a HubException "{code}: {client message}", with the
/// same localization and server-error redaction as an HTTP problem response, over a live connection.
/// </summary>
public sealed class ErrorMappingTests
{
    private const string GenericUnexpected = "An unexpected error occurred.";

    private const string GenericUnavailable = "The service is temporarily unavailable. Try again later.";

    private static readonly string MappingCategory = typeof(HubExceptionMappingFilter).FullName!;

    [Fact]
    public async Task SharedKernelException_ReachesTheClient_AsItsCodeAndMessage()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));

        message.Should().Be("order.not_found: Order 42 was not found.");
    }

    [Fact]
    public async Task ServerErrorException_OutsideDevelopment_KeepsItsCode_ButNotItsMessage()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnavailable));

        message.Should().Be($"search.unreachable: {GenericUnavailable}");
        message.Should().NotContain("search.internal");
    }

    [Fact]
    public async Task ServerErrorException_InDevelopment_ShowsItsMessage()
    {
        await using var app = await StartAsync(SignalRTestHost.Development);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnavailable));

        message.Should().Be($"search.unreachable: {HubMessages.InternalDetail}");
    }

    [Fact]
    public async Task UnknownException_InProduction_IsTheGenericUnexpectedError()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnknown));

        message.Should().Be($"{ErrorCodes.Unexpected.Default}: {GenericUnexpected}");
        message.Should().NotContain("db.internal").And.NotContain("secret");
    }

    [Fact]
    public async Task UnknownException_InDevelopment_ShowsTheExceptionMessage()
    {
        await using var app = await StartAsync(SignalRTestHost.Development);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowUnknown));

        message.Should().Be($"{ErrorCodes.Unexpected.Default}: {HubMessages.Secret}");
    }

    [Fact]
    public async Task HubException_ThrownByTheHub_PassesUnchanged()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowHubException));

        message.Should().Be("kept exactly as thrown");
    }

    [Fact]
    public async Task ValidationException_WithSeveralErrors_IsOneValidationError()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowValidation));

        message.Should().Be($"{ErrorCodes.Validation.Failed}: 2 validation errors occurred.");
    }

    [Fact]
    public async Task ValidationException_WithOneError_KeepsThatErrorsCode()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowSingleValidation));

        message.Should().Be("name.required: Name is required.");
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

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));

        message.Should().Be("order.not_found: 42 numaralı sipariş bulunamadı.");
    }

    [Fact]
    public async Task ErrorMessage_WithoutATranslation_KeepsItsOwnText()
    {
        await using var app = await StartLocalizedAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.Errors,
            new Dictionary<string, string> { [HeaderNames.AcceptLanguage] = "en-US" });

        var message = await connection.InvokeExpectingErrorAsync(nameof(ErrorsHub.ThrowNotFound));

        message.Should().Be("order.not_found: Order 42 was not found.");
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
