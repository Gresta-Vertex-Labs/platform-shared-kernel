using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Compression;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Serialization;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// PT-01: <see cref="PayloadTransformOptions"/> defaults.
/// PT-04 / PT-13: <see cref="MessagingBusBuilder.WithPayloadTransform"/> Build()-time dependency guard.
/// PT-08: <see cref="MessagingBusBuilder.ConfigurePayloadTransform"/> DI-resolution wiring.
/// PT-12: default-disabled — Build() succeeds with zero registered dependencies when
///        <see cref="MessagingBusBuilder.WithPayloadTransform"/> is never called.
/// </summary>
public sealed class PayloadTransformConfigurationTests
{
    // -------------------------------------------------------------------------
    // PT-01: PayloadTransformOptions defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void PayloadTransformOptions_Defaults_BothFlagsFalse()
    {
        var options = new PayloadTransformOptions();

        options.EnableCompression.Should().BeFalse();
        options.EnableEncryption.Should().BeFalse();
    }

    [Fact]
    public void PayloadTransformOptions_SectionName_IsExpectedValue()
    {
        PayloadTransformOptions.SectionName.Should().Be("SharedKernel:Messaging:PayloadTransform");
    }

    // -------------------------------------------------------------------------
    // PT-04 / PT-13: Build()-time dependency guard
    // -------------------------------------------------------------------------

    [Fact]
    public void WithPayloadTransform_EnableCompression_WithoutCompressorRegistered_BuildThrows()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableCompression = true);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no IPayloadCompressor is registered*",
                "Build() must throw with a diagnostic message about the missing IPayloadCompressor");
    }

    [Fact]
    public void WithPayloadTransform_EnableEncryption_WithoutEncryptionServiceRegistered_BuildThrows()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableEncryption = true);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no ISymmetricEncryptionService is registered*",
                "Build() must throw with a diagnostic message about the missing ISymmetricEncryptionService");
    }

    [Fact]
    public void WithPayloadTransform_BothFlagsEnabled_WithBothDependenciesRegistered_BuildSucceeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPayloadCompressor>());
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o =>
            {
                o.EnableCompression = true;
                o.EnableEncryption = true;
            })
            .Build();

        act.Should().NotThrow("both required dependencies are registered");
    }

    [Fact]
    public void WithPayloadTransform_NoConfiguration_BuildSucceeds_EvenWithoutDependenciesRegistered()
    {
        // Both flags default to false — calling WithPayloadTransform() with no configuration must
        // never require IPayloadCompressor/ISymmetricEncryptionService to be registered.
        var services = new ServiceCollection();

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform()
            .Build();

        act.Should().NotThrow("neither flag is enabled, so neither dependency is required");
    }

    // -------------------------------------------------------------------------
    // PT-12: default-disabled regression — WithPayloadTransform() never called
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_WithoutCallingWithPayloadTransform_Succeeds()
    {
        var services = new ServiceCollection();

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .Build();

        act.Should().NotThrow("omitting WithPayloadTransform() entirely must have no observable effect");
    }

    // -------------------------------------------------------------------------
    // PT-08: ConfigurePayloadTransform DI-resolution wiring
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigurePayloadTransform_BothFlagsEnabled_ResolvesBothDependencies_AndCallsAddSerializer()
    {
        var cfg = Substitute.For<IBusFactoryConfigurator>();
        var ctx = Substitute.For<IBusRegistrationContext>();
        var compressor = Substitute.For<IPayloadCompressor>();
        var encryptionService = Substitute.For<ISymmetricEncryptionService>();
        ctx.GetService(typeof(IPayloadCompressor)).Returns(compressor);
        ctx.GetService(typeof(ISymmetricEncryptionService)).Returns(encryptionService);

        var options = new PayloadTransformOptions { EnableCompression = true, EnableEncryption = true };

        MessagingBusBuilder.ConfigurePayloadTransform(cfg, ctx, options);

        ctx.Received(1).GetService(typeof(IPayloadCompressor));
        ctx.Received(1).GetService(typeof(ISymmetricEncryptionService));
        cfg.Received(1).AddSerializer(Arg.Any<PayloadTransformSerializerFactory>(), true);
    }

    [Fact]
    public void ConfigurePayloadTransform_OnlyCompressionEnabled_NeverResolvesEncryptionService()
    {
        var cfg = Substitute.For<IBusFactoryConfigurator>();
        var ctx = Substitute.For<IBusRegistrationContext>();
        var compressor = Substitute.For<IPayloadCompressor>();
        ctx.GetService(typeof(IPayloadCompressor)).Returns(compressor);

        var options = new PayloadTransformOptions { EnableCompression = true, EnableEncryption = false };

        MessagingBusBuilder.ConfigurePayloadTransform(cfg, ctx, options);

        ctx.Received(1).GetService(typeof(IPayloadCompressor));
        ctx.DidNotReceive().GetService(typeof(ISymmetricEncryptionService));
        cfg.Received(1).AddSerializer(Arg.Any<PayloadTransformSerializerFactory>(), true);
    }

    [Fact]
    public void ConfigurePayloadTransform_OnlyEncryptionEnabled_NeverResolvesCompressor()
    {
        var cfg = Substitute.For<IBusFactoryConfigurator>();
        var ctx = Substitute.For<IBusRegistrationContext>();
        var encryptionService = Substitute.For<ISymmetricEncryptionService>();
        ctx.GetService(typeof(ISymmetricEncryptionService)).Returns(encryptionService);

        var options = new PayloadTransformOptions { EnableCompression = false, EnableEncryption = true };

        MessagingBusBuilder.ConfigurePayloadTransform(cfg, ctx, options);

        ctx.DidNotReceive().GetService(typeof(IPayloadCompressor));
        ctx.Received(1).GetService(typeof(ISymmetricEncryptionService));
        cfg.Received(1).AddSerializer(Arg.Any<PayloadTransformSerializerFactory>(), true);
    }
}
