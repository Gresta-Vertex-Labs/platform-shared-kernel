using Microsoft.Extensions.Options;
using SharedKernel.Configuration;

namespace SharedKernel.Persistence.EfCore.Options;

/// <summary>
/// Configuration options for the SharedKernel persistence layer service identity.
/// Bound to the <c>SharedKernel:Persistence</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ServiceName"/> replaces the hardcoded <c>"system"</c> literal in
/// the actor of unauthenticated (background, seed) writes.
/// </para>
/// <para>
/// Set with <c>UseServiceName(string)</c> on the <c>AddSharedKernelPostgres</c> builder, or bind the <c>ServiceName</c> key of this section.
/// The default value <c>"system"</c> is used when neither is set.
/// </para>
/// <para>
/// Implements <see cref="ISectionBoundOptions"/> (<c>01.Core/SharedKernel.Configuration</c>)
/// so <c>AddValidatedOptions&lt;PersistenceServiceOptions&gt;(configuration)</c> can read the section
/// path from the type itself. <see cref="SectionName"/> remains a compatible <c>static</c> property
/// (rather than the former <c>const</c> field) — every existing
/// <c>configuration.GetSection(PersistenceServiceOptions.SectionName)</c> call site keeps compiling.
/// </para>
/// </remarks>
internal sealed class PersistenceServiceOptions : ISectionBoundOptions
{
    /// <summary>
    /// The configuration section path this type binds from —
    /// <c>"SharedKernel:Persistence"</c>. Used by
    /// <c>AddSharedKernelPostgres</c> instead of a
    /// bare <c>GetSection("SharedKernel:Persistence")</c> literal at each call site.
    /// </summary>
    public static string SectionName => "SharedKernel:Persistence";

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
