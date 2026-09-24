# SharedKernel.Analyzers

44 Roslyn analyzer rules that enforce the Platform.SharedKernel conventions **at compile time**, inside your
own build, so they hold without depending on a reviewer noticing.

Each rule encodes a decision that is otherwise unenforceable prose: inject `IClock` rather than reading
the clock directly, never discard a `Result`, never combine CORS credentials with a wildcard origin,
never let PII reach a log unmasked. Every diagnostic names the fix, so a violation explains itself where
it appears.

**Compiler-only.** This package contributes no runtime assembly and no API surface — nothing to call,
nothing to wire up, and no dependency added to your own package graph.

## Install

```xml
<PackageReference Include="SharedKernel.Analyzers" Version="*" PrivateAssets="all" />
```

That is the entire setup. The rules run on your next build.

`PrivateAssets="all"` keeps the analyzers out of your own package's dependency graph so they are not
imposed on your consumers. The package is marked as a development dependency, so NuGet applies this for
you on install; it is written out above to make the intent explicit and to survive a hand-edited csproj.

Analyzers are **not inherited transitively**. Referencing another SharedKernel package does not bring
these rules with it — every project that wants them declares the reference itself. Put it in your
service template, or in a shared `Directory.Build.props` so a whole solution picks it up once.

## What a violation looks like

```csharp
public sealed class OrderService
{
    public void Place(Order order)
    {
        var now = DateTime.UtcNow;   // SK0001
        _repository.Save(order);     // SK0030, when Save returns Result
    }
}
```

```text
warning SK0001: Direct access to 'DateTime.UtcNow' is not allowed — inject IClock via DI instead
warning SK0030: This Result's outcome is never checked — a failure will pass silently. Assign it,
                branch on it, return it, pass it as an argument, or explicitly discard it with '_ = ...'.
```

## Choosing severities

Every rule ships at **Warning**. Nothing here fails your build until you ask it to. Set severities in
`.editorconfig`:

```ini
[*.cs]
# Fail the build on the rules you consider non-negotiable
dotnet_diagnostic.SK0030.severity = error
dotnet_diagnostic.SK0032.severity = error
dotnet_diagnostic.SK0035.severity = error

# Or switch off one that does not apply to this service
dotnet_diagnostic.SK0024.severity = none
```

Severity can be set per category too, which is a good way to adopt the security rules first:

```ini
[*.cs]
dotnet_analyzer_diagnostic.category-Security.severity = error
```

Scope a relaxation to part of the tree with a narrower section:

```ini
[tests/**/*.cs]
dotnet_diagnostic.SK0001.severity = none
```

## Suppressing one occurrence

When a rule is genuinely wrong for a single call site, suppress it narrowly **and say why** — an
unexplained suppression is indistinguishable from an oversight:

```csharp
#pragma warning disable SK0202 // Cross-tenant purge, approved by infra-team, ticket INF-4421
var all = context.Orders.IgnoreQueryFilters().ToList();
#pragma warning restore SK0202
```

Or at member or type level:

```csharp
[SuppressMessage("Usage", "SK0001:Direct DateTime usage",
    Justification = "Benchmark harness measures wall-clock time deliberately.")]
public void Benchmark() { }
```

Prefer either of these over switching a rule off globally. A per-site suppression records a decision;
`severity = none` erases the rule.

## Categories

| Category | Meaning |
|---|---|
| `Usage` | An API is being used in a way the platform does not support. |
| `Design` | The shape of a type or a DI registration is wrong. |
| `Security` | A real security consequence — `SK0032` and `SK0035`. Good candidates to raise to `error` first. |
| `Advisory` | A heuristic nudge toward a better pattern, not a prohibition. `SK0034` only, and it never escalates to `error`. |

## Rules

Rule IDs link to full documentation: rationale, a violating example, a compliant fix, and the
suppression guidance for that specific rule.

### Core standards

Primitives, domain modelling and error handling.

