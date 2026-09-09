---
name: ref-secure-defaults-assertion-disambiguation
description: AssertMethodBodyInvokesMethod's overload-disambiguation + sibling-delegation extension (2026-09-09) — what to do when a locked method gains a same-named overload
metadata:
  type: reference
---

`SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(Type, string, Type, string)` in
`00.Governance/SharedKernel.ArchitectureTests/SecureDefaultsAssertion.cs` resolves its target
method by NAME ONLY and throws "ambiguous" if more than one method shares that name. This breaks
the instant a domain adds a same-named overload to a locked type for an unrelated reason (e.g.
`01.Core`'s P-524 added an `internal EncryptToString(string, byte[], Action<byte[]>?)` testing
seam alongside the pre-existing `public EncryptToString(string, byte[])`).

**Fix the helper, not the domain.** Renaming a locked production method to placate this helper's
limitation is backwards. As of 2026-09-09 the method carries two additive, backward-compatible
extensions (all pre-existing positional 4-arg call sites unaffected):

1. An optional 5th parameter `Type[]? parameterTypes = null` — when the name is ambiguous and this
   is supplied, disambiguates by exact positional parameter-type match (via a new
   `CecilStyleFullName(Type)` helper that renders a reflection `Type` the way Mono.Cecil renders
   `TypeReference.FullName`, handling arrays/closed generics). `null` preserves the original
   ambiguous-name-rejection.
2. Same-declaring-type sibling-delegation follow-through in `MethodBodyInvokes` (visited-set
   guarded against cycles) — when a method's own body doesn't directly call the target but DOES
   call a sibling method on the SAME declaring type, recurse into that sibling. Proven NECESSARY
   (not cosmetic): a public overload that only forwards to a private/internal sibling (e.g.
   `EncryptToString(string,byte[]) => EncryptToString(plaintext, associatedData, null)`) never
   contains the target call directly — only the sibling does. Bounded to the same declaring type
   only, so it can never be satisfied by an unrelated type.

**When disambiguating, always target the PUBLIC/production-facing overload**, not whichever
overload happens to resolve — a test-only internal seam passing vacuously defeats the lock's
purpose. Verify non-vacuously by temporarily pointing the assertion at a deliberately-wrong callee
NAME (not editing the domain's source) and confirming it still throws, then revert.

See [[feedback-do-not-edit-forbidden-domains-even-temporarily]] for why source-file edits (even
temporary, even reverted) should be avoided when a task scopes you out of a domain.
