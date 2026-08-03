namespace SharedKernel.Persistence.EfCore.ReadReplica;

// Domain-local magic-string discipline (SK0022) — the keyed-DI service key used to register and
// resolve a consuming service's replica DbContextOptions<TContext>. A single shared literal is safe
// across every TContext, since the keyed service TYPE (DbContextOptions<TContext>) already varies
// per concrete context type — the (ServiceType, key) pair stays unique.
internal static class ReadReplicaKeys
{
    public const string ReplicaOptions = "SharedKernel.Persistence.EfCore.ReadReplica.Options";
}
