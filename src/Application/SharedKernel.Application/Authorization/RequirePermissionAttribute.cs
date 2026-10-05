namespace SharedKernel.Application.Authorization;

/// <summary>
/// Declares a permission the caller must hold before a command or query is handled.
/// </summary>
/// <remarks>
/// <para>
/// The values of one attribute are alternatives: the caller needs any one of them. Several
/// attributes on the same request all apply: the caller needs one value of each. So
/// <c>[RequirePermission("invoices.write", "invoices.admin")]</c> admits a caller holding either, and
/// <c>[RequirePermission("invoices.write")] [RequirePermission("customers.read")]</c> admits only a
/// caller holding both. These are the same semantics as the endpoint attribute of
/// <c>SharedKernel.Presentation.Core</c> (<c>[RequireEndpointPermission]</c>).
/// </para>
/// <para>
/// Always enforced by the pipeline <c>AddSharedKernelApplication</c> (<c>SharedKernel.Application.Pipeline</c>) registers, on every path a request is
/// sent from (HTTP, messages, jobs, workflows): an unauthenticated caller gets
/// <c>Error.Unauthorized</c>, a caller without the permission <c>Error.Forbidden</c> whose message
/// names no permission. The caller is read from a registered <c>IRequestContext</c> (<c>SharedKernel.Execution</c>); when a scanned
/// request carries this attribute and none is registered, the host start fails. A request without this
/// attribute is not checked. The request must return
/// <c>Result</c> or <c>Result&lt;T&gt;</c>, because the denial is returned as a failed result
/// (<c>SK0040</c> flags any other response type).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [RequirePermission("invoices.write")]
/// public sealed record IssueInvoice(Guid CustomerId, decimal Amount) : ICommand&lt;Guid&gt;;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with the permissions of which the caller must hold at least one.
    /// </summary>
    /// <param name="permissions">One or more permission names; none may be blank.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="permissions"/> is empty or contains a <see langword="null"/>, empty or
    /// whitespace value.
    /// </exception>
    public RequirePermissionAttribute(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (permissions.Length == 0)
        {
            throw new ArgumentException(
                "[RequirePermission] needs at least one permission.",
                nameof(permissions));
        }

        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                throw new ArgumentException(
                    "[RequirePermission] does not accept a null, empty or whitespace permission.",
                    nameof(permissions));
            }
        }

        Permissions = [.. permissions];
    }

    /// <summary>
    /// Gets the permissions of this attribute; the caller must hold at least one of them.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }
}
