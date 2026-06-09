using MassTransit;

namespace SharedKernel.Messaging.MassTransit.Sagas;

/// <summary>
/// Base record for MassTransit saga state instances.
/// Derive from this record to define the persistent state of a saga (orchestration workflow).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CorrelationId"/> is the primary key of the saga instance, used by MassTransit to
/// correlate incoming events to the correct running saga.
/// </para>
/// <para>
/// <see cref="Version"/> implements <c>ISagaVersion</c> from MassTransit, enabling EF Core
/// optimistic concurrency checks. This is why this type lives in the MassTransit package
/// rather than in <c>SharedKernel.Messaging.Abstractions</c>.
/// </para>
/// <para>
/// EF Core mapping configuration lives in the consuming service's
/// <c>IEntityTypeConfiguration&lt;TSaga&gt;</c> — no EF attributes are placed on this record.
/// </para>
/// </remarks>
public abstract record SagaStateBase : SagaStateMachineInstance, ISagaVersion
{
    /// <summary>
    /// The primary key of the saga instance. Used by MassTransit to correlate events to this saga.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// The name of the current state in the saga state machine.
    /// Managed by MassTransit — do not set this property manually.
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// The UTC timestamp when this saga instance was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The UTC timestamp of the last state transition.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency version token, incremented by MassTransit on each state change.
    /// Implements <c>ISagaVersion</c> for EF Core row-version conflict detection.
    /// </summary>
    public int Version { get; set; }
}
