using System.Collections.Frozen;
using static SharedKernel.Validation.Internal.Checksums;

namespace SharedKernel.Validation.Internal;

/// <summary>The outcome of checking the national part of a VAT number.</summary>
internal enum VatCheck
{
    Valid,
    InvalidFormat,
    InvalidCheckDigit,
}

/// <summary>
/// Per-country VAT number rules: the format of the part after the prefix and, where the tax
/// authority publishes one, its check-digit algorithm.
/// </summary>
/// <remarks>
/// <para>
/// Covers the 27 EU member states under their VIES prefixes (Greece is <c>EL</c>), Northern
/// Ireland (<c>XI</c>), the United Kingdom, Switzerland, Norway and Türkiye (the 10-digit VKN).
/// Algorithms follow the national specifications as implemented by python-stdnum, and the tests use
/// that project's published valid numbers.
/// </para>
/// <para>
/// Numbers issued to individuals rather than businesses are checked for format only where their
/// check digit depends on a birth date or a separate personal-number scheme: 10-digit Bulgarian,
/// 9- and 10-digit Czech (except the special 9-digit numbers starting with 6), and Latvian personal
/// codes. Every business number is checked in full.
/// </para>
/// </remarks>
internal static class VatRules
{
    public static readonly FrozenDictionary<string, Func<string, VatCheck>> ByPrefix =
        new Dictionary<string, Func<string, VatCheck>>(StringComparer.Ordinal)
        {
            ["AT"] = Austria,
            ["BE"] = Belgium,
            ["BG"] = Bulgaria,
            ["CY"] = Cyprus,
            ["CZ"] = Czechia,
            ["DE"] = Germany,
            ["DK"] = Denmark,
            ["EE"] = Estonia,
            ["EL"] = Greece,
            ["ES"] = Spain,
            ["FI"] = Finland,
            ["FR"] = France,
            ["HR"] = Croatia,
            ["HU"] = Hungary,
            ["IE"] = Ireland,
            ["IT"] = Italy,
            ["LT"] = Lithuania,
            ["LU"] = Luxembourg,
            ["LV"] = Latvia,
            ["MT"] = Malta,
            ["NL"] = Netherlands,
            ["PL"] = Poland,
            ["PT"] = Portugal,
            ["RO"] = Romania,
            ["SE"] = Sweden,
            ["SI"] = Slovenia,
            ["SK"] = Slovakia,
            ["XI"] = UnitedKingdom,
            ["GB"] = UnitedKingdom,
            ["CH"] = Switzerland,
            ["NO"] = Norway,
            ["TR"] = Turkiye,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static VatCheck Result(bool formatOk, bool checkOk) =>
        !formatOk ? VatCheck.InvalidFormat : checkOk ? VatCheck.Valid : VatCheck.InvalidCheckDigit;

    private static bool Digits(string n, int length) => n.Length == length && Text.AllDigits(n);

    // AT: 'U' + 8 digits; check = (6 - luhn(digits 1..7)) mod 10.
    private static VatCheck Austria(string n)
    {
        if (n.Length != 9 || n[0] != 'U' || !Text.AllDigits(n.AsSpan(1)))
        {
            return VatCheck.InvalidFormat;
        }

        int check = ((6 - Luhn(n.AsSpan(1, 7))) % 10 + 10) % 10;
        return Result(true, n[8] - '0' == check);
    }

    // BE: 10 digits starting 0 or 1; (first 8 + last 2) mod 97 == 0.
    private static VatCheck Belgium(string n)
    {
        bool format = Digits(n, 10) && n[0] is '0' or '1' && ToLong(n) > 0;
        return Result(format, format && (ToLong(n.AsSpan(0, 8)) + ToInt(n.AsSpan(8))) % 97 == 0);
    }

    // BG: 9 digits (legal entities, checked) or 10 digits (individuals, format only).
    private static VatCheck Bulgaria(string n)
    {
        if (!Text.AllDigits(n) || n.Length is not (9 or 10))
        {
            return VatCheck.InvalidFormat;
        }

        if (n.Length == 10)
        {
            return VatCheck.Valid;
        }

        int check = 0;
        for (int i = 0; i < 8; i++)
        {
            check += (i + 1) * (n[i] - '0');
        }

        check %= 11;
        if (check == 10)
        {
            check = 0;
            for (int i = 0; i < 8; i++)
            {
                check += (i + 3) * (n[i] - '0');
            }

            check %= 11;
        }

        return Result(true, n[8] - '0' == check % 10);
    }

    // CY: 8 digits + check letter; must not start with 12.
    private static VatCheck Cyprus(string n)
    {
        if (n.Length != 9 || !Text.AllDigits(n.AsSpan(0, 8)) || !char.IsAsciiLetterUpper(n[8]) || n.StartsWith("12", StringComparison.Ordinal))
        {
            return VatCheck.InvalidFormat;
        }

        ReadOnlySpan<int> odd = [1, 0, 5, 7, 9, 13, 15, 17, 19, 21];
        int sum = 0;
        for (int i = 0; i < 8; i++)
        {
            int digit = n[i] - '0';
            sum += i % 2 == 0 ? odd[digit] : digit;
        }

        return Result(true, n[8] == (char)('A' + sum % 26));
    }

    // CZ: 8 digits (legal entities, checked), 9 digits starting 6 (special, checked), 9–10 digits (individuals, format only).
    private static VatCheck Czechia(string n)
    {
        if (!Text.AllDigits(n) || n.Length is < 8 or > 10)
        {
            return VatCheck.InvalidFormat;
        }

        if (n.Length == 8)
        {
            if (n[0] == '9')
            {
                return VatCheck.InvalidFormat;
            }

            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                sum += (8 - i) * (n[i] - '0');
            }

            int check = (11 - sum % 11) % 11;
            return Result(true, n[7] - '0' == (check == 0 ? 1 : check) % 10);
        }

        if (n.Length == 9 && n[0] == '6')
        {
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                sum += (8 - i) * (n[i + 1] - '0');
            }

            int check = (8 - (10 - sum % 11) % 11) % 10;
            return Result(true, n[8] - '0' == (check + 10) % 10);
        }

