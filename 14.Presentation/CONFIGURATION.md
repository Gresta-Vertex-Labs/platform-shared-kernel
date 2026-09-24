# 14.Presentation — Configuration Reference

Every setting of the four packages, read from the options classes and their validators. Usage is in each package's
`README.md`; the rules for maintainers are in [`CLAUDE.md`](CLAUDE.md).

| Section | Options class | Registered by |
| --- | --- | --- |
| [`SharedKernel:Presentation:WebApi`](#sharedkernelpresentationwebapi) | `SharedKernel.Presentation.WebApi.Options.SharedKernelWebApiOptions` | `builder.AddSharedKernelWebApi(configure)` |
| [`SharedKernel:Presentation:OpenApi`](#sharedkernelpresentationopenapi) | `SharedKernel.Presentation.OpenApi.Options.SharedKernelOpenApiOptions` | `builder.AddSharedKernelOpenApi(configure)` |
| [`SharedKernel:Presentation:SignalR`](#sharedkernelpresentationsignalr) | `SharedKernel.Presentation.SignalR.Options.SharedKernelSignalROptions` | `builder.AddSharedKernelSignalR(configure)` |
| [`SharedKernel:Presentation:Grpc`](#sharedkernelpresentationgrpc) | `SharedKernel.Presentation.Grpc.Options.SharedKernelGrpcOptions` | `builder.AddSharedKernelGrpc(configure)` |

How every section behaves:

- Each options class names its section (`ISectionBoundOptions.SectionName`) and is registered with
  `AddValidatedOptions`: bound from configuration, validated, and checked when the host starts (`ValidateOnStart`).
- The `configure` callback of the `Add…` method runs after binding, so code overrides configuration. Calling the
  `Add…` method again applies each callback.
- A list with defaults (`Cors:ExposedHeaders`, `Problems:PreconditionFailedErrorCodes`) keeps them: configured values
  are added to the defaults. Clear the list in the `configure` callback to replace them.
- `TimeSpan` values use the `[d.]hh:mm:ss` form, such as `"00:00:30"`. A JSON `null` sets a nullable setting to
  `null`.
- Environment variables use `__` for `:`, such as `SharedKernel__Presentation__WebApi__Cors__AllowedOrigins__0`.
- Invalid settings throw `OptionsValidationException`, whose message names each key and the fix. For WebApi the
  exception comes from the first read: with Kestrel at `builder.Build()` (the server reads the limits), with `TestServer`
  at `UseSharedKernelWebApi()`, and at the latest when the host starts. OpenApi also reads its settings in
  `MapSharedKernelOpenApi()`.

---

## `SharedKernel:Presentation:WebApi`

`SharedKernelWebApiOptions`. Every default is the secure choice: correlation ids, security headers, a 4 MiB body
limit, no cross-origin access, uncached responses and redacted server errors.

```json
{
  "SharedKernel": {
    "Presentation": {
      "WebApi": {
        "RemoveServerHeader": true,
        "TrustInboundBaggage": false,
        "CorrelationId": {
          "Enabled": true,
          "MaxLength": 128,
          "AllowedCharacterPattern": "^[A-Za-z0-9\\-_:.]+$"
        },
        "Cors": {
          "AllowedOrigins": [ "https://app.example.com" ],
          "AllowedMethods": [],
          "AllowedHeaders": [],
          "ExposedHeaders": [],
          "AllowCredentials": false,
          "PreflightMaxAge": "00:10:00"
        },
        "SecurityHeaders": {
          "Enabled": true,
          "Hsts": true,
          "HstsMaxAge": "365.00:00:00",
          "HstsIncludeSubDomains": true,
          "HstsPreload": false,
          "ContentTypeOptions": "nosniff",
          "FrameOptions": "DENY",
          "ReferrerPolicy": "no-referrer",
          "PermissionsPolicy": "geolocation=(), microphone=(), camera=()",
          "ContentSecurityPolicy": "default-src 'none'; frame-ancestors 'none'",
          "CacheControl": "no-store"
        },
        "Limits": {
          "MaxRequestBodySize": 4194304,
          "MaxJsonDepth": null
        },
        "Problems": {
          "TypeBaseUri": null,
          "IncludeExceptionDetails": null,
          "UnavailableRetryAfter": "00:00:30",
          "PreconditionFailedErrorCodes": []
        }
      }
    }
  }
}
```

The example shows the defaults, except `Cors:AllowedOrigins` and `Problems:UnavailableRetryAfter`. The empty
`ExposedHeaders` and `PreconditionFailedErrorCodes` add nothing to their defaults, which stay in place.

### Top level

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `RemoveServerHeader` | `bool` | `true` | — | Kestrel sends no `Server` header |
| `TrustInboundBaggage` | `bool` | `false` | — | `false`: hosting reads no W3C `baggage` from the request and the first middleware removes any inbound item from the request `Activity`. `true` keeps the caller's baggage; set it only behind a gateway that removes caller-supplied baggage |

### `CorrelationId`

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | — | Resolve the correlation id and write it to the `X-Correlation-Id` response header, the problem's `correlationId` and `Activity` baggage (`correlation.id`) |
| `MaxLength` | `int` | `128` | 1 to 1024 | The longest inbound id accepted; a longer one is replaced |
| `AllowedCharacterPattern` | `string` (regular expression) | `^[A-Za-z0-9\-_:.]+$` (`WebApiCorrelationIdOptions.DefaultAllowedCharacterPattern`) | Required; a valid regular expression | An inbound id must match it. A custom pattern is compiled once and evaluated with a 100 ms timeout; a timeout rejects the id |

An inbound id is also rejected when it contains a control character or the header has several values. A rejected or
missing id is replaced by the trace id, or a new GUID when the request is not traced.

### `Cors`

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `AllowedOrigins` | `string[]` | empty | No empty entry; never `null` (the origin); see `AllowCredentials` | The origins allowed; `*` allows any. Empty: no CORS policy exists and no cross-origin request is allowed |
| `AllowedMethods` | `string[]` | empty | — | Allowed methods; empty allows any |
| `AllowedHeaders` | `string[]` | empty | — | Allowed request headers; empty allows any |
| `ExposedHeaders` | `string[]` | `X-Correlation-Id`, `ETag`, `Location`, `Retry-After`, `Sunset`, `Deprecation`, `Link`, `api-supported-versions`, `api-deprecated-versions` | — | Response headers browser scripts may read; configured values are added to the defaults |
| `AllowCredentials` | `bool` | `false` | Requires explicit origins (no empty list, no `*`); outside Development, no `http://` origin | Browsers may send cookies and HTTP authentication |
| `PreflightMaxAge` | `TimeSpan` | `00:10:00` | Not negative | How long a browser caches a preflight response |

The two `AllowCredentials` failures and the `null` origin are also logged at Critical (EventId 14004). When origins
are configured, a WebSocket request from an origin the policy does not allow is refused with 403
`forbidden.origin_not_allowed`.

### `SecurityHeaders`

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | — | `false` writes none of the headers below, HSTS and the default `Cache-Control` included |
| `Hsts` | `bool` | `true` | — | Send `Strict-Transport-Security`: over HTTPS only, never to `localhost`, never in Development |
| `HstsMaxAge` | `TimeSpan` | `365.00:00:00` | Not negative | HSTS `max-age` |
| `HstsIncludeSubDomains` | `bool` | `true` | — | HSTS `includeSubDomains` |
| `HstsPreload` | `bool` | `false` | — | HSTS `preload`; hard to undo, so opt in deliberately |
| `ContentTypeOptions` | `string?` | `nosniff` | — | `X-Content-Type-Options` |
| `FrameOptions` | `string?` | `DENY` | — | `X-Frame-Options` |
| `ReferrerPolicy` | `string?` | `no-referrer` | — | `Referrer-Policy` |
| `PermissionsPolicy` | `string?` | `geolocation=(), microphone=(), camera=()` | — | `Permissions-Policy` |
| `ContentSecurityPolicy` | `string?` | `default-src 'none'; frame-ancestors 'none'` | — | `Content-Security-Policy`; an endpoint replaces it with `WithContentSecurityPolicy(policy)` |
| `CacheControl` | `string?` | `no-store` | — | `Cache-Control` on a response that sets neither `Cache-Control` nor `ETag`. Error responses are always `no-store`, whatever this is |

A `null` or empty value turns that header off. A header the endpoint set itself is never overwritten.

### `Limits`

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `MaxRequestBodySize` | `long?` (bytes) | `4194304` (4 MiB) | Greater than 0, or `null` | Kestrel's request body limit; a larger body is answered 413 `request.too_large`. `null` keeps Kestrel's default (30,000,000 bytes). `TestServer` enforces no limit. Endpoints change it with `WithRequestSizeLimit(bytes)`, `DisableRequestSizeLimit()` or `[RequestSizeLimit]` |
| `MaxJsonDepth` | `int?` | `null` | At least 1, or `null` | System.Text.Json's `MaxDepth` for minimal APIs and MVC. It limits responses too. `null` keeps the framework's limit (64) |

### `Problems`

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `TypeBaseUri` | `Uri?` | `null` | Absolute; no query or fragment | Every problem's `type` becomes this URI followed by its `errorCode`, such as `https://errors.example.com/order.not_found`. A missing trailing `/` is added. `null`: `type` is the RFC section of the status |
| `IncludeExceptionDetails` | `bool?` | `null` | — | Include the `exception` member (type, message, stack trace) of an unhandled exception. `null` means Development only; `true` outside Development logs a warning at startup (EventId 14012) |
| `UnavailableRetryAfter` | `TimeSpan?` | `null` | Not negative | `Retry-After` on a 503, in whole seconds rounded up; `null` sends none |
| `PreconditionFailedErrorCodes` | `string[]` | `persistence.concurrency_conflict`, `storage.precondition_failed`, `storage.already_exists` | No empty entry | A `Conflict` with one of these codes, in a request carrying `If-Match` or `If-None-Match`, is answered 412 with its code; every other conflict stays 409. Codes compare ordinally; configured values are added to the defaults |

The three default codes are owned by 06.Persistence (`ConcurrencyVersion.ConflictErrorCode`) and 08.Storage
(`StorageErrorCodes`); a 00.Governance test pins the literals to those constants.

### Constants

| Constant | Value | Use |
| --- | --- | --- |
| `SharedKernelWebApiOptions.SectionName` | `SharedKernel:Presentation:WebApi` | The section path |
| `WebApiCorrelationIdOptions.DefaultAllowedCharacterPattern` | `^[A-Za-z0-9\-_:.]+$` | The default correlation id pattern |
| `IdempotencyKey.MaxLength` | `256` | The longest `Idempotency-Key`, without the pair of double quotes a client may add; not a setting |

---

## `SharedKernel:Presentation:OpenApi`

`SharedKernelOpenApiOptions`. The defaults document a service authenticated with bearer tokens, and serve the
documents in the Development environment only.

```json
{
  "SharedKernel": {
    "Presentation": {
      "OpenApi": {
        "Title": "Orders API",
        "Description": "Places and tracks orders.",
        "Bearer": true,
        "ApiKeyHeaderName": "X-Api-Key",
        "MutualTls": false,
        "ExposeInProduction": false
      }
    }
  }
}
```

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `Title` | `string?` | The application name (`IHostEnvironment.ApplicationName`) | — | Title of every document and of the Scalar reference page |
| `Description` | `string?` | `null` | — | Markdown description of every document; a version's deprecation and sunset notices are appended in that version's document |
| `Versioning` | `Action<ApiVersioningOptions>?` | `null` | Code only; configuration cannot set it | Runs after the platform defaults (version 1.0, assumed when a request names none; URL segment, then `X-Api-Version` header; versions reported). Declares sunset and deprecation policies; needs `using Asp.Versioning;` |
| `Bearer` | `bool` | `true` | — | Declare the HTTP bearer (JWT) security scheme |
| `ApiKeyHeaderName` | `string?` | `null` | An HTTP header name (an RFC 9110 token: letters, digits and the symbols it allows) or empty | Declare an API key security scheme in this header; `null` or empty declares none |
| `MutualTls` | `bool` | `false` | — | Declare the mutual TLS security scheme (OpenAPI 3.1) |
| `ExposeInProduction` | `bool` | `false` | — | Serve the documents and the reference outside Development. When they are served there without an authorization convention, `AllowAnonymous()` or a fallback policy, the host logs a warning at startup (EventId 14301) |

`SharedKernelOpenApiOptions.SectionName` is `SharedKernel:Presentation:OpenApi`.

---

## `SharedKernel:Presentation:SignalR`

`SharedKernelSignalROptions`. SignalR's own settings (message size, keep-alive, timeouts) stay on `HubOptions`, with the
framework's defaults.

```json
{
  "SharedKernel": {
    "Presentation": {
      "SignalR": {
        "InvocationRateLimit": {
          "PermitLimit": 20,
          "Window": "00:00:10"
        }
      }
    }
  }
}
```

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `InvocationRateLimit:PermitLimit` | `int?` | `null` (off) | At least 1, or `null` | Hub method invocations one connection may make per `Window`, which is also the largest burst. A refused invocation fails with `rate_limit.exceeded: Too many requests.` |
| `InvocationRateLimit:Window` | `TimeSpan` | `00:00:01` | Greater than zero | The period over which `PermitLimit` invocations are allowed |

`SharedKernelSignalROptions.SectionName` is `SharedKernel:Presentation:SignalR`.

---

## `SharedKernel:Presentation:Grpc`

`SharedKernelGrpcOptions`.

```json
{
  "SharedKernel": {
    "Presentation": {
      "Grpc": {
        "ErrorDomain": "orders.example.com"
      }
    }
  }
}
```

| Key | Type | Default | Validation | Effect |
| --- | --- | --- | --- | --- |
| `ErrorDomain` | `string` | The application name | Required: a blank value falls back to the application name, and fails only when that is blank too | The `domain` of the `google.rpc.ErrorInfo` every error status carries: the logical owner of this service's error codes. Keep it stable once clients depend on it |

`SharedKernelGrpcOptions.SectionName` is `SharedKernel:Presentation:Grpc`. gRPC's own settings (message sizes,
compression) stay on `GrpcServiceOptions`.

---

## Not settings

These are code, per endpoint or per host, and have no configuration key:

| What | Where |
| --- | --- |
| Middleware inside the WebApi pipeline | `UseSharedKernelWebApi(pipeline => pipeline.AtStart(…).BeforeAuthentication(…).BeforeAuthorization(…))` |
| Authorization requirements | `[RequirePermission]`, `[RequireRole]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]` and their conventions |
| Required or accepted headers | `RequireIdempotencyKey()`, `AcceptIdempotencyKey()`, `RequireIfMatch()`, `AcceptIfMatch()`, their attributes, the `IdempotencyKey` and `IfMatch<TVersion>` parameters |
| A body limit or CSP of one endpoint | `WithRequestSizeLimit(bytes)`, `DisableRequestSizeLimit()`, `WithContentSecurityPolicy(policy)` |
| Rate limiting policies | ASP.NET Core `AddRateLimiter()`, or 13.ServiceDefaults' `AddSharedKernelRateLimiting()` |
| Request localization | `UseRequestLocalization()` in the `BeforeAuthorization` hook, and an `ILocalizationCatalog` |

The pre-P-562 settings (`CorrelationIdOptions`, `CorsPolicyOptions`, `SecurityHeadersOptions`, `PayloadLimitsOptions`,
`UploadValidationOptions`, `ApiVersionLifecycleOptions`, `OpenApiSecuritySchemesOptions`, `HubInvocationRateLimitOptions`,
`WebApiOptions`) no longer exist; the constant `HttpContextIdempotencyExtensions.MaxIdempotencyKeyLength` is now
`IdempotencyKey.MaxLength`.
