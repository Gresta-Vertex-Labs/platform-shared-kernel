using System.Net;
using System.Net.Sockets;

namespace SharedKernel.DataPrivacy.Masking;

/// <summary>
/// Masks personal data before it reaches a log line, an audit record, a support screen or any
/// other place that must not see the full value.
/// </summary>
/// <remarks>
/// <para>
/// Every member accepts <see langword="null"/> and never throws: a masking call that can throw is a
/// liability at exactly the call site meant to keep a value out of an exception message. Empty or
/// whitespace input returns <see cref="string.Empty"/>, except <see cref="Suppress"/>.
/// </para>
/// <para>
/// Separators (spaces, hyphens, dots, parentheses, <c>+</c>) keep their position; only letters and
/// digits are replaced with <c>*</c>. A value too short to have a safe partial reveal is masked in
/// full, never shown in full. <see cref="CardNumber"/> and <see cref="NationalId"/> give the same
/// output as <c>SharedKernel.Validation</c>'s <c>CardNumber</c> and <c>NationalId</c> types.
/// </para>
/// </remarks>
public static class PiiMasking
{
    /// <summary>The fixed value <see cref="Suppress"/> returns, and <see cref="IpAddress"/> returns for input that is not an IP address.</summary>
    public const string RedactedSentinel = "[REDACTED]";

    private const string FixedMask = "***";
    private const int CardNumberMinimumDigits = 12;

    /// <summary>Masks an email address and keeps its domain: <c>j.doe@example.com</c> → <c>j***@example.com</c>.</summary>
    /// <param name="email">The email address.</param>
    /// <returns>The masked address; see <see cref="Email(string?, bool)"/>.</returns>
    public static string Email(string? email) => Email(email, revealDomain: true);

    /// <summary>
    /// Masks an email address: the local part keeps its first character followed by a fixed
    /// <c>***</c>, so its length is not revealed.
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="revealDomain">
    /// <see langword="true"/> to keep the domain (<c>j***@example.com</c>); <see langword="false"/>
    /// to keep only its top-level part (<c>j***@***.com</c>), for when the domain is personal.
    /// </param>
    /// <returns>
    /// The masked address. A one-character local part is masked in full (<c>***@x.com</c>). Input
    /// without <c>@</c> is masked as a local part; with several, the last one splits local part and domain.
    /// </returns>
    public static string Email(string? email, bool revealDomain)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        int at = email.LastIndexOf('@');
        if (at < 0)
        {
            return MaskLocalPart(email);
        }

        string domain = email[(at + 1)..];
        if (!revealDomain)
        {
            int dot = domain.LastIndexOf('.');
            domain = dot < 0 ? FixedMask : FixedMask + domain[dot..];
        }

