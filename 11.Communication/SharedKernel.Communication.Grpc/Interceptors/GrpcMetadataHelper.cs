using Grpc.Core;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Shared helper for gRPC metadata operations used by
/// <see cref="CorrelationTracingInterceptor"/> and <see cref="TenantIdInterceptor"/>.
/// This is the sole authoritative implementation of these operations — neither interceptor
/// may define its own local copy.
/// </summary>
internal static class GrpcMetadataHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="metadata"/> contains an entry
    /// whose key equals <paramref name="key"/> (case-insensitive comparison).
    /// </summary>
    /// <param name="metadata">The metadata collection to search.</param>
    /// <param name="key">The metadata key to look up.</param>
    internal static bool HasMetadataEntry(Metadata metadata, string key)
    {
        foreach (var entry in metadata)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a new <see cref="Metadata"/> instance containing all entries from
    /// <paramref name="source"/> plus a new entry with the given <paramref name="key"/>
    /// and <paramref name="value"/>. The original <paramref name="source"/> is never mutated.
    /// </summary>
    /// <param name="source">The existing metadata to clone.</param>
    /// <param name="key">The key for the new entry to append.</param>
    /// <param name="value">The value for the new entry to append.</param>
    /// <returns>A new <see cref="Metadata"/> instance with all existing entries plus the new one.</returns>
    internal static Metadata CloneAndAdd(Metadata source, string key, string value)
    {
        var clone = new Metadata();
        foreach (var entry in source)
        {
            clone.Add(entry);
        }
        clone.Add(key, value);
        return clone;
    }
}
