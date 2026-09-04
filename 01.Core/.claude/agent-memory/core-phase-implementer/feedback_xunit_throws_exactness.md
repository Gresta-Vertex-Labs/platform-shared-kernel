---
name: feedback-xunit-throws-exactness
description: xunit Assert.Throws<T> requires an exact exception-type match, not IsAssignableFrom — trips up ArgumentException.ThrowIfNullOrWhiteSpace tests specifically
type: feedback
---

`Assert.Throws<T>` in this repo's pinned xunit version requires the thrown exception's runtime type to
match `T` **exactly** — it does not accept a subtype. This bites every guard-clause test that uses the
BCL's `ArgumentException.ThrowIfNullOrWhiteSpace(value)`: that method throws `ArgumentNullException`
(a subtype of `ArgumentException`) for a null input, but plain `ArgumentException` for empty/whitespace
input. A single `[Theory]` with `[InlineData(null)] [InlineData("")] [InlineData("   ")]` asserting
`Assert.Throws<ArgumentException>` fails on the null case with "Exception type was not an exact match."

**Why:** confirmed empirically during P-482 (`SharedKernel.Localization`) — every guard method built on
`ArgumentException.ThrowIfNullOrWhiteSpace` needs its null case split into its own
`Assert.Throws<ArgumentNullException>` test, separate from a `[Theory]` covering `""`/`"   "` with
`Assert.Throws<ArgumentException>`.

**How to apply:** whenever writing a guard-clause test in this repo against a method that calls
`ArgumentException.ThrowIfNullOrWhiteSpace`, split null from empty/whitespace into two test cases with
different expected exception types. Never assume `Assert.Throws<ArgumentException>` will catch a null
input just because `ArgumentNullException` is an `ArgumentException`.

A related, separate gotcha found the same session: `Assert.Throws<T>(() => someSyncCall())` can
sometimes resolve to an obsolete `Assert.Throws<T>(Func<Task>)` overload when the lambda body is a
single expression-bodied statement, producing a compile error steering you toward `ThrowsAsync`. The
call is genuinely synchronous (e.g. `provider.GetRequiredService<T>()`), so the fix is not to make it
async — wrap the lambda body in braces (`() => { someSyncCall(); }`) to force it to bind as `Action`
instead of `Func<Task>`.
