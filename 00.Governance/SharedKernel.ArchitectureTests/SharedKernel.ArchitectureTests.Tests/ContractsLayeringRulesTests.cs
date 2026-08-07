using System.Collections.Immutable;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ContractsLayeringRules"/> — introduced by WO-054 P-350
/// (<c>SK.00.EventEnvelopeConstructionGuard</c>).
/// </summary>
/// <remarks>
/// <para>
/// T-275–T-278 use contrived fixtures compiled via <see cref="CSharpCompilation"/> — the same
/// technique used by <c>RedisTopologyRulesTests</c>/<c>PresentationLayeringRulesTests</c>. Unlike
/// <c>PresentationLayeringRulesTests</c>'s single-assembly stub technique, these fixtures use
/// TWO separate compiled assemblies: a "Contracts stub" assembly (mirroring
/// <c>SharedKernel.Contracts.dll</c>, containing the stubbed <c>IDomainEvent</c>,
/// <c>EventEnvelope&lt;TEvent&gt;</c>, and <c>EventEnvelope.Wrap</c>) and a separate "caller"
/// assembly that references the stub via <c>extraReferences</c>. This separation is required,
/// not stylistic: <c>EventEnvelope.Wrap&lt;TEvent&gt;</c>'s own stub implementation legitimately
/// contains a <c>newobj EventEnvelope&lt;TEvent&gt;</c> instruction — if that stub lived in the
/// SAME compiled assembly as a "pass path" caller fixture, the rule (which scans every type in
/// the assemblies it is given) would report a false violation against the stub factory itself,
/// not the caller under test. <see cref="ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>
/// is invoked ONLY against the caller assembly in every test below — never the Contracts-stub
/// assembly — exactly mirroring the real caller-controlled-exclusion convention (the caller never
/// passes the real <c>SharedKernel.Contracts</c> assembly to this rule).
/// </para>
/// <para>
/// <strong>T-279/T-280 stale-dependency correction (verified empirically 2026-08-07, before this
/// phase's implementation session):</strong> the phase spec's Dependencies section recorded
/// <c>07.Messaging</c> P-340 (<c>SK.07.EnvelopeTenancy</c>, ET-04) as <c>○</c> Pending as of this
/// phase's authoring (2026-08-04) — i.e. it assumed the real, currently-shipped
/// <c>MassTransitEventPublisher.PublishEnvelope&lt;TEvent&gt;</c> STILL contained the raw
/// object-initializer violation, making T-279 (a NON-GATING fire-path test against the CURRENT
/// real assembly) satisfiable immediately and T-280 (a GATING pass-path test against the
/// eventual, P-340-corrected assembly) deferred. That premise is stale: <c>07.Messaging</c>'s own
/// <c>state-map.md</c> shows ET-01 through ET-09 (all of <c>SK.07.EnvelopeTenancy</c>) as
/// <c>●</c> Complete, dated 2026-08-05 — a day before this phase was even authored — and the real,
/// compiled <c>MassTransitEventPublisher.cs</c> source (read directly, not assumed) confirms
/// <c>PublishEnvelope&lt;TEvent&gt;</c> now constructs the envelope EXCLUSIVELY via
/// <c>EventEnvelope.Wrap(integrationEvent, sourceService, correlationId, causationId, tenantId)</c>
/// — the raw object initializer is gone. There is therefore no longer a live violation in the real
/// assembly to reproduce: T-279, AS ORIGINALLY WRITTEN, is unsatisfiable — not because the rule is
/// broken, but because its premise (a still-buggy real assembly) no longer holds. Per this domain's
/// established "verify empirically, then correct in place, never manufacture a false fire" discipline
/// (see the <c>SK.00.StorageTopology</c>/<c>SK.00.SearchTopology</c>/<c>SK.00.IntelligenceTopology</c>/
/// <c>SK.00.WorkflowTopology</c>/<c>SK.00.EfPropertyUsageGuard</c> closeout precedents in
/// <c>00.Governance/CLAUDE.md</c>), T-279 is NOT force-failed and the rule is NOT weakened to
/// manufacture one. Instead: the contrived fire-path fixture (T-275) remains the PRIMARY red proof
/// that this rule mechanically detects the exact violation shape
/// <c>MassTransitEventPublisher.PublishEnvelope&lt;TEvent&gt;</c> used to exhibit — proven by
/// <see cref="NoDirectEventEnvelopeConstructionOutsideContracts_DirectObjectInitializerConstruction_RuleFails"/>
/// mirroring that exact real, historical shape (property-by-property object initializer assigning
/// <c>EventId</c>/<c>OccurredOn</c>/<c>EventType</c>/<c>EventVersion</c>/<c>SourceService</c>/
/// <c>Payload</c>). T-280's GATING acceptance criterion — re-point the rule at the real,
/// POST-P-340-corrected assembly and confirm zero violations — is satisfiable NOW rather than
/// deferred, and is wired below as
/// <see cref="NoDirectEventEnvelopeConstructionOutsideContracts_RealMassTransitAssembly_RulePasses"/>.
/// This single real-assembly test discharges BOTH T-279's and T-280's real-assembly obligations:
/// T-279's original "fire against the current assembly" instruction cannot be executed (there is
/// nothing left to fire on); T-280's "pass against the corrected assembly" instruction is executed
/// here, immediately, GATING-satisfied. See <c>00.Governance/state-map.md</c>'s
/// <c>SK.00.EventEnvelopeConstructionGuard</c> task rows and Cross-Domain Dependencies section for
/// the corresponding row-level corrections recorded alongside this test.
/// </para>
/// </remarks>
public class ContractsLayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // Shared "SharedKernel.Contracts" stub assembly source — mirrors the real shape
    // (IDomainEvent, EventEnvelope<TEvent>, EventEnvelope.Wrap<TEvent>) closely enough for the
    // predicate's Namespace+Name+ParameterCount discriminator to resolve identically to the real
    // compiled type. Never passed directly to the rule under test — mirrors the real
    // SharedKernel.Contracts.dll never being passed either.
    // ---------------------------------------------------------------------------

    private const string ContractsStubSource = """
        namespace SharedKernel.Contracts.Events
        {
            public interface IDomainEvent
            {
                System.Guid Id { get; }
                System.DateTimeOffset OccurredOn { get; }
            }

            public sealed record EventEnvelope<TEvent> where TEvent : IDomainEvent
            {
                public System.Guid EventId { get; init; }
                public System.DateTimeOffset OccurredOn { get; init; }
                public string EventType { get; init; } = string.Empty;
                public int EventVersion { get; init; }
                public string? CorrelationId { get; init; }
                public string? CausationId { get; init; }
                public System.Guid? TenantId { get; init; }
                public string SourceService { get; init; } = string.Empty;
                public TEvent Payload { get; init; } = default!;
            }

            public static class EventEnvelope
            {
                public static EventEnvelope<TEvent> Wrap<TEvent>(
                    TEvent domainEvent,
                    string sourceService,
                    string? correlationId = null,
                    string? causationId = null,
                    System.Guid? tenantId = null)
                    where TEvent : IDomainEvent
                {
                    return new EventEnvelope<TEvent>
                    {
                        EventId = domainEvent.Id,
                        OccurredOn = domainEvent.OccurredOn,
                        EventType = typeof(TEvent).Name,
                        EventVersion = 1,
                        CorrelationId = correlationId,
                        CausationId = causationId,
                        TenantId = tenantId,
                        SourceService = sourceService,
                        Payload = domainEvent,
                    };
                }
            }
        }
        """;

    // ---------------------------------------------------------------------------
    // T-275 — Fire path: direct object-initializer construction outside SharedKernel.Contracts
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-275: A contrived fixture type directly constructs
    /// <c>new EventEnvelope&lt;FixtureEvent&gt; { ... }</c> via object-initializer syntax outside
    /// <c>SharedKernel.Contracts</c> — must fail
    /// <see cref="ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>,
    /// naming the offending type. Mirrors the exact real, historical shape
    /// <c>MassTransitEventPublisher.PublishEnvelope&lt;TEvent&gt;</c> used before its P-340 fix
    /// (assigning every field via object initializer — <c>EventId</c>, <c>OccurredOn</c>,
    /// <c>EventType</c>, <c>EventVersion</c>, <c>SourceService</c>, <c>Payload</c>) — this is the
    /// PRIMARY red proof for this rule (see class-level remarks for why the real-assembly
    /// fire-path test, T-279, could not be executed as originally specified).
    /// </summary>
    [Fact]
    public void NoDirectEventEnvelopeConstructionOutsideContracts_DirectObjectInitializerConstruction_RuleFails()
    {
        var contractsStubAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.ContractsStub.DirectConstruction",
            ContractsStubSource);

        const string callerSource = """
            namespace Fixture
            {
                using SharedKernel.Contracts.Events;

                public sealed record FixtureEvent(System.Guid Id, System.DateTimeOffset OccurredOn) : IDomainEvent;

                public static class DirectEnvelopePublisher
                {
                    public static EventEnvelope<FixtureEvent> PublishEnvelope(FixtureEvent domainEvent, string sourceService)
                    {
                        return new EventEnvelope<FixtureEvent>
                        {
                            EventId = domainEvent.Id,
                            OccurredOn = domainEvent.OccurredOn,
                            EventType = typeof(FixtureEvent).Name,
                            EventVersion = 1,
                            SourceService = sourceService,
                            Payload = domainEvent,
                        };
                    }
                }
            }
            """;

        var callerAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.Caller.DirectConstruction",
            callerSource,
            extraReferences: new[] { contractsStubAssembly });

        var result = ContractsLayeringRules
            .NoDirectEventEnvelopeConstructionOutsideContracts(callerAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DirectEnvelopePublisher.PublishEnvelope directly constructs " +
                     "EventEnvelope<FixtureEvent> via object-initializer syntax, never calling " +
                     "EventEnvelope.Wrap<TEvent>()");

        result.FailingTypeNames.Should().Contain(
            "Fixture.DirectEnvelopePublisher",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-276 — Pass path: legitimate EventEnvelope.Wrap<TEvent>() call — structural
    // self-exemption proof (caller's own IL emits only Call/Callvirt, never Newobj)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-276: A contrived fixture type that legitimately calls
    /// <c>EventEnvelope.Wrap&lt;FixtureEvent&gt;(...)</c> must pass
    /// <see cref="ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>.
    /// Confirms empirically (via the two-assembly separation) that the caller's OWN compiled IL
    /// contains only a <c>call</c> to <c>EventEnvelope.Wrap</c> — the <c>newobj</c> that actually
    /// constructs the record lives inside the Contracts-stub assembly's own <c>Wrap</c> method
    /// body, which is never passed to the rule.
    /// </summary>
    [Fact]
    public void NoDirectEventEnvelopeConstructionOutsideContracts_LegitimateWrapCall_RulePasses()
    {
        var contractsStubAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.ContractsStub.WrapCall",
            ContractsStubSource);

        const string callerSource = """
            namespace Fixture
            {
                using SharedKernel.Contracts.Events;

                public sealed record FixtureEvent(System.Guid Id, System.DateTimeOffset OccurredOn) : IDomainEvent;

                public static class WrapOnlyPublisher
                {
                    public static EventEnvelope<FixtureEvent> PublishEnvelope(FixtureEvent domainEvent, string sourceService)
                    {
                        return EventEnvelope.Wrap(domainEvent, sourceService);
                    }
                }
            }
            """;

        var callerAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.Caller.WrapCall",
            callerSource,
            extraReferences: new[] { contractsStubAssembly });

        var result = ContractsLayeringRules
            .NoDirectEventEnvelopeConstructionOutsideContracts(callerAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "WrapOnlyPublisher.PublishEnvelope constructs the envelope exclusively via " +
                     "EventEnvelope.Wrap<TEvent>() — its own compiled IL contains only a call to " +
                     "Wrap, never a newobj targeting EventEnvelope<TEvent>; the newobj that " +
                     "actually constructs the record lives inside the Contracts-stub assembly, " +
                     "which was never passed to the rule");
    }

    // ---------------------------------------------------------------------------
    // T-277 — Pass path: `with` expression on an already-Wrap-constructed envelope —
    // copy-constructor exclusion proof
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-277: A contrived fixture type that applies <c>envelope with { CorrelationId = "new-id" }</c>
    /// to an already-<c>Wrap</c>-constructed envelope must pass
    /// <see cref="ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>.
    /// Confirms empirically that a <c>with</c> expression compiles to a <c>callvirt</c> against
    /// the compiler-synthesized <c>&lt;Clone&gt;$</c> method — NOT a <c>newobj</c> at all — so the
    /// record's private, one-parameter copy constructor (structurally distinct from this
    /// predicate's zero-parameter fire condition) is never even reachable from the caller's own
    /// compiled IL in the first place.
    /// </summary>
    [Fact]
    public void NoDirectEventEnvelopeConstructionOutsideContracts_WithExpressionOnExistingEnvelope_RulePasses()
    {
        var contractsStubAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.ContractsStub.WithExpression",
            ContractsStubSource);

        const string callerSource = """
            namespace Fixture
            {
                using SharedKernel.Contracts.Events;

                public sealed record FixtureEvent(System.Guid Id, System.DateTimeOffset OccurredOn) : IDomainEvent;

                public static class EnvelopeCorrelationRewriter
                {
                    public static EventEnvelope<FixtureEvent> WithNewCorrelationId(
                        EventEnvelope<FixtureEvent> envelope, string newCorrelationId)
                    {
                        return envelope with { CorrelationId = newCorrelationId };
                    }
                }
            }
            """;

        var callerAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.Caller.WithExpression",
            callerSource,
            extraReferences: new[] { contractsStubAssembly });

        var result = ContractsLayeringRules
            .NoDirectEventEnvelopeConstructionOutsideContracts(callerAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "a `with` expression compiles to a callvirt against the compiler-synthesized " +
                     "<Clone>$ method, never a newobj — the record's private one-parameter copy " +
                     "constructor is structurally distinct from (and never reached via) this " +
                     "predicate's zero-parameter fire condition");
    }

    // ---------------------------------------------------------------------------
    // T-278 — Pass path: trivial negative control — zero EventEnvelope usage
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-278: A contrived fixture assembly with zero <c>EventEnvelope</c> usage of any kind must
    /// pass <see cref="ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>.
    /// </summary>
    [Fact]
    public void NoDirectEventEnvelopeConstructionOutsideContracts_NoEventEnvelopeUsage_RulePassesVacuously()
    {
        const string callerSource = """
            namespace Fixture
            {
                public static class UnrelatedType
                {
                    public static int Add(int a, int b) => a + b;
                }
            }
            """;

        var callerAssembly = CompileInMemory(
            "Fixture.EventEnvelopeGuard.Caller.NoUsage",
            callerSource);

        var result = ContractsLayeringRules
            .NoDirectEventEnvelopeConstructionOutsideContracts(callerAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "UnrelatedType never references EventEnvelope<TEvent> at all — there is " +
                     "nothing yet to enforce");
    }

    // ---------------------------------------------------------------------------
    // T-279/T-280 — Real assembly: SharedKernel.Messaging.MassTransit
    // See class-level remarks for the full stale-dependency correction record.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Discharges T-280's GATING acceptance criterion NOW (rather than deferred): points
    /// <see cref="ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/> at
    /// the real, currently-shipped <c>SharedKernel.Messaging.MassTransit</c> assembly and
    /// confirms zero violations. <c>07.Messaging</c>'s P-340 (<c>SK.07.EnvelopeTenancy</c>,
    /// ET-04) shipped 2026-08-05 — before this phase's own authoring (2026-08-04, one day
    /// EARLIER by calendar date but the implementer session ran after both) — so
    /// <c>MassTransitEventPublisher.PublishEnvelope&lt;TEvent&gt;</c> already constructs the
    /// envelope exclusively via <c>EventEnvelope.Wrap&lt;TEvent&gt;()</c>. There is no longer a
    /// live violation to reproduce (T-279's original fire-path premise), so this single
    /// real-assembly test discharges both T-279's and T-280's real-assembly obligations — see
    /// the class-level remarks for the full record.
    /// </summary>
    [Fact]
    public void NoDirectEventEnvelopeConstructionOutsideContracts_RealMassTransitAssembly_RulePasses()
    {
        var massTransitAssembly = typeof(SharedKernel.Messaging.MassTransit.Extensions.MessagingBusBuilder).Assembly;

        var result = ContractsLayeringRules
            .NoDirectEventEnvelopeConstructionOutsideContracts(massTransitAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "MassTransitEventPublisher.PublishEnvelope<TEvent> was refactored onto " +
                     "EventEnvelope.Wrap<TEvent>() exclusively by 07.Messaging's P-340 " +
                     "(SK.07.EnvelopeTenancy, ET-04), shipped 2026-08-05 — the real assembly " +
                     "contains no direct EventEnvelope<TEvent> object-initializer construction");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// Follows the established pattern from <c>RedisTopologyRulesTests</c> and
    /// <c>PresentationLayeringRulesTests</c>, including the <c>extraReferences</c> parameter for
    /// the two-assembly (Contracts-stub + caller) technique this file's fixtures require.
    /// </summary>
    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        Assembly[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                references.Add(
                    MetadataReference.CreateFromImage(
                        ImmutableArray.Create(File.ReadAllBytes(extraReference.Location))));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));

            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
