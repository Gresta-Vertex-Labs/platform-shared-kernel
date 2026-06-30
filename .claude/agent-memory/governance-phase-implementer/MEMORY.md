# Governance Phase Implementer — Memory Index

- [Roslyn & test harness version decisions](ref_roslyn_versions.md) — Roslyn 4.14.0 pin, RS2008 suppression, test package conflict
- [NetArchTest API surface](ref_netarchtest_api.md) — ConditionList not IArchRule; Assembly parameter pattern
- [BenchmarkDotNet API decisions](ref_benchmarkdotnet_api.md) — Job.Short removed in 0.15.x, explicit form
- [Analyzer implementation patterns](ref_analyzer_patterns.md) — namespace suppression, SK0003 scope, test harness pattern
- [SK0012 ReflectionGuard & stale Phase Backlog closure](ref_reflection_guard_pattern.md) — IL walk for MakeGenericMethod, exemption registry, InternalsVisibleTo, stale entry closure pattern
- [CommunicationLayeringRules & BaseType chain-walk predicates](ref_communication_arch_rules.md) — SK0013, gRPC/HotChocolate ICustomRule patterns, System.Uri fixture pitfall, task count discrepancy
- [HealthCheckTagIntegrityRules & CompositionRootExclusivityRules](ref_servicedefaults_governance_rules.md) — WO-027 P-173, Ldstr literal-collection technique, ConditionList[] pattern, Phase Key Registry gap, recurring task-count discrepancy, pre-existing Overall Progress total drift
- [StringConstantsClassDetector & const-folding pitfall](ref_string_constants_detector_pattern.md) — WO-028 P-178, third Mono.Cecil technique (field-shape+value resolution), const string vs static readonly string IL divergence, BCL declaring-type confirmation via Cecil-read-stream technique
- [ApplicationPipelineRules & CompilerGenerated exclusion pitfall](ref_application_pipeline_rules_pattern.md) — WO-036 P-225, fourth Mono.Cecil technique (generic-constraint structural match), NetArchTest excludes [CompilerGenerated]/async state machines entirely, GenericInstanceType.FullName includes closed args, PipelineOrderAssertion reflection helper, MediatR.Contracts vs MediatR assembly split
