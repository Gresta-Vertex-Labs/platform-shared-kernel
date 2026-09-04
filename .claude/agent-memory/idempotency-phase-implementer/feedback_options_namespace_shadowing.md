---
name: feedback_options_namespace_shadowing
description: A production namespace literally named "Options" (e.g. SharedKernel.Idempotency.Redis.Options) breaks unqualified Microsoft.Extensions.Options.Options.Create(...) calls in any test file that "using"s both — CS0234.
type: feedback
---

Hit while writing tests for `18.Idempotency/SharedKernel.Idempotency.Redis` and `.EfCore` — both packages follow the platform convention of an `Options/` folder mapping to a namespace literally named `...Options` (e.g. `SharedKernel.Idempotency.Redis.Options`, holding `RedisIdempotencyOptions`).

**What happened:** any test file with both `using Microsoft.Extensions.Options;` and `using SharedKernel.Idempotency.Redis.Options;` (or the EfCore equivalent) fails to compile on any unqualified `Options.Create(...)` call with `CS0234: 'Create' does not exist in the namespace 'SharedKernel.Idempotency.Redis.Tests.Options'` — because the test project's OWN namespace tree also has a sibling `...Tests.Options` (from an `Options/` test folder), and C#'s namespace resolution finds that sibling namespace before falling through to the `Microsoft.Extensions.Options.Options` static class pulled in by the `using` directive.

**How to apply:** whenever a test project mirrors a production package's `Options/` folder (which is nearly always, per this repo's folder convention) and needs `Microsoft.Extensions.Options.Options.Create(...)`, add an explicit alias up front:
```csharp
using MsOptions = Microsoft.Extensions.Options.Options;
```
and call `MsOptions.Create(...)` instead of the unqualified `Options.Create(...)`. This is not a rare edge case — it will recur in every domain whose package has an `Options/` folder (i.e. most of them) the moment a test needs `IOptions<T>.Create`. Worth checking for this pattern proactively rather than discovering it via a build error each time.
