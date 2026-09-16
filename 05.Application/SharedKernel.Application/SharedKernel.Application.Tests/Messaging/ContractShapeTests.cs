using FluentAssertions;
using MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Tests.Messaging;

/// <summary>
/// Verifies the exact contract shapes of the command/query vocabulary in
/// <c>SharedKernel.Application.Messaging</c>.
/// </summary>
public sealed class ContractShapeTests
{
    private sealed record TestCommand : ICommand;

    private sealed record TestCommandWithResponse : ICommand<Guid>;

    private sealed record TestQuery : IQuery<string>;

    private sealed class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class TestCommandWithResponseHandler : ICommandHandler<TestCommandWithResponse, Guid>
    {
        public Task<Result<Guid>> Handle(TestCommandWithResponse request, CancellationToken cancellationToken)
            => Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<Result<string>> Handle(TestQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Success("ok"));
    }

    [Fact]
    public void ICommandBase_IsZeroMemberMarkerInterface()
    {
        typeof(ICommandBase).GetMembers().Should().BeEmpty();
    }

    [Fact]
    public void ICommand_ImplementsICommandBaseAndIRequestOfResult()
    {
        typeof(ICommand).Should().BeAssignableTo<ICommandBase>();
        typeof(ICommand).Should().BeAssignableTo<IRequest<Result>>();
    }

    [Fact]
    public void ICommandOfTResponse_ImplementsICommandBaseAndIRequestOfResultOfTResponse()
    {
        typeof(ICommand<Guid>).Should().BeAssignableTo<ICommandBase>();
        typeof(ICommand<Guid>).Should().BeAssignableTo<IRequest<Result<Guid>>>();
    }

    [Fact]
    public void IQueryOfTResponse_DoesNotImplementICommandBase()
    {
        typeof(IQuery<string>).Should().NotBeAssignableTo<ICommandBase>();
        typeof(IQuery<string>).Should().BeAssignableTo<IRequest<Result<string>>>();
    }

    [Fact]
    public void IQueryBase_IsZeroMemberMarkerInterface()
    {
        typeof(IQueryBase).GetMembers().Should().BeEmpty();
    }

    [Fact]
    public void IQueryOfTResponse_ImplementsIQueryBase()
    {
        typeof(IQuery<string>).Should().BeAssignableTo<IQueryBase>();
    }

    [Fact]
    public void ICommand_DoesNotImplementIQueryBase()
    {
        typeof(ICommand).Should().NotBeAssignableTo<IQueryBase>();
        typeof(ICommand<Guid>).Should().NotBeAssignableTo<IQueryBase>();
    }

    [Fact]
    public void ICommandHandler_IsPureAliasOverIRequestHandlerOfResult()
    {
        typeof(ICommandHandler<TestCommand>).Should().BeAssignableTo<IRequestHandler<TestCommand, Result>>();
        typeof(ICommandHandler<TestCommand>).GetMembers(
                System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)
            .Should().BeEmpty();
    }

    [Fact]
    public void ICommandHandlerOfTResponse_IsPureAliasOverIRequestHandlerOfResultOfTResponse()
    {
        typeof(ICommandHandler<TestCommandWithResponse, Guid>).Should()
            .BeAssignableTo<IRequestHandler<TestCommandWithResponse, Result<Guid>>>();
    }

    [Fact]
    public void IQueryHandler_IsPureAliasOverIRequestHandlerOfResultOfTResponse()
    {
        typeof(IQueryHandler<TestQuery, string>).Should()
            .BeAssignableTo<IRequestHandler<TestQuery, Result<string>>>();
    }

    [Fact]
    public async Task ICommandHandler_Handle_ReturnsResult()
    {
        var handler = new TestCommandHandler();

        var result = await handler.Handle(new TestCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ICommandHandlerOfTResponse_Handle_ReturnsResultOfTResponse()
    {
        var handler = new TestCommandWithResponseHandler();

        var result = await handler.Handle(new TestCommandWithResponse(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task IQueryHandler_Handle_ReturnsResultOfTResponse()
    {
        var handler = new TestQueryHandler();

        var result = await handler.Handle(new TestQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }
}
