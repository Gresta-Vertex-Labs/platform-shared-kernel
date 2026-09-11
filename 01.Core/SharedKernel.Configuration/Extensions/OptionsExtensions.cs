using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace SharedKernel.Configuration.Extensions;

/// <summary>
/// Registers an options class so that it is bound from configuration and <b>proven valid before
/// the application accepts traffic</b>, rather than failing at the first request that happens to
/// read it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule:</b> every options type in the platform is registered through one of these
/// methods. Never call <c>services.Configure&lt;TOptions&gt;(section)</c> directly — it binds
/// without validating, so a missing connection string or an out-of-range timeout becomes a
/// runtime failure in whichever request first touches it, minutes or hours after deployment.
/// </para>
/// <para>
/// <b>Which overload to use:</b>
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Situation</term>
/// <description>Call</description>
/// </listheader>
/// <item>
/// <term>Data Annotations are enough (the common case)</term>
/// <description><see cref="AddValidatedOptions{TOptions}(IServiceCollection, IConfigurationSection, string?)"/></description>
/// </item>
/// <item>
/// <term>…and the options type implements <see cref="ISectionBoundOptions"/></term>
/// <description><see cref="AddValidatedOptions{TOptions}(IServiceCollection, IConfiguration, string?)"/> — the section path is not named at the call site at all</description>
/// </item>
/// <item>
/// <term>A rule spans two properties, or Data Annotations reflection is unwanted</term>
/// <description><see cref="AddValidatedOptions{TOptions, TValidator}(IServiceCollection, IConfigurationSection, bool, string?)"/></description>
/// </item>
/// <item>
/// <term>…and the options type implements <see cref="ISectionBoundOptions"/></term>
/// <description><see cref="AddValidatedOptions{TOptions, TValidator}(IServiceCollection, IConfiguration, bool, string?)"/></description>
/// </item>
/// </list>
/// <para>
/// <b>Trimming and AOT — measured, not assumed.</b> These methods carry
/// <see cref="RequiresUnreferencedCodeAttribute"/> and <see cref="RequiresDynamicCodeAttribute"/>
/// because configuration binding genuinely needs both: the BCL's own
/// <c>OptionsBuilder&lt;TOptions&gt;.Bind</c> is annotated with each. A generic wrapper like this
/// one cannot avoid that — .NET's configuration-binding source generator intercepts <c>Bind</c>
/// calls in the <i>calling</i> assembly, so it can never specialize a <c>Bind&lt;TOptions&gt;</c>
/// that lives inside a library and is generic over an options type the generator has not seen.
/// Each <c>TOptions</c> type parameter is nonetheless annotated with
/// <see cref="DynamicallyAccessedMembersAttribute"/>, which preserves the properties and
/// parameterless constructor the binder needs for a flat options class. The residual risk is an
/// options class whose own properties are complex types: the trimmer cannot see through that, and
/// those nested members can be removed. Enable the trim and AOT analyzers on a trimmed publish
/// and treat any warning pointing here as real.
/// </para>
/// <para>
/// <b>Three verified behaviours worth knowing before relying on them:</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <c>ValidateOnStart()</c> requires a real host. With a bare <see cref="IServiceCollection"/>
/// and <c>BuildServiceProvider()</c> — a worker with no <c>IHost</c>, or a unit test — nothing
/// validates at build time; the first read of <c>IOptions&lt;TOptions&gt;.Value</c> throws
/// <see cref="OptionsValidationException"/> instead. The fail-fast guarantee belongs to
/// <c>IHost.StartAsync()</c>, not to this method.
/// </description></item>
/// <item><description>
/// A configuration reload that introduces an invalid value throws from
/// <c>IConfigurationRoot.Reload()</c> itself — an <see cref="AggregateException"/> wrapping
/// <see cref="OptionsValidationException"/> — but only once something has resolved
/// <c>IOptionsMonitor&lt;TOptions&gt;</c>, whose change callback re-creates and re-validates
/// eagerly. With <c>reloadOnChange: true</c> on a file source, that throw surfaces on the
/// file-watcher's thread where no caller is waiting to catch it. With no monitor resolved,
/// <c>Reload()</c> is silent and the failure defers to the next read.
/// </description></item>
/// <item><description>
/// Data Annotations validation is scoped to the named instance it was registered for (the BCL's
/// <see cref="DataAnnotationValidateOptions{TOptions}"/> skips other names). A custom
/// <c>TValidator</c> is <b>not</b>: it is registered once against
/// <c>IValidateOptions&lt;TOptions&gt;</c> and is therefore invoked for <i>every</i> named
/// instance of that type. A validator that must apply to only one name has to check its own
/// <c>name</c> argument and return <see cref="ValidateOptionsResult.Skip"/> otherwise.
/// </description></item>
/// </list>
/// </remarks>
public static class OptionsExtensions
{
    /// <summary>
    /// Members of an options class that the configuration binder and the Data Annotations
    /// validator reach at runtime, and which a trimmer must therefore keep.
    /// </summary>
    private const DynamicallyAccessedMemberTypes BoundOptionsMembers =
        DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    private const string TrimmingWarning =
        "Configuration binding reads the properties of TOptions reflectively. TOptions' own "
        + "properties and parameterless constructor are preserved via DynamicallyAccessedMembers, "
        + "but members of types nested inside TOptions are not, and Data Annotations validation "
        + "has the same limitation. Verify a trimmed publish, or bind such options by hand.";

