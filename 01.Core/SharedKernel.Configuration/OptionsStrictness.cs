namespace SharedKernel.Configuration;

/// <summary>
/// Opt-in checks that turn a configuration <i>mistake</i> — a misspelled section path or a
/// misspelled key — into a startup failure, instead of an options instance that silently holds
/// its defaults.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these are off by default.</b> Both checks reject configuration that is sometimes
/// legitimate. An options type whose every property has a sensible default may have no section
/// at all in a given environment, and <see cref="RequireSection"/> would refuse to start it. And
/// during a rolling deployment, configuration for the <i>next</i> release is often live before
/// every pod runs that release's code — <see cref="RejectUnknownKeys"/> would crash the pods
/// still running the old code the moment a new key appears. Turn each on per options type, where
/// neither situation applies.
/// </para>
/// <para>
/// The flags combine: <c>OptionsStrictness.RequireSection | OptionsStrictness.RejectUnknownKeys</c>.
/// </para>
/// </remarks>
[Flags]
public enum OptionsStrictness
{
    /// <summary>
    /// No extra checks. A missing section binds as defaults and an unrecognised key is ignored;
    /// only the registered validators can reject the result.
    /// </summary>
    None = 0,

    /// <summary>
    /// Fail at host startup when the bound section does not exist — the signature of a
    /// misspelled section path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported as an <c>OptionsValidationException</c> alongside any other validation failures
    /// for the same instance, and re-evaluated on every configuration reload.
    /// </para>
    /// <para>
    /// "Exists" is <c>IConfigurationSection.Exists()</c>: the section has a value or at least one
    /// child key. An empty JSON object (<c>"Database": {}</c>) and an explicit
    /// <c>"Database": null</c> therefore both count as <b>missing</b>; an environment variable
    /// such as <c>Database__Port</c> on its own counts as present.
    /// </para>
    /// </remarks>
    RequireSection = 1,

    /// <summary>
    /// Fail at host startup when the section contains a key that matches no property of the
    /// options type — the signature of a misspelled key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sets <c>BinderOptions.ErrorOnUnknownConfiguration</c>, so the binder itself throws an
    /// <see cref="InvalidOperationException"/> naming every unrecognised key. It is not an
    /// <c>OptionsValidationException</c>: the failure happens while binding, before any validator
    /// runs.
    /// </para>
    /// <para>
    /// Matching is case-insensitive and applies to nested objects too. Keys under a dictionary
    /// property are accepted, since any key is valid there. A key matching a property with a
    /// non-public setter is accepted but still not bound.
    /// </para>
    /// </remarks>
    RejectUnknownKeys = 2,
}
