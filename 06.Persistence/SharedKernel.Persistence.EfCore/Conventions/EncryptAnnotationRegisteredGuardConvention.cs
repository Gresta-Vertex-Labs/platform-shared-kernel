using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Fails model building when a property still carries the
/// <see cref="PersistenceModelAnnotationNames.Encrypt"/> annotation after every registered
/// <c>IPersistenceModelConventionFactory</c> convention has run.
/// </summary>
/// <remarks>
/// <para>
/// <c>PropertyBuilderEncryptExtensions.Encrypt</c> (in <c>SharedKernel.Persistence.EfCore.Encryption</c>) sets this
/// annotation. <c>EncryptionModelConvention</c> — registered only when
/// <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c> was called — removes it once it has genuinely wired
/// the property for encryption. If <c>.WithEncryption()</c> was never called, that convention never runs, the
/// annotation survives untouched to this convention, and the service would otherwise start up silently persisting
/// plaintext for a property its own model declares must be encrypted — a fail-open hazard this convention closes by
/// failing the model build instead.
/// </para>
/// <para>
/// Registered unconditionally, always last, by <see cref="Context.SharedKernelDbContext.ConfigureConventions"/> —
/// after every <c>IPersistenceModelConventionFactory</c>-contributed convention has had its chance to consume the
/// annotation. This is the only coupling between this core package and any opt-in encryption-like capability
/// package: a shared string constant, never a shared type or assembly reference.
/// </para>
/// </remarks>
public sealed class EncryptAnnotationRegisteredGuardConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null
                    && property.FindAnnotation(PersistenceModelAnnotationNames.EncryptApplied) is null)
                {
                    throw new InvalidOperationException(
                        $"'{entityType.ShortName()}.{property.Name}' is annotated with '.Encrypt(...)' but field-level " +
                        "encryption was never wired in. Call 'EfCorePersistenceBuilder<TContext>.WithEncryption()' " +
                        "(from the SharedKernel.Persistence.EfCore.Encryption package) in this context's builder " +
                        "chain, or remove the '.Encrypt(...)' call. Refusing to start with an encrypted property " +
                        "whose encryption pipeline is not actually wired, which would otherwise persist plaintext.");
                }
            }
        }
    }
}
