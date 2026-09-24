namespace BillingApi.Features;

/// <summary>
/// The permissions the use cases declare with <c>[RequirePermission]</c>. The pipeline's authorization behavior checks
/// them on every path a command or query takes — HTTP today, a message or a job tomorrow — so no endpoint repeats them.
/// </summary>
public static class Permissions
{
    public const string Read = "billing.read";
    public const string Write = "billing.write";
    public const string Admin = "billing.admin";
}
