---
name: devops
description: Hand a build, CI, packaging, versioning, release, secrets or container request to the devops-lead agent, which executes it; with no input it reviews the build posture. Use for anything about how the code is built, checked, packed or published.
argument-hint: <request, or empty for a posture review>
---

Invoke the `devops-lead` agent with the input below. Do not analyze, plan, reformat, or add commentary — pass the input through as-is and let the agent handle everything autonomously.

**Input:**

$ARGUMENTS

> Free text. Any build, packaging, versioning, CI/CD, container, deployment, configuration, secrets, or repository-governance concern. There is no phase spec and no work-order ID — the agent classifies the request itself.

---

## Step 1 — Spawn the agent

Use the Agent tool to spawn the `devops-lead` agent in **foreground** mode (wait for completion), passing a brief that contains:

1. The user's input above, verbatim and unmodified.
2. This standing instruction: **read `.claude/agents/_common.md`, then `eng/README.md` (build internals: root build files, the tier check, MinVer versioning, CI workflows, test run settings) and `CONTRIBUTING.md` (build and test lanes, what must pass, release) in full before doing anything else.** Together they are the record of every build, packaging, versioning, pipeline and configuration decision already made. A topic neither covers has not been decided yet.
3. This reminder: any repo-state snapshot in the agent's own file is dated; re-verify on disk (`.github/`, `Directory.Build.props`/`.targets`, `Directory.Packages.props`, `global.json`, `NuGet.Config`, `eng/`) before acting on it.
4. This reminder: execute directly. No planning mode, no confirmation gate — write the files, run the verification, update `eng/README.md` (and `CONTRIBUTING.md` when contributor-facing behaviour changed), and report.

## Step 2 — Empty input

If `$ARGUMENTS` is empty or whitespace only, instruct the agent to run a **posture review** instead of a change:

- Read `eng/README.md` and `CONTRIBUTING.md` in full and root `CLAUDE.md` read-only.
- Verify on disk what actually exists: `.github/`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`, `NuGet.Config`, `Dockerfile*`, `.dockerignore`, `deploy/`, `eng/`.
- Report the current build/release posture, naming every contradiction between what `eng/README.md`/`CONTRIBUTING.md` claim and what is on disk.
- Propose the single highest-value next step with its reasoning and blast radius. If it needs tracking, add it to the root `state-map.md` `## Open Work` as a work order with Domain `eng` (next id from `## ID Counters`).
- Make no speculative changes beyond correcting a stale `eng/README.md` or `CONTRIBUTING.md`.

## Step 3 — Report

Relay the agent's completion report unchanged. Do not summarize it, re-verify its claims, or add your own commentary.

If the Agent tool call fails, output the error and leave every file unchanged.

---

## Format Contract

- This skill modifies **no file directly** — every write is performed by the `devops-lead` agent.
- The user's input is forwarded verbatim. It is never rewritten, expanded, scoped down, or interpreted before dispatch.
- The agent's jurisdiction is enforced by the agent itself: `.github/**`, MSBuild build-configuration files, `NuGet.Config`, `global.json`, `Dockerfile*`/`.dockerignore`/`deploy/**`, `eng/**`, the build sections of `CONTRIBUTING.md`, and narrow packaging-only `.csproj` edits.
- The agent never touches root or domain `CLAUDE.md`, any domain `state-map.md`, any `.cs` file, or any `00.Governance` authored artifact. On the root `state-map.md` it only adds and closes its own `eng` work orders.
- No confirmation gate. The agent executes in a single autonomous pass.
