using System.Globalization;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SharedKernel.Localization;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Design D1/D13: the status message and every field violation are the client messages HTTP shows — translated into
/// the request culture, with the values of their placeholders.
/// </summary>
public sealed class LocalizationTests
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    [Fact]
    public async Task ResultFailure_IsTranslatedIntoTheRequestCulture_WithItsArguments()
    {
        await using var app = await StartAsync();

        var exception = await FailAsync(app, Failures.LocalizedResult, "tr-TR");

        var status = exception.ShouldHaveRichStatus(StatusCode.NotFound);
        status.Message.Should().Be("42 numaralı sipariş bulunamadı.");
        status.ErrorInfo().Reason.Should().Be("order.not_found");
    }

    [Fact]
    public async Task ResultFailure_WithoutATranslation_KeepsItsOwnMessage()
    {
        await using var app = await StartAsync();

        var exception = await FailAsync(app, Failures.LocalizedResult, "en-US");

        exception.ShouldHaveRichStatus(StatusCode.NotFound).Message.Should().Be("Order 42 was not found.");
    }

    [Fact]
    public async Task FieldViolations_AreTranslatedOneByOne()
    {
        await using var app = await StartAsync();

        var exception = await FailAsync(app, Failures.ValidationException, "tr-TR");

        exception.ShouldHaveRichStatus(StatusCode.InvalidArgument).FieldViolations().Should().Equal(
            ("customer.name", "Ad zorunludur.", "customer.name_required"),
            ("customer.email", "Email is invalid.", "customer.email_invalid"),
            ("order.lines_empty", "An order needs at least one line.", "order.lines_empty"));
    }

    private static Task<WebApplication> StartAsync() =>
        GrpcTestHost.StartAsync(
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton<ILocalizationCatalog>(
                    new LocalizationCatalogBuilder()
                        .Add("order.not_found", Turkish, "{orderId} numaralı sipariş bulunamadı.")
                        .Add("customer.name_required", Turkish, "Ad zorunludur.")
                        .Build());
                builder.Services.AddRequestLocalization(options =>
                {
                    CultureInfo[] cultures = [new("en-US"), Turkish];
                    options.SupportedCultures = cultures;
                    options.SupportedUICultures = cultures;
                    options.SetDefaultCulture("en-US");
                });
            },
            configurePipeline: app => app.UseRequestLocalization());

    private static async Task<RpcException> FailAsync(WebApplication app, string failure, string language)
    {
        var headers = new Metadata { { HeaderNames.AcceptLanguage, language } };

        var act = async () => await app.CreateClient().FailAsync(new EchoRequest { Value = failure }, headers);

        return (await act.Should().ThrowAsync<RpcException>()).Which;
    }
}
