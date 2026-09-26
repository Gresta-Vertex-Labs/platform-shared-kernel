namespace SharedKernel.Workflows.Temporal.Tests;

/// <summary>Fixed tenant identities shared by the workflow tests.</summary>
internal static class TestTenants
{
    public static readonly TenantId A = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000001"));
    public static readonly TenantId B = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000002"));
    public static readonly TenantId Encrypted = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000003"));
    public static readonly TenantId Plain = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000004"));
    public static readonly TenantId Idem = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000005"));
    public static readonly TenantId Propagation = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000006"));
    public static readonly TenantId Replay = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000007"));
    public static readonly TenantId Sigqry = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000008"));
    public static readonly TenantId RoundTrip = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-000000000009"));
}
