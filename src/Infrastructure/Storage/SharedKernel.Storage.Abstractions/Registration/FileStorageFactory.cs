namespace SharedKernel.Storage;

/// <summary>The registry behind <see cref="IFileStorageFactory"/> and the keyed stores.</summary>
internal sealed class FileStorageFactory : IFileStorageFactory
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, Entry> _entries;

    public FileStorageFactory(IServiceProvider services, IEnumerable<FileStoreRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);

        _services = services;
        _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (FileStoreRegistration registration in registrations)
        {
            _entries.Add(registration.Name, new Entry(registration, services));
        }

        StoreNames = [.. _entries.Values.Select(e => e.Registration.Name)];
    }

    public IReadOnlyCollection<string> StoreNames { get; }

    public bool IsTenantScoped(string storeName) => Find(storeName).Registration.TenantScoped;

    public IFileStorage GetStore(string storeName)
    {
        Entry entry = Find(storeName);
        if (entry.Registration.TenantScoped)
        {
            throw new InvalidOperationException(
                $"Storage store '{entry.Registration.Name}' is tenant-scoped: resolve ITenantFileStorage and call ForTenant(tenantId).");
        }

        return entry.Store.Value;
    }

    public ITenantFileStorage GetTenantStore(string storeName)
    {
        Entry entry = Find(storeName);
        if (!entry.Registration.TenantScoped)
        {
            throw new InvalidOperationException(
                $"Storage store '{entry.Registration.Name}' is shared by all tenants: resolve IFileStorage.");
        }

        return entry.TenantStore.Value;
    }

    public IFileStorage Open(FileReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Entry entry = Find(reference.Store);
        return (entry.Registration.TenantScoped, reference.TenantId) switch
        {
            (false, null) => entry.Store.Value,
            (true, { } tenantId) => entry.TenantStore.Value.ForTenant(tenantId),
            (true, null) => throw new InvalidOperationException(
                $"The reference has no tenant, but storage store '{entry.Registration.Name}' is tenant-scoped."),
            (false, _) => throw new InvalidOperationException(
                $"The reference has a tenant, but storage store '{entry.Registration.Name}' is shared by all tenants."),
        };
    }

    internal IFileStorage GetDefaultStore() =>
        GetSingle(tenantScoped: false, "IFileStorage", "[FromKeyedServices(\"name\")] IFileStorage").Store.Value;

    internal ITenantFileStorage GetDefaultTenantStore() =>
        GetSingle(tenantScoped: true, "ITenantFileStorage", "[FromKeyedServices(\"name\")] ITenantFileStorage").TenantStore.Value;

    private Entry GetSingle(bool tenantScoped, string serviceName, string keyedForm)
    {
        Entry[] candidates = [.. _entries.Values.Where(e => e.Registration.TenantScoped == tenantScoped)];
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"No {(tenantScoped ? "tenant-scoped" : "shared")} storage store is registered, so {serviceName} cannot be resolved. "
                + "Register one with AddSharedKernelStorage().Add<Provider>(...)."
                + (tenantScoped ? "AddTenantStore(name)." : "AddStore(name).")),
            _ => throw new InvalidOperationException(
                $"{candidates.Length} {(tenantScoped ? "tenant-scoped" : "shared")} storage stores are registered "
                + $"({string.Join(", ", candidates.Select(c => c.Registration.Name))}), so an unkeyed {serviceName} is ambiguous. "
                + $"Inject {keyedForm} or use IFileStorageFactory."),
        };
    }

    private Entry Find(string storeName)
    {
        ArgumentNullException.ThrowIfNull(storeName);

        return _entries.TryGetValue(storeName, out Entry? entry)
            ? entry
            : throw new InvalidOperationException(
                _entries.Count == 0
                    ? $"No storage store named '{storeName}' is registered; no stores are registered at all."
                    : $"No storage store named '{storeName}' is registered. Registered stores: {string.Join(", ", StoreNames)}.");
    }

    private sealed class Entry
    {
        public Entry(FileStoreRegistration registration, IServiceProvider services)
        {
            Registration = registration;
            var provider = new Lazy<IFileStorage>(() => CreateStore(registration, services));
            Store = new Lazy<IFileStorage>(() => new ScopedFileStorage(provider.Value, tenantId: null));
            TenantStore = new Lazy<ITenantFileStorage>(() => new TenantFileStorage(provider.Value));
        }

        public FileStoreRegistration Registration { get; }

        /// <summary>The shared store as handed out; never the provider's store itself.</summary>
        public Lazy<IFileStorage> Store { get; }

        public Lazy<ITenantFileStorage> TenantStore { get; }

        private static IFileStorage CreateStore(FileStoreRegistration registration, IServiceProvider services)
        {
            IFileStorage store = registration.Factory(services)
                ?? throw new InvalidOperationException($"The factory of storage store '{registration.Name}' returned null.");

            if (!string.Equals(store.StoreName, registration.Name, StringComparison.Ordinal) || store.TenantId is not null)
            {
                throw new InvalidOperationException(
                    $"The factory of storage store '{registration.Name}' returned a store named '{store.StoreName}' "
                    + "or bound to a tenant; it must return the provider's store for that name, over the whole bucket.");
            }

            return store;
        }
    }
}
