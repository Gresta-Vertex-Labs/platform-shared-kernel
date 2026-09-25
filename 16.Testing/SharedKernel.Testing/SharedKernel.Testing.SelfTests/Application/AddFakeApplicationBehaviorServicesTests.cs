using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Execution.Transactions;
using SharedKernel.Execution.Context;
using SharedKernel.Testing.Application;
using Xunit;
using SharedKernel.Persistence.Testing;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class AddFakeApplicationBehaviorServicesTests
{
    [Fact]
    public void AddFakeApplicationBehaviorServices_RegistersFakeUnitOfWork()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeUnitOfWork>(provider.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public void AddFakeApplicationBehaviorServices_RegistersFakeRequestContext()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeRequestContext>(provider.GetRequiredService<IRequestContext>());
    }

    [Fact]
    public void AddFakeApplicationBehaviorServices_RegistersFakeRequestIdempotencyStore()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeRequestIdempotencyStore>(provider.GetRequiredService<IRequestIdempotencyStore>());
    }

    [Fact]
    public void AddFakeApplicationBehaviorServices_AllThreeAreSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IUnitOfWork>(), provider.GetRequiredService<IUnitOfWork>());
        Assert.Same(provider.GetRequiredService<IRequestContext>(), provider.GetRequiredService<IRequestContext>());
        Assert.Same(
            provider.GetRequiredService<IRequestIdempotencyStore>(),
            provider.GetRequiredService<IRequestIdempotencyStore>());
    }

    [Fact]
    public void AddFakeApplicationBehaviorServices_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeApplicationBehaviorServices());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeApplicationBehaviorServices();
        return services.BuildServiceProvider();
    }
}
