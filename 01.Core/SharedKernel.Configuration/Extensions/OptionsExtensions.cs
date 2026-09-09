using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Registers <typeparamref name="TOptions"/> and binds it to the supplied
    /// <paramref name="section"/>, validating it with a caller-supplied
    /// <typeparamref name="TValidator"/> instead of Data Annotations reflection, and enforces
    /// that validation eagerly at host startup.
    /// </summary>
    /// <typeparam name="TOptions">
    /// The options class. Must have a public parameterless constructor.
    /// </typeparam>
    /// <typeparam name="TValidator">
    /// An <see cref="IValidateOptions{TOptions}"/> implementation — typically a
    /// <c>partial class</c> annotated with the in-box
    /// <see cref="Microsoft.Extensions.Options.OptionsValidatorAttribute"/> source generator
    /// (which itself evaluates <see cref="System.ComponentModel.DataAnnotations"/> attributes on
    /// <typeparamref name="TOptions"/> via generated code, not reflection), or any other
    /// hand-written <see cref="IValidateOptions{TOptions}"/>.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="section">
    /// The <see cref="IConfigurationSection"/> whose values are bound to
    /// <typeparamref name="TOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This overload is strictly additive to
    /// <see cref="AddValidatedOptions{TOptions}(IServiceCollection, IConfigurationSection)"/>,
    /// which remains the platform default and is completely unchanged by this method's
    /// existence. Choose this overload when reflection-based Data Annotations validation is
    /// undesirable (e.g. a strict AOT/trimming posture) — it calls <b>no</b>
    /// <c>ValidateDataAnnotations()</c> at all; the entire point of this path is avoiding that
    /// reflection-based validator.
    /// </para>
    /// <para>
    /// <typeparamref name="TValidator"/> is registered via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService,TImplementation}(IServiceCollection)"/>
    /// against <see cref="IValidateOptions{TOptions}"/> — never a plain <c>AddSingleton</c> — so
    /// calling this method more than once for the same <typeparamref name="TOptions"/> (or a
    /// consumer registering its own validator first) never double-registers.
    /// </para>
    /// <para>
    /// <see cref="Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.ValidateOnStart{TOptions}"/>
    /// is called identically to the Data Annotations overload, so a misconfigured application
    /// still throws during <c>IHost.StartAsync()</c> rather than failing silently at first use.
    /// </para>
    /// <para>Usage:
    /// <code>
    /// [OptionsValidator]
    /// public partial class MyServiceOptionsValidator : IValidateOptions&lt;MyServiceOptions&gt;
    /// {
    /// }
    ///
    /// services.AddValidatedOptions&lt;MyServiceOptions, MyServiceOptionsValidator&gt;(
    ///     configuration.GetSection("MyService"));
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddValidatedOptions<TOptions, TValidator>(
        this IServiceCollection services,
        IConfigurationSection section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.TryAddSingleton<IValidateOptions<TOptions>, TValidator>();

        services
            .AddOptions<TOptions>()
            .Bind(section)
            .ValidateOnStart();

        return services;
    }
}
