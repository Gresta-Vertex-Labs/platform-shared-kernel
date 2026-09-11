namespace SharedKernel.Configuration;

/// <summary>
/// Declares, on the options type itself, which configuration section it binds from — so the
/// section path is written exactly once and the compiler enforces that it exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The platform's convention is that a configuration section path is a
/// named constant on the options type, never a literal retyped at each call site. Before this
/// interface, that convention was carried by a bare <c>public const string SectionName</c>:
/// nothing required an options type to have one, nothing stopped a caller passing the wrong
/// section, and nothing could read it generically. Implementing this interface turns the
/// convention into a compile-time contract.
/// </para>
/// <para>
/// <b>How to adopt it.</b> Declare <c>SectionName</c> as a <c>static</c> property rather than a
/// <c>const</c> field — a const field is a field, and cannot satisfy a <c>static abstract</c>
/// property. The member stays publicly readable as <c>MyOptions.SectionName</c> either way, so
/// existing <c>configuration.GetSection(MyOptions.SectionName)</c> call sites keep compiling.
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
/// // The section path is no longer named at the call site at all:
/// services.AddValidatedOptions&lt;DatabaseOptions&gt;(builder.Configuration);
/// </code>
/// </example>
/// <para>
/// <b>Opt-in, never required.</b> Every <c>AddValidatedOptions</c> overload that takes an explicit
/// <see cref="Microsoft.Extensions.Configuration.IConfigurationSection"/> works on any options
/// class and ignores this interface entirely. Adopt it per options type, at whatever pace suits;
/// an options type that does not implement it loses nothing.
/// </para>
/// <para>
/// <b>Cost.</b> None at runtime. <c>SectionName</c> is reached through a generic type parameter,
/// which the compiler resolves to a direct static call — no reflection, no
/// <see cref="System.Reflection.MemberInfo"/> lookup, nothing for a trimmer to miss.
/// </para>
/// </remarks>
public interface ISectionBoundOptions
{
    /// <summary>
    /// The configuration section path this options type binds from, in
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> form — colon-delimited for
    /// nesting, e.g. <c>"SharedKernel:Storage:S3"</c>.
    /// </summary>
    /// <remarks>
    /// Must be a stable, non-empty literal. It is part of the deployment contract: changing it
    /// silently stops binding every already-deployed <c>appsettings.json</c>, environment
    /// variable, and secret that targets the old path, and the options type then binds to
    /// defaults rather than failing loudly — which is why
    /// <see cref="SharedKernel.Configuration.Extensions.OptionsExtensions"/> validation
    /// matters as the backstop. Never compose it from a variable or a build-time condition.
    /// </remarks>
    static abstract string SectionName { get; }
}
