using HotChocolate;
using HotChocolate.Execution;
using SharedKernel.Communication.GraphQL.Errors;

namespace SharedKernel.Communication.GraphQL.Tests;

public sealed class SharedKernelErrorFilterTests
{
    private readonly SharedKernelErrorFilter _filter = new();

    private static IError BuildError(
        string message = "Test error",
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var builder = ErrorBuilder.New().SetMessage(message);
        if (exception is not null) builder.SetException(exception);
        if (extensions is not null)
        {
            foreach (var kv in extensions)
                builder.SetExtension(kv.Key, kv.Value);
        }
        return builder.Build();
    }

    [Fact]
    public void OnError_SetsTitleFromMessage()
    {
        var error = BuildError("Something went wrong");
        var result = _filter.OnError(error);
        result.Extensions.Should().ContainKey("title");
        result.Extensions!["title"].Should().Be("Something went wrong");
    }

    [Fact]
    public void OnError_SetsStatusFrom_Extensions()
    {
        var error = BuildError(extensions: new Dictionary<string, object?> { ["status"] = 422 });
        var result = _filter.OnError(error);
        result.Extensions!["status"].Should().Be(422);
    }

    [Fact]
    public void OnError_DefaultsStatusTo500_WhenNoStatusExtension()
    {
        var error = BuildError();
        var result = _filter.OnError(error);
        result.Extensions!["status"].Should().Be(500);
    }

    [Fact]
    public void OnError_SetsDetailFromException_WhenExceptionPresent()
    {
        var exception = new InvalidOperationException("inner detail");
        var error = BuildError(exception: exception);
        var result = _filter.OnError(error);
        result.Extensions!["detail"].Should().Be("inner detail");
    }

    [Fact]
    public void OnError_DoesNotSetDetail_WhenNoException()
    {
        var error = BuildError();
        var result = _filter.OnError(error);
        result.Extensions.Should().NotContainKey("detail");
    }

    [Fact]
    public void OnError_SetsTypeUri()
    {
        var error = BuildError();
        var result = _filter.OnError(error);
        result.Extensions!["type"].Should().NotBeNull();
    }

    [Fact]
    public void OnError_TypeUri_Reflects404()
    {
        var error = BuildError(extensions: new Dictionary<string, object?> { ["status"] = 404 });
        var result = _filter.OnError(error);
        result.Extensions!["type"].As<string>().Should().Contain("15.5.5");
    }

    [Fact]
    public void OnError_NeverThrows_EvenWithNullExtensions()
    {
        var error = BuildError();
        var act = () => _filter.OnError(error);
        act.Should().NotThrow();
    }

    [Fact]
    public void OnError_PreservesExistingExtensionKeys()
    {
        var error = BuildError(extensions: new Dictionary<string, object?> { ["custom"] = "value", ["status"] = 400 });
        var result = _filter.OnError(error);
        result.Extensions!["custom"].Should().Be("value");
    }

    [Fact]
    public void OnError_StatusFromStringExtension_ParsesCorrectly()
    {
        var error = BuildError(extensions: new Dictionary<string, object?> { ["status"] = "403" });
        var result = _filter.OnError(error);
        result.Extensions!["status"].Should().Be(403);
    }
}
