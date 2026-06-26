# `WebhookDeliveryOptions` configuration reference

**Configuration section path:** `SharedKernel:Integration:Webhooks`

Bound and validated via `AddSharedKernelWebhooks()`, which calls `SharedKernel.Configuration`'s
`.Bind(section).ValidateDataAnnotations().ValidateOnStart()` pipeline under the hood. Validation
runs eagerly at application startup — a misconfigured deployment fails immediately with an
actionable `OptionsValidationException`, not on first dispatch.

```jsonc
// appsettings.json
{
  "SharedKernel": {
    "Integration": {
      "Webhooks": {
        "MaxAttempts": 5,
        "BaseBackoffDelay": "00:00:02",
        "MaxBackoffDelay": "00:01:00",
        "RequestTimeout": "00:00:10",
        "SignatureTolerance": "00:05:00",
        "MaxConcurrentDeliveries": 8
      }
    }
  }
}
```

`TimeSpan` values bind from the standard `[d.]hh:mm:ss[.fffffff]` configuration format shown above.

---

## Properties

| Property | Type | Default | Validation bound |
| --- | --- | --- | --- |
| `MaxAttempts` | `int` | `5` | `[Range(1, int.MaxValue)]` — must be at least 1 |
| `BaseBackoffDelay` | `TimeSpan` | `00:00:02` (2 seconds) | Must be `> TimeSpan.Zero` (via `IValidatableObject`) |
| `MaxBackoffDelay` | `TimeSpan` | `00:01:00` (60 seconds) | Must be `> TimeSpan.Zero` **and** `>= BaseBackoffDelay` (via `IValidatableObject`) |
| `RequestTimeout` | `TimeSpan` | `00:00:10` (10 seconds) | Must be `> TimeSpan.Zero` (via `IValidatableObject`) |
| `SignatureTolerance` | `TimeSpan` | `00:05:00` (5 minutes) | Must be `> TimeSpan.Zero` (via `IValidatableObject`) |
| `MaxConcurrentDeliveries` | `int` | `8` | `[Range(1, int.MaxValue)]` — must be at least 1 |

### `MaxAttempts`

The maximum number of HTTP attempts the resilience pipeline makes per delivery before the
subscription's delivery is considered exhausted. Once exhausted without a 2xx response, the
dispatcher publishes exactly one `WebhookDeliveryExhaustedEvent` via `IEventPublisher` and returns a
`WebhookDeliveryResult` with `IsSuccess == false`.

### `BaseBackoffDelay`

The initial delay before the first retry, fed into `Microsoft.Extensions.Http.Resilience`'s standard
resilience handler. Retry/backoff is configured once on the named `HttpClient` — never a hand-rolled
retry loop inside `WebhookDispatcher`.

### `MaxBackoffDelay`

The ceiling on the exponential backoff delay between retries. Must be greater than or equal to
`BaseBackoffDelay` — a `MaxBackoffDelay` smaller than `BaseBackoffDelay` is rejected at startup with
a validation message naming both properties and their values.

### `RequestTimeout`

The per-attempt HTTP request timeout. Each individual attempt — not the overall delivery across all
retries — is bounded by this value.

### `SignatureTolerance`

The maximum allowed clock skew between a webhook's signed timestamp and the verifier's current time,
used as the default `tolerance` argument when `WebhookSignatureVerifier.Verify` is called without an
explicit override. This is the only sanctioned source of the verification tolerance window — do not
hardcode a different value at a call site.

### `MaxConcurrentDeliveries`

The upper bound on simultaneous in-flight deliveries within a single `IWebhookDispatcher.DispatchAsync`
fan-out, enforced via a bounded semaphore gate. Protects against unbounded `Task.WhenAll` over an
arbitrarily large subscription list — a hard requirement of this package's design, not a tunable
safety net that can be disabled.

---

## Validation failure examples

Each of the following fails `ValidateOnStart()` with a message identifying the offending property:

| Invalid configuration | Resulting validation failure |
| --- | --- |
| `MaxAttempts: 0` | `RangeAttribute` failure — `MaxAttempts` must be between 1 and `int.MaxValue` |
| `MaxConcurrentDeliveries: 0` | `RangeAttribute` failure — `MaxConcurrentDeliveries` must be between 1 and `int.MaxValue` |
| `BaseBackoffDelay: "00:00:00"` | `IValidatableObject` failure — `BaseBackoffDelay` must be greater than zero |
| `MaxBackoffDelay: "00:00:00"` | `IValidatableObject` failure — `MaxBackoffDelay` must be greater than zero |
| `RequestTimeout: "00:00:00"` | `IValidatableObject` failure — `RequestTimeout` must be greater than zero |
| `SignatureTolerance: "00:00:00"` | `IValidatableObject` failure — `SignatureTolerance` must be greater than zero |
| `BaseBackoffDelay: "00:00:30"`, `MaxBackoffDelay: "00:00:10"` | `IValidatableObject` failure — `MaxBackoffDelay (00:00:10)` must be greater than or equal to `BaseBackoffDelay (00:00:30)` |
