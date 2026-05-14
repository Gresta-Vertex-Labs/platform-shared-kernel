using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Configuration.Extensions;

/// <summary>
/// DI extension methods for registering validated options.
/// </summary>
public static class OptionsExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TOptions"/> and binds it to the supplied
    /// <paramref name="section"/>, applying Data Annotations validation that is eagerly
    /// evaluated at host startup.
    /// </summary>
    /// <typeparam name="TOptions">
    /// The options class. Must have a public parameterless constructor and be annotatable
    /// with <see cref="System.ComponentModel.DataAnnotations"/> attributes.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="section">
    /// The <see cref="IConfigurationSection"/> whose values are bound to
    /// <typeparamref name="TOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>,
    /// <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{TOptions}"/>, and
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>.
    /// </para>
    /// <para>
    /// <see cref="Microsoft.Extensions.DependencyInjection.OptionsBuilderDataAnnotationsExtensions.ValidateDataAnnotations{TOptions}"/>
    /// is called so that any Data Annotations violations are caught eagerly at startup via
    /// <c>ValidateOnStart()</c>. A misconfigured application will throw during
    /// <c>IHost.StartAsync()</c> rather than failing silently at first use.
    /// </para>
    /// <para>Usage:
    /// <code>
    /// services.AddValidatedOptions&lt;MyServiceOptions&gt;(
    ///     configuration.GetSection("MyService"));
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfigurationSection section)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services
            .AddOptions<TOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
