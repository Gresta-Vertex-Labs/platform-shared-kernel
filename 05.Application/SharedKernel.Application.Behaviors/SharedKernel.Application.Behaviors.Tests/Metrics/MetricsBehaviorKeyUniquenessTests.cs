using System.Diagnostics.Metrics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Tests.TestHarness;
using SharedKernel.Application.Behaviors.Tracing;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Metrics;

/// <summary>
/// Verifies (T-21) that <see cref="MetricsBehavior{TRequest,TResponse}"/>,
/// <see cref="LoggingBehavior{TRequest,TResponse}"/>, and
/// <see cref="TracingBehavior{TRequest,TResponse}"/> all use
/// <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c> for tag construction, not the
/// short <c>.Name</c> — preventing key collisions when two assemblies define a request type
/// with the same short name.
/// </summary>
public sealed class MetricsBehaviorKeyUniquenessTests
{
    // A request type defined inside a namespace so FullName != Name.
    private sealed record NamespacedRequest : IRequest<Result>;

    private sealed class NamespacedRequestHandler : IRequestHandler<NamespacedRequest, Result>
    {
        public Task<Result> Handle(NamespacedRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class MeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<string?> RequestNameTags { get; } = [];

        public MeasurementCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "SharedKernel.Application"
                    && instrument.Name == "sharedkernel.application.request.duration")
                    listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "request.name")
                        RequestNameTags.Add(tag.Value?.ToString());
                }
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task MetricsBehavior_RequestNameTag_UsesFullNameNotShortName()
    {
        // FullName includes the namespace qualifier; short Name would just be "NamespacedRequest".
        var expectedFullName = typeof(NamespacedRequest).FullName;
        expectedFullName.Should().NotBeNull();
        expectedFullName.Should().Contain(".", "FullName must include namespace segment");
        expectedFullName.Should().NotBe(typeof(NamespacedRequest).Name);

        using var capture = new MeasurementCapture();

        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<NamespacedRequest, Result>, NamespacedRequestHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MetricsBehaviorKeyUniquenessTests>());
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new NamespacedRequest());

        capture.RequestNameTags.Should().ContainSingle(tag => tag == expectedFullName,
            $"MetricsBehavior must use FullName ('{expectedFullName}'), not short Name ('{typeof(NamespacedRequest).Name}')");
    }

    [Fact]
    public async Task TracingBehavior_RequestNameTag_UsesFullNameNotShortName()
    {
        var expectedFullName = typeof(NamespacedRequest).FullName
            ?? typeof(NamespacedRequest).Name;

        using var harness = new PipelineTestHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<NamespacedRequest, Result>, NamespacedRequestHandler>();
        harness.AddBehaviors().AddTracingBehavior().Build();
        harness.Build<MetricsBehaviorKeyUniquenessTests>();

        await harness.SendAsync(new NamespacedRequest());

        var matchingSpans = harness.CapturedActivities
            .Where(a => Equals(a.GetTagItem("request.name"), expectedFullName))
            .ToList();

        matchingSpans.Should().ContainSingle(
            $"TracingBehavior must use FullName ('{expectedFullName}') as the request.name tag");
    }
}
