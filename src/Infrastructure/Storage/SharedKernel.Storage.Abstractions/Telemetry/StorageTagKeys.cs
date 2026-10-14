namespace SharedKernel.Storage;

/// <summary>
/// The tag keys of storage spans and metrics. Values are bounded configuration (store names, the provider
/// connection name, operation names), never object keys, buckets or endpoints.
/// </summary>
public static class StorageTagKeys
{
    /// <summary>The store name: <c>storage.store</c>.</summary>
    public const string Store = "storage.store";

    /// <summary>The operation, for example <c>upload</c> or <c>delete_many</c>: <c>storage.operation</c>.</summary>
    public const string Operation = "storage.operation";

    /// <summary>The provider connection name, for example <c>S3</c> or <c>Obs</c>: <c>storage.provider</c>.</summary>
    public const string Provider = "storage.provider";

    /// <summary>The transfer direction of a byte measurement, <c>upload</c> or <c>download</c>: <c>storage.direction</c>.</summary>
    public const string Direction = "storage.direction";
}
