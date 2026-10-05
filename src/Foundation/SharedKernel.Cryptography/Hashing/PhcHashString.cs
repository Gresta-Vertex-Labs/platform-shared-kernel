using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// A hash in the PHC string format: <c>$algorithm[$v=version][$name=value,...]$salt$hash</c>, with the salt and
/// hash in unpadded standard Base64.
/// </summary>
/// <remarks>
/// <para>
/// The format names its algorithm and parameters, so one column can hold hashes from several algorithms and cost
/// settings, and <see cref="OneWayHasher"/> can route each stored hash to the algorithm that produced it. Argon2
/// implementations everywhere read and write this format.
/// </para>
/// <para>
/// Parsing is strict and bounded: at most <see cref="MaxLength"/> characters, lowercase algorithm and parameter
/// names, a non-empty salt and hash, and only the PHC value alphabet in parameter values.
/// </para>
/// </remarks>
public sealed class PhcHashString
{
    /// <summary>The longest string <see cref="TryParse"/> accepts.</summary>
    public const int MaxLength = 1024;

    private const int MaxNameLength = 32;
    private readonly byte[] _salt;
    private readonly byte[] _hash;

    /// <summary>Creates a PHC hash string.</summary>
    /// <param name="algorithmId">The algorithm identifier, for example <c>argon2id</c>. Lowercase letters, digits and hyphens.</param>
    /// <param name="version">The optional algorithm version written as <c>v=</c>, or <see langword="null"/>.</param>
    /// <param name="parameters">The parameters in the order they are written. Names are unique.</param>
    /// <param name="salt">The salt. Must not be empty. Copied.</param>
    /// <param name="hash">The hash output. Must not be empty. Copied.</param>
    /// <exception cref="ArgumentException">An identifier, parameter or value is not valid in the PHC format.</exception>
    public PhcHashString(
        string algorithmId,
        int? version,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> hash)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!IsName(algorithmId))
        {
            throw new ArgumentException($"'{algorithmId}' is not a valid PHC algorithm identifier.", nameof(algorithmId));
        }

        if (version is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "The version must not be negative.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, string value) in parameters)
        {
            if (!IsName(name) || name == "v" || !names.Add(name))
            {
                throw new ArgumentException($"'{name}' is not a valid or unique PHC parameter name.", nameof(parameters));
            }

            if (!IsValue(value))
            {
                throw new ArgumentException($"The value of parameter '{name}' is not valid in the PHC format.", nameof(parameters));
            }
        }

        if (salt.IsEmpty)
        {
            throw new ArgumentException("The salt must not be empty.", nameof(salt));
        }

        if (hash.IsEmpty)
        {
            throw new ArgumentException("The hash must not be empty.", nameof(hash));
        }

        AlgorithmId = algorithmId;
        Version = version;
        Parameters = [.. parameters];
        _salt = salt.ToArray();
        _hash = hash.ToArray();
    }

    /// <summary>The algorithm identifier, for example <c>pbkdf2-sha256</c> or <c>argon2id</c>.</summary>
    public string AlgorithmId { get; }

    /// <summary>The algorithm version from the <c>v=</c> segment, or <see langword="null"/> when absent.</summary>
    public int? Version { get; }

    /// <summary>The parameters in the order they appear.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    /// <summary>The salt.</summary>
    public ReadOnlySpan<byte> Salt => _salt;

    /// <summary>The hash output.</summary>
    public ReadOnlySpan<byte> Hash => _hash;

    /// <summary>Gets a parameter value by name.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value, when found.</param>
    /// <returns><see langword="true"/> when the parameter is present.</returns>
    public bool TryGetParameter(string name, [NotNullWhen(true)] out string? value)
    {
        foreach ((string key, string candidate) in Parameters)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Gets a parameter as a non-negative decimal integer with no sign, whitespace or leading zeros.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value, when found and well-formed.</param>
    /// <returns><see langword="true"/> when the parameter is present and is a canonical non-negative integer.</returns>
    public bool TryGetInt32Parameter(string name, out int value)
    {
        value = 0;
        return TryGetParameter(name, out string? text)
            && (text.Length == 1 || text[0] != '0')
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Returns a copy with <paramref name="name"/> set to <paramref name="value"/>, added at the end when absent.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>The new hash string.</returns>
    public PhcHashString WithParameter(string name, string value)
    {
        var parameters = new List<KeyValuePair<string, string>>(Parameters.Count + 1);
        bool replaced = false;
        foreach (KeyValuePair<string, string> parameter in Parameters)
        {
            if (string.Equals(parameter.Key, name, StringComparison.Ordinal))
            {
                parameters.Add(new(name, value));
                replaced = true;
            }
            else
            {
                parameters.Add(parameter);
            }
        }

        if (!replaced)
        {
            parameters.Add(new(name, value));
        }

        return new PhcHashString(AlgorithmId, Version, parameters, _salt, _hash);
    }

    /// <summary>Returns a copy without the parameter <paramref name="name"/>.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The new hash string, or this instance when the parameter is absent.</returns>
    public PhcHashString WithoutParameter(string name)
    {
        if (!TryGetParameter(name, out _))
        {
            return this;
        }

        return new PhcHashString(
            AlgorithmId,
            Version,
            [.. Parameters.Where(p => !string.Equals(p.Key, name, StringComparison.Ordinal))],
            _salt,
            _hash);
    }

    /// <summary>Formats the hash string.</summary>
    /// <returns>The PHC string, for example <c>$pbkdf2-sha256$i=600000$c2FsdA$aGFzaA</c>.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder(128);
        builder.Append('$').Append(AlgorithmId);

        if (Version is int version)
        {
            builder.Append("$v=").Append(version.ToString(CultureInfo.InvariantCulture));
        }

        if (Parameters.Count > 0)
        {
            builder.Append('$');
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Parameters[i].Key).Append('=').Append(Parameters[i].Value);
            }
        }

        builder.Append('$').Append(EncodeBase64(_salt));
        builder.Append('$').Append(EncodeBase64(_hash));
        return builder.ToString();
    }

    /// <summary>Parses a PHC hash string. Never throws for malformed input.</summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="result">The parsed hash string, when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a well-formed PHC string with a salt and hash.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out PhcHashString? result)
    {
        result = null;
        if (value is null || value.Length is < 5 or > MaxLength || value[0] != '$')
        {
            return false;
        }

        string[] segments = value[1..].Split('$');
        if (segments.Length is < 3 or > 5 || !IsName(segments[0]))
        {
            return false;
        }

        int index = 1;
        int? version = null;
        if (segments.Length >= 4 && segments[index].StartsWith("v=", StringComparison.Ordinal))
        {
            string text = segments[index][2..];
            if (text.Length == 0 || (text.Length > 1 && text[0] == '0')
                || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedVersion))
            {
                return false;
            }

            version = parsedVersion;
            index++;
        }

        var parameters = new List<KeyValuePair<string, string>>();
        if (segments.Length - index == 3)
        {
            foreach (string pair in segments[index].Split(','))
            {
                int separator = pair.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    return false;
                }

                parameters.Add(new(pair[..separator], pair[(separator + 1)..]));
            }

            index++;
        }

        if (segments.Length - index != 2
            || !TryDecodeBase64(segments[index], out byte[]? salt)
            || !TryDecodeBase64(segments[index + 1], out byte[]? hash))
        {
            return false;
        }

        try
        {
            result = new PhcHashString(segments[0], version, parameters, salt, hash);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsName(string name)
    {
        if (name.Length is 0 or > MaxNameLength)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValue(string value)
    {
        if (value.Length is 0 or > 256)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '/' or '+' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string EncodeBase64(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryDecodeBase64(string text, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        if (text.Length == 0 || text.Length % 4 == 1 || text.Contains('='))
        {
            return false;
        }

        string padded = (text.Length % 4) switch
        {
            2 => text + "==",
            3 => text + "=",
            _ => text,
        };

        byte[] buffer = new byte[padded.Length / 4 * 3];
        if (!Convert.TryFromBase64String(padded, buffer, out int written))
        {
            return false;
        }

        bytes = buffer[..written];

        // Reject non-canonical encodings, whose unused trailing bits are not zero, so one hash has one spelling.
        return string.Equals(EncodeBase64(bytes), text, StringComparison.Ordinal);
    }
}
