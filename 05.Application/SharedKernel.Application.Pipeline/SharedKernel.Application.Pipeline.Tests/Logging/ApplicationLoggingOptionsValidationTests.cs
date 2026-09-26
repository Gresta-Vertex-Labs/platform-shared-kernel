using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;

namespace SharedKernel.Application.Pipeline.Tests.Logging;

/// <summary>
/// Verifies <c>AddSharedKernelApplication</c> wires <see cref="ApplicationLoggingOptions"/>
/// through <c>.ValidateDataAnnotations().ValidateOnStart()</c> so an invalid
/// <see cref="ApplicationLoggingOptions.SlowRequestThreshold"/> fails via <see cref="IStartupValidator"/>
/// (the same mechanism a real host invokes at startup) rather than at the first request that reaches
/// <c>LoggingBehavior</c>.
/// </summary>
public sealed class ApplicationLoggingOptionsValidationTests
{
    [Fact]
    public void AddSharedKernelApplication_ZeroThreshold_FailsStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(ApplicationLoggingOptionsValidationTests).Assembly, app => app.UseMediatR());
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance); // this assembly declares [RequirePermission] requests
        services.Configure<ApplicationLoggingOptions>(o => o.SlowRequestThreshold = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var act = () => validator.Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*SlowRequestThreshold*");
    }

    [Fact]
    public void AddSharedKernelApplication_NegativeThreshold_FailsStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(ApplicationLoggingOptionsValidationTests).Assembly, app => app.UseMediatR());
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance); // this assembly declares [RequirePermission] requests
        services.Configure<ApplicationLoggingOptions>(o => o.SlowRequestThreshold = TimeSpan.FromSeconds(-1));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var act = () => validator.Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*SlowRequestThreshold*");
    }

    [Fact]
    public void AddSharedKernelApplication_DefaultThreshold_PassesStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(ApplicationLoggingOptionsValidationTests).Assembly, app => app.UseMediatR());
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance); // this assembly declares [RequirePermission] requests

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var act = () => validator.Validate();

        act.Should().NotThrow();
    }
}
