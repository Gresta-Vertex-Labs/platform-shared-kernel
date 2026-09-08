using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Compression;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Serialization;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// PT-01: <see cref="PayloadTransformOptions"/> defaults.
/// PT-04 / PT-13: <see cref="MessagingBusBuilder.WithPayloadTransform"/> Build()-time dependency guard.
/// PT-08: <see cref="MessagingBusBuilder.ConfigurePayloadTransform"/> DI-resolution wiring.
/// PT-12: default-disabled — Build() succeeds with zero registered dependencies when
///        <see cref="MessagingBusBuilder.WithPayloadTransform"/> is never called.
/// PA-15 (P-499): Build()-time best-effort <c>ISynchronousEncryptionKeyProvider</c> guard.
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
    // PA-15 (P-499): Build()-time best-effort ISynchronousEncryptionKeyProvider guard
    // -------------------------------------------------------------------------

    [Fact]
    public void WithPayloadTransform_EnableEncryption_KeyProviderInstanceNotSynchronous_BuildThrows()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(new FakeRemoteEncryptionKeyProvider());
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableEncryption = true)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ISynchronousEncryptionKeyProvider*",
                "Build() must throw when the registered IEncryptionKeyProvider instance is " +
                "statically provable not to implement ISynchronousEncryptionKeyProvider");
    }

    [Fact]
    public void WithPayloadTransform_EnableEncryption_KeyProviderInstanceIsSynchronous_BuildSucceeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(new FakeEncryptionKeyProvider());
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableEncryption = true)
            .Build();

        act.Should().NotThrow(
            "the registered IEncryptionKeyProvider instance genuinely implements " +
            "ISynchronousEncryptionKeyProvider");
    }

    [Fact]
    public void WithPayloadTransform_EnableEncryption_KeyProviderTypeNotSynchronous_BuildThrows()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider, FakeRemoteEncryptionKeyProvider>();
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableEncryption = true)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ISynchronousEncryptionKeyProvider*",
                "Build() must throw when the registered IEncryptionKeyProvider ImplementationType " +
                "is statically provable not to implement ISynchronousEncryptionKeyProvider");
    }

    [Fact]
    public void WithPayloadTransform_EnableEncryption_KeyProviderTypeIsSynchronous_BuildSucceeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider, FakeEncryptionKeyProvider>();
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableEncryption = true)
            .Build();

        act.Should().NotThrow(
            "the registered IEncryptionKeyProvider ImplementationType genuinely implements " +
            "ISynchronousEncryptionKeyProvider");
    }

    [Fact]
    public void WithPayloadTransform_EnableEncryption_KeyProviderRegisteredViaFactory_BuildDoesNotThrow_EagerCheckSkipped()
    {
        // Not statically provable without invoking the factory, which Build() must never do —
        // the eager check is skipped and SK.01.P492's own runtime NotSupportedException remains
        // the guaranteed backstop (proven directly against AesGcmEncryptionService below).
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(_ => new FakeRemoteEncryptionKeyProvider());
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithPayloadTransform(o => o.EnableEncryption = true)
            .Build();

        act.Should().NotThrow(
            "an ImplementationFactory-registered provider is not statically inspectable, so the " +
            "eager Build()-time check must be skipped rather than invoking the factory");
    }

    [Fact]
    public void FactorySkippedProvider_CallingSynchronousEncrypt_ThrowsNotSupportedException_GuaranteedRuntimeBackstop()
    {
        // Proves SK.01.P492's own runtime backstop still fires when Build()'s eager check was
        // skipped (the ImplementationFactory case above) — the registered provider is genuinely
        // not synchronous, so the real AesGcmEncryptionService must refuse the synchronous
        // Encrypt/Decrypt path rather than silently blocking a thread.
        var encryptionService = new AesGcmEncryptionService(new FakeRemoteEncryptionKeyProvider());

        var act = () => encryptionService.Encrypt([1, 2, 3], []);

        act.Should().Throw<NotSupportedException>(
            "AesGcmEncryptionService must refuse the synchronous Encrypt member against a provider " +
            "not confirmed genuinely synchronous, directing the caller to EncryptAsync instead");
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
