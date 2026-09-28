<!--
  Thanks for contributing to Platform.SharedKernel!

  The "PR template" check reads this description. Fill in every section and tick every
  checklist box, or the check fails and a bot comment lists exactly what is missing.
  It re-runs automatically whenever you edit the description.

  PR title: Conventional Commits, checked separately.
    feat(caching): add fencing token to renewable locks
    fix(security)!: reject DPoP proofs without an ath claim     <- "!" marks a breaking change
-->

## 📝 Summary

<!-- What does this PR change, and why? Two or three sentences are enough. -->


## 🔗 Related issues

<!-- "Closes #123", "Refs #45" — or "None" plus a short reason for small, self-evident fixes. -->


## 💥 Breaking changes

<!-- Tick exactly ONE box. If breaking: add "!" to the PR title and describe below what breaks,
     who is affected, and the exact migration steps. -->

- [ ] No breaking changes
- [ ] This PR breaks public API or behavior — migration notes below


## 🔐 Security impact

<!-- Tick exactly ONE box. Security-sensitive means: cryptography, authentication, authorization,
     tenant isolation, secrets or keys, input validation at a trust boundary, or personal data
     reaching logs. If ticked, explain below what changed and why it is safe. -->

- [ ] None
- [ ] Touches security-sensitive code — explained below


## 🧪 How was this tested?

<!-- Tests added or changed, commands run, manual checks. At minimum:
     dotnet test Platform.SharedKernel.Unit.slnf
     Container-backed changes: also run the Platform.SharedKernel.Integration.slnf lane. -->


## ✅ Checklist

<!-- Tick every box. Where an item does not apply to your change, tick it anyway — each is
     phrased so that "not applicable" satisfies it. -->

- [ ] The PR title follows Conventional Commits (`type(scope): summary`)
- [ ] Tier rules are respected — every new reference is one its `<SharedKernelTier>` may take (SKTIER checks pass)
- [ ] Tests cover the change and pass locally
- [ ] Public API changes, if any, are recorded in `PublicAPI.Unshipped.txt` and have XML docs
- [ ] New logging, if any, uses `[LoggerMessage]` with an explicit `EventId` in the domain's range
- [ ] No secrets, keys, connection strings or personal data in code, tests, fixtures or logs
- [ ] Documentation is updated where behavior or usage changed


## 💬 Notes for reviewers

<!-- Optional. Design decisions, trade-offs, where to start reading, follow-up work. -->

