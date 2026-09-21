namespace SharedKernel.Persistence.EfCore.Extensibility;

/// <summary>
/// Model annotation keys that a sibling opt-in capability package (e.g.
/// <c>SharedKernel.Persistence.EfCore.Encryption</c>) shares with this core package by bare string value, so the
/// core package can fail model building loudly when the annotation is present but the capability that must consume
/// it was never wired in — without taking a compile-time reference to that package.
/// </summary>
/// <remarks>
/// This is the only coupling between <c>SharedKernel.Persistence.EfCore</c> and any opt-in capability package: a
/// shared constant, never a shared type. See <see cref="Conventions.EncryptAnnotationRegisteredGuardConvention"/>.
/// </remarks>
internal static partial class PersistenceModelAnnotationNames
{
    /// <summary>
    /// The annotation key <c>PropertyBuilderEncryptExtensions.Encrypt</c> (in
    /// <c>SharedKernel.Persistence.EfCore.Encryption</c>) sets on an encrypted property. Left in place
    /// permanently — <c>EncryptionInterceptor</c> reads it at every save/materialization to know a property is
    /// encrypted and what its purpose label is.
    /// </summary>
    public const string Encrypt = "SharedKernel:Persistence:Encrypt";

    /// <summary>
    /// The annotation key <c>EncryptionModelConvention</c> sets, alongside <see cref="Encrypt"/>, once it has
    /// genuinely validated and wired a property for encryption. <see cref="Conventions.EncryptAnnotationRegisteredGuardConvention"/>
    /// treats <see cref="Encrypt"/>'s presence WITHOUT this marker as proof that no encryption convention ever ran.
    /// </summary>
    public const string EncryptApplied = "SharedKernel:Persistence:Encrypt:Applied";
}
