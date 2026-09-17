using Microsoft.Extensions.Options;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>Validates <see cref="RedisL2Options"/>.</summary>
internal sealed class RedisL2OptionsValidator : IValidateOptions<RedisL2Options>
{
    private const int MaximumKeyPrefixLength = 64;
    private static readonly TimeSpan MaximumCircuitBreakerDuration = TimeSpan.FromMinutes(10);

    public ValidateOptionsResult Validate(string? name, RedisL2Options options)
    {
        var failures = new List<string>();

        if (options.KeyPrefix is null)
            failures.Add("KeyPrefix must not be null; use an empty string for no prefix.");
        else if (options.KeyPrefix.Length > MaximumKeyPrefixLength)
            failures.Add($"KeyPrefix must be at most {MaximumKeyPrefixLength} characters.");

        if (options.DistributedCacheCircuitBreakerDuration < TimeSpan.Zero
            || options.DistributedCacheCircuitBreakerDuration > MaximumCircuitBreakerDuration)
        {
            failures.Add("DistributedCacheCircuitBreakerDuration must be between zero and 10 minutes.");
        }

        if (options.BackplaneCircuitBreakerDuration < TimeSpan.Zero
            || options.BackplaneCircuitBreakerDuration > MaximumCircuitBreakerDuration)
        {
            failures.Add("BackplaneCircuitBreakerDuration must be between zero and 10 minutes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
