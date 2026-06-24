using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class AddFakeDomainServicesTests
{
    [Fact]
    public void AddFakeDomainServices_RegistersFakeClock_AsIClockSingleton()
    {
        var services = new ServiceCollection();
        services.AddFakeDomainServices();

        var provider = services.BuildServiceProvider();
        var clock = provider.GetRequiredService<IClock>();

        Assert.IsType<FakeClock>(clock);
    }

    [Fact]
    public void AddFakeDomainServices_ReturnsSameInstance_AcrossResolutions()
    {
        var services = new ServiceCollection();
        services.AddFakeDomainServices();

        var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IClock>();
        var second = provider.GetRequiredService<IClock>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFakeDomainServices_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddFakeDomainServices());

    [Fact]
    public void AddFakeDomainServices_ReturnsServicesForChaining()
    {
        var services = new ServiceCollection();
        var result = services.AddFakeDomainServices();

        Assert.Same(services, result);
    }
}
