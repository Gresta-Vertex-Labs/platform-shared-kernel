Invoke the `devops-lead` agent with the input below. Do not analyze, plan, reformat, or add commentary — pass the input through as-is and let the agent handle everything autonomously.

**Input:**

$ARGUMENTS

> Free text. Any build, packaging, versioning, CI/CD, container, Kubernetes, deployment, configuration, secrets, or repository-governance concern. There is no phase spec and no work-order ID — the agent classifies the request itself.

---

## Step 1 — Spawn the agent

Use the Agent tool to spawn the `devops-lead` agent in **foreground** mode (wait for completion), passing a brief that contains:

1. The user's input above, verbatim and unmodified.
2. This standing instruction: **read the root `PLATFORM.md` in full before doing anything else.** It is the agent's brain file and the single source of truth for every build, packaging, versioning, pipeline, container, deployment, and configuration decision already made in this repo. If it is empty or a section is missing, that is itself the answer — nothing has been decided in that area yet.
3. This reminder: the repo-state snapshot in the agent's own `## Current Repo Baseline` section is **dated and must be re-verified on disk** before acting on it.
4. This reminder: execute directly. No planning mode, no confirmation gate — write the files, run the verification, update `PLATFORM.md`, and report.

## Step 2 — Empty input

If `$ARGUMENTS` is empty or whitespace only, instruct the agent to run a **posture review** instead of a change:

- Read root `PLATFORM.md` in full and root `CLAUDE.md` read-only.
- Verify on disk what actually exists: `.github/`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`, `NuGet.Config`, `Dockerfile*`, `.dockerignore`, `deploy/`, `build/`, `eng/`.
- Report the current build/release posture, naming every contradiction between what `PLATFORM.md` claims and what is on disk.
- Propose the single highest-value next step with its reasoning and its blast radius, and update `PLATFORM.md`'s `## DevOps Work Log` with any newly identified `○` Pending rows.
- Do not make speculative changes beyond correcting a stale `PLATFORM.md`.

## Step 3 — Report

Relay the agent's completion report unchanged. Do not summarize it, re-verify its claims, or add your own commentary.

If the Agent tool call fails, output the error and leave every file unchanged.

---

## Format Contract

- This command modifies **no file directly** — every write is performed by the `devops-lead` agent.
- The user's input is forwarded verbatim. It is never rewritten, expanded, scoped down, or interpreted before dispatch.
- The agent's jurisdiction is enforced by the agent itself, not here: `.github/**`, MSBuild build-configuration files, `NuGet.Config`, version config, `Dockerfile*`/`.dockerignore`/`deploy/**`, `build/**`/`eng/**`, `PLATFORM.md`, and narrow packaging-only `.csproj` edits.
- The agent never touches root `CLAUDE.md`, root `state-map.md`, any `{NN}.Domain/CLAUDE.md` or `{NN}.Domain/state-map.md`, any `.cs` file, or any `00.Governance` authored artifact.
- There is no devops state-map. Multi-step work is tracked in `PLATFORM.md`'s `## DevOps Work Log`, never as a new row on the root Domain Summary Board.
- No confirmation gate. The agent executes in a single autonomous pass.
