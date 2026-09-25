# P-562 — how the work ran

The record of the presentation gold-standard pass: what each step owned, where it landed, and how it was checked.
The decisions live in [p562-design.md](p562-design.md) (D0–D16),
[p562-review-findings.md](p562-review-findings.md) (the first review) and
[p562-final-review-findings.md](p562-final-review-findings.md) (R1–R38, X1–X4, integration round 2, follow-ups).
Branch `presentation-p562`, from `main` at `3a296bb5`.

## Steps

| Step | Scope | Landed |
| --- | --- | --- |
| Review and design | Findings B1–B16, R1–R11, F1–F10; binding design D0–D16; owner decisions on authorization, removals, additions and the package split | `e8a0969e` |
| Wave 1 | `ErrorType.Unavailable` (503) and `Timeout` (504) in 01.Core; the outage errors of 07, 08, 09, 11.Rest and 17 moved to them | `1a815c21` |
| Wave 2 | WebApi core rebuilt: one error pipeline, native authorization, one-call setup, typed results (D1–D10) | `9a286953` |
| Package pins | Asp.Versioning 10.2, Asp.Versioning.OpenApi, Grpc.StatusProto, Scalar 2.17.8 | `09b43cc2` |
| Wave 3 | Parallel streams: **S** SignalR (`e2722f60`), **O** OpenApi add-on (`7b23b4b2`), **G** gRPC (`9a98360d`); build follow-ups `1915ff50`, `ae42674d` | as listed |
| Fix | The authorization attributes became real `[Authorize]` attributes: SignalR ignores any other `IAuthorizeData` on a hub method | `cfb1f74a` |
| Wave 4 | Integration: **I1** samples (`112bebdd`), **I2** platform integration (`94071b67`); CatalogApi in the packaging job, API documents unpublished in Production (`9cdfe39b`) | as listed |
| Final review | Read-only reviews backed by probe projects: correctness (C1–C22), security (S1–S16), developer experience (D1–D25); decisions R1–R38 | `92f94439` |
| Wave 5 | Remediation: **A** WebApi core, R1–R28 (`ce76f1c7`); **C** REST client, R38 (`d8ac9b21`); **B1** OpenApi, R36 (`2dea8de0`); **B2** SignalR, R34–R35 (`f0c9736d`); **B3** gRPC, R30–R33 (`b17cbd69`); owner-approved decisions X1–X4 recorded (`2ce5f83b`) | as listed |
| Cross-domain fixes | **X1** step-up expiry, 12 + 14 (`1cdfc1ba`); **X2** inbound baggage, 13 + 01 (`7e82d802`); **X3** idempotency per caller, 05 + 18 (`0ae0249f`); **X4** opaque ETags, 06 (`c759839e`) | as listed |
| Integration round 2 | R37 and follow-ups: **I3** 00.Governance (`0a96d769`); the idempotency code constants and their drift test (`e6f6b1df`); **I1** 14 code and consumer-verify (`78ba2499`); **I2** samples against the packed feed (`067aabf6`); ShippingApi reads `IClock` (`bd2e6c74`); **I4** 16.Testing and the ETag key warm-up (`7d32c30e`); **J1** optional, validated headers (`60575d46`); records (`1f789aa6`) | as listed |
| Docs | **D1** 14.Presentation: domain and package READMEs, `CONFIGURATION.md`, `CLAUDE.md` rewritten as rules with the history archived (`e057b25c`); a blank `Idempotency-Key` counts as missing, as a blank `If-Match`, and stale code texts D1 found (`a1727e3a`); **D2** root `CLAUDE.md`, changelog and the other domains' docs (`5c0e727b`); stale code comments D2 found, and these records | as listed |

## How the work was run

- Streams ran in parallel, each in its own git worktree under `C:\wt`: the agent tool's own worktrees exceed the
  Windows path limit. An agent owned a fixed set of folders, never committed, and finished with its projects building
  and its tests passing.
- The orchestrator reviewed each result, committed it in its worktree and merged it with `--no-ff`. Nothing was
  pushed.
- Reviews were read-only and backed their claims with small probe projects. Every accepted finding became a decision
  with an owner; findings outside the approved scope became follow-ups.
- Samples and consumer harnesses were verified against freshly packed packages, as CI's `packaging-verify` job does,
  through a throw-away NuGet cache: every tree packs the same MinVer version, so the shared cache would serve one
  tree's packages to another.
- The final check ran CI's jobs on the merged branch: the full Release build and unit lane, both consumer-verify
  harnesses, and the packaging job with every consumer harness, sample test suite and the OrderApi smoke test.
