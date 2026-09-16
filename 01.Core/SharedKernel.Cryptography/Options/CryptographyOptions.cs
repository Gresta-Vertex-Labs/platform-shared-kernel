using Microsoft.Extensions.Options;
using SharedKernel.Configuration;

namespace SharedKernel.Cryptography.Options;

/// <summary>Configuration for <c>SharedKernel.Cryptography</c>, bound from <c>SharedKernel:Cryptography</c>.</summary>
/// <remarks>Validated when the host starts; an invalid value stops startup rather than failing on first use.</remarks>
/// <example>
/// <code>
/// "SharedKernel": {
///   "Cryptography": {
///     "OneWayHashing": {
///       "Algorithm": "pbkdf2-sha256",
///       "CurrentPepperId": "p2",
///       "Peppers": { "p1": "&lt;base64, from a secret store&gt;", "p2": "&lt;base64&gt;" }
///     },
///     "Pbkdf2": { "Iterations": 600000 }
///   }
/// }
/// </code>
/// </example>
public sealed class CryptographyOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Cryptography";

    /// <summary>Settings for <see cref="Hashing.IOneWayHasher"/>.</summary>
    [ValidateObjectMembers]
    public OneWayHashingOptions OneWayHashing { get; set; } = new();
}
