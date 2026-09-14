# SharedKernel.Core

Core building blocks for .NET services built on [`SharedKernel.Primitives`](../SharedKernel.Primitives/README.md):

- **Railway extensions** for `Result` and `Result<T>`: `Map`, `Bind`, `Ensure`, `Tap`, `TapError`, `Match`, with `Task` and `ValueTask` overloads.
- **Exception boundaries** (`ResultTry`) that turn a throwing call into a failed result without leaking exception text.
- **Result aggregation** (`ResultCombine`) that collects every failure instead of stopping at the first.
- **Guard clauses** on two paths: `Guard.Against.*` returns an `Error`, and `Guard.Throw.*` throws.
- **A base exception hierarchy** in which every exception carries a structured `Error`.

It has no third-party dependencies. Its only dependency is `SharedKernel.Primitives`.

```shell
dotnet add package SharedKernel.Core
```

## Guard clauses

```csharp
using SharedKernel.Guards;
```

That one `using` brings in every guard. Guards on both paths share the same names and parameters. The parameter name comes from the argument expression, so `nameof(...)` isn't needed.

### Functional path: `Guard.Against`

Each guard returns `null` when the input is valid and a validation `Error` when it isn't. It never throws, even for `null` input. Use it where invalid input is an expected outcome, such as factory methods and handlers that return `Result<T>`.

```csharp
public static Result<Customer> Create(string? name, string? email, int creditLimit)
{
    // Stop at the first failure.
    return (Guard.Against.NullOrWhiteSpace(name)
            ?? Guard.Against.Email(email)
            ?? Guard.Against.Negative(creditLimit))
        .ToResult(() => new Customer(name!, email!, creditLimit));
}

// Or report every failure at once.
ValidationResult validation = Guard.Collect(
    Guard.Against.NullOrWhiteSpace(request.Name),
    Guard.Against.Email(request.Email),
    Guard.Against.OutOfRange(request.Quantity, 1, 100));
```

`ToResult(() => ...)` calls the factory only when every guard passed. That makes it safe to construct an object whose own constructor would reject the input.

### Imperative path: `Guard.Throw`

The same guards throw `DomainException` carrying the same `Error`. Use them in constructors and wherever a violation means an invariant was broken. The compiler knows the value is non-null afterwards.

```csharp
public Customer(string? name, string? email, int creditLimit)
{
    Guard.Throw.NullOrWhiteSpace(name);
    Guard.Throw.Email(email);
    Guard.Throw.Negative(creditLimit);

    Name = name;   // no nullable warning
    Email = email;
    CreditLimit = creditLimit;
}
```

### Available guards

| Group | Guards | `Error.Code` |
| --- | --- | --- |
| Null and empty | `Null` (reference and nullable value types), `NullOrEmpty`, `NullOrWhiteSpace` | `validation.required` |
| String length | `ShorterThan`, `LongerThan` | `validation.min_length`, `validation.max_length` |
| Numbers (any numeric type) | `Negative`, `NegativeOrZero` | `validation.out_of_range` |
| Comparison (any `IComparable<T>`) | `OutOfRange`, `LessThan`, `GreaterThan` | `validation.out_of_range` |
| Values | `Default`, `InvalidGuid` | `validation.required` |
| Enums | `InvalidEnumValue`, `InvalidSmartEnum` | `validation.out_of_range` |
| Dates | `NotUtc` (`DateTime` and `DateTimeOffset`) | `validation.invalid_format` |
| Format | `InvalidFormat` (regular expression), `Email` | `validation.invalid_format` |
| Collections | `Empty`, `MinCount`, `MaxCount` | `validation.required`, `validation.out_of_range` |
| Custom rules | `True`, `False` (you supply the `Error`) | yours |

A `null` input is always reported as `validation.required`, even to a length, format, range or collection guard.

### Behaviour you can rely on

- **Messages don't change with server culture.** They're formatted with the invariant culture. Translate them by `Error.Code`, not by parsing the text.
- **`InvalidFormat` doesn't put the pattern in its message.** The message can reach an HTTP response. Compiled patterns are cached (up to 256), with a 250 ms timeout against catastrophic backtracking.
- **`NaN` is a violation** in the numeric and range guards.
- **`MinCount` and `MaxCount` don't read the whole sequence.** They stop as soon as the answer is known.

