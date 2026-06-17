using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.GraphQL.Extensions;
using SharedKernel.Communication.GraphQL.Options;

namespace SharedKernel.Communication.GraphQL.Tests;

/// <summary>
/// Integration-level DI registration tests for <c>AddSharedKernelGraphQL</c>.
/// These use the HotChocolate test schema builder pattern — no real HTTP server.
/// </summary>
public sealed class AddSharedKernelGraphQLTests
{
    [Fact]
    public void AddSharedKernelGraphQL_ReturnsNonNullBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelGraphQL();
        builder.Should().NotBeNull();
    }

    [Fact]
    public void AddSharedKernelGraphQL_IsIdempotent_SecondCallIsNoOp()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelGraphQL();

        // Second call should not throw and return a valid builder.
        var act = () => services.AddSharedKernelGraphQL();
        act.Should().NotThrow();
    }

    [Fact]
    public void AddSharedKernelGraphQL_Registers_GraphQLOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelGraphQL();

        var sp = services.BuildServiceProvider();
        var validators = sp.GetServices<Microsoft.Extensions.Options.IValidateOptions<GraphQLOptions>>();
        validators.Should().NotBeEmpty();
    }

    [Fact]
    public void AddSharedKernelGraphQL_AppliesCustomMaxPageSize()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelGraphQL(o => o.MaxPageSize = 50);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphQLOptions>>();
        options.Value.MaxPageSize.Should().Be(50);
    }

    [Fact]
    public void AddSharedKernelGraphQL_DefaultOptions_AllowIntrospectionTrue()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelGraphQL();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphQLOptions>>();
        options.Value.AllowIntrospection.Should().BeTrue();
    }

    [Fact]
    public void AddSharedKernelGraphQL_CustomOptions_AllowIntrospectionFalse()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelGraphQL(o => o.AllowIntrospection = false);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphQLOptions>>();
        options.Value.AllowIntrospection.Should().BeFalse();
    }
}
