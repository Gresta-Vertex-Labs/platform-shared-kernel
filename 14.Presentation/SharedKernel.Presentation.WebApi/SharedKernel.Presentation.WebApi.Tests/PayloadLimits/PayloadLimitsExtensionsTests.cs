using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.PayloadLimits;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.PayloadLimits;

/// <summary>
/// Unit tests for <see cref="PayloadLimitsExtensions"/> covering the DI-registration half
/// (<see cref="PayloadLimitsExtensions.AddSharedKernelPayloadLimits"/>) and the
/// request-scoped-middleware half's graceful degradation when
/// <see cref="IHttpMaxRequestBodySizeFeature.IsReadOnly"/> is <see langword="true"/> (T-46).
/// </summary>
public class PayloadLimitsExtensionsTests
{
    [Fact]
    public void AddSharedKernelPayloadLimits_NoConfiguration_WiresDocumentedDefaultMaxJsonDepth()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelPayloadLimits();
        var provider = services.BuildServiceProvider();

        var minimalApiJsonOptions = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;
        var mvcJsonOptions = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value;

        minimalApiJsonOptions.SerializerOptions.MaxDepth.Should().Be(32);
        mvcJsonOptions.JsonSerializerOptions.MaxDepth.Should().Be(32);
    }

    [Fact]
    public void AddSharedKernelPayloadLimits_CustomMaxJsonDepth_WiresConfiguredValueIntoBothSurfaces()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelPayloadLimits(options => options.MaxJsonDepth = 8);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions.MaxDepth.Should().Be(8);
        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions.MaxDepth.Should().Be(8);
    }

    [Fact]
    public void AddSharedKernelPayloadLimits_MvcServicesNeverRegistered_DoesNotThrow()
    {
        // The MVC-surface registration is a plain IOptions configuration callback: it must never
        // require AddControllers()/AddMvc() to have been called, and must never throw merely
        // because MVC services are absent from the container.
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelPayloadLimits();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task UseSharedKernelPayloadLimits_FeatureIsReadOnly_DoesNotThrow_DegradesGracefully()
    {
        var nextInvoked = false;
        var pipeline = BuildPipeline(
            _ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            },
            options => options.MaxRequestBodySizeBytes = 100);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new ReadOnlyMaxRequestBodySizeFeature());

        Func<Task> act = async () => await pipeline(httpContext);

        await act.Should().NotThrowAsync();
        nextInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task UseSharedKernelPayloadLimits_FeatureWritable_SetsConfiguredMaxRequestBodySize()
    {
        var pipeline = BuildPipeline(_ => Task.CompletedTask, options => options.MaxRequestBodySizeBytes = 12_345);

        var httpContext = new DefaultHttpContext();
        var feature = new WritableMaxRequestBodySizeFeature();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        await pipeline(httpContext);

        feature.MaxRequestBodySize.Should().Be(12_345);
    }

    [Fact]
    public async Task UseSharedKernelPayloadLimits_NoFeatureAvailable_DoesNotThrow()
    {
        // Mirrors the IsReadOnly guard: a host/server shape exposing no
        // IHttpMaxRequestBodySizeFeature at all must not crash the pipeline either.
        var nextInvoked = false;
        var pipeline = BuildPipeline(
            _ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            },
            options => options.MaxRequestBodySizeBytes = 100);

        var httpContext = new DefaultHttpContext();

        Func<Task> act = async () => await pipeline(httpContext);

        await act.Should().NotThrowAsync();
        nextInvoked.Should().BeTrue();
    }

    private static RequestDelegate BuildPipeline(RequestDelegate terminal, Action<PayloadLimitsOptions>? configure = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(services);
        appBuilder.UseSharedKernelPayloadLimits(configure);
        appBuilder.Run(terminal);
        return appBuilder.Build();
    }

    private sealed class ReadOnlyMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => true;

        public long? MaxRequestBodySize
        {
            get => null;
            set => throw new InvalidOperationException(
                "The maximum request body size cannot be changed after it has been set.");
        }
    }

    private sealed class WritableMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; }
    }
}
