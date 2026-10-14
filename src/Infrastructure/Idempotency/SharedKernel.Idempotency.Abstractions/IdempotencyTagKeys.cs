namespace SharedKernel.Idempotency.Abstractions;

/// <summary>The tag keys of the idempotency spans and metrics (<c>idempotency.{snake_name}</c>).</summary>
/// <remarks>
/// The cross-cutting <c>outcome</c> and <c>error.*</c> keys come from <c>SharedKernel.Primitives</c>. No key,
/// fingerprint, reservation token, tenant or stored response is ever a tag value.
/// </remarks>
public static class IdempotencyTagKeys
{
    /// <summary>What the key guards: <c>request</c> or <c>message</c> (<c>"idempotency.purpose"</c>).</summary>
    public const string Purpose = "idempotency.purpose";

    /// <summary>The store member: <c>begin</c>, <c>complete</c> or <c>release</c> (<c>"idempotency.operation"</c>).</summary>
    public const string Operation = "idempotency.operation";

    /// <summary>The closed result set of <see cref="IdempotencyTelemetry"/> (<c>"idempotency.result"</c>).</summary>
    public const string Result = "idempotency.result";

    /// <summary>The provider that served the call: <c>redis</c> or <c>efcore</c> (<c>"idempotency.provider"</c>).</summary>
    public const string Provider = "idempotency.provider";

    /// <summary>Set to <see langword="true"/> on a span whose store outage was let through by fail-open (<c>"idempotency.fail_open"</c>).</summary>
    public const string FailOpen = "idempotency.fail_open";
}
