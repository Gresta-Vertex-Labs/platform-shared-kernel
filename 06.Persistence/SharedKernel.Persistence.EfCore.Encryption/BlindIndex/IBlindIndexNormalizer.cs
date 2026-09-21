namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>
/// A named normalization step for blind indexes that the built-in <see cref="BlindIndexNormalization"/> flags do
/// not cover, for example stripping the formatting of a phone number.
/// </summary>
/// <remarks>
/// A property selects it by <see cref="Name"/> with <c>.WithBlindIndex(normalizer: "phone")</c>; register the
/// implementation with <c>FieldEncryptionBuilder.AddBlindIndexNormalizer&lt;T&gt;()</c>. The model stores only the
/// name, so migrations and compiled models work. A name the container cannot resolve fails the first save or
/// lookup that needs it. The implementation must be deterministic and must never change its output for a given
/// input once data is indexed.
/// </remarks>
public interface IBlindIndexNormalizer
{
    /// <summary>The name a property refers to. Compared ordinally.</summary>
    string Name { get; }

    /// <summary>Returns the normalized form of <paramref name="value"/>.</summary>
    /// <param name="value">The value, after the built-in normalization flags have been applied.</param>
    /// <returns>The normalized value.</returns>
    string Normalize(string value);
}
