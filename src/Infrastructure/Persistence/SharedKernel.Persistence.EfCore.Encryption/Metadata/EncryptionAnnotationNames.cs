using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption.Metadata;

/// <summary>The model annotations field encryption reads and writes.</summary>
/// <remarks>
/// Every value is a <see langword="string"/>, <see langword="bool"/> or <see langword="int"/>, never a delegate or
/// object: <c>dotnet ef migrations add</c> writes annotations into the model snapshot and
/// <c>dotnet ef dbcontext optimize</c> into the compiled model, and both can only emit C# literals.
/// </remarks>
internal static class EncryptionAnnotationNames
{
    /// <summary>The purpose label (<see langword="string"/>), set by <c>.Encrypt(purpose)</c>.</summary>
    public const string Purpose = PersistenceModelAnnotationNames.Encrypt;

    /// <summary>Set to <see langword="true"/> by the model convention once it validated the property.</summary>
    public const string Applied = PersistenceModelAnnotationNames.EncryptApplied;

    /// <summary>
    /// <see cref="BlindIndexNormalization"/> flags as an <see langword="int"/>, set by
    /// <c>.WithBlindIndex(...)</c>. Its presence means the property has a blind index.
    /// </summary>
    public const string BlindIndexNormalization = "SharedKernel:Persistence:Encrypt:BlindIndex";

    /// <summary>The name of a registered <see cref="BlindIndex.IBlindIndexNormalizer"/> (<see langword="string"/>), optional.</summary>
    public const string BlindIndexNormalizer = "SharedKernel:Persistence:Encrypt:BlindIndex:Normalizer";

    /// <summary>The name of the shadow property holding the blind index (<see langword="string"/>), set by the model convention.</summary>
    public const string BlindIndexProperty = "SharedKernel:Persistence:Encrypt:BlindIndex:Property";
}
