using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.Options;

/// <summary>Validates <see cref="WebApiOptions"/> at startup, so a misconfiguration stops the host instead of a request.</summary>
internal sealed partial class WebApiOptionsValidator : IValidateOptions<WebApiOptions>
{
    internal const int MaxCorrelationIdLength = 1024;

    internal const string WildcardOrigin = "*";

    private readonly ILogger<WebApiOptionsValidator> _logger;

    /// <summary>Initializes a new instance of the <see cref="WebApiOptionsValidator"/> class.</summary>
    /// <param name="logger">The logger; a missing logging registration never prevents validation.</param>
    public WebApiOptionsValidator(ILogger<WebApiOptionsValidator>? logger = null)
    {
        _logger = logger ?? NullLogger<WebApiOptionsValidator>.Instance;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WebApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        ValidateCorrelationId(options.CorrelationId, failures);
        ValidateCors(options.Cors, failures);
        ValidateSecurityHeaders(options.SecurityHeaders, failures);
        ValidateLimits(options.Limits, failures);
        ValidateProblems(options.Problems, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateCorrelationId(WebApiCorrelationIdOptions options, List<string> failures)
    {
        if (options.MaxLength is < 1 or > MaxCorrelationIdLength)
        {
            failures.Add($"CorrelationId:MaxLength must be between 1 and {MaxCorrelationIdLength}.");
        }

        if (string.IsNullOrWhiteSpace(options.AllowedCharacterPattern))
        {
            failures.Add("CorrelationId:AllowedCharacterPattern must be a regular expression.");
            return;
        }

        try
        {
            _ = new Regex(options.AllowedCharacterPattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException exception)
        {
            failures.Add($"CorrelationId:AllowedCharacterPattern is not a valid regular expression: {exception.Message}");
        }
    }

    private void ValidateCors(WebApiCorsOptions options, List<string> failures)
    {
        if (options.AllowedOrigins.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Cors:AllowedOrigins must not contain an empty entry.");
        }

        var noExplicitOrigin = options.AllowedOrigins.Count == 0
            || options.AllowedOrigins.Any(origin => string.Equals(origin, WildcardOrigin, StringComparison.Ordinal));

        if (options.AllowCredentials && noExplicitOrigin)
        {
            const string Failure = "Cors:AllowCredentials requires explicit Cors:AllowedOrigins; browsers reject credentials "
                + "with no origin or with '*'. List the origins, or set AllowCredentials to false.";

            Log.CorsConfigurationInvalid(_logger, Failure);
            failures.Add(Failure);
        }

        if (options.PreflightMaxAge < TimeSpan.Zero)
        {
            failures.Add("Cors:PreflightMaxAge must not be negative.");
        }
    }

    private static void ValidateSecurityHeaders(WebApiSecurityHeadersOptions options, List<string> failures)
    {
        if (options.HstsMaxAge < TimeSpan.Zero)
        {
            failures.Add("SecurityHeaders:HstsMaxAge must not be negative.");
        }
    }

    private static void ValidateLimits(WebApiLimitsOptions options, List<string> failures)
    {
        if (options.MaxRequestBodySize is <= 0)
        {
            failures.Add("Limits:MaxRequestBodySize must be greater than zero, or null for the server default.");
        }

        if (options.MaxJsonDepth < 1)
        {
            failures.Add("Limits:MaxJsonDepth must be at least 1.");
        }
    }

    private static void ValidateProblems(WebApiProblemsOptions options, List<string> failures)
    {
        if (options.TypeBaseUri is { IsAbsoluteUri: false })
        {
            failures.Add("Problems:TypeBaseUri must be an absolute URI.");
        }

        if (options.UnavailableRetryAfter < TimeSpan.Zero)
        {
            failures.Add("Problems:UnavailableRetryAfter must not be negative.");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 4,
            Level = LogLevel.Critical,
            Message = "CORS settings failed startup validation: {FailureReason}")]
        public static partial void CorsConfigurationInvalid(ILogger logger, string failureReason);
    }
}
