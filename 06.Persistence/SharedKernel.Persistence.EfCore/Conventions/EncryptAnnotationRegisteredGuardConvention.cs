using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Fails model building when a property still carries the <see cref="PersistenceModelAnnotationNames.Encrypt"/>
/// annotation after every registered <c>IPersistenceModelConventionFactory</c> convention has run.
/// </summary>
/// <remarks>
/// <para>
/// <c>PropertyBuilderEncryptExtensions.Encrypt</c> (in <c>SharedKernel.Persistence.EfCore.Encryption</c>) sets the
/// annotation; that package's model convention — registered only when field encryption is wired in — marks each
/// property it validated with <see cref="PersistenceModelAnnotationNames.EncryptApplied"/>. A property that carries
/// the first annotation without the second means encryption was never wired in, and the service would otherwise
/// start up silently persisting plaintext for a property its own model declares must be encrypted.
/// </para>
/// <para>
/// Registered unconditionally, always last, by <see cref="Context.SharedKernelDbContext.ConfigureConventions"/>.
/// Walks properties at every depth — direct, inside nested complex types and inside complex collections — through
/// <see cref="PersistenceModelAnnotationNames.GetPropertiesIncludingComplex"/>, the same traversal the encryption
/// package uses, so the two can never disagree about which properties exist.
/// </para>
/// </remarks>
internal sealed class EncryptAnnotationRegisteredGuardConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var (complexPath, property) in PersistenceModelAnnotationNames.GetPropertiesIncludingComplex(entityType))
            {
                if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is null
                    || property.FindAnnotation(PersistenceModelAnnotationNames.EncryptApplied) is not null)
                {
                    continue;
                }

                var path = string.Join('.', complexPath.Select(c => c.Name).Append(property.Name));
                throw new InvalidOperationException(
                    $"'{entityType.ShortName()}.{path}' is annotated with '.Encrypt(...)' but field-level " +
                    "encryption was never wired in. Call 'UseFieldEncryption(...)' (from the " +
                    "SharedKernel.Persistence.EfCore.Encryption package) on this context's persistence builder, or " +
                    "remove the '.Encrypt(...)' call. Refusing to start with an encrypted property whose encryption " +
                    "pipeline is not actually wired, which would otherwise persist plaintext.");
            }
        }
    }
}
