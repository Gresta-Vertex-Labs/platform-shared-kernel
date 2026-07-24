---
name: session3_tests_phase_qdrant_defects
description: 10.Intelligence SK.10.Tests session — Qdrant real-container conformance proof caught 3 real defects (2 in Qdrant Core-phase production code, 1 in 16.Testing's shared fixture). Techniques for verifying SDK claims against a real server, not just client reflection.
type: project
---

## Reflecting a client SDK proves the client; it proves nothing about the server version deployed

The session1/session2 finding "Qdrant genuinely supports collection-level metadata (confirmed via
assembly reflection)" was **true but incomplete** — it proved the *client proto* carries the field.
It did NOT prove the *pinned test-container server version* implements it. `Testcontainers.Qdrant`'s
candidate image `qdrant/qdrant:v1.13.4` (pinned by a 16.Testing session before any real consumer
existed) predates Qdrant server's collection-metadata feature entirely (shipped `v1.16.0`,
2024-11-17, per Qdrant's own GitHub release notes for qdrant/qdrant#7123). Writing metadata against
a `v1.13.4` server silently succeeds and reads back empty (`Config.Metadata.Count == 0`, no error) —
defeating `QdrantCollectionProvisioner`'s `Fingerprint`-based schema-drift detection with **zero
test failure** until a real round-trip assertion actually checked the value.

**How to apply:** for ANY "verified via assembly reflection" claim about a wire-protocol feature
(not just method signatures), also run a real round-trip smoke test against the actual pinned
container version before trusting it as proof. A throwaway console app is the fast way to do this —
see the technique below. This generalizes beyond Qdrant: any client SDK reflecting a proto/wire field
that "should" exist server-side needs the server-side check too.

## Throwaway console-probe technique for empirical SDK/server verification

When a claim needs verification against a *real running dependency* (not just static reflection):
1. `mkdir` a scratch dir under the scratchpad, `dotnet new console`, `dotnet add package X --version Y`.
2. `docker run -d --name X-probe -p <host>:<container> image:tag` for a throwaway instance.
3. Write a minimal `Program.cs` exercising exactly the claim (e.g. create-with-metadata → read-back).
4. `dotnet run`, inspect output, iterate.
5. `docker rm -f X-probe`, `rm -rf` the scratch project when done.
This found the Qdrant v1.13.4 vs v1.16.0 collection-metadata gap in minutes and is the pattern to
reach for whenever a Core-phase "verified via reflection" claim needs re-checking against real I/O
during a later Tests-phase session.

## Two genuine Qdrant Core-phase production defects found only by real-container proof

1. **`QdrantVectorCollection.ExtractDenseVector` NRE'd on the default `ReturnVector = false` path.**
   Qdrant omits the `Vectors` submessage entirely (a **null reference**, not an empty default
   instance) when `vectorsSelector: false` was passed — `vectors.VectorsOptionsCase` on a null
   `vectors` throws. Every `QueryAsync` call using a plain `new VectorQuery { ... }` (i.e. the
   documented default, no `ReturnVector` opt-in) hit this. `GetAsync`/`ScrollAsync` never hit it
   because they hardcode `vectorsSelector: true` unconditionally. **Fix**: null-guard
   `ExtractDenseVector(VectorsOutput? vectors)`.
2. **`QdrantFilterCompiler`'s `Exists` translation used the wrong Qdrant primitive.** It compiled to
   `must_not: [is_null(field)]` (`IsNullCondition`). Qdrant's `IsNullCondition` matches ONLY a key
   that is genuinely *present* with a JSON `null` value — never a key that is simply absent from the
   payload. So `NOT IsNull` is vacuously true for every record whether or not the field was ever set
   — `Exists("tags")` matched every record in the corpus, including ones with no `tags` key at all.
   **Fix**: use `IsEmptyCondition` (`must_not: [is_empty(field)]`) instead — `IsEmpty` matches "key
   absent OR empty array OR null value," so its negation is genuinely "field present with a real
   value." **Domain-brain implication**: `10.Intelligence/CLAUDE.md`'s Filter AST NOTE previously
   documented "Exists is IsNull-negation on Qdrant" as if it were a verified Design-time fact — it
   was never checked against a real server before this session. Corrected in CLAUDE.md (2026-07-24).

