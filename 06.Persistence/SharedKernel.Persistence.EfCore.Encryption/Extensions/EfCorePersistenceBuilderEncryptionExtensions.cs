using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Configuration;
using SharedKernel.Persistence.EfCore.Encryption.Crypto;
using SharedKernel.Persistence.EfCore.Encryption.Interception;
using SharedKernel.Persistence.EfCore.Encryption.KeyRing;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;

namespace SharedKernel.Persistence.EfCore.Encryption.Extensions;

/// <summary>Wires field-level encryption into a SharedKernel EF Core context.</summary>
public static class EfCorePersistenceBuilderEncryptionExtensions
{
    /// <summary>
    /// Turns on field-level AES-256-GCM encryption for every <c>.Encrypt(...)</c> property of the context, with
    /// blind-index lookups, the query guard, the maintenance job and, optionally, per-tenant data keys.
    /// </summary>
    /// <typeparam name="TContext">The context.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="configure">Chooses the key source and options; see <see cref="FieldEncryptionBuilder"/>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="EncryptionOptions"/> binds from <c>SharedKernel:Persistence:Encryption</c> when an
    /// <see cref="IConfiguration"/> is registered, and is validated when the host starts; the key source is resolved
    /// then too, so a missing key provider fails startup, not the first request.
    /// </para>
    /// <para>
    /// Registers, per context, a scoped <see cref="IEncryptionRotationJob"/> and <see cref="ITenantEncryptionKeyManager"/>;
    /// the unkeyed registrations belong to the first context that calls this method.
    /// </para>
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> UseFieldEncryption<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        Action<FieldEncryptionBuilder>? configure = null)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var settings = RegisterInfrastructure(services);

        configure?.Invoke(new FieldEncryptionBuilder(services, settings));

        services.TryAddScoped<EncryptionMaintenanceJob<TContext>>();
        services.TryAddScoped<IEncryptionRotationJob>(sp => sp.GetRequiredService<EncryptionMaintenanceJob<TContext>>());
        services.TryAddScoped<TenantEncryptionKeyManager<TContext>>();
        services.TryAddScoped<ITenantEncryptionKeyManager>(sp => sp.GetRequiredService<TenantEncryptionKeyManager<TContext>>());

        return builder;
    }

    private static FieldEncryptionSettings RegisterInfrastructure(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(FieldEncryptionSettings))?.ImplementationInstance is FieldEncryptionSettings existing)
            return existing;

        var settings = new FieldEncryptionSettings();
        services.AddSingleton(settings);

        services.AddOptions<EncryptionOptions>().ValidateOnStart();
        services.AddSingleton<IConfigureOptions<EncryptionOptions>, BindEncryptionOptionsFromConfiguration>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EncryptionOptions>, KeySourceStartupCheck>());

        services.TryAddSingleton<FieldKeyRing>();
        services.TryAddSingleton<FieldCipher>();
        services.TryAddSingleton<ConfigurationBlindIndexKeyProvider>();
        services.TryAddSingleton<BlindIndexer>();
        services.TryAddSingleton<TenantKeyStore>();
        services.TryAddSingleton<FieldEncryptionRuntime>();
        services.TryAddSingleton<EncryptionInterceptor>();
        services.TryAddSingleton<EncryptedMemberQueryGuard>();
        services.AddSingleton<IPersistenceOptionsExtension, EncryptionOptionsContributor>();
        services.AddSingleton<IPersistenceModelConventionFactory, EncryptionModelConventionFactory>();

        services.AddHostedService<KeyRingRefreshHostedService>();
        services.AddKeyedSingleton<IEncryptionKeyProviderProbe, FieldEncryptionKeyRingProbe>(FieldEncryptionServiceKeys.KeyRingProbe);

        return settings;
    }

    /// <summary>Binds <see cref="EncryptionOptions.SectionName"/> when the container has an <see cref="IConfiguration"/>.</summary>
    private sealed class BindEncryptionOptionsFromConfiguration(IServiceProvider services) : IConfigureOptions<EncryptionOptions>
    {
        public void Configure(EncryptionOptions options) =>
            services.GetService<IConfiguration>()?.GetSection(EncryptionOptions.SectionName).Bind(options);
    }

    /// <summary>Resolves the key source when options are first validated (at host start), so a missing provider fails startup.</summary>
    private sealed class KeySourceStartupCheck(IServiceProvider services) : IValidateOptions<EncryptionOptions>
    {
        public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
        {
            try
            {
                var settings = services.GetRequiredService<FieldEncryptionSettings>();
                if (settings.KeySource == FieldEncryptionSettings.ConfigurationKeySource)
                {
                    // Checked against the options being validated: resolving the provider would read the options
                    // that are being created right now.
                    if (options.Keys.CurrentKeyId is null || options.Keys.Keys.Count == 0)
                        return ValidateOptionsResult.Fail("Field encryption reads its keys from configuration, but 'SharedKernel:Persistence:Encryption:Keys' is empty.");
                }
                else
                {
                    _ = settings.ResolveKeyProvider(services);
                }

                if (settings.TenantDataKeys)
                    _ = settings.EnvelopeProviderFactory?.Invoke(services) ?? services.GetService<Cryptography.Envelope.IEnvelopeEncryptionProvider>()
                        ?? throw new InvalidOperationException(
                            "Tenant data keys need an 'IEnvelopeEncryptionProvider' (a KMS master key that wraps them).");

                return ValidateOptionsResult.Success;
            }
            catch (InvalidOperationException exception)
            {
                return ValidateOptionsResult.Fail(exception.Message);
            }
        }
    }
}
