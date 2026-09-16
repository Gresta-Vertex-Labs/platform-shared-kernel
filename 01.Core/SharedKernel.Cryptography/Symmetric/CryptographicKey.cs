using System.Text;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>A versioned symmetric key: an id recorded in every payload it encrypts, and its secret material.</summary>
/// <remarks>
/// The material is copied on construction and never appears in <see cref="ToString"/>. Key ids are opaque to this
/// package; keep them stable, because a payload is decrypted with whichever key its id resolves to.
/// </remarks>
public sealed class CryptographicKey
{
    /// <summary>The longest key id accepted, in UTF-8 bytes.</summary>
    public const int MaxIdLength = 255;

    private readonly byte[] _material;

    /// <summary>Creates a key.</summary>
    /// <param name="id">The key id. Must not be empty or whitespace, and at most <see cref="MaxIdLength"/> UTF-8 bytes.</param>
    /// <param name="material">The key material. Must not be empty. AES-256-GCM requires exactly 32 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="material"/> is not valid.</exception>
    public CryptographicKey(string id, ReadOnlySpan<byte> material)
    {
        ValidateId(id);

        if (material.IsEmpty)
        {
            throw new ArgumentException("The key material must not be empty.", nameof(material));
        }

        Id = id;
        _material = material.ToArray();
    }

    /// <summary>The key id.</summary>
    public string Id { get; }

    /// <summary>The key material.</summary>
    public ReadOnlySpan<byte> Material => _material;

    /// <summary>Returns the key id without the material.</summary>
    /// <returns><c>CryptographicKey { Id = ... }</c>.</returns>
    public override string ToString() => $"CryptographicKey {{ Id = {Id} }}";

    internal static void ValidateId(string id, string paramName = "id")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, paramName);

        if (Encoding.UTF8.GetByteCount(id) > MaxIdLength)
        {
            throw new ArgumentException($"The key id must be at most {MaxIdLength} UTF-8 bytes.", paramName);
        }
    }
}
