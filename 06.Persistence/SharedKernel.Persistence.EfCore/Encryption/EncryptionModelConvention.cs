using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// EF Core model-finalizing convention that automatically applies
/// <see cref="EncryptedValueConverter"/> to all string properties annotated with
/// <c>builder.Property(...).Encrypt()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically in <c>SharedKernelDbContext.OnModelCreating</c> via
/// <c>modelBuilder.Conventions.Add</c>. No manual call is needed from consuming code.
/// </para>
/// <para>
/// The convention always wires the converter — pass-through mode (when
/// <c>EncryptionOptions.Enabled == false</c>) is gated inside the converter itself, not here.
/// This ensures the converter is present so that consumers can enable encryption without a
/// model rebuild.
/// </para>
/// <para>
/// Properties without the <c>"SharedKernel:Encrypt"</c> annotation are untouched.
/// </para>
/// </remarks>
public sealed class EncryptionModelConvention : IModelFinalizingConvention
{
    private readonly IOptionsMonitor<EncryptionOptions> _optionsMonitor;
    private readonly IEncryptionVersionOverride _versionOverride;

    /// <summary>
    /// Initialises a new <see cref="EncryptionModelConvention"/>.
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor supplied by DI or a null-object fallback.</param>
    /// <param name="versionOverride">
    /// Scoped rotation-target-version accessor, resolved via DI, or the shared no-op instance when
    /// <c>.WithEncryption()</c> was not called. Passed to every <see cref="EncryptedValueConverter"/>
    /// this convention constructs.
    /// </param>
    public EncryptionModelConvention(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        IEncryptionVersionOverride? versionOverride = null)
    {
        _optionsMonitor = optionsMonitor;
        _versionOverride = versionOverride ?? EncryptionVersionOverride.NoOp;
    }

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var annotation = property.FindAnnotation(PropertyBuilderEncryptExtensions.AnnotationKey);
                if (annotation?.Value is not true)
                {
                    continue;
                }

                // Only string properties are supported.
                if (property.ClrType != typeof(string))
                {
                    continue;
                }

                var converter = new EncryptedValueConverter(_optionsMonitor, _versionOverride);
                property.SetValueConverter(converter);
            }
        }
    }
}