**How to apply:** neither defect was catchable by NSubstitute-mocked unit tests (session2's coverage)
— both needed a real conformance suite against a real container. This reinforces the domain's own
Test Rules hard requirement that behavioral/filter-translation coverage is never mocked.

## Qdrant point-id charset is stricter than it looks — conformance corpora must use legal ids

`QdrantRecordMapper.ToPointId` accepts ONLY an unsigned-64-bit-integer string (`ulong.TryParse` with
`NumberStyles.None` — pure digits, no sign) or a canonical `"D"`-format GUID. Mnemonic test-corpus
ids like `"a1"`, `"wait-1"`, `"v1-record"`, `"delete-single"` all fail this and get rejected as
`IntelligenceErrors.InvalidRecordId` **before any I/O** — meaning a conformance test corpus seeded
with such ids fails at seed time with a confusing "seed failed" error, not a clear "your id is
illegal" message pointing at the real cause. Fix pattern used: short numeric-string `const string`
fields (e.g. `IdA1 = "101"`) preserving the original mnemonic grouping for assertion readability,
while being wire-legal.

## FakeHttpMessageHandler + disposal timing (System.ClientModel pipeline)

When wiring a real SDK client with no interface seam (e.g. `OpenAI.Embeddings.EmbeddingClient`)
through `16.Testing`'s `FakeHttpMessageHandler`, reading `handler.Requests[0].Content` **after** the
outer SDK call returns throws `ObjectDisposedException` — `System.ClientModel`'s pipeline disposes
the request's content stream once the call completes. Fix: capture the body **inside** the response
factory delegate passed to `EnqueueResponse`, at the one moment `Content` is guaranteed still alive:
```csharp
handler.EnqueueResponse(request =>
{
    capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
    return someResponse;
});
```
This is a load-bearing pattern for any future `10.Intelligence` (or other domain) test asserting on
outbound request bodies against a `System.ClientModel`/`HttpClientPipelineTransport`-based SDK.

## State-map staleness: root Domain Summary Board missed an entire Core-phase session

Found (again — see session1's identical finding) that the root `state-map.md`'s row for
`10.Intelligence` had not been updated since the *Scaffold*-phase session (2026-07-22), even though
an entire Core-phase session (2026-07-24, C-02–C-05/C-09–C-11, 8/11 done) had landed in between and
was fully reflected in `10.Intelligence/state-map.md`'s own file. The `state-map-phase` skill's
Sub-map mode only propagates to root when a phase key's promotion condition fires (all tasks `●`) —
since `SK.10.Core` never fully closed (Milvus-blocked), no propagation ever happened, and nobody did
a manual root catch-up. **How to apply**: before finishing any session that touches a domain's own
sub-state-map, diff the root Domain Summary Board row against the sub-map's actual current numbers —
if they disagree, do a manual Root-mode catch-up update (R3–R7 of the `state-map-phase` skill) in the
same pass, even though the mechanical propagation didn't trigger it. This is now the second
confirmed instance of this exact staleness pattern in this domain — treat it as expected, not
surprising, and always check.

## Cross-domain fix performed directly, not just recorded as an obligation

Unlike the platform's usual "record the downstream obligation, never perform it" convention for
genuinely-separate-domain work, this session **directly fixed** `16.Testing`'s `QdrantContainerFixture`
image-tag pin (`v1.13.4` → `v1.16.0`) because: (1) it was blocking this domain's own Tests-phase
real-container proof — not merely a nice-to-have improvement for 16.Testing; (2) the root cause and
fix were narrowly scoped (one `const string`, plus updating the fixture's own justifying XML doc);
(3) 16.Testing's own brain explicitly invites "re-verify pins... at Core-phase implementation time"
as the consuming domain's responsibility once a real consumer lands. Updated `16.Testing/CLAUDE.md`'s
own Technology Stack prose and added a changelog entry there too, so the shared brain doesn't go
stale relative to the code. **How to apply**: a downstream-domain fix is in-scope to perform directly
(not just record) when it is (a) blocking your own phase's completion, (b) narrowly scoped, and (c)
the owning domain's own brain already anticipates this class of correction. Still update *that*
domain's own CLAUDE.md/changelog, never silently leave it undocumented there.
