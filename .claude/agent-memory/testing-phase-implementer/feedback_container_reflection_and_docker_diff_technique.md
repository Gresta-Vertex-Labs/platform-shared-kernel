---
name: feedback_container_reflection_and_docker_diff_technique
description: Reusable techniques for Docker-gated Containers/ self-tests -- reaching an unexposed Testcontainers management port via reflection, and safely diffing `docker ps` output without a pipe deadlock.
type: feedback
---

Two concrete techniques worked out while writing `MilvusContainerFixtureTests.cs` (T-52/T-53, WO-045, 2026-07-22), worth reusing on any future `Containers/` fixture test.

**1. Reaching a container's management/side-channel port when the fixture doesn't expose it as a property.**
`MilvusContainerFixture` exposes only `.Endpoint` (the gRPC port, 19530) — no property for the docker-healthcheck management port (9091), and that management port's host-mapped value has NO fixed/derivable offset from the gRPC port's own mapped value (confirmed empirically across several runs: sometimes +1, sometimes -1, sometimes +580). The only way to reach it without editing the already-shipped fixture (out of scope for a Tests-phase task) is reflection into the fixture's private backing field:
```csharp
var field = typeof(MilvusContainerFixture).GetField("_container", BindingFlags.NonPublic | BindingFlags.Instance)!;
var container = (MilvusContainer)field.GetValue(fixture)!;
var managementPort = container.GetMappedPublicPort(9091); // GetMappedPublicPort is a PUBLIC method on the Testcontainers base type
```
This is the same class of test-only reflection already sanctioned domain-wide by `Domain/SpecificationAssert` (`Criteria.Compile()`) — acceptable in `SharedKernel.Testing.SelfTests`, never in production code. Only the private-field lookup needs reflection; the method call itself is public.

**Why:** The task spec (T-53) explicitly required proving `GET /healthz` over the management port, but the already-shipped C-70–C-72 fixture (a different phase, out of scope to modify here) never surfaced that port. Rather than skip the acceptance criterion or modify a `●`-complete Core-phase file, reflection into the known private field name (confirmed by reading the fixture's own source first) is the correct minimal-footprint fix.

**How to apply:** Before reaching for reflection, always read the target fixture's actual source for the private field's exact name/type (don't guess) and confirm the underlying Testcontainers container type's public surface (e.g. `GetMappedPublicPort(int)`) via the shipped DLL or docs. If the fixture ever renames its backing field, this reflection lookup breaks loudly (`GetField` returns null) rather than silently — that's an accepted trade-off, not a hidden hazard.

**2. Shelling out to `docker` CLI from a test without deadlocking.**
`Process.Start` + sequential `await process.StandardOutput.ReadToEndAsync()` then `await process.StandardError.ReadToEndAsync()` is a classic .NET pipe-buffer deadlock trap: if the child process writes enough to the stream NOT currently being drained, it blocks forever waiting for buffer space, and the parent never reaches the second `ReadToEndAsync()` call to drain it. Hit this exactly once (mid-implementation, using `docker logs <container>` to look for embedded-etcd evidence — Milvus logs heavily to both stdout and stderr) — the process hung indefinitely, had to be found via `docker ps` and killed manually.

**Fix:** always read stdout and stderr CONCURRENTLY:
```csharp
var stdoutTask = process.StandardOutput.ReadToEndAsync();
var stderrTask = process.StandardError.ReadToEndAsync();
await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());
```
Switched the actual shipped test away from `docker logs` (verbose, deadlock-prone) to `docker ps --format "{{.Image}}"` (tiny, single-line-per-container output) for the "no external etcd/MinIO container" acceptance check — diffing a before/after snapshot of running images. Even with the small-output command, the concurrent-read pattern is used defensively.

**How to apply:** Any future test that shells out to an external CLI and needs both streams must use the concurrent-read pattern, never sequential — regardless of how small the expected output looks, since defensive code here is cheap and the failure mode (a silently hung `dotnet test` run) is expensive to diagnose.

**3. Residual race condition, documented not eliminated.**
The before/after `docker ps` image-diff approach has a narrow, accepted race: if a sibling Docker-gated test (e.g. `MinioContainerFixtureTests`, which also lives in `SharedKernel.Testing.SelfTests/Containers/`) starts its own container within the same ~7-second window, the diff could show a false-positive "new minio image" unrelated to the fixture under test. This project has no `[CollectionDefinition(DisableParallelization = true)]`/`xunit.runner.json` override, so xUnit's default cross-test-class parallelization means this is a real, if infrequent, possibility. Not solved in this pass — the true fix (serializing all `Containers/` Docker-gated tests into one xUnit collection) is a broader change than a single Tests-phase task warrants, and was explicitly out of scope. If this ever causes real flakiness, that serialization is the fix to reach for.
