using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.SignalR.Options;

/// <summary>
/// Validates <see cref="SharedKernelSignalROptions"/> at startup, so a misconfiguration stops the host instead of the
/// first hub invocation.
/// </summary>
internal sealed class SharedKernelSignalROptionsValidator : IValidateOptions<SharedKernelSignalROptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SharedKernelSignalROptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        var rateLimit = options.InvocationRateLimit;

        if (rateLimit.PermitLimit is < 1)
        {
            failures.Add("InvocationRateLimit:PermitLimit must be at least 1, or null to turn the limit off.");
        }

        if (rateLimit.Window <= TimeSpan.Zero)
        {
            failures.Add("InvocationRateLimit:Window must be greater than zero.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
