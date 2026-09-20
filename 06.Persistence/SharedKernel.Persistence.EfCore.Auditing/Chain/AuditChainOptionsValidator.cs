using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Validates <see cref="AuditChainOptions"/> beyond what Data Annotations can express — that
/// <see cref="AuditChainOptions.HmacKeyBase64"/> is valid Base64 decoding to at least 32 bytes
/// (<c>IHmacSigner</c>'s own minimum key length).
/// </summary>
/// <remarks>
/// Registered alongside the Data Annotations validator by <c>.WithAuditTrail(IConfiguration)</c>
/// via <c>AddValidatedOptions&lt;AuditChainOptions, AuditChainOptionsValidator&gt;(..., validateDataAnnotations: true)</c>.
/// </remarks>
public sealed class AuditChainOptionsValidator : IValidateOptions<AuditChainOptions>
{
    /// <summary>The minimum decoded key length, bytes — <c>IHmacSigner</c>'s own documented minimum.</summary>
    public const int MinimumKeyLengthBytes = 32;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuditChainOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HmacKeyBase64))
        {
            // Data Annotations' [Required] already reports this — avoid a duplicate failure message.
            return ValidateOptionsResult.Success;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(options.HmacKeyBase64);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AuditChainOptions.HmacKeyBase64)} is not valid Base64.");
        }

        if (decoded.Length < MinimumKeyLengthBytes)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AuditChainOptions.HmacKeyBase64)} decodes to {decoded.Length} bytes; at " +
                $"least {MinimumKeyLengthBytes} are required.");
        }

        if (options.AdvisoryLockTimeout < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AuditChainOptions.AdvisoryLockTimeout)} must not be negative (use " +
                $"{nameof(TimeSpan)}.{nameof(TimeSpan.Zero)} to disable the timeout, not a negative value).");
        }

        return ValidateOptionsResult.Success;
    }
}
