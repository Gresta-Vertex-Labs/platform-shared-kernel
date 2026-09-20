using Microsoft.EntityFrameworkCore.Metadata;
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
/// <para>
/// <strong>Complex-type properties are checked too.</strong> EF Core 10 complex-type (value-object) properties are
/// not included in <see cref="IConventionEntityType.GetProperties"/> — they live in their own
/// <see cref="IConventionComplexType"/>, reached only through <see cref="IConventionEntityType.GetComplexProperties"/>.
/// A model whose encrypted properties are declared exclusively inside a complex type would otherwise pass this
/// guard silently — the exact fail-open gap this convention exists to close, just one level deeper. The traversal
/// here mirrors <c>EncryptionModelConvention.ProcessModelFinalizing</c>'s own two-loop shape (in
/// <c>SharedKernel.Persistence.EfCore.Encryption</c>) exactly, so neither can miss a shape the other one checks.
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
                EnsureConsumed(entityType, property, propertyPath: property.Name);

            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                foreach (var property in complexProperty.ComplexType.GetProperties())
                    EnsureConsumed(entityType, property, propertyPath: $"{complexProperty.Name}.{property.Name}");
            }
        }
    }

    private static void EnsureConsumed(IConventionEntityType entityType, IConventionProperty property, string propertyPath)
    {
        if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null
            && property.FindAnnotation(PersistenceModelAnnotationNames.EncryptApplied) is null)
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}.{propertyPath}' is annotated with '.Encrypt(...)' but field-level " +
                "encryption was never wired in. Call 'EfCorePersistenceBuilder<TContext>.WithEncryption()' " +
                "(from the SharedKernel.Persistence.EfCore.Encryption package) in this context's builder " +
                "chain, or remove the '.Encrypt(...)' call. Refusing to start with an encrypted property " +
                "whose encryption pipeline is not actually wired, which would otherwise persist plaintext.");
        }
    }
}
