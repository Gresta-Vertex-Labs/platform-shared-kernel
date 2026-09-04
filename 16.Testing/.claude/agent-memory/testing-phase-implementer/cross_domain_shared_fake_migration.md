---
name: cross_domain_shared_fake_migration
description: When another domain's breaking-change phase (e.g. 06.Persistence's P-448) migrates a shared 16.Testing fake out-of-band to keep its own tests compiling, it typically fixes SharedKernel.Testing but leaves SharedKernel.Testing.SelfTests broken — that's this domain's own responsibility to finish.
type: project
---

`SharedKernel.Testing.SelfTests` is a nested `.Tests`-shaped project that other domains' test
projects (e.g. `06.Persistence/SharedKernel.Persistence.EfCore.Tests`) never reference — only
`SharedKernel.Testing` (the production-facing fake library) is a real cross-domain dependency.

**What happened (2026-09-01, P-448 vs. P-450):** `01.Core` shipped a breaking change to
`IEncryptionKeyProvider` (sync → async). `06.Persistence`'s own P-448 phase needed
`SharedKernel.Persistence.EfCore.Tests` to keep compiling, and that test project references
`SharedKernel.Testing` — so P-448 migrated `16.Testing/SharedKernel.Testing/Cryptography/
FakeEncryptionKeyProvider.cs` (and `FakeSymmetricEncryptionService.cs`) to the async shape as a
side effect, entirely outside `16.Testing`'s own dispatch/session. It had no reason to fix
`SharedKernel.Testing.SelfTests/Cryptography/FakeEncryptionKeyProviderTests.cs` (still calling the
removed sync methods) since that project isn't in its own build graph at all.

**Why this matters:** `16.Testing/state-map.md` still recorded the async migration task (C-131) as
`⚑` Blocked, because no one had gone back to update its tracking after the out-of-band fix. The
actual build (`SharedKernel.Testing.csproj`) was fine; only `SharedKernel.Testing.SelfTests` was
broken.

**How to apply:** Before implementing a "blocked" fake-migration task in `16.Testing`, always run
`dotnet build` on BOTH `SharedKernel.Testing.csproj` and `SharedKernel.Testing.SelfTests.csproj`
separately — a green `SharedKernel.Testing.csproj` does not mean the domain is unblocked; check
whether the shape has already changed and only the SelfTests proof file needs migrating. Credit the
finding accurately in the changelog (state whose phase actually did the migration) rather than
claiming the work as this session's own.
