# SharedKernel.FeatureManagement.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**`FakeFeatureClient` is an OpenFeature `IFeatureClient` whose flags a test sets directly.** Code built on
`SharedKernel.FeatureManagement` evaluates flags through `IFeatureClient` and the typed `FeatureFlag<T>`
extensions (`IsEnabledAsync`, `GetValueAsync`, `GetDetailsAsync`); the fake answers those calls without
configuration, targeting or `Microsoft.FeatureManagement`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.FeatureManagement.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.FeatureManagement`.

## Contents

| Member | What it does |
| --- | --- |
| `SetEnabled(flag, enabled = true)` | Sets a boolean flag |
| `Set(flag, value)` | Sets a fixed value for a typed flag |
| `Set(flag, context => value)` | Answers per evaluation context — for per-tenant or per-user behaviour |
| `SetObject(flag, value, typeInfo)` | Sets an object flag, serialized with the given `JsonTypeInfo<T>` |
| `WasEvaluated(flag)`, `EvaluatedFlags` | What the code under test actually read |
| `TrackedEvents` | Every `Track(...)` call |
| `Reset()` | Clears flags and history |

An unset flag returns its declared default with reason `FlagNotFound`, exactly as the production client does for a
flag missing from configuration, so a test never passes only because the fake invented a value.

## Registration

```csharp
services.AddFakeFeatureFlags(flags => flags.SetEnabled(Flags.NewCheckout));
```

Registers one `FakeFeatureClient` as both itself and `IFeatureClient` (singletons). Registered after the service's
own `AddSharedKernelFeatureManagement(...)`, it is the client resolved.

## Example

```csharp
var flags = new FakeFeatureClient()
    .Set(Flags.MaxBasketSize, 5)
    .Set(Flags.PremiumShipping, context => context.TargetingKey == premiumUserId);
var basket = new BasketService(flags);

var result = await basket.AddAsync(item, ct);

result.IsFailure.Should().BeTrue();          // six items exceed the flag's limit
flags.WasEvaluated(Flags.MaxBasketSize).Should().BeTrue();
```

## Related packages

- References `SharedKernel.FeatureManagement` (and through it OpenFeature).
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) supplies the shared basics (`FakeClock`,
  `InMemoryLogger`, `TestRequestContext`, fakers and assertions).
