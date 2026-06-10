namespace SharedKernel.Messaging.Abstractions.SchemaEvolution;

/// <summary>
/// Translates a message from an older schema (<typeparamref name="TOld"/>) to a newer
/// schema (<typeparamref name="TNew"/>) during schema evolution.
/// </summary>
/// <typeparam name="TOld">The legacy message schema type arriving at the transport.</typeparam>
/// <typeparam name="TNew">The current message schema type expected by registered consumers.</typeparam>
/// <remarks>
/// <para>
/// Implementations are registered as singletons via
/// <c>MessagingBusBuilder.WithVersionTranslator&lt;TOld, TNew, TTranslator&gt;()</c>. When a message
/// of CLR type <typeparamref name="TOld"/> arrives at the transport, it is deserialized and
/// projected to <typeparamref name="TNew"/> via <see cref="Translate"/> before delivery to the
/// consumer registered for <typeparamref name="TNew"/>. No consumer code change is required.
/// </para>
/// <para>
/// This enables rolling upgrades: a producer still emitting the old schema can coexist with
/// consumers that have already migrated to the new schema, without forcing synchronized
/// deployments across services.
/// </para>
/// </remarks>
public interface IMessageVersionTranslator<in TOld, out TNew>
{
    /// <summary>
    /// Projects a message from the old schema to the new schema.
    /// </summary>
    /// <param name="old">The message in the old schema, as deserialized from the transport.</param>
    /// <returns>The equivalent message in the new schema.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Synchronous only — translation must be a pure projection; no I/O, no external
    /// service calls, no side effects.</strong> This method is invoked from the deserialization
    /// pipeline; any I/O, external service call, or side effect performed here is a hard
    /// violation.
    /// </para>
    /// <para>
    /// Implementations should be stateless. Given the same <paramref name="old"/> input, repeated
    /// calls must return equivalent output — the translator may be invoked any number of times
    /// for redelivered or retried messages.
    /// </para>
    /// </remarks>
    TNew Translate(TOld old);
}
