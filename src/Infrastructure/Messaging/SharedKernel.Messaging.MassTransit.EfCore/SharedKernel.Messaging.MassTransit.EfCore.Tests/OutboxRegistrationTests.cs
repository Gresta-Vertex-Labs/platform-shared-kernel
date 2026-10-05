using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.EfCore.Tests;

/// <summary>
/// <c>WithEntityFrameworkOutbox&lt;TDbContext&gt;()</c> moved to this package with P-570 and now reaches the bus
/// through <see cref="MessagingBusBuilder.ConfigureMassTransit"/>: proves the registration step still runs inside
/// <c>AddMassTransit</c>, and only when it was asked for.
/// </summary>
public sealed class OutboxRegistrationTests
{
    [Fact]
    public void WithEntityFrameworkOutbox_Build_RegistersTheOutbox()
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelMessaging(o => o.ServiceName = "outbox-test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithEntityFrameworkOutbox<RegistrationTestDbContext>(o => o.BatchSize = 25)
            .Build();

        services.Should().Contain(d => IsOutboxService(d), "the EF Core outbox must be registered with MassTransit");
    }

    [Theory]
    [InlineData(OutboxDatabase.PostgreSql)]
    [InlineData(OutboxDatabase.SqlServer)]
    [InlineData(OutboxDatabase.MySql)]
    [InlineData(OutboxDatabase.Sqlite)]
    public void WithEntityFrameworkOutbox_EachDatabase_RegistersTheOutbox(OutboxDatabase database)
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelMessaging(o => o.ServiceName = "outbox-test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithEntityFrameworkOutbox<RegistrationTestDbContext>(o => o.Database = database)
            .Build();

        services.Should().Contain(d => IsOutboxService(d));
    }

    [Fact]
    public void OutboxOptions_Database_DefaultsToPostgreSql()
        => new OutboxOptions().Database.Should().Be(OutboxDatabase.PostgreSql);

    [Fact]
    public void WithEntityFrameworkOutbox_UndefinedDatabase_Throws()
    {
        var builder = new ServiceCollection().AddSharedKernelMessaging(o => o.ServiceName = "outbox-test-service");

        var act = () => builder.WithEntityFrameworkOutbox<RegistrationTestDbContext>(o => o.Database = (OutboxDatabase)99);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*OutboxOptions.Database*");
    }

    [Fact]
    public void WithoutEntityFrameworkOutbox_Build_RegistersNoOutbox()
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelMessaging(o => o.ServiceName = "outbox-test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .Build();

        services.Should().NotContain(d => IsOutboxService(d), "the outbox is opt-in");
    }

    private static bool IsOutboxService(ServiceDescriptor descriptor)
        => descriptor.ServiceType.FullName?.Contains("Outbox", StringComparison.Ordinal) == true
            || (!descriptor.IsKeyedService
                && descriptor.ImplementationType?.FullName?.Contains("Outbox", StringComparison.Ordinal) == true);
}

internal sealed class RegistrationTestDbContext(DbContextOptions<RegistrationTestDbContext> options) : DbContext(options);
