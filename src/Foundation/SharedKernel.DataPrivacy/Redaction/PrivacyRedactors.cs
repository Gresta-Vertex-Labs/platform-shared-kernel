using Microsoft.Extensions.Compliance.Redaction;
using SharedKernel.DataPrivacy.Masking;

namespace SharedKernel.DataPrivacy.Redaction;

/// <summary>A <see cref="Redactor"/> that applies one <see cref="PiiMasking"/> rule.</summary>
/// <remarks>
/// Derive from this class to redact a service-specific classification with a
/// <see cref="PiiMasking"/> rule of your own choosing, then register it with
/// <see cref="IRedactionBuilder.SetRedactor{T}(Microsoft.Extensions.Compliance.Classification.DataClassificationSet[])"/>.
/// </remarks>
public abstract class MaskingRedactor : Redactor
{
    /// <inheritdoc />
    public override int GetRedactedLength(ReadOnlySpan<char> input) => Mask(input.ToString()).Length;

    /// <inheritdoc />
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        string masked = Mask(source.ToString());
        masked.AsSpan().CopyTo(destination);
        return masked.Length;
    }

    /// <summary>Masks <paramref name="value"/>.</summary>
    /// <param name="value">The value to mask.</param>
    /// <returns>The masked value.</returns>
    protected abstract string Mask(string value);
}

/// <summary>Redacts with <see cref="PiiMasking.Email(string?)"/>, keeping the domain.</summary>
public sealed class EmailRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.Email(value);
}

/// <summary>Redacts with <see cref="PiiMasking.Phone"/>.</summary>
public sealed class PhoneNumberRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.Phone(value);
}

/// <summary>Redacts with <see cref="PiiMasking.CardNumber"/>.</summary>
public sealed class CardNumberRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.CardNumber(value);
}

/// <summary>Redacts with <see cref="PiiMasking.Iban"/>.</summary>
public sealed class BankAccountRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.Iban(value);
}

/// <summary>Redacts with <see cref="PiiMasking.NationalId"/>.</summary>
public sealed class NationalIdRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.NationalId(value);
}

/// <summary>Redacts with <see cref="PiiMasking.PersonName"/>.</summary>
public sealed class PersonNameRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.PersonName(value);
}

/// <summary>Redacts with <see cref="PiiMasking.IpAddress"/>.</summary>
public sealed class IpAddressRedactor : MaskingRedactor
{
    /// <inheritdoc />
    protected override string Mask(string value) => PiiMasking.IpAddress(value);
}

/// <summary>Replaces every value, including an empty one, with <see cref="PiiMasking.RedactedSentinel"/>.</summary>
public sealed class SuppressingRedactor : Redactor
{
    /// <inheritdoc />
    public override int GetRedactedLength(ReadOnlySpan<char> input) => PiiMasking.RedactedSentinel.Length;

    /// <inheritdoc />
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        PiiMasking.RedactedSentinel.AsSpan().CopyTo(destination);
        return PiiMasking.RedactedSentinel.Length;
    }
}

/// <summary>Replaces a value with its <see cref="Pseudonymizer"/> token, so log lines about one person stay correlatable.</summary>
/// <param name="pseudonymizer">The pseudonymizer holding the key.</param>
public sealed class PseudonymizingRedactor(Pseudonymizer pseudonymizer) : Redactor
{
    private readonly Pseudonymizer _pseudonymizer = pseudonymizer ?? throw new ArgumentNullException(nameof(pseudonymizer));

    /// <inheritdoc />
    public override int GetRedactedLength(ReadOnlySpan<char> input) => input.IsEmpty ? 0 : Pseudonymizer.TokenLength;

    /// <inheritdoc />
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        string token = _pseudonymizer.Pseudonymize(source);
        token.AsSpan().CopyTo(destination);
        return token.Length;
    }
}
