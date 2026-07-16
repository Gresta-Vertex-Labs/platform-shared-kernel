---
name: reference_sibling_agent_memories
description: Where to look for other agents' context on the same WO-043 storage build-out, when this domain's own memory isn't enough
metadata:
  type: reference
---

Other agents maintain their own memory about this same cross-domain work order; their files are
useful background reading (read-only — never edit another agent's memory directory) when a new
08.Storage phase request references cross-domain context this domain's own memory doesn't cover:

- `.claude/agent-memory/arch-lead/project_wo043_storage_buildout.md` — the original dispatch
  rationale for P-265–P-271: why the four Abstractions additions were bundled into one phase, why
  Azure Blob Storage and multipart/resumable-upload were deliberately scoped out, and the root
  `CLAUDE.md` gap (missing "What Goes Where" rows, missing `.Obs` in the Abstractions table) found
  and fixed during dispatch.
- `.claude/agent-memory/governance-arch-planner/project_storage_topology_phase.md` — SK0023
  (`NonSingletonAmazonS3ClientRegistration`) and `StorageTopologyRules` design detail, including the
  exact string literals (`"SharedKernel.Storage.S3"`, `"SharedKernel.Storage.Obs"`, `"Amazon"`,
  `"Amazon.S3"`) that governance's enforcement suite already hard-codes against this domain's
  package/namespace shape — read this before renaming any package or changing the `.Abstractions`
  dependency list, since it would invalidate that design.

Root `state-map.md` (search `P-265` through `P-271`) remains the single authoritative source for
current status/acceptance-criteria text — these memory files capture the *reasoning* behind past
decisions, not current state.
