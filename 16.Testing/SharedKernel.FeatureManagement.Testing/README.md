# SharedKernel.FeatureManagement.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **`FakeFeatureClient` is an OpenFeature `IFeatureClient` whose flags a test sets directly — fixed values or
> per-context rules — so code built on `SharedKernel.FeatureManagement` is tested without configuration, a provider
> or a host.**

| You get | So that |
| --- | --- |
| `SetEnabled`, `Set(flag, value)`, `SetObject(flag, value, typeInfo)` | A flag's value is one fluent call per test |
| `Set(flag, context => value)` | "On for tenant A only" is tested with the real `FeatureTargetingContext` |
| An unset flag returns its default with `FlagNotFound` | A test never passes only because the fake invented a value |
| `WasEvaluated`, `EvaluatedFlags`, `TrackedEvents` | You assert which flags the code actually read and which events it tracked |
| `AddFakeFeatureFlags(configure)` | One call swaps the flag client in a real `IServiceCollection` |

## Install

```xml
<PackageReference Include="SharedKernel.FeatureManagement.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. Production code must never reference a Testing package;
`TestingNeverReferencedByProduction` fails the build's architecture tests when it does.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.FeatureManagement` (and through it OpenFeature) |
| Namespaces | `SharedKernel.Testing.FeatureManagement` |

## Quick start

```csharp
using OpenFeature;
using SharedKernel.FeatureManagement;
using SharedKernel.Testing.FeatureManagement;
using Xunit;

public static class Flags
{
    public static readonly FeatureFlag<bool> NewCheckout = FeatureFlag.Boolean("NewCheckout");
    public static readonly FeatureFlag<string> CheckoutTheme = FeatureFlag.String("CheckoutTheme", "classic");
}

public sealed class CheckoutLayout(IFeatureClient flags)
{
    public async Task<string> GetAsync(CancellationToken ct) =>
        await flags.IsEnabledAsync(Flags.NewCheckout, ct)
            ? $"new-checkout/{await flags.GetValueAsync(Flags.CheckoutTheme, ct)}"
            : "legacy-checkout";
}

public sealed class CheckoutLayoutTests
{
    [Fact]
    public async Task New_checkout_uses_the_configured_theme()
    {
        var flags = new FakeFeatureClient()
            .SetEnabled(Flags.NewCheckout)
            .Set(Flags.CheckoutTheme, "dark");

        Assert.Equal("new-checkout/dark", await new CheckoutLayout(flags).GetAsync(CancellationToken.None));
        Assert.True(flags.WasEvaluated(Flags.CheckoutTheme));
    }
}
```

In a real host or `WebApplicationFactory`:

```csharp
services.AddFakeFeatureFlags(flags => flags.SetEnabled(Flags.NewCheckout));
```

## How it works

- **Faithful:** evaluate through the same `FeatureClientExtensions` calls production code uses (`IsEnabledAsync`,
  `GetValueAsync`, `GetDetailsAsync`). An unset flag returns the caller's default with `ErrorType.FlagNotFound`; a
  flag set to another type returns the default with `ErrorType.TypeMismatch`; no evaluation throws. A set value
  reports `Reason.Static`.
- **Context:** a rule receives the client's context (`SetContext`) merged with the call's context, the call's
  winning.
- **Simplified:** a deterministic value map, not a rules engine — no percentage rollouts, time windows or variants
  (those are `Microsoft.FeatureManagement` configuration, tested against the real registration). Hooks added with
  `AddHooks` are stored but never run; `AddHandler`/`RemoveHandler` do nothing; `ProviderStatus` is always `Ready`.
- **Lifetimes and threading:** `AddFakeFeatureFlags` registers one singleton instance; the fake is thread-safe.

## Recipes

### 1. Turn a flag on for one tenant only

```csharp
var acme = new TenantId(Guid.NewGuid());   // SharedKernel.Execution.Tenancy
var flags = new FakeFeatureClient()
    .Set(Flags.NewCheckout, ctx => ctx.GetValue(FeatureContextKeys.TenantId)?.AsString == acme.ToString());

Assert.True(await flags.IsEnabledAsync(Flags.NewCheckout, FeatureTargetingContext.ForTenant(acme).ToEvaluationContext()));
```

### 2. Set an object flag

```csharp
flags.SetObject(Flags.Promo, new Banner("Spring sale", 2), AppJson.Default.Banner);
```

Pass the same `JsonTypeInfo<T>` the flag was declared with. `Set` on an object flag throws `ArgumentException`.

### 3. Prove the default path

```csharp
var details = await new FakeFeatureClient().GetDetailsAsync(Flags.CheckoutTheme);

Assert.Equal("classic", details.Value);
Assert.Equal(ErrorType.FlagNotFound, details.ErrorType);   // OpenFeature.Constant
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddFakeFeatureFlags(this IServiceCollection, Action<FakeFeatureClient>? configure = null)` | One `FakeFeatureClient` singleton, resolvable as itself and as `IFeatureClient` |

Call it instead of `AddSharedKernelFeatureManagement`; registered after it, the fake is the `IFeatureClient` resolved.

### `FakeFeatureClient` (implements `IFeatureClient`)

| Member | What it does |
| --- | --- |
| `SetEnabled(FeatureFlag<bool> flag, bool enabled = true)` | Sets a boolean flag for every caller |
| `Set<T>(FeatureFlag<T> flag, T value)` | Sets a fixed `bool`, `string`, `int` or `double` value |
| `Set<T>(FeatureFlag<T> flag, Func<EvaluationContext, T> rule)` | Computes the value per evaluation from the merged context |
| `SetObject<T>(FeatureFlag<T> flag, T value, JsonTypeInfo<T> typeInfo)` | Sets an object flag |
| `WasEvaluated(FeatureFlag flag)` | Whether the flag was evaluated at least once |
| `EvaluatedFlags` | Every evaluated flag key, in order, including repeats |
| `TrackedEvents` | Every `Track(...)` event name, in order |
| `Reset()` | Forgets every value, rule, evaluation and tracked event |

All setters return the fake, for chaining.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.FeatureManagement.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.FeatureManagement.Testing/SharedKernel.FeatureManagement.Testing.Tests)
and prove the fake against the real client's contract (defaults, type mismatches, merged contexts, registration).
Pair it with [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
for `TestRequestContext` and `FakeClock`. Rollout, targeting and time-window configuration is tested against
[`SharedKernel.FeatureManagement`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/SharedKernel.FeatureManagement/README.md)
itself.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | The architecture tests fail a production reference |
| Test a percentage rollout or time window with the fake | Test it against the real `AddSharedKernelFeatureManagement` registration | The fake is a value map, not a rules engine |
| Rely on OpenFeature hooks or provider events in a test | Test hooks against a real OpenFeature client | The fake stores hooks but never runs them, and raises no events |
| Call `Set` for an object flag | Call `SetObject` with the flag's `JsonTypeInfo<T>` | `Set` throws `ArgumentException` for object flags |
| Expect `Reset()` to clear the client context | Call `SetContext(EvaluationContext.Empty)` too | `Reset()` clears values, rules and history only |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
