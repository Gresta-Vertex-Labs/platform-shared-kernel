using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Options;

/// <summary>
/// Configuration options for the SharedKernel persistence layer service identity.
/// Bound to the <c>SharedKernel:Persistence</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ServiceName"/> replaces the hardcoded <c>"system"</c> literal in
/// <c>AuditInterceptor.ResolveUserId()</c> for unauthenticated (background, seed) writes.
/// </para>
/// <para>
/// Register via <c>EfCorePersistenceBuilder.WithServiceName(string)</c>.
/// The default value <c>"system"</c> is used when <c>WithServiceName</c> is not called.
/// </para>
/// </remarks>
public sealed class PersistenceServiceOptions
{
    /// <summary>
    /// The service identity string written to audit columns (<c>CreatedBy</c>, <c>ModifiedBy</c>,
    /// <c>DeletedBy</c>) when the current request is unauthenticated or when running in a
    /// background context. Defaults to <c>"system"</c>.
    /// </summary>
    public string ServiceName { get; set; } = "system";
}

/// <summary>
/// Startup validator for <see cref="PersistenceServiceOptions"/>.
/// Fires during DI composition when <c>ValidateOnStart()</c> is registered.
/// </summary>
internal sealed class PersistenceServiceOptionsValidator : IValidateOptions<PersistenceServiceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PersistenceServiceOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrEmpty(options.ServiceName))
        {
            failures.Add("PersistenceServiceOptions.ServiceName must be non-null and non-empty.");
        }
        else if (options.ServiceName.Length > 256)
        {
            failures.Add(
                $"PersistenceServiceOptions.ServiceName must be 256 characters or fewer. " +
                $"Current length: {options.ServiceName.Length}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
