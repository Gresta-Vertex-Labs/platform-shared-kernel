using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using SharedKernel.Core.Exceptions;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.SignalR.Tests.TestSupport;

/// <summary>The paths the test hosts map their hubs at.</summary>
internal static class HubPaths
{
    public const string Errors = "/hubs/errors";

    public const string Results = "/hubs/results";

    public const string Context = "/hubs/context";

    public const string MethodAuthorization = "/hubs/method-authorization";

    public const string Protected = "/hubs/protected";

    public const string Plain = "/hubs/plain";

    public const string RateLimited = "/hubs/rate-limited";
}

/// <summary>A value a hub method returns, to prove a Result's value reaches the client as itself.</summary>
public sealed record OrderDto(int Id, string Status);

/// <summary>Messages the hubs use, some with translations in the localization tests.</summary>
public static class HubMessages
{
    public static readonly LocalizedMessage<int> OrderNotFound =
        LocalizedMessage.Define<int>("order.not_found", "Order {orderId} was not found.", "orderId");

    public const string InternalDetail = "http://search.internal:9200 refused the connection.";

    public const string Secret = "Server=db.internal;Password=secret";
}

/// <summary>Counts how many times hub methods actually ran, to prove refused invocations never run.</summary>
public sealed class InvocationCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>Throws every kind of exception the error mapping distinguishes.</summary>
public sealed class ErrorsHub(InvocationCounter counter) : Hub
{
    public string ThrowNotFound() => throw new NotFoundException(HubMessages.OrderNotFound.ToError(ErrorType.NotFound, 42));

    public string ThrowUnavailable() => throw Error.Unavailable("search.unreachable", HubMessages.InternalDetail).ToException();

    public string ThrowUnknown() => throw new InvalidOperationException(HubMessages.Secret);

    public string ThrowHubException() => throw new HubException("kept exactly as thrown");

    public string ThrowValidation() => throw new ValidationException(
    [
        Error.Validation("name.required", "Name is required."),
        Error.Validation("email.invalid", "Email is invalid."),
    ]);

    public string ThrowSingleValidation() => throw new ValidationException(Error.Validation("name.required", "Name is required."));

    public string Echo(string text) => text;

    // SignalR binds a CancellationToken parameter only on streaming methods; an ordinary one watches the connection.
    public async Task<string> WaitForever()
    {
        counter.Increment();
        await Task.Delay(Timeout.Infinite, Context.ConnectionAborted);
        return "unreachable";
    }
}

/// <summary>Returns Result and Result&lt;T&gt; in every shape a hub method can.</summary>
public sealed class ResultsHub : Hub
{
    public Result Succeed() => Result.Success();

    public Result Fail() => Error.Conflict("order.already_paid", "The order is already paid.");

    public Result<int> Number() => 42;

    public Result<int> NumberNotFound() => HubMessages.OrderNotFound.ToError(ErrorType.NotFound, 42);

    public Result<string> Text() => "hello";

    public Result<OrderDto> Order() => new OrderDto(7, "open");

    public Task<Result<OrderDto>> OrderAsync() => Task.FromResult(Result<OrderDto>.Success(new OrderDto(8, "shipped")));

    public async ValueTask<Result<Guid>> IdAsync()
    {
        await Task.Yield();
        return Guid.Parse("11111111-2222-3333-4444-555555555555");
    }

    public Result<int> Outage() => Error.Unavailable("search.unreachable", HubMessages.InternalDetail);

    public Result Uninitialized() => default;
}

/// <summary>Reads the connection's tenant and correlation id.</summary>
public sealed class ContextHub : Hub
{
    public string? Tenant() => Context.GetTenantId()?.ToString();

    public string? Correlation() => Context.GetCorrelationId();

    public async Task<string?> JoinTenantGroup()
    {
        if (Context.GetTenantId() is not { } tenantId)
        {
            return null;
        }

        var group = HubGroupNaming.TenantGroup(tenantId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        return group;
    }
}

/// <summary>
/// Requirements on hub methods. The attributes are <c>[Authorize]</c> attributes, so SignalR enforces them itself.
/// Every method counts its runs, to prove a refused invocation never runs.
/// </summary>
public sealed class MethodAuthorizationHub(InvocationCounter counter) : Hub
{
    [RequirePermission("orders.read", "orders.admin")]
    public string ReadOrders() => Run("orders");

    [RequirePermission("orders.read")]
    [RequireRole("auditor")]
    public string Audit() => Run("audited");

    [RequireFreshAuthentication(300)]
    public string Transfer() => Run("transferred");

    [RequireAuthenticationMethod("mfa")]
    public string ChangePassword() => Run("changed");

    public string Open() => Run("open");

    private string Run(string result)
    {
        counter.Increment();
        return result;
    }
}

/// <summary>A requirement on the hub class, checked when the connection is opened.</summary>
[RequirePermission("hub.connect")]
public sealed class ProtectedHub : Hub
{
    public string Ping() => "pong";
}

/// <summary>A hub without requirements of its own; tests protect it with <c>MapHub&lt;T&gt;().RequirePermission(…)</c>.</summary>
public sealed class PlainHub : Hub
{
    public string Ping() => "pong";
}

/// <summary>Hub methods for the invocation rate limit.</summary>
public sealed class RateLimitedHub(InvocationCounter counter) : Hub
{
    public string Ping()
    {
        counter.Increment();
        return "pong";
    }

    public string ConnectionId() => Context.ConnectionId;

    public bool StoresRateLimiterOnConnection() => Context.Items.Values.Any(value => value is RateLimiter);
}
