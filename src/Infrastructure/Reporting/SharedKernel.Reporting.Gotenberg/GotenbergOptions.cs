using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Reporting.Gotenberg;

/// <summary>The Gotenberg service to convert with. Bound from <c>SharedKernel:Reporting:Gotenberg</c>.</summary>
/// <example>
/// <code>
/// "SharedKernel": { "Reporting": { "Gotenberg": { "BaseUrl": "http://gotenberg.reporting.svc.cluster.local:3000" } } }
/// </code>
/// </example>
public sealed class GotenbergOptions : ISectionBoundOptions, IValidatableObject
{
    /// <summary>Gets the configuration section: <c>SharedKernel:Reporting:Gotenberg</c>.</summary>
    public static string SectionName => "SharedKernel:Reporting:Gotenberg";

    /// <summary>Gets or sets the Gotenberg base address, e.g. <c>http://gotenberg:3000</c>. Required.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets how long one conversion attempt may take. Defaults to 60 seconds. Keep it at or below Gotenberg's
    /// own <c>--api-timeout</c> (30 s by default) plus a margin, or raise both.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how many times a conversion that failed on a transient error (a 5xx, a timeout, a dropped
    /// connection) is retried. A conversion has no side effects, so a retry is always safe. Defaults to 2.
    /// </summary>
    [Range(0, 5)]
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Gets or sets the basic-authentication user name, for a Gotenberg started with <c>--api-enable-basic-auth</c>.
    /// Set it together with <see cref="Password"/>, from a secret store.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the basic-authentication password. Set it together with <see cref="Username"/>.</summary>
    public string? Password { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrl is { } baseUrl && (!baseUrl.IsAbsoluteUri || baseUrl.Scheme is not ("http" or "https")))
        {
            yield return new ValidationResult($"BaseUrl '{baseUrl}' must be an absolute http or https address.", [nameof(BaseUrl)]);
        }

        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(30))
        {
            yield return new ValidationResult("Timeout must be positive and at most 30 minutes.", [nameof(Timeout)]);
        }

        if (string.IsNullOrEmpty(Username) != string.IsNullOrEmpty(Password))
        {
            yield return new ValidationResult("Username and Password must be set together.", [nameof(Username), nameof(Password)]);
        }
    }
}
