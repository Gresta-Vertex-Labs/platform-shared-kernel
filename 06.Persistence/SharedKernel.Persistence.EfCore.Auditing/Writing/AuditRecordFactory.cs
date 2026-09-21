using System.Diagnostics;
using System.Security.Cryptography;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.EfCore.Auditing.Format;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Persistence.EfCore.Auditing.Writing;

/// <summary>
/// Turns an <see cref="AuditEntry"/> into a <see cref="PendingLedgerRecord"/>: validates field lengths up
/// front (A19), resolves identity from <see cref="IRequestContext"/> and the service name, captures the
/// correlation and W3C trace ids, stamps the time and builds the payload commitment.
/// </summary>
internal sealed class AuditRecordFactory(
    AuditCallerScope scope,
    IClock clock,
    string serviceName)
{
    public AuditCallerScope Scope => scope;

    private IRequestContext Context => scope.RequestContext;

    /// <summary>Builds the record for <paramref name="entry"/> in the chain of <paramref name="tenantId"/>.</summary>
    /// <exception cref="ArgumentException">A field is empty or longer than its <see cref="AuditFieldLimits"/> limit.</exception>
    public PendingLedgerRecord Create(AuditEntry entry, Guid? tenantId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var actorId = Context.UserId is { Length: > 0 } userId ? userId : serviceName;
        var activity = Activity.Current;

        Require(entry.Action, nameof(AuditEntry.Action), AuditFieldLimits.Action);
        Require(entry.ResourceType, nameof(AuditEntry.ResourceType), AuditFieldLimits.ResourceType);
        Require(entry.ResourceId, nameof(AuditEntry.ResourceId), AuditFieldLimits.ResourceId);
        Optional(entry.ErrorCode, nameof(AuditEntry.ErrorCode), AuditFieldLimits.ErrorCode);
        Optional(entry.ApprovalId, nameof(AuditEntry.ApprovalId), AuditFieldLimits.Identifier);
        Optional(entry.IdempotencyKey, nameof(AuditEntry.IdempotencyKey), AuditFieldLimits.Identifier);
        Require(actorId, "ActorId (IRequestContext.UserId)", AuditFieldLimits.Identifier);
        Require(serviceName, "SourceService (PersistenceServiceOptions.ServiceName)", AuditFieldLimits.Identifier);

        var clientId = Truncate(Context.ClientId);
        var sessionId = Truncate(Context.SessionId);
        var impersonatorId = Truncate(Context.ImpersonatorId);
        var correlationId = Truncate(activity?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId));
        var traceId = activity is { IdFormat: ActivityIdFormat.W3C } ? activity.TraceId.ToHexString() : null;

        if (!Enum.IsDefined(entry.Outcome))
            throw new ArgumentException($"{nameof(AuditEntry.Outcome)} '{entry.Outcome}' is not defined.", nameof(entry));

        var salt = RandomNumberGenerator.GetBytes(AuditV3Format.SaltLength);
        var occurredOn = AuditTimestamp.Truncate(clock.UtcNow);

        var fields = new LedgerRecordFields
        {
            Id = Guid.CreateVersion7(occurredOn),
            TenantId = tenantId,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            Action = entry.Action,
            Outcome = entry.Outcome,
            ErrorCode = entry.ErrorCode,
            ActorId = actorId,
            ActorKind = Context.ActorKind,
            ClientId = clientId,
            SessionId = sessionId,
            ImpersonatorId = impersonatorId,
            SourceService = serviceName,
            CorrelationId = correlationId,
            TraceId = traceId,
            ApprovalId = entry.ApprovalId,
            IdempotencyKey = entry.IdempotencyKey,
            OccurredOn = occurredOn,
            PayloadHash = AuditV3Format.ComputePayloadCommitment(salt, entry.BeforeSnapshot, entry.AfterSnapshot),
        };

        return new PendingLedgerRecord(fields, salt, entry.BeforeSnapshot, entry.AfterSnapshot);
    }

    private static void Require(string? value, string field, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Audit field '{field}' is required.", field);
        Optional(value, field, limit);
    }

    private static void Optional(string? value, string field, int limit)
    {
        if (value is not null && value.Length > limit)
        {
            throw new ArgumentException(
                $"Audit field '{field}' is {value.Length} characters long; the limit is {limit} ({nameof(AuditFieldLimits)}).",
                field);
        }
    }

    // Ambient, caller-uncontrolled context values (session/client/impersonator ids, correlation baggage)
    // are truncated rather than rejected: an oversized header must not make an audited command fail.
    private static string? Truncate(string? value) =>
        value is { Length: > AuditFieldLimits.Identifier } ? value[..AuditFieldLimits.Identifier] : value;
}