| Rule | Flags | Do this instead |
|---|---|---|
| [`SK0001`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0001-directdatetimeusage) | `DateTime` or `DateTimeOffset` `.Now`/`.UtcNow` accessed directly | Inject `IClock` |
| [`SK0002`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0002-directmicrosoftfeaturemanagerusage) | A Microsoft feature-management evaluator interface, or `OpenFeature.Api.Instance`, referenced directly | Inject `OpenFeature.IFeatureClient` and evaluate a `SharedKernel.FeatureManagement.FeatureFlag<T>` |
| [`SK0003`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0003-rawexceptionthrow) | `throw new Exception(...)` or `ApplicationException` | Return `Result<T>.Failure(error)`, or throw a typed SharedKernel exception carrying an `Error` |
| [`SK0004`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0004-nullerrorreturn) | `null` returned where an `Error` is expected | Return `Error.None` |
| [`SK0005`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0005-stringonlyexceptionconstructor) | A `SharedKernelException` subclass constructed from a string only | Pass an `Error` payload |
| [`SK0006`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0006-guardclausethrow) | An `IGuardClause` functional-path method that throws | Return `Error?`, or move the throw to the `Guard.Throw` companion |
| [`SK0007`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0007-redischannelservicemessagingsubstitute) | `IRedisChannelService` injected into a messaging-context class | Inject `IMessageBus` for durable delivery |
| [`SK0008`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0008-aggregaterootdispatchcoupling) | Dispatch code taking `IAggregateRoot` as a constructor parameter | Inject `IHasDomainEvents` for narrower coupling |
| [`SK0009`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0009-domaineventmissingversionattribute) | An `IDomainEvent` type with no `[DomainEventVersion]` | Add `[DomainEventVersion(N)]` |
| [`SK0010`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0010-specificationorderingconflict) | A specification constructor calling both `ApplyOrderBy` and `ApplyOrderByDescending` | Pick one primary ordering; add secondary sorts via `ThenBy`/`ThenByDescending` |
| [`SK0011`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0011-guidformatcodemisuse) | `Guid.ToString` with a non-canonical format code | `ToString()` or `ToString("D")` |

### Application and communication

CQRS pipeline wiring and outbound HTTP.

| Rule | Flags | Do this instead |
|---|---|---|
| [`SK0013`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0013-rawhttpclientconstructorinjection) | `HttpClient` injected directly into a constructor | Register a typed client via `AddRestClient<TClient>()` |
| [`SK0014`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0014-closedgenericresiliencepipelineregistration) | A closed-generic `ResiliencePipeline<T>` registration | Use the non-generic, string-keyed `ResiliencePipeline` |
| [`SK0015`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0015-streampipelinebehaviormisregistration) | `IStreamPipelineBehavior<,>` registered against `IPipelineBehavior<,>` | Register it against `IStreamPipelineBehavior<,>` |
| [`SK0016`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0016-requesttypeshortnameusage) | `typeof(X).Name` used as a metric tag, log scope key or cache key | `typeof(X).FullName ?? typeof(X).Name` |
| [`SK0017`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0017-commandimplementscacheablequery) | A command implementing `ICacheableQuery<TResponse>` | Remove it — caching is queries-only |
| [`SK0018`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0018-queryimplementsinvalidatescache) | A query implementing `IInvalidatesCache` | Remove it — invalidation is commands-only |
| [`SK0040`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0040-pipelinemarkerresponseshapemismatch) | `[RequirePermission]`/`IIdempotentRequest` on a request whose MediatR response isn't `Result`/`Result<T>` | Declare the response as `Result`/`Result<T>` |

### Logging authoring

How every log statement must be written.

| Rule | Flags | Do this instead |
|---|---|---|
| [`SK0020`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0020-directiloggerextensionmethodusage) | A direct `ILogger` extension-method call | Author the statement with `[LoggerMessage]` |
| [`SK0021`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0021-handwrittenloggermessagedefinedelegate) | A hand-written `LoggerMessage.Define` delegate | Author the statement with `[LoggerMessage]` |

### Cross-cutting

Named constants, provider clients, security and data privacy.