    private const string AotWarning =
        "Configuration binding may generate code at runtime to materialize TOptions. Verify a "
        + "native-AOT publish, or bind such options by hand.";

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to <paramref name="section"/> and validates it with
    /// Data Annotations at host startup.
    /// </summary>
    /// <typeparam name="TOptions">
    /// The options class. Needs a public parameterless constructor and settable properties, and
    /// carries its rules as <see cref="System.ComponentModel.DataAnnotations"/> attributes.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="section">The configuration section whose values are bound.</param>
    /// <param name="name">
    /// The named options instance to register, or <see langword="null"/> for the default unnamed
    /// instance. Supply a name only when one service needs two differently-configured instances
    /// of the same options type; resolve those with <c>IOptionsMonitor&lt;TOptions&gt;.Get(name)</c>
    /// or <c>IOptionsSnapshot&lt;TOptions&gt;.Get(name)</c> — never plain
    /// <c>IOptions&lt;TOptions&gt;</c>, which only ever sees the default instance.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="section"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Registers <see cref="IOptions{TOptions}"/>, <see cref="IOptionsSnapshot{TOptions}"/> and
    /// <see cref="IOptionsMonitor{TOptions}"/>, and makes a misconfigured application throw
    /// <see cref="OptionsValidationException"/> from <c>IHost.StartAsync()</c> — see the
    /// type-level remarks for the two cases where that is not literally true.
    /// </para>
    /// <para>
    /// Calling this twice for the same <typeparamref name="TOptions"/> and <paramref name="name"/>
    /// registers the Data Annotations validator exactly once, so failures are reported once rather
    /// than duplicated. (The BCL's own <c>ValidateDataAnnotations()</c> uses a plain
    /// <c>AddSingleton</c> and does duplicate them; this method deliberately does not call it.)
    /// The bind itself is re-applied harmlessly — binding the same section twice produces the same
    /// values.
    /// </para>
    /// <para>
    /// Composes with
    /// <see cref="AddValidatedOptions{TOptions, TValidator}(IServiceCollection, IConfigurationSection, bool, string?)"/>:
    /// calling both for one options type runs Data Annotations <i>and</i> the custom validator,
    /// because validators are registered as a collection rather than as a single winner.
    /// </para>
    /// <example>
    /// <code>
    /// services.AddValidatedOptions&lt;DatabaseOptions&gt;(
    ///     builder.Configuration.GetSection("SharedKernel:Database"));
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode(TrimmingWarning)]
    [RequiresDynamicCode(AotWarning)]
    public static IServiceCollection AddValidatedOptions<
        [DynamicallyAccessedMembers(BoundOptionsMembers)] TOptions>(
        this IServiceCollection services,
        IConfigurationSection section,
        string? name = null)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        string resolvedName = name ?? Microsoft.Extensions.Options.Options.DefaultName;

        AddDataAnnotationsValidatorOnce<TOptions>(services, resolvedName);
        BindAndValidateOnStart<TOptions>(services, section, resolvedName);

        return services;
    }

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to the section it declares through
    /// <see cref="ISectionBoundOptions.SectionName"/> and validates it with Data Annotations at
    /// host startup.
    /// </summary>
    /// <typeparam name="TOptions">
    /// The options class, which names its own configuration section by implementing
    /// <see cref="ISectionBoundOptions"/>.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The configuration to resolve <see cref="ISectionBoundOptions.SectionName"/> against —
    /// normally the root <c>builder.Configuration</c>.
    /// </param>
    /// <param name="name">
    /// The named options instance, or <see langword="null"/> for the default one. Note that this
    /// overload always binds the one section the type declares, so two named instances registered
    /// through it read the <i>same</i> section; for two instances fed by different sections, use
    /// the explicit-section overload.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Identical to the explicit-section overload in every respect except where the section path
    /// comes from. Prefer this one: it is the only form in which a caller cannot pass the wrong
    /// section, because there is no section argument to get wrong.
    /// </para>
    /// <para>
    /// <see cref="ISectionBoundOptions.SectionName"/> is read through a generic type parameter,
    /// which compiles to a direct static call — this overload adds no reflection over the other.
    /// </para>
    /// <example>
    /// <code>
    /// public sealed class DatabaseOptions : ISectionBoundOptions
    /// {
    ///     public static string SectionName => "SharedKernel:Database";
    ///
    ///     [Required] public string ConnectionString { get; set; } = string.Empty;
    /// }
    ///
    /// services.AddValidatedOptions&lt;DatabaseOptions&gt;(builder.Configuration);
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode(TrimmingWarning)]
    [RequiresDynamicCode(AotWarning)]
    public static IServiceCollection AddValidatedOptions<
        [DynamicallyAccessedMembers(BoundOptionsMembers)] TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string? name = null)
        where TOptions : class, ISectionBoundOptions
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddValidatedOptions<TOptions>(
            configuration.GetSection(TOptions.SectionName),
            name);
    }

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to <paramref name="section"/> and validates it with a
    /// caller-supplied <typeparamref name="TValidator"/> at host startup — for rules Data
    /// Annotations cannot express, or to keep Data Annotations reflection out of the validation
    /// path entirely.
    /// </summary>
    /// <typeparam name="TOptions">The options class.</typeparam>
    /// <typeparam name="TValidator">
    /// An <see cref="IValidateOptions{TOptions}"/> implementation with a constructor the container
    /// can satisfy. Typically a <c>partial class</c> carrying the in-box
    /// <see cref="OptionsValidatorAttribute"/>, whose <c>Validate</c> body the BCL source
    /// generator writes at compile time from the same Data Annotations attributes — so validation
    /// itself performs no reflection. Any hand-written implementation works equally well, and is
    /// the only way to express a rule spanning two properties.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="section">The configuration section whose values are bound.</param>
    /// <param name="validateDataAnnotations">
    /// <see langword="true"/> to run Data Annotations validation <i>as well as</i>
    /// <typeparamref name="TValidator"/>. Defaults to <see langword="false"/>: an
    /// <see cref="OptionsValidatorAttribute"/>-generated validator already evaluates those
    /// attributes, so enabling this alongside one validates everything twice and reports each
    /// attribute failure twice. Set it to <see langword="true"/> when
    /// <typeparamref name="TValidator"/> is hand-written and checks only cross-property rules,
    /// leaving the per-property attributes to be enforced here.
    /// </param>
    /// <param name="name">
    /// The named options instance, or <see langword="null"/> for the default one. Be aware that
    /// <typeparamref name="TValidator"/> is registered once per type, not once per name, and so
    /// runs for every named instance — see the type-level remarks.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="section"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <typeparamref name="TValidator"/> is registered with
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// — <b>never</b> <c>TryAddSingleton</c>. <see cref="IValidateOptions{TOptions}"/> is a
    /// collection: the options pipeline runs every registered validator for a type, not the first
    /// one. A <c>TryAddSingleton</c> here silently registers nothing whenever any other validator
    /// for the same <typeparamref name="TOptions"/> already exists — including the Data
    /// Annotations validator added by the sibling overload — so the supplied rules would never run
    /// and invalid configuration would start the host cleanly. <c>TryAddEnumerable</c> still
    /// prevents registering the identical
    /// (<see cref="IValidateOptions{TOptions}"/>, <typeparamref name="TValidator"/>) pair twice.
    /// </para>
    /// <example>
    /// <code>
    /// // Zero-reflection validation: the generator writes Validate(...) at compile time.
    /// [OptionsValidator]
    /// public sealed partial class DatabaseOptionsValidator : IValidateOptions&lt;DatabaseOptions&gt;;
    ///
    /// services.AddValidatedOptions&lt;DatabaseOptions, DatabaseOptionsValidator&gt;(
    ///     builder.Configuration.GetSection("SharedKernel:Database"));
    ///
    /// // A cross-property rule no attribute can express, with attributes still enforced.
    /// public sealed class PoolOptionsValidator : IValidateOptions&lt;PoolOptions&gt;
    /// {
    ///     public ValidateOptionsResult Validate(string? name, PoolOptions options) =&gt;
    ///         options.MinSize &lt;= options.MaxSize
    ///             ? ValidateOptionsResult.Success
    ///             : ValidateOptionsResult.Fail("MinSize must not exceed MaxSize.");
    /// }
    ///
    /// services.AddValidatedOptions&lt;PoolOptions, PoolOptionsValidator&gt;(
    ///     builder.Configuration.GetSection("SharedKernel:Pool"),
    ///     validateDataAnnotations: true);
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode(TrimmingWarning)]
    [RequiresDynamicCode(AotWarning)]
    public static IServiceCollection AddValidatedOptions<
        [DynamicallyAccessedMembers(BoundOptionsMembers)] TOptions,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        this IServiceCollection services,
        IConfigurationSection section,
        bool validateDataAnnotations = false,
        string? name = null)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        string resolvedName = name ?? Microsoft.Extensions.Options.Options.DefaultName;

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());

        if (validateDataAnnotations)
        {
            AddDataAnnotationsValidatorOnce<TOptions>(services, resolvedName);
        }

        BindAndValidateOnStart<TOptions>(services, section, resolvedName);

        return services;
    }

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to the section it declares through
    /// <see cref="ISectionBoundOptions.SectionName"/> and validates it with a caller-supplied
    /// <typeparamref name="TValidator"/> at host startup.
    /// </summary>
    /// <typeparam name="TOptions">
    /// The options class, which names its own configuration section by implementing
    /// <see cref="ISectionBoundOptions"/>.
    /// </typeparam>
    /// <typeparam name="TValidator">
    /// An <see cref="IValidateOptions{TOptions}"/> implementation — see the explicit-section
    /// overload for how to write one.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The configuration to resolve <see cref="ISectionBoundOptions.SectionName"/> against.
    /// </param>
    /// <param name="validateDataAnnotations">
    /// <see langword="true"/> to run Data Annotations validation as well as
    /// <typeparamref name="TValidator"/>. See the explicit-section overload for when that is
    /// wanted and when it double-reports.
    /// </param>
    /// <param name="name">
    /// The named options instance, or <see langword="null"/> for the default one.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The combination this overload exists for — a compile-enforced section path and a
    /// zero-reflection generated validator — is the closest this package gets to a fully
    /// statically-checked options registration. Binding itself is still reflective; see the
    /// type-level trimming remarks.
    /// </remarks>
    [RequiresUnreferencedCode(TrimmingWarning)]
    [RequiresDynamicCode(AotWarning)]
    public static IServiceCollection AddValidatedOptions<
        [DynamicallyAccessedMembers(BoundOptionsMembers)] TOptions,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateDataAnnotations = false,
        string? name = null)
        where TOptions : class, ISectionBoundOptions
        where TValidator : class, IValidateOptions<TOptions>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddValidatedOptions<TOptions, TValidator>(
            configuration.GetSection(TOptions.SectionName),
            validateDataAnnotations,
            name);
    }

    /// <summary>
    /// Binds the section and arms <c>ValidateOnStart</c> for one named options instance.
    /// </summary>
    [RequiresUnreferencedCode(TrimmingWarning)]
    [RequiresDynamicCode(AotWarning)]
    private static void BindAndValidateOnStart<
        [DynamicallyAccessedMembers(BoundOptionsMembers)] TOptions>(
        IServiceCollection services,
        IConfigurationSection section,
        string resolvedName)
        where TOptions : class
        => services
            .AddOptions<TOptions>(resolvedName)
            .Bind(section)
            .ValidateOnStart();

    /// <summary>
    /// Registers the BCL's Data Annotations validator for one named options instance, unless an
    /// equivalent registration for that same name is already present.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a call to the BCL's <c>ValidateDataAnnotations()</c>, which registers
    /// <see cref="DataAnnotationValidateOptions{TOptions}"/> with a plain <c>AddSingleton</c> and
    /// therefore duplicates both the validator and every failure message it produces when a
    /// registration method is called twice for the same options type.
    /// </para>
    /// <para>
    /// The duplicate check is per <i>name</i>, not per type, and that is precisely why it is
    /// written by hand rather than as <c>TryAddEnumerable</c>: <c>TryAddEnumerable</c>
    /// de-duplicates on implementation type, and every named instance shares the one
    /// implementation type <see cref="DataAnnotationValidateOptions{TOptions}"/> — so it would
    /// register the validator for the first name and silently leave every other named instance of
    /// that type unvalidated. Registering a pre-built instance (the validator is immutable and
    /// stateless) is what makes the per-name check possible at all, since a factory-registered
    /// descriptor exposes no inspectable name.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(TrimmingWarning)]
    private static void AddDataAnnotationsValidatorOnce<
        [DynamicallyAccessedMembers(BoundOptionsMembers)] TOptions>(
        IServiceCollection services,
        string resolvedName)
        where TOptions : class
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IValidateOptions<TOptions>)
                && descriptor.ImplementationInstance
                    is DataAnnotationValidateOptions<TOptions> alreadyRegistered
                && string.Equals(alreadyRegistered.Name, resolvedName, StringComparison.Ordinal))
            {
                return;
            }
        }

        services.Add(
            ServiceDescriptor.Singleton<IValidateOptions<TOptions>>(
                new DataAnnotationValidateOptions<TOptions>(resolvedName)));
    }
}
