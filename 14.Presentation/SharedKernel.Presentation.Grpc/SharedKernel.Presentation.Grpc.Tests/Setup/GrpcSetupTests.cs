using FluentAssertions;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.Grpc.Interceptors;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Setup;

/// <summary>Design D13: <c>AddSharedKernelGrpc</c> — options binding and defaults, idempotency, registrations.</summary>
public sealed class GrpcSetupTests
{
    [Fact]
    public void ErrorDomain_DefaultsToTheApplicationName()
    {
        var builder = CreateBuilder();
        builder.Environment.ApplicationName = "orders-api";

        builder.AddSharedKernelGrpc();

        ResolveOptions(builder).ErrorDomain.Should().Be("orders-api");
    }

    [Fact]
    public void ErrorDomain_IsBoundFromConfiguration_AndConfigureWins()
    {
        var fromConfiguration = CreateBuilder(("SharedKernel:Presentation:Grpc:ErrorDomain", "orders.example.com"));
        fromConfiguration.AddSharedKernelGrpc();

        var fromConfigure = CreateBuilder(("SharedKernel:Presentation:Grpc:ErrorDomain", "orders.example.com"));
        fromConfigure.AddSharedKernelGrpc(options => options.ErrorDomain = "billing.example.com");

        ResolveOptions(fromConfiguration).ErrorDomain.Should().Be("orders.example.com");
        ResolveOptions(fromConfigure).ErrorDomain.Should().Be("billing.example.com");
    }

    [Fact]
    public void BlankErrorDomain_FallsBackToTheApplicationName()
    {
        var builder = CreateBuilder();
        builder.Environment.ApplicationName = "orders-api";

        builder.AddSharedKernelGrpc(options => options.ErrorDomain = "  ");

        ResolveOptions(builder).ErrorDomain.Should().Be("orders-api");
    }

    [Fact]
    public void BlankErrorDomain_WithoutAnApplicationName_FailsValidation()
    {
        var builder = CreateBuilder();
        builder.Environment.ApplicationName = " ";
        builder.AddSharedKernelGrpc();

        var act = () => ResolveOptions(builder);

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(nameof(SharedKernelGrpcOptions.ErrorDomain));
    }

    [Fact]
    public void CalledTwice_RegistersTheInterceptorOnce_AndAppliesEachConfigure()
    {
        var builder = CreateBuilder();

        builder.AddSharedKernelGrpc(options => options.ErrorDomain = "first.example.com");
        builder.AddSharedKernelGrpc(options => options.ErrorDomain = "second.example.com");

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value.Interceptors
            .Should().ContainSingle(registration => registration.Type == typeof(GrpcExceptionInterceptor));
        provider.GetRequiredService<IOptions<SharedKernelGrpcOptions>>().Value.ErrorDomain.Should().Be("second.example.com");
    }

    [Fact]
    public void RegistersTheSharedKernelAuthorization()
    {
        var builder = CreateBuilder();

        builder.AddSharedKernelGrpc();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IAuthorizationPolicyProvider>().GetType().Name.Should().Be("SharedKernelAuthorizationPolicyProvider");
        provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>().GetType().Name.Should().Be("SharedKernelAuthorizationResultHandler");
    }

    [Fact]
    public void ReturnsTheGrpcServerBuilder_OfTheSameServices()
    {
        var builder = CreateBuilder();

        var grpc = builder.AddSharedKernelGrpc();

        grpc.Services.Should().BeSameAs(builder.Services);
    }

    [Fact]
    public void NullBuilder_IsRejected()
    {
        var act = () => GrpcHostBuilderExtensions.AddSharedKernelGrpc(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static HostApplicationBuilder CreateBuilder(params (string Key, string Value)[] configuration)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(configuration.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)));
        return builder;
    }

    private static SharedKernelGrpcOptions ResolveOptions(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SharedKernelGrpcOptions>>().Value;
    }

    [Fact]
    public void P579_ThePackage_ReferencesNeitherTheWebApiPackageNorContracts()
    {
        // gRPC takes what it shares with HTTP from SharedKernel.Presentation.Core, so a gRPC host takes no HTTP API
        // stack; protobuf messages, not Contracts DTOs, are its wire contract (GrpcNeverReferencesContracts).
        var references = typeof(GrpcHostBuilderExtensions).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();

        references.Should().Contain("SharedKernel.Presentation.Core");
        references.Should().NotContain(["SharedKernel.Presentation.WebApi", "SharedKernel.Contracts"]);
    }
}
