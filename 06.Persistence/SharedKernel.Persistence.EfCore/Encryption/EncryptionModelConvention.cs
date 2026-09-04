using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
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
/// <para>
/// <strong>P-227, simplified per D-109/P-448:</strong> This convention resolves
/// <see cref="ISymmetricEncryptionService"/> from DI alongside the existing
/// <see cref="IEncryptionVersionOverride"/>, and passes both to each
/// <see cref="EncryptedValueConverter"/> it constructs. It no longer resolves
/// <see cref="IEncryptionKeyProvider"/> at all — <see cref="EncryptedValueConverter"/> stopped
/// needing one directly (D-108).
/// </para>
/// </remarks>
public sealed class EncryptionModelConvention : IModelFinalizingConvention
{
    private readonly IOptionsMonitor<EncryptionOptions> _optionsMonitor;
    private readonly IEncryptionVersionOverride _versionOverride;
    private readonly ISymmetricEncryptionService? _symmetricEncryptionService;

    /// <summary>
    /// Initialises a new <see cref="EncryptionModelConvention"/>.
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor supplied by DI or a null-object fallback.</param>
    /// <param name="symmetricEncryptionService">
    /// The cryptographic service used by <see cref="EncryptedValueConverter"/> for AES-256-GCM operations
    /// (P-227). May be <see langword="null"/> when <c>.WithEncryption()</c> was not called and the
    /// converter operates in disabled pass-through mode.
    /// </param>
    /// <param name="versionOverride">
    /// Scoped rotation-target-version accessor, resolved via DI, or the shared no-op instance when
    /// <c>.WithEncryption()</c> was not called. Passed to every <see cref="EncryptedValueConverter"/>
    /// this convention constructs.
    /// </param>
    /// <remarks>
    /// <strong>D-109/P-448 (breaking):</strong> this constructor no longer takes an
    /// <see cref="IEncryptionKeyProvider"/> parameter.
    /// </remarks>
    public EncryptionModelConvention(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        ISymmetricEncryptionService? symmetricEncryptionService = null,
        IEncryptionVersionOverride? versionOverride = null)
    {
        _optionsMonitor = optionsMonitor;
        _symmetricEncryptionService = symmetricEncryptionService;
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

                ValueConverter converter;
                if (_symmetricEncryptionService is not null)
                {
                    // P-227, simplified per D-109: full delegation to ISymmetricEncryptionService.
                    converter = new EncryptedValueConverter(
                        _optionsMonitor,
                        _symmetricEncryptionService,
                        _versionOverride);
                }
                else
                {
                    // Fallback: encryption not configured — converter runs in pass-through mode
                    // (EncryptionOptions.Enabled defaults to false). Uses a NullSymmetricEncryptionService
                    // stub so the converter's pass-through branch is exercised without crypto.
                    converter = new EncryptedValueConverter(
                        _optionsMonitor,
                        NullSymmetricEncryptionService.Instance,
                        _versionOverride);
                }

                property.SetValueConverter(converter);
            }
        }
    }
}