        return $"{MaskLocalPart(email[..at])}@{domain}";
    }

    /// <summary>
    /// Masks a phone number, keeping the last four digits when it has at least four, the last two
    /// when it has two or three, and none otherwise: <c>+1 (555) 123-4567</c> → <c>+* (***) ***-4567</c>.
    /// </summary>
    /// <param name="phoneNumber">The phone number.</param>
    /// <returns>The masked number.</returns>
    public static string Phone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return string.Empty;
        }

        int digits = Count(phoneNumber, char.IsDigit);
        int keep = digits switch
        {
            >= 4 => 4,
            >= 2 => 2,
            _ => 0,
        };

        return Mask(phoneNumber, char.IsDigit, 0, keep);
    }

    /// <summary>
    /// Masks a payment card number, keeping the first six and last four digits, the most PCI DSS
    /// allows to be displayed: <c>4111 1111 1111 1111</c> → <c>4111 11** **** 1111</c>.
    /// </summary>
    /// <param name="cardNumber">The card number.</param>
    /// <returns>The masked number. Fewer than twelve digits is not a card number, and every digit is masked.</returns>
    public static string CardNumber(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return string.Empty;
        }

        return Count(cardNumber, char.IsDigit) < CardNumberMinimumDigits
            ? Mask(cardNumber, char.IsDigit, 0, 0)
            : Mask(cardNumber, char.IsDigit, 6, 4);
    }

    /// <summary>
    /// Masks an IBAN or bank account number, keeping the first two and last four letters or digits:
    /// <c>DE89 3704 0044 0532 0130 00</c> → <c>DE** **** **** **** **30 00</c>.
    /// </summary>
    /// <param name="iban">The IBAN or account number.</param>
    /// <returns>The masked number. With fewer than ten letters and digits, every one is masked.</returns>
    public static string Iban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return string.Empty;
        }

        return Count(iban, char.IsLetterOrDigit) < 10
            ? Mask(iban, char.IsLetterOrDigit, 0, 0)
            : Mask(iban, char.IsLetterOrDigit, 2, 4);
    }

    /// <summary>
    /// Masks a national ID, passport or tax number, keeping the last four letters or digits:
    /// <c>10000000146</c> → <c>*******0146</c>.
    /// </summary>
    /// <param name="nationalId">The identity number.</param>
    /// <returns>The masked number. With four or fewer letters and digits, every one is masked.</returns>
    public static string NationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
        {
            return string.Empty;
        }

        return Count(nationalId, char.IsLetterOrDigit) <= 4
            ? Mask(nationalId, char.IsLetterOrDigit, 0, 0)
            : Mask(nationalId, char.IsLetterOrDigit, 0, 4);
    }

    /// <summary>
    /// Masks a person's name to the initial of each word followed by a fixed <c>***</c>:
    /// <c>Ayşe Nur Yılmaz</c> → <c>A*** N*** Y***</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The masked name, with words separated by single spaces.</returns>
    public static string PersonName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string[] words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            int initialLength = char.IsHighSurrogate(word[0]) && word.Length > 1 ? 2 : 1;
            words[i] = string.Concat(word.AsSpan(0, initialLength), FixedMask);
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// Anonymizes an IP address the way analytics tools do: the last octet of an IPv4 address is
    /// set to zero (<c>192.168.1.23</c> → <c>192.168.1.0</c>) and an IPv6 address keeps only its
    /// first 48 bits (<c>2001:db8:85a3::8a2e:370:7334</c> → <c>2001:db8:85a3::</c>).
    /// </summary>
    /// <param name="ipAddress">The IP address, without a port.</param>
    /// <returns>The anonymized address, or <see cref="RedactedSentinel"/> for input that is not an IP address.</returns>
    public static string IpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return string.Empty;
        }

        if (!IPAddress.TryParse(ipAddress.Trim(), out IPAddress? address))
        {
            return RedactedSentinel;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out int length);
        int keep = address.AddressFamily == AddressFamily.InterNetwork ? 3 : 6;
        bytes[keep..length].Clear();

        return new IPAddress(bytes[..length]).ToString();
    }

    /// <summary>
    /// Keeps the first <paramref name="keepStart"/> and last <paramref name="keepEnd"/> characters
    /// and replaces every other character with <c>*</c>: <c>Partial("ORD-2024-000123", 4, 3)</c> →
    /// <c>ORD-********123</c>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="keepStart">How many leading characters to keep; a negative value counts as zero.</param>
    /// <param name="keepEnd">How many trailing characters to keep; a negative value counts as zero.</param>
    /// <returns>The masked value. When the kept characters would cover the whole value, every character is masked.</returns>
    public static string Partial(string? value, int keepStart, int keepEnd)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        keepStart = Math.Max(keepStart, 0);
        keepEnd = Math.Max(keepEnd, 0);
        if (keepStart + keepEnd >= value.Length)
        {
            return new string('*', value.Length);
        }

        return string.Create(value.Length, (value, keepStart, keepEnd), static (span, state) =>
        {
            state.value.AsSpan().CopyTo(span);
            span[state.keepStart..^state.keepEnd].Fill('*');
        });
    }

    /// <summary>Returns <see cref="RedactedSentinel"/> for every input, for values with no safe partial reveal.</summary>
    /// <param name="value">Ignored; present so every member has the same <c>string? → string</c> shape.</param>
    /// <returns><see cref="RedactedSentinel"/>, always, including for <see langword="null"/>.</returns>
    public static string Suppress(string? value) => RedactedSentinel;

    private static string MaskLocalPart(string local) =>
        local.Length > 1 ? string.Concat(local.AsSpan(0, 1), FixedMask) : FixedMask;

    private static int Count(string value, Func<char, bool> counts)
    {
        int count = 0;
        foreach (char c in value)
        {
            if (counts(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Replaces every character matched by <paramref name="masks"/> with <c>*</c>, except the first
    /// <paramref name="keepStart"/> and last <paramref name="keepEnd"/> of them; other characters are copied.
    /// </summary>
    private static string Mask(string value, Func<char, bool> masks, int keepStart, int keepEnd)
    {
        int total = Count(value, masks);
        return string.Create(value.Length, (value, masks, keepStart, keepEnd, total), static (span, state) =>
        {
            int index = 0;
            for (int i = 0; i < state.value.Length; i++)
            {
                char c = state.value[i];
                if (!state.masks(c))
                {
                    span[i] = c;
                    continue;
                }

                span[i] = index < state.keepStart || index >= state.total - state.keepEnd ? c : '*';
                index++;
            }
        });
    }
}