        return VatCheck.Valid;
    }

    // DE: 9 digits, not starting 0; ISO 7064 MOD 11,10.
    private static VatCheck Germany(string n)
    {
        bool format = Digits(n, 9) && n[0] != '0';
        return Result(format, format && Iso7064Mod11_10(n));
    }

    // DK: 8 digits, not starting 0; weights 2,7,6,5,4,3,2,1 sum mod 11 == 0.
    private static VatCheck Denmark(string n)
    {
        bool format = Digits(n, 8) && n[0] != '0';
        return Result(format, format && WeightedSum(n, [2, 7, 6, 5, 4, 3, 2, 1]) % 11 == 0);
    }

    // EE: 9 digits; weights 3,7,1,3,7,1,3,7,1 sum mod 10 == 0.
    private static VatCheck Estonia(string n)
    {
        bool format = Digits(n, 9);
        return Result(format, format && WeightedSum(n, [3, 7, 1, 3, 7, 1, 3, 7, 1]) % 10 == 0);
    }

    // EL: 9 digits; doubling checksum.
    private static VatCheck Greece(string n)
    {
        if (!Digits(n, 9))
        {
            return VatCheck.InvalidFormat;
        }

        int checksum = 0;
        for (int i = 0; i < 8; i++)
        {
            checksum = checksum * 2 + (n[i] - '0');
        }

        return Result(true, n[8] - '0' == checksum * 2 % 11 % 10);
    }

    // ES: 9 characters, middle 7 digits. DNI (8 digits + letter), NIE (X/Y/Z), K/L/M (DNI letter), or CIF (company).
    private static VatCheck Spain(string n)
    {
        if (n.Length != 9 || !Text.AllDigits(n.AsSpan(1, 7)))
        {
            return VatCheck.InvalidFormat;
        }

        const string DniLetters = "TRWAGMYFPDXBNJZSQVHLCKE";
        char first = n[0];
        char last = n[8];

        if (first is 'K' or 'L' or 'M')
        {
            return Result(true, last == DniLetters[ToInt(n.AsSpan(1, 7)) % 23]);
        }

        if (char.IsAsciiDigit(first))
        {
            return Result(Text.AllDigits(n.AsSpan(0, 8)), last == DniLetters[ToInt(n.AsSpan(0, 8)) % 23]);
        }

        if (first is 'X' or 'Y' or 'Z')
        {
            int number = (first - 'X') * 10_000_000 + ToInt(n.AsSpan(1, 7));
            return Result(true, last == DniLetters[number % 23]);
        }

        if ("ABCDEFGHJNPQRSUVW".Contains(first))
        {
            int digit = LuhnCheckDigit(n.AsSpan(1, 7));
            return Result(true, last == (char)('0' + digit) || last == "JABCDEFGHI"[digit]);
        }

        return VatCheck.InvalidFormat;
    }

    // FI: 8 digits; weights 7,9,10,5,8,4,2,1 sum mod 11 == 0.
    private static VatCheck Finland(string n)
    {
        bool format = Digits(n, 8);
        return Result(format, format && WeightedSum(n, [7, 9, 10, 5, 8, 4, 2, 1]) % 11 == 0);
    }

    // FR: 2-character key + 9-digit SIREN. The SIREN passes Luhn (except Monaco, 000…); a numeric
    // key equals (SIREN || "12") mod 97; an alphanumeric key uses the newer scheme.
    private static VatCheck France(string n)
    {
        const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        if (n.Length != 11 || Alphabet.IndexOf(n[0]) < 0 || Alphabet.IndexOf(n[1]) < 0 || !Text.AllDigits(n.AsSpan(2)))
        {
            return VatCheck.InvalidFormat;
        }

        ReadOnlySpan<char> siren = n.AsSpan(2);
        if (!siren.StartsWith("000") && Luhn(siren) != 0)
        {
            return VatCheck.InvalidCheckDigit;
        }

        if (Text.AllDigits(n.AsSpan(0, 2)))
        {
            return Result(true, ToInt(n.AsSpan(0, 2)) == (ToLong(siren) * 100 + 12) % 97);
        }

        int check = char.IsAsciiDigit(n[0])
            ? Alphabet.IndexOf(n[0]) * 24 + Alphabet.IndexOf(n[1]) - 10
            : Alphabet.IndexOf(n[0]) * 34 + Alphabet.IndexOf(n[1]) - 100;
        return Result(true, (ToLong(siren) + 1 + check / 11) % 11 == check % 11);
    }

    // HR: 11 digits (OIB); ISO 7064 MOD 11,10.
    private static VatCheck Croatia(string n)
    {
        bool format = Digits(n, 11);
        return Result(format, format && Iso7064Mod11_10(n));
    }

    // HU: 8 digits; weights 9,7,3,1,9,7,3,1 sum mod 10 == 0.
    private static VatCheck Hungary(string n)
    {
        bool format = Digits(n, 8);
        return Result(format, format && WeightedSum(n, [9, 7, 3, 1, 9, 7, 3, 1]) % 10 == 0);
    }

    // IE: 7 digits + 1–2 letters (current), or digit + letter/+/* + 5 digits + letter (old style).
    private static VatCheck Ireland(string n)
    {
        const string Alphabet = "WABCDEFGHIJKLMNOPQRSTUV";
        if (n.Length is not (8 or 9) || !char.IsAsciiDigit(n[0]) || !Text.AllDigits(n.AsSpan(2, 5)))
        {
            return VatCheck.InvalidFormat;
        }

        foreach (char c in n.AsSpan(7))
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                return VatCheck.InvalidFormat;
            }
        }

        if (Text.AllDigits(n.AsSpan(0, 7)))
        {
            string body = n[..7];
            int extra = n.Length == 9 ? Alphabet.IndexOf(n[8]) : 0;
            return Result(true, n[7] == Alphabet[(WeightedSum(body, [8, 7, 6, 5, 4, 3, 2]) + 9 * extra) % 23]);
        }

        if (n.Length == 8 && (char.IsAsciiLetterUpper(n[1]) || n[1] is '+' or '*'))
        {
            string body = "0" + n.Substring(2, 5) + n[0];
            return Result(true, n[7] == Alphabet[WeightedSum(body, [8, 7, 6, 5, 4, 3, 2]) % 23]);
        }

        return VatCheck.InvalidFormat;
    }

    // IT: 11 digits; first 7 not all zero; province code 001–100, 120, 121, 888 or 999; Luhn.
    private static VatCheck Italy(string n)
    {
        if (!Digits(n, 11) || ToInt(n.AsSpan(0, 7)) == 0)
        {
            return VatCheck.InvalidFormat;
        }

        int province = ToInt(n.AsSpan(7, 3));
        bool provinceOk = province is >= 1 and <= 100 or 120 or 121 or 888 or 999;
        return Result(provinceOk, Luhn(n) == 0);
    }

    // LT: 9 digits (8th digit 1) or 12 digits (11th digit 1); two-pass weighted mod 11.
    private static VatCheck Lithuania(string n)
    {
        bool format = Text.AllDigits(n) && (n.Length == 9 && n[7] == '1' || n.Length == 12 && n[10] == '1');
        if (!format)
        {
            return VatCheck.InvalidFormat;
        }

        ReadOnlySpan<char> body = n.AsSpan(0, n.Length - 1);
        int check = 0;
        for (int i = 0; i < body.Length; i++)
        {
            check += (1 + i % 9) * (body[i] - '0');
        }

        check %= 11;
        if (check == 10)
        {
            check = 0;
            for (int i = 0; i < body.Length; i++)
            {
                check += (1 + (i + 2) % 9) * (body[i] - '0');
            }
        }

        return Result(true, n[^1] - '0' == check % 11 % 10);
    }

    // LU: 8 digits; first 6 mod 89 equals the last 2.
    private static VatCheck Luxembourg(string n)
    {
        bool format = Digits(n, 8);
        return Result(format, format && ToInt(n.AsSpan(0, 6)) % 89 == ToInt(n.AsSpan(6)));
    }

    // LV: 11 digits. Legal entities (first digit > 3) checked; personal codes format only.
    private static VatCheck Latvia(string n)
    {
        if (!Digits(n, 11))
        {
            return VatCheck.InvalidFormat;
        }

        return n[0] > '3'
            ? Result(true, WeightedSum(n, [9, 1, 4, 8, 3, 10, 2, 5, 7, 6, 1]) % 11 == 3)
            : VatCheck.Valid;
    }

    // MT: 8 digits, not starting 0; weights 3,4,6,7,8,9,10,1 sum mod 37 == 0.
    private static VatCheck Malta(string n)
    {
        bool format = Digits(n, 8) && n[0] != '0';
        return Result(format, format && WeightedSum(n, [3, 4, 6, 7, 8, 9, 10, 1]) % 37 == 0);
    }

    // NL: 9 digits + 'B' + 2 digits. The 9 digits pass the 11-test (older numbers), or the whole
    // number passes ISO 7064 MOD 97-10 with the NL prefix (numbers issued since 2020).
    private static VatCheck Netherlands(string n)
    {
        bool format = n.Length == 12 && Text.AllDigits(n.AsSpan(0, 9)) && n[9] == 'B' && Text.AllDigits(n.AsSpan(10))
            && ToLong(n.AsSpan(0, 9)) > 0 && ToInt(n.AsSpan(10)) > 0;
        if (!format)
        {
            return VatCheck.InvalidFormat;
        }

        int elevenTest = (WeightedSum(n.AsSpan(0, 8), [9, 8, 7, 6, 5, 4, 3, 2]) - (n[8] - '0')) % 11;
        return Result(true, elevenTest == 0 || Mod97("NL" + n) == 1);
    }

    // PL: 10 digits; weights 6,5,7,2,3,4,5,6,7,-1 sum mod 11 == 0.
    private static VatCheck Poland(string n)
    {
        bool format = Digits(n, 10);
        return Result(format, format && ((WeightedSum(n, [6, 5, 7, 2, 3, 4, 5, 6, 7, -1]) % 11) + 11) % 11 == 0);
    }

    // PT: 9 digits, not starting 0; mod 11.
    private static VatCheck Portugal(string n)
    {
        bool format = Digits(n, 9) && n[0] != '0';
        return Result(format, format && n[8] - '0' == (11 - WeightedSum(n, [9, 8, 7, 6, 5, 4, 3, 2]) % 11) % 11 % 10);
    }

    // RO: 2–10 digits, not starting 0 (CUI); weights 7,5,3,2,1,7,5,3,2 over the left-padded body.
    private static VatCheck Romania(string n)
    {
        if (!Text.AllDigits(n) || n.Length is < 2 or > 10 || n[0] == '0')
        {
            return VatCheck.InvalidFormat;
        }

        string body = n[..^1].PadLeft(9, '0');
        int check = 10 * WeightedSum(body, [7, 5, 3, 2, 1, 7, 5, 3, 2]) % 11 % 10;
        return Result(true, n[^1] - '0' == check);
    }

    // SE: 12 digits ending in 01; the first 10 pass Luhn.
    private static VatCheck Sweden(string n)
    {
        bool format = Digits(n, 12) && n.EndsWith("01", StringComparison.Ordinal);
        return Result(format, format && Luhn(n.AsSpan(0, 10)) == 0);
    }

    // SI: 8 digits, not starting 0; mod 11 (a result of 10 means 0).
    private static VatCheck Slovenia(string n)
    {
        if (!Digits(n, 8) || n[0] == '0')
        {
            return VatCheck.InvalidFormat;
        }

        int check = 11 - WeightedSum(n, [8, 7, 6, 5, 4, 3, 2]) % 11;
        return Result(true, check != 11 && n[7] - '0' == (check == 10 ? 0 : check));
    }

    // SK: 10 digits, not starting 0, third digit 2, 3, 4, 7, 8 or 9; divisible by 11.
    private static VatCheck Slovakia(string n)
    {
        bool format = Digits(n, 10) && n[0] != '0' && n[2] is '2' or '3' or '4' or '7' or '8' or '9';
        return Result(format, format && ToLong(n) % 11 == 0);
    }

    // GB / XI: 9 or 12 digits (standard, branch), or GD/HA government and health codes.
    private static VatCheck UnitedKingdom(string n)
    {
        if (n.Length == 5 && (n.StartsWith("GD", StringComparison.Ordinal) || n.StartsWith("HA", StringComparison.Ordinal)))
        {
            if (!Text.AllDigits(n.AsSpan(2)))
            {
                return VatCheck.InvalidFormat;
            }

            int code = ToInt(n.AsSpan(2));
            return n[0] == 'G' ? Result(code < 500, true) : Result(code >= 500, true);
        }

        if (n.Length == 11 && (n.StartsWith("GD8888", StringComparison.Ordinal) || n.StartsWith("HA8888", StringComparison.Ordinal)))
        {
            if (!Text.AllDigits(n.AsSpan(6)))
            {
                return VatCheck.InvalidFormat;
            }

            int code = ToInt(n.AsSpan(6, 3));
            bool range = n[0] == 'G' ? code < 500 : code >= 500;
            return Result(range, code % 97 == ToInt(n.AsSpan(9)));
        }

        if (n.Length is 9 or 12 && Text.AllDigits(n))
        {
            int checksum = WeightedSum(n.AsSpan(0, 9), [8, 7, 6, 5, 4, 3, 2, 10, 1]) % 97;
            bool ok = ToInt(n.AsSpan(0, 3)) >= 100 ? checksum is 0 or 42 or 55 : checksum == 0;
            return Result(true, ok);
        }

        return VatCheck.InvalidFormat;
    }

    // CH: the part after "CHE": 9 digits (UID, mod 11 with weights 5,4,3,2,7,6,5,4) + MWST, TVA, IVA or TPV.
    private static VatCheck Switzerland(string n)
    {
        if (n.Length is not (12 or 13) || !Text.AllDigits(n.AsSpan(0, 9)) || n[9..] is not ("MWST" or "TVA" or "IVA" or "TPV"))
        {
            return VatCheck.InvalidFormat;
        }

        int check = (11 - WeightedSum(n, [5, 4, 3, 2, 7, 6, 5, 4]) % 11) % 11;
        return Result(true, check != 10 && n[8] - '0' == check);
    }

    // NO: 9-digit organisation number (weights 3,2,7,6,5,4,3,2,1 sum mod 11 == 0) + MVA.
    private static VatCheck Norway(string n)
    {
        bool format = n.Length == 12 && Text.AllDigits(n.AsSpan(0, 9)) && n.EndsWith("MVA", StringComparison.Ordinal);
        return Result(format, format && WeightedSum(n, [3, 2, 7, 6, 5, 4, 3, 2, 1]) % 11 == 0);
    }

    // TR: 10-digit VKN (Vergi Kimlik Numarası).
    private static VatCheck Turkiye(string n)
    {
        if (!Digits(n, 10))
        {
            return VatCheck.InvalidFormat;
        }

        int sum = 0;
        for (int i = 1; i <= 9; i++)
        {
            int digit = n[9 - i] - '0';
            int c1 = (digit + i) % 10;
            if (c1 != 0)
            {
                int c2 = c1 * (1 << i) % 9;
                sum += c2 == 0 ? 9 : c2;
            }
        }

        return Result(true, n[9] - '0' == (10 - sum % 10) % 10);
    }
}
