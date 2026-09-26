---
name: project-wo041-logging-retrofit
description: 11.Communication's EventId sub-block allocation and P-255 logging retrofit facts — reference before designing any future logging-related phase in this domain
metadata:
  type: project
---

> WO-086 (2026-09): the GraphQL package moved to `14.Presentation` (`SharedKernel.Presentation.GraphQL`); its 11200-11299 sub-block stays retired here — never reuse it. Rest still logs nothing.

WO-041 P-255 (authored 2026-07-09) retrofitted `.Grpc` and `.Internal` to the platform `[LoggerMessage]` standard. This is the reference for any future logging-related phase in `11.Communication`.

**EventId sub-block allocation (permanent — do not renumber once shipped):**
`LoggingEventIdRanges.Communication` = 11000 (from `01.Core` P-249), subdivided in **package-declaration order** (Rest, Grpc, GraphQL, Internal — matches both the root CLAUDE.md folder-map row 11 listing and this domain's own Packages table) into four 100-wide sub-blocks:
- Rest = 11000-11099 (reserved, unused as of 2026-07-09 — Rest has zero logging)
- Grpc = 11100-11199 (in use: 11100 `LogCorrelationEnrichmentFailed`, 11101 `LogTenantIdEnrichmentFailed`)
- GraphQL = 11200-11299 (retired — the package left this domain in WO-086; never reassign)
- Internal = 11300-11399 (in use: 11300-11308, `KubernetesServiceEndpointResolver` + `StaticServiceDiscoveryStartupWarning`)

**Why:** `01.Core`'s `LoggingEventIdRanges` (P-249) only reserves the domain-level 1000-wide block (`Communication = 11000`) — subdividing into per-package 100-wide sub-blocks is each domain's own responsibility per the `PackageSubBlockWidth` convention. Package declaration order was chosen as the tiebreaker since it's already documented and stable (root CLAUDE.md folder-map row, this domain's own Packages table) — no other ordering criterion was needed.

**How to apply:** Before adding a new `[LoggerMessage]` call site to Rest, start numbering at its reserved base 11000 (11200-11299 is retired) — never reuse or collide with Grpc's 11100-11199 or Internal's 11300-11399. If a fifth `11.Communication` package is ever added, it needs its own 100-wide sub-block appended after Internal's (11400+), not inserted between existing packages (would require renumbering already-shipped EventIds, which P-249's own rules in `01.Core/CLAUDE.md` forbid for domain-level bases — the same "never renumber, only append" discipline applies at the sub-block level here by extension).

**Verification gate:** `00.Governance` P-250 ships `LoggingEventIdIntegrityAssertion` (a Mono.Cecil-based helper, not a NetArchTest ConditionList) that asserts global EventId uniqueness + per-assembly range membership across every shipped assembly. P-255's T-28 task points this at `SharedKernel.Communication.Grpc`/`.Internal` once P-249 and P-250 both ship — as of P-255's authoring both are still `○` Pending, so T-28 cannot execute yet even though D-23/G-14/G-15/I-09/I-10/I-11 can.

**Source-of-truth check performed (not assumed):** actually read `KubernetesServiceEndpointResolver.cs`, `StaticServiceDiscoveryStartupWarning.cs`, `CorrelationTracingInterceptor.cs`, `TenantIdInterceptor.cs`, plus grepped Rest/GraphQL for zero hits, before designing the phase — do this again for any future logging phase rather than trusting CLAUDE.md prose, since CLAUDE.md documents *intended* behavior and the resolver file was a confirmed case of prose (delegate pattern documented) diverging from shipped code (ad-hoc LogDebug calls also present).
