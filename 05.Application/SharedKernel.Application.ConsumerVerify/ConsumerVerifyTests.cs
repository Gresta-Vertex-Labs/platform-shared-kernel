using System.Collections.Concurrent;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Transactions;
using SharedKernel.Application.Context;
using SharedKernel.Application.Extensions;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Application.ConsumerVerify;

public sealed record PlaceOrder(string Customer, decimal Amount, string IdempotencyKey)
    : ICommand<Guid>, IAuthorizeRequest, IIdempotentRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => ["orders.place"];
}

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(command => command.Customer).NotEmpty();
        RuleFor(command => command.Amount).GreaterThan(0);
    }
}

public sealed class PlaceOrderHandler(ICommandScope scope, Journal journal) : ICommandHandler<PlaceOrder, Guid>
{
    public Task<Result<Guid>> Handle(PlaceOrder request, CancellationToken cancellationToken)
    {
        journal.HandlerCalls++;
        scope.OnCompleted(_ =>
        {
            journal.AfterCommit = journal.Commits;
            return Task.CompletedTask;
        });

        return Task.FromResult(request.Customer == "blocked"
            ? Result<Guid>.Failure(Error.BusinessRule("order.customer_blocked", "The customer is blocked."))
            : Result<Guid>.Success(Guid.NewGuid()));
    }
}

public sealed class Journal
{
    public int HandlerCalls { get; set; }
    public int Commits { get; set; }
    public int? AfterCommit { get; set; }
}

public sealed class UnitOfWork(Journal journal) : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _beforeCommit = [];

    public bool IsTransactionActive { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        journal.Commits++;
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync<object?>(async ct => { await operation(ct); return null; }, cancellationToken);

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, System.Data.IsolationLevel? isolationLevel, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, cancellationToken);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, System.Data.IsolationLevel? isolationLevel, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        IsTransactionActive = true;
        _beforeCommit.Clear();
        try
        {
            var result = await operation(cancellationToken);
            if (result is IHasSuccessFlag { IsSuccess: false })
                return result;

            await SaveChangesAsync(cancellationToken);
            foreach (var callback in _beforeCommit)
                await callback(cancellationToken);
            return result;
        }
        finally
        {
            IsTransactionActive = false;
        }
    }

    public void OnBeforeCommit(Func<CancellationToken, Task> callback) => _beforeCommit.Add(callback);
}

public sealed class RequestContext : IRequestContext
{
    public bool IsAuthenticated { get; init; } = true;
    public string? UserId => "user-1";
    public Guid? TenantId => null;
    public HashSet<string> Permissions { get; } = ["orders.place"];

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(Permissions.Contains(permission));
}

public sealed class IdempotencyStore : IRequestIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (string Fingerprint, string Token, string? Response)> _entries = new();

    public Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var entry = _entries.GetOrAdd(key, (requestFingerprint, token, null));

        if (entry.Fingerprint != requestFingerprint)
            return Task.FromResult(IdempotencyBeginResult.FingerprintMismatch());
        if (entry.Token == token)
            return Task.FromResult(IdempotencyBeginResult.Started(token));

        return Task.FromResult(entry.Response is null
            ? IdempotencyBeginResult.InProgress()
            : IdempotencyBeginResult.Completed(entry.Response));
    }

    public Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.Token != reservationToken)
            return Task.FromResult(false);

        _entries[key] = entry with { Response = serializedResponse };
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseAsync(string key, string reservationToken, CancellationToken cancellationToken)
        => Task.FromResult(_entries.TryGetValue(key, out var entry) && entry.Token == reservationToken
            && _entries.TryRemove(key, out _));
}

/// <summary>Exercises the packed public API of SharedKernel.Application and .Behaviors the way a consuming service would.</summary>
public sealed class ConsumerVerifyTests
{
    [Fact]
    public void Behaviors_PackageCarriesNoInfrastructureDependency()
    {
        var references = typeof(ApplicationBehaviorsBuilder).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        Assert.Contains("SharedKernel.Application", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Polly", StringComparison.Ordinal)
            || name.StartsWith("SharedKernel.Caching", StringComparison.Ordinal)
            || name == "SharedKernel.Core"
            || name == "Microsoft.Extensions.Hosting.Abstractions");
    }

    [Fact]
    public async Task Command_Succeeds_CommitsThenRunsPostCommitCallback()
    {
        var (sender, journal, _) = Build();

        var result = await sender.Send(new PlaceOrder("ada", 10m, "key-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, journal.Commits);
        Assert.Equal(1, journal.AfterCommit);
    }

    [Fact]
    public async Task Command_BusinessFailure_CommitsNothing()
    {
        var (sender, journal, _) = Build();

        var result = await sender.Send(new PlaceOrder("blocked", 10m, "key-2"));

        Assert.Equal("order.customer_blocked", result.Error.Code);
        Assert.Equal(0, journal.Commits);
        Assert.Null(journal.AfterCommit);
    }

    [Fact]
    public async Task Command_InvalidInput_ReturnsEveryFieldError()
    {
        var (sender, journal, _) = Build();

        var result = await sender.Send(new PlaceOrder("", 0m, "key-3"));

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(ErrorCodes.Validation.Failed, result.Error.Code);
        Assert.Equal(2, result.Error.Details.Count);
        Assert.Equal(0, journal.HandlerCalls);
    }

    [Fact]
    public async Task Command_MissingPermission_IsForbidden()
    {
        var (sender, journal, context) = Build();
        context.Permissions.Clear();

        var result = await sender.Send(new PlaceOrder("ada", 10m, "key-4"));

        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Equal(0, journal.HandlerCalls);
    }

    [Fact]
    public async Task Command_Duplicate_ReplaysOriginalResponse_AndRejectsReusedKey()
    {
        var (sender, journal, _) = Build();

        var first = await sender.Send(new PlaceOrder("ada", 10m, "key-5"));
        var replay = await sender.Send(new PlaceOrder("ada", 10m, "key-5"));
        var reused = await sender.Send(new PlaceOrder("ada", 99m, "key-5"));

        Assert.Equal(first.Value, replay.Value);
        Assert.Equal("idempotency.key_reused", reused.Error.Code);
        Assert.Equal(1, journal.HandlerCalls);
    }

    private static (ISender Sender, Journal Journal, RequestContext Context) Build()
    {
        var journal = new Journal();
        var context = new RequestContext();
        var services = new ServiceCollection();

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<PlaceOrderHandler>());
        services.AddScoped<IValidator<PlaceOrder>, PlaceOrderValidator>();
        services.AddSingleton(journal);
        services.AddSingleton<IRequestContext>(context);
        services.AddSingleton<IRequestIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSharedKernelApplication();
        services.AddSharedKernelApplicationBehaviors()
            .AddDefaultBehaviors()
            .AddAuthorizationBehavior()
            .AddIdempotencyBehavior()
            .AddTransactionBehavior()
            .Build();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return (provider.CreateScope().ServiceProvider.GetRequiredService<ISender>(), journal, context);
    }
}
