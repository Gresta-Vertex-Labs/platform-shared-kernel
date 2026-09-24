using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Presentation.WebApi.Authorization;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Requires the caller to have authenticated with at least one of the given methods (<c>amr</c> values such as
/// <c>mfa</c>, <c>otp</c> or <c>hwk</c>), no longer than <see cref="MaxAgeSeconds"/> ago when that is set. An
/// <see cref="AuthorizeAttribute"/>, so it works natively wherever <see cref="RequirePermissionAttribute"/> does:
/// minimal APIs (<c>RequireAuthenticationMethod(…)</c>), MVC, SignalR hubs and hub methods, and gRPC.
/// </summary>
/// <remarks>
/// <para>
/// Evaluated with <see cref="Security.Abstractions.IUserContext.WasAuthenticatedWith"/>; methods within the attribute
/// are alternatives (OR). With <see cref="MaxAgeSeconds"/>, a method also needs a known verification time,
/// <see cref="Security.Abstractions.IUserContext.GetAuthenticationMethodTime"/>, no more than that many seconds before
/// the injected clock's time: a step-up records when it was verified, and a method the credential carried dates from
/// the sign-in. Over HTTP a signed-in caller without any of them is answered 401 with an RFC 9470
/// <c>insufficient_user_authentication</c> challenge (with <c>max_age</c> when <see cref="MaxAgeSeconds"/> is set) and
/// the code <c>unauthorized.step_up_required</c>; a gRPC call gets the same challenge header and ends as
/// <c>Unauthenticated</c>. SignalR checks the attributes of a hub method before any hub filter runs, so a refused
/// invocation fails with SignalR's own <c>HubException</c> message, "Failed to invoke '…' because user is
/// unauthorized", and the method never runs.
/// </para>
/// <para>
/// <b>On long-lived connections, a step-up method needs <see cref="MaxAgeSeconds"/>.</b> A step-up such as the TOTP one
/// adds its method to a request's principal only while it is recent, but a SignalR connection keeps the principal it
/// connected with: without a maximum age the method stays on the connection, and a hub method guarded by this
/// attribute stays callable, for as long as the connection is open. With one, every hub method call compares the
/// method's time with the clock, so the step-up ends on the connection as it does over HTTP; keep it no longer than
/// the step-up's own window (<c>TotpStepUpOptions.FreshnessWindow</c>) to get the same limit everywhere. Put the
/// attribute on hub methods: on the hub class, or on <c>MapHub&lt;T&gt;()</c>, it is checked once, when the
/// connection opens. A gRPC call is authorized once, when it starts, so a streaming call that started in time keeps
/// running past the maximum age; keep step-up operations in unary calls, or check
/// <see cref="Security.Abstractions.IUserContext.GetAuthenticationMethodTime"/> inside the stream.
/// </para>
/// <para>
/// The constructor and <see cref="MaxAgeSeconds"/> fix the requirement: <see cref="Policy"/> and <see cref="Roles"/>
/// are read-only here. <see cref="AuthorizeAttribute.AuthenticationSchemes"/> can be set, as on <c>[Authorize]</c>, to
/// choose the schemes that authenticate the caller; it adds to the policy and never replaces the requirement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireAuthenticationMethodAttribute : AuthorizeAttribute, IAuthorizeData
{
    private string _policy;
    private int _maxAgeSeconds;

    /// <summary>Initializes a new instance of the <see cref="RequireAuthenticationMethodAttribute"/> class.</summary>
    /// <param name="methods">The authentication methods, any one of which is enough.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="methods"/> is empty, or contains a null, empty or white-space value, or a <c>|</c>.
    /// </exception>
    public RequireAuthenticationMethodAttribute(params string[] methods)
    {
        var values = SharedKernelPolicyNames.ValidateValues(methods, nameof(methods));
        Methods = values;
        _policy = SharedKernelPolicyNames.ForAuthenticationMethods(values);
        base.Policy = _policy;
    }

    /// <summary>Gets the authentication methods, any one of which is enough.</summary>
    public IReadOnlyCollection<string> Methods { get; }

    /// <summary>
    /// Gets or sets how long ago, in seconds, one of the methods may have been verified; greater than zero. <c>0</c>, the
    /// default, sets no limit: a method counts for as long as the caller's principal carries it.
    /// </summary>
    /// <remarks>
    /// Set it as a named argument, <c>[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]</c>; it is encoded into
    /// <see cref="Policy"/>. A caller whose method is older, or has no known time, is asked to step up again, with
    /// <c>max_age</c> in the challenge.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value set is zero or negative.</exception>
    public int MaxAgeSeconds
    {
        get => _maxAgeSeconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            _maxAgeSeconds = value;
            _policy = SharedKernelPolicyNames.ForAuthenticationMethods(Methods, value);
            base.Policy = _policy;
        }
    }

    /// <summary>
    /// Gets the name of the policy ASP.NET Core evaluates for this attribute, encoded from its methods and
    /// <see cref="MaxAgeSeconds"/>.
    /// </summary>
    /// <remarks>Read-only: it hides the setter of <see cref="AuthorizeAttribute.Policy"/>, which would replace the requirement.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string Policy => _policy;

    /// <summary>
    /// Always <see langword="null"/>: roles are required with <see cref="RequireRoleAttribute"/>, evaluated through
    /// <c>IUserContext</c>.
    /// </summary>
    /// <remarks>
    /// Read-only: it hides the setter of <see cref="AuthorizeAttribute.Roles"/>, whose check reads role claims directly
    /// and ignores how the authentication package maps roles.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string? Roles => null;

    /// <inheritdoc />
    string? IAuthorizeData.Policy
    {
        get => _policy;
        set => throw new NotSupportedException(AuthorizeDataMessages.Fixed);
    }

    /// <inheritdoc />
    string? IAuthorizeData.Roles
    {
        get => null;
        set => throw new NotSupportedException(AuthorizeDataMessages.Fixed);
    }
}
