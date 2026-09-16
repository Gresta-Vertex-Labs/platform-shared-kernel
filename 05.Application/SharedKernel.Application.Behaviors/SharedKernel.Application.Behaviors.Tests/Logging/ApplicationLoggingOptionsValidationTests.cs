using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Logging;

namespace SharedKernel.Application.Behaviors.Tests.Logging;

/// <summary>
/// Verifies <c>ApplicationBehaviorsBuilder.Build()</c> wires <see cref="ApplicationLoggingOptions"/>
/// through <c>.ValidateDataAnnotations().ValidateOnStart()</c> so an invalid
/// <see cref="ApplicationLoggingOptions.SlowRequestThreshold"/> fails via <see cref="IStartupValidator"/>
/// (the same mechanism a real host invokes at startup) rather than at the first request that reaches
/// <c>LoggingBehavior</c>.
/// </summary>
public sealed class ApplicationLoggingOptionsValidationTests
{
    [Fact]
    public void Build_LoggingBehaviorOptedIn_ZeroThreshold_FailsStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplicationBehaviors().AddLoggingBehavior().Build();
        services.Configure<ApplicationLoggingOptions>(o => o.SlowRequestThreshold = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var act = () => validator.Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*SlowRequestThreshold*");
    }

    [Fact]
    public void Build_LoggingBehaviorOptedIn_NegativeThreshold_FailsStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplicationBehaviors().AddLoggingBehavior().Build();
        services.Configure<ApplicationLoggingOptions>(o => o.SlowRequestThreshold = TimeSpan.FromSeconds(-1));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var act = () => validator.Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*SlowRequestThreshold*");
    }

    [Fact]
    public void Build_LoggingBehaviorOptedIn_DefaultThreshold_PassesStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplicationBehaviors().AddLoggingBehavior().Build();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var act = () => validator.Validate();

        act.Should().NotThrow();
    }
}
