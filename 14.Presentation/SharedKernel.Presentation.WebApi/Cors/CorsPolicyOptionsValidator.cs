using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>
/// Fails startup validation when <see cref="CorsPolicyOptions.AllowCredentials"/> is combined with
/// an empty or wildcard <see cref="CorsPolicyOptions.AllowedOrigins"/> list.
/// </summary>
/// <remarks>
/// This is the classic OWASP-catalogued misconfiguration the CORS specification itself forbids, but
/// that ASP.NET Core's own CORS middleware only throws on at the FIRST real credentialed
/// cross-origin request in production. Registered via
/// <see cref="CorsExtensions.AddSharedKernelCors"/> with <c>ValidateOnStart()</c>, so the
/// combination fails fast at <c>IHost.StartAsync()</c> instead.
/// </remarks>
internal sealed partial class CorsPolicyOptionsValidator : IValidateOptions<CorsPolicyOptions>
{
    private const string ValidationFailureMessage =
        "CorsPolicyOptions.AllowCredentials cannot be combined with an empty or wildcard "
            + "AllowedOrigins list. Specify one or more explicit origins, or set "
            + "AllowCredentials to false.";

    private readonly ILogger<CorsPolicyOptionsValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorsPolicyOptionsValidator"/> class.
    /// </summary>
    /// <param name="logger">
    /// The logger used to record the startup-failure security audit event. When no
    /// <see cref="ILogger{TCategoryName}"/> is registered in the container, a no-op
    /// <see cref="NullLogger{T}"/> is used instead — this validator never fails to construct (nor,
    /// therefore, to run its fail-fast validation) merely because logging was not configured.
    /// </param>
    public CorsPolicyOptionsValidator(ILogger<CorsPolicyOptionsValidator>? logger = null)
    {
        _logger = logger ?? NullLogger<CorsPolicyOptionsValidator>.Instance;
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, CorsPolicyOptions options)
    {
        var isEmptyOrWildcard = options.AllowedOrigins.Count == 0
            || options.AllowedOrigins.Any(origin => origin == "*");

        if (options.AllowCredentials && isEmptyOrWildcard)
        {
            Log.CorsPolicyValidationFailed(_logger, ValidationFailureMessage);
            return ValidateOptionsResult.Fail(ValidationFailureMessage);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="CorsPolicyOptionsValidator"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 4,
            Level = LogLevel.Critical,
            Message = "CORS policy options failed startup validation: {FailureReason}")]
        public static partial void CorsPolicyValidationFailed(ILogger logger, string failureReason);
    }
}
