namespace SharedKernel.Idempotency.Redis.Tests.Support;

/// <summary>Builds the tag filters the metric assertions take.</summary>
internal static class Tags
{
    public static KeyValuePair<string, object?> Of(string key, object value) => new(key, value);
}
