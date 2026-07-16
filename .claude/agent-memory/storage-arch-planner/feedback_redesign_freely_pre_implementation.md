---
name: feedback_redesign_freely_pre_implementation
description: Bundle every known contract fix into one Design phase while a package has zero shipped code — redesigning after providers exist is a breaking change, redesigning before is free
metadata:
  type: project
---

WO-043's P-265 bundled four separate contract additions (server-side copy, batch delete, streaming
list, connectivity probe) into a single "Abstractions contract finalization" phase specifically
because `SharedKernel.Storage.Abstractions` had zero shipped code and zero provider implementations
at design time. One of the four (converting `ListAsync` from a fully-materialized
`Task<Result<IReadOnlyList<FileMetadata>>>` to a streaming `IAsyncEnumerable<FileMetadata>`) was a
self-consistency catch: the domain's own stated philosophy ("blob bytes never buffered in memory")
was being violated by its own already-drafted signature, just applied to metadata volume instead of
object bytes.

**Why:** Fixing a signature before any provider package exists costs nothing — there's no
`S3FileStorage`/`ObsFileStorage` implementation to rewrite and no consuming service to migrate.
Fixing it after `.S3`/`.Obs` ship is a breaking change against two providers simultaneously.

**How to apply:** When a new phase input asks for one new capability on an interface that hasn't
shipped yet (check the Package Board — state `○` and no Core-phase tasks marked `●`), proactively
re-read the ENTIRE current interface contract in `CLAUDE.md` for other latent inconsistencies
against this domain's own stated philosophy (Stream-first, Result-first, zero-third-party-deps in
Abstractions, etc.) before locking the Design phase — don't just bolt on the one requested member.
Once a phase input targets a package that already has Core-phase tasks at `●` (real shipped code),
switch back to treating any signature change as a breaking change requiring explicit migration
guidance, not a free redesign.
