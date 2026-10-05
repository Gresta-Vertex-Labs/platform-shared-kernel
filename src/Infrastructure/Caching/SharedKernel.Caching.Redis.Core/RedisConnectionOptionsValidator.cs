using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core;

/// <summary>Validates <see cref="RedisConnectionOptions"/> rules that data annotations cannot express.</summary>
internal sealed class RedisConnectionOptionsValidator : IValidateOptions<RedisConnectionOptions>
{
    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(1);

    public ValidateOptionsResult Validate(string? name, RedisConnectionOptions options)
    {
        var failures = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            try
            {
                if (ConfigurationOptions.Parse(options.ConnectionString).EndPoints.Count == 0)
                    failures.Add("ConnectionString must name at least one endpoint.");
            }
            catch (Exception ex) when (ex is ArgumentException or RedisConnectionException)
            {
                // The parser's message can echo the string, which may contain a password.
                failures.Add("ConnectionString is not a valid StackExchange.Redis connection string.");
            }
        }

        if (options.ConnectTimeout < MinimumTimeout || options.ConnectTimeout > MaximumTimeout)
            failures.Add("ConnectTimeout must be between 100 milliseconds and 1 minute.");

        if (options.CommandTimeout < MinimumTimeout || options.CommandTimeout > MaximumTimeout)
            failures.Add("CommandTimeout must be between 100 milliseconds and 1 minute.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
