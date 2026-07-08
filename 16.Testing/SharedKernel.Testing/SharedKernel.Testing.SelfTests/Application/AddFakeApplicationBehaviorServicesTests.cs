using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Testing.Application;
using Xunit;

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
    public void AddFakeApplicationBehaviorServices_RegistersFakeAuthorizationContext()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeAuthorizationContext>(provider.GetRequiredService<IAuthorizationContext>());
    }

    [Fact]
    public void AddFakeApplicationBehaviorServices_RegistersFakeIdempotencyKeyStore()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeIdempotencyKeyStore>(provider.GetRequiredService<IIdempotencyKeyStore>());
    }

    [Fact]
    public void AddFakeApplicationBehaviorServices_AllThreeAreSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IUnitOfWork>(), provider.GetRequiredService<IUnitOfWork>());
        Assert.Same(provider.GetRequiredService<IAuthorizationContext>(), provider.GetRequiredService<IAuthorizationContext>());
        Assert.Same(provider.GetRequiredService<IIdempotencyKeyStore>(), provider.GetRequiredService<IIdempotencyKeyStore>());
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
