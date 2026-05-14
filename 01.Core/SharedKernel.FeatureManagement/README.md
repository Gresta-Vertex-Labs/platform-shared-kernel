# SharedKernel.FeatureManagement

Feature flag abstraction for the Platform.SharedKernel ecosystem. Wraps `Microsoft.FeatureManagement` behind a stable `IFeatureManager` interface so consuming services never take a direct dependency on the third-party package. Depends on `SharedKernel.Primitives`.

## Included Types

- `IFeatureManager` — the only feature-flag interface consuming services should inject
- `FeatureDefinition` — sealed record describing a named flag with a default value
- `MicrosoftFeatureManagerAdapter` — internal adapter backing `IFeatureManager` with `Microsoft.FeatureManagement`

## Quick Start

```csharp
// Register (Program.cs)
builder.Services.AddSharedKernelFeatureManagement(builder.Configuration);

// appsettings.json
{
  "FeatureManagement": {
    "NewCheckout": true,
    "BetaDashboard": false
  }
}

// Consume
public class CheckoutService(IFeatureManager features)
{
    public async Task<bool> IsNewCheckoutEnabled(CancellationToken ct) =>
        await features.IsEnabledAsync("NewCheckout", ct);
}
```

Swap the adapter for any AOT-safe implementation without touching consumers.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