| Rule | Flags | Do this instead |
|---|---|---|
| [`SK0022`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0022-crosscuttingmagicstringliteral) | A raw literal at a cross-cutting call site — HTTP header, OTel baggage/tag key, config section, claim type | Reference `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys`, or a domain-local constant |
| [`SK0023`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0023-nonsingletonamazons3clientregistration) | `IAmazonS3` registered as Scoped or Transient | `AddSingleton` |
| [`SK0024`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0024-rawsearchfieldnameliteral) | A raw literal supplied as a search field name | `nameof(TDocument.Property)` or a field-constants class |
| [`SK0025`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0025-obsoleteelasticsearchclientusage) | The deprecated NEST / `Elasticsearch.Net` client | `Elastic.Clients.Elasticsearch` |
| [`SK0026`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0026-rawintelligenceproviderclientconstructorinjection) | A raw vector-DB or model-SDK client injected | Inject the `SharedKernel.AI.Abstractions` contracts |
| [`SK0027`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0027-rawintelligenceidentifierliteral) | A raw literal used as a collection name, field name or embedding-model id | `nameof(...)` or a domain-local constant |
| [`SK0028`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0028-nondeterministicapiusageinsideworkflow) | A non-deterministic or side-effecting API inside a `[Workflow]` type | Use the deterministic `Workflow.*` equivalents |
| [`SK0029`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0029-rawtemporalclientconstructorinjection) | A raw Temporal client type injected | Inject `IWorkflowDispatcher` or `IWorkflowHandle` |
| [`SK0030`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0030-resultoutcomediscarded) | A `Result`/`Result<T>` produced as a bare statement and never inspected | Branch on it, return it, pass it as an argument, or discard explicitly with `_ =` |
| [`SK0031`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0031-rawsecuritycontextconstructorinjection) | A raw security-context type injected | Inject `IUserContext` (identity) or `ITenantProvider` (tenant) |
| [`SK0032`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0032-corswildcardoriginwithcredentials) | `AllowCredentials()` combined with a wildcard or always-allow origin | Name explicit origins |
| [`SK0033`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0033-reflectionbasedobjectmapperusage) | AutoMapper's reflection-based API, or Mapster's runtime adapter | A `Riok.Mapperly` `[Mapper]` partial class |
| [`SK0034`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0034-amountcurrencypaircoupling) | A `decimal` amount member paired with a `string` currency-code member | Consider `SharedKernel.Domain.Money` — advisory only |
| [`SK0035`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0035-unmaskedclassifieddataatloggingcallsite) | Classified or PII data passed unmasked to a `[LoggerMessage]` parameter | Classify the parameter so log redaction masks it, or mask it with `PiiMasking` |
| [`SK0036`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0036-rawrpcexceptionconstruction) | `RpcException`/`Status` constructed outside the gRPC presentation layer | Return a `Result`; end it with `ThrowIfFailure()` / `GetValueOrThrow()` from `SharedKernel.Core.Extensions` |
| [`SK0037`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0037-valueobjectmissingensurevalid) | A `ValueObject` subclass whose constructor completes without calling `EnsureValid()` | Call `EnsureValid()` as the last statement of every constructor |
| [`SK0038`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0038-integrationeventmissingattribute) | A non-abstract `IIntegrationEvent` type with no `[IntegrationEvent]` attribute | Add `[IntegrationEvent("context.event-name", Version = N)]` |
| [`SK0039`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0039-invalidintegrationeventattribute) | An `[IntegrationEvent]` literal name that breaks the name rule, or a `Version` literal below 1 | Lowercase segments separated by `.`, `-` or `_`, such as `orders.order-placed`; versions start at 1 |

### Persistence

Multi-tenant EF Core safety.

| Rule | Flags | Do this instead |
|---|---|---|
| [`SK0201`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0201-tenanteddbcontextonmodelcreatingguard) | A `TenantedDbContext.OnModelCreating` override that does not call `base.OnModelCreating` | Call it first, or the platform model configuration is silently skipped |
| [`SK0202`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0202-ignorequeryfiltersoutsidetenantedrepository) | `IgnoreQueryFilters()` called outside permitted scope | Keep it inside `SharedKernel.Persistence.EfCore` or a `TenantedRepository` |

### Messaging

MassTransit registration correctness.

| Rule | Flags | Do this instead |
|---|---|---|
| [`SK0703`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0703-messagebussingletonregistration) | `IMessageBus` or `IEventPublisher` registered as Singleton | `AddScoped` |
| [`SK0704`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0704-hardcodedqueueuriingetsendendpoint) | A hardcoded queue or exchange URI passed to `GetSendEndpoint` | Convention-based resolution via `IEndpointNameFormatter` |
| [`SK0705`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0705-faultconsumerdirectregistration) | `IFaultConsumer<TMessage>` registered directly | `MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>()` |
| [`SK0708`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md#sk0708-batchconsumerregisteredviaaddconsumer) | A batch consumer registered via `AddConsumer<T>()` | `MessagingBusBuilder.AddBatchConsumer<...>()` |

Numbering gaps — `SK0012`, `SK0301`–`SK0303`, `SK0701`–`SK0702`, `SK0706`–`SK0707` — are architecture
rules enforced by `SharedKernel.ArchitectureTests` from a test project rather than by the compiler. They
are not part of this package.

## Which of these will I actually see?

Most rules only fire on code that already uses the thing they govern. A service with no MassTransit bus
never sees `SK0703`–`SK0708`; one with no Temporal workflows never sees `SK0028`/`SK0029`. Adopting the
full set on a small service is normal, and quiet.

The rules that apply to essentially any C# project are `SK0001` (clock access), `SK0003`–`SK0005`
(exception and error shape), `SK0011` (GUID formatting), `SK0016` (type-name collisions), `SK0020` and
`SK0021` (log authoring), `SK0022` (magic strings), `SK0030` (discarded results) and `SK0033`
(reflection-based mappers).

## Requirements

| | |
|---|---|
| Analyzer target framework | `netstandard2.0`, as required by the Roslyn compiler host |
| Built against | `Microsoft.CodeAnalysis.CSharp` 4.14.0 |
| Your project | Any target framework; needs a compiler supporting Roslyn 4.14 or later |
| Verified on | .NET SDK 10.0.300 |

## Full rule documentation

[00.Governance usage guide](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/README.md)
covers every rule in depth, alongside the companion `SharedKernel.ArchitectureTests` layering rules and
`SharedKernel.Linter` formatting configuration.

## License

MIT.
