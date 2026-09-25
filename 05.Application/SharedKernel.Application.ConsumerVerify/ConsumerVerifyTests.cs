using System.Collections.Concurrent;
using FluentValidation;
using SharedKernel.Application.Mediator.MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Application.Idempotency;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Validation.FluentValidation;
using SharedKernel.Execution.Transactions;
using SharedKernel.Execution.Context;
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
    public SharedKernel.Execution.Tenancy.TenantId? TenantId => null;
    public HashSet<string> Permissions { get; } = ["orders.place"];

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(Permissions.Contains(permission));
}

public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(IdempotencyPurpose, string), (string Fingerprint, string Token, bool Completed, string? Response)> _entries = new();

    public Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var entry = _entries.GetOrAdd((purpose, key), (fingerprint, token, false, null));

        if (entry.Fingerprint != fingerprint)
            return Task.FromResult(IdempotencyReservation.FingerprintMismatch());
        if (entry.Token == token)
            return Task.FromResult(IdempotencyReservation.Started(token));

        return Task.FromResult(entry.Completed
            ? IdempotencyReservation.Completed(entry.Response)
            : IdempotencyReservation.InProgress());
    }

    public Task<bool> CompleteAsync(
        IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue((purpose, key), out var entry) || entry.Token != token || entry.Completed)
            return Task.FromResult(false);

        _entries[(purpose, key)] = entry with { Completed = true, Response = response };
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken)
        => Task.FromResult(_entries.TryGetValue((purpose, key), out var entry) && entry.Token == token && !entry.Completed
            && _entries.TryRemove((purpose, key), out _));
}

/// <summary>Exercises the packed public API of SharedKernel.Application, .Pipeline and .Mediator.MediatR the way a consuming service would.</summary>
public sealed class ConsumerVerifyTests
{
    [Fact]
    public void Pipeline_PackageCarriesNoInfrastructureOrMediatorDependency()
    {
        var references = typeof(ApplicationBehaviorsBuilder).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        Assert.Contains("SharedKernel.Application", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Polly", StringComparison.Ordinal)
            || name.StartsWith("SharedKernel.Caching", StringComparison.Ordinal)
            || name == "SharedKernel.Core"
            || name == "Microsoft.Extensions.Hosting.Abstractions"
            || name.StartsWith("MediatR", StringComparison.Ordinal)
            || name.StartsWith("FluentValidation", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ICommand).Assembly.GetReferencedAssemblies(), assembly => assembly.Name!.StartsWith("MediatR", StringComparison.Ordinal));
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

        services.AddSharedKernelMediatR(typeof(PlaceOrderHandler).Assembly);
        services.AddScoped<IValidator<PlaceOrder>, PlaceOrderValidator>();
        services.AddFluentValidationRequestValidators();
        services.AddSingleton(journal);
        services.AddSingleton<IRequestContext>(context);
        services.AddIdempotencyStore<IdempotencyStore>(IdempotencyPurpose.Request);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
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
