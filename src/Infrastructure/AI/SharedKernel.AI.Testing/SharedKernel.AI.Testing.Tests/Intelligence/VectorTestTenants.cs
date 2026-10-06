using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>Fixed tenant ids shared by these tests.</summary>
internal static class VectorTestTenants
{
    public static readonly TenantId TenantA = new(Guid.Parse("0a0a0a0a-0000-4000-8000-00000000000a"));
    public static readonly TenantId TenantB = new(Guid.Parse("0b0b0b0b-0000-4000-8000-00000000000b"));
}
