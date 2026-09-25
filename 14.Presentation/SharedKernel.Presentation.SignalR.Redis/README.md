# SharedKernel.Presentation.SignalR.Redis

The Redis scale-out backplane for `SharedKernel.Presentation.SignalR`. Reference it only in a SignalR host that runs more than one replica; without it SignalR stays in memory, which is right for local development and a single replica.

```csharp
builder.Services
    .AddSharedKernelSignalR()
    .WithRedisBackplane(builder.Configuration.GetConnectionString("signalr-redis")!);
```

The backplane opens its own Redis connection and never shares `02.Caching`'s `IConnectionMultiplexer`, so a backplane outage is never reported as a cache outage.