To add a guard of your own, write an extension method on `IGuardClause` that returns `Error?` and never throws. Analyzer `SK0006` enforces the no-throw rule.

## Railway extensions

```csharp
using SharedKernel.Core.Extensions;

Result<OrderDto> result = await orders.FindAsync(id)            // Task<Result<Order>>
    .Ensure(order => order.IsActive, OrderErrors.Inactive)
    .Bind(order => pricing.QuoteAsync(order))                    // async continuation
    .Map(quote => quote.ToDto())
    .TapError(error => log.QuoteFailed(error.Code));
```

On a failure every later step is skipped and the original `Error` flows through unchanged.

| Method | On success | On failure |
| --- | --- | --- |
| `Map` | Transforms the value | Passes the error through |
| `Bind` | Runs the next `Result`-returning step | Passes the error through |
| `Ensure` | Fails with your error if the condition is false | Passes the error through |
| `Tap` / `TapError` | Runs a side effect and returns the result unchanged | Runs a side effect and returns the result unchanged |
| `MapError` | Passes the value through | Transforms the error |
| `Match` | Folds the result to a single value | Folds the result to a single value |
| `GetValueOrThrow` / `ThrowIfFailure` | Returns the value | Throws the exception that matches the error |

These work on `Result<T>` and on the non-generic `Result`, each in three shapes:

- a plain result with synchronous or `Task`-returning steps,
- `Task<Result…>` with synchronous or `Task`-returning steps,
- `ValueTask<Result…>` with synchronous or `ValueTask`-returning steps.

Awaiting a chain rethrows the original exception of a failed task and `OperationCanceledException` for a cancelled one. Neither becomes a failed result.

## Exception boundaries

```csharp
Result<Invoice> invoice = await ResultTry.TryAsync(
    ct => paymentsClient.GetInvoiceAsync(invoiceId, ct),
    cancellationToken);
```

`Try`, and `TryAsync` with or without a `CancellationToken`, run a delegate and turn a thrown exception into a failed result. Each has an overload that takes your own `Func<Exception, Error>` mapper.

- **The exception text never reaches the result.** Without a mapper, the error is `unexpected.exception` with a fixed, safe message, because an SDK or driver message can contain host names or connection details. The exception itself is recorded on the current OpenTelemetry `Activity`.
- **Cancellation is never caught.** `OperationCanceledException` always propagates, so a cancelled request is not reported as a failure.

## Aggregating results

```csharp
ValidationResult<IReadOnlyList<Money>> totals = ResultCombine.Combine(
    Money.Create(net, currency),
    Money.Create(tax, currency));
```

Every input is evaluated. A failure carries every failing `Error` in input order.

## Exceptions

| Exception | Pair it with `ErrorType` |
| --- | --- |
| `ValidationException` (one or many errors) | `Validation` |
| `NotFoundException` | `NotFound` |
| `ConflictException` | `Conflict` |
| `UnauthorizedException` | `Unauthorized` (401: not authenticated) |
| `ForbiddenException` | `Forbidden` (403: authenticated but not allowed) |
| `DomainException` (not sealed) | `BusinessRule`, or any other error |

Every exception requires an `Error`; there are no string-only constructors. `error.ToException()` creates the matching type for you:

```csharp
throw Error.NotFound("order.not_found", "The order does not exist.").ToException();
```

The HTTP status a presentation layer returns is decided by `Error.Type`, not by the exception class.

## Other helpers

- **`string`:** `ToSnakeCase`, `ToKebabCase`, `ToCamelCase` and `ToPascalCase` split words the same way (`HTMLParser` becomes `html_parser` or `htmlParser`) and ignore the server's culture.
- **`IEnumerable<T>`:** `IsNullOrEmpty` (the compiler knows the sequence is non-null when it returns `false`) and `WhereNotNull`.
- **`DateTimeOffset`:** `StartOfDay`. For a whole day, query `start <= x < start.AddDays(1)`.

For batching, use the BCL's `Enumerable.Chunk`. For Unix time, use `DateTimeOffset.ToUnixTimeMilliseconds`.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the [01.Core overview](../README.md) for the other core packages.
