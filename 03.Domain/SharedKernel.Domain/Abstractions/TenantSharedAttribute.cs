namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks an entity type that is deliberately <em>not</em> tenant-scoped: global reference data shared by every
/// tenant (countries, currencies, product catalogues maintained by the operator).
/// </summary>
/// <remarks>
/// <para>
/// A multi-tenant persistence model requires every entity type to implement <see cref="IHasTenant"/> — including
/// the child entities of an aggregate — so that its rows are filtered, guarded on write and protected by
/// row-level security like the root's. An entity type without <see cref="IHasTenant"/> fails the model build
/// unless it carries this attribute (or is configured as tenant-shared in its mapping).
/// </para>
/// <para>
/// Rows of a tenant-shared type are visible to every tenant and are not guarded on write: treat them as
/// read-only reference data maintained by an operator process.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class TenantSharedAttribute : Attribute
{
}
