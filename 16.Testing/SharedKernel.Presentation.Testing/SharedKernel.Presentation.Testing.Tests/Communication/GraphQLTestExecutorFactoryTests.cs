using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class GraphQLTestExecutorFactoryTests
{
    [Fact]
    public void Create_RegistersRequestExecutorBuilder_OnServiceCollection()
    {
        var services = new ServiceCollection();

        var builder = GraphQLTestExecutorFactory.Create(services);

        Assert.NotNull(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void Create_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => GraphQLTestExecutorFactory.Create(null!));
}
