using System.Data;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class DatabaseTenantResolutionStrategyTests
{
    [Fact]
    public async Task TryResolveAsync_WithKnownHost_ReturnsExpectedTenantId()
    {
        var tenantId = Guid.NewGuid();
        var command = Substitute.For<IDbCommand>();
        var parameters = Substitute.For<IDataParameterCollection>();
        command.Parameters.Returns(parameters);
        command.CreateParameter().Returns(_ => Substitute.For<IDbDataParameter>());
        command.ExecuteScalar().Returns(tenantId.ToString());

        var connection = Substitute.For<IDbConnection>();
        connection.CreateCommand().Returns(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.api.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result);
    }

    [Fact]
    public async Task TryResolveAsync_WithUnknownHost_ReturnsNull()
    {
        var command = Substitute.For<IDbCommand>();
        var parameters = Substitute.For<IDataParameterCollection>();
        command.Parameters.Returns(parameters);
        command.CreateParameter().Returns(_ => Substitute.For<IDbDataParameter>());
        command.ExecuteScalar().Returns((object?)null);

        var connection = Substitute.For<IDbConnection>();
        connection.CreateCommand().Returns(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("unknown.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_UsesParameterizedQuery_NoStringBuiltSql()
    {
        var command = Substitute.For<IDbCommand>();
        var parameters = Substitute.For<IDataParameterCollection>();
        command.Parameters.Returns(parameters);
        var parameter = Substitute.For<IDbDataParameter>();
        command.CreateParameter().Returns(parameter);
        command.ExecuteScalar().Returns((object?)null);

        var connection = Substitute.For<IDbConnection>();
        connection.CreateCommand().Returns(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.api.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        await strategy.TryResolveAsync(context, CancellationToken.None);

        // The command text must not contain the request-derived host value — it must be passed
        // exclusively via a bound parameter.
        Assert.DoesNotContain("acme.api.example.com", command.CommandText);
        parameters.Received(1).Add(parameter);
        Assert.Equal("acme.api.example.com", parameter.Value);
    }
}
