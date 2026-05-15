using System.Reflection;
using SharedKernel.Caching.Redis.Abstractions;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests.Abstractions;

/// <summary>
/// Design-phase contract verification for <see cref="IDistributedLockService"/>.
/// Confirms the interface surface matches SK.02.Design D-03 without requiring a live implementation.
/// </summary>
public sealed class IDistributedLockServiceContractTests
{
    private static readonly Type InterfaceType = typeof(IDistributedLockService);

    [Fact]
    public void IDistributedLockService_IsPublicInterface()
    {
        Assert.True(InterfaceType.IsInterface);
        Assert.True(InterfaceType.IsPublic);
    }

    [Fact]
    public void IDistributedLockService_HasAcquireAsyncMethod()
    {
        var method = InterfaceType.GetMethod("AcquireAsync");
        Assert.NotNull(method);
    }

    [Fact]
    public void AcquireAsync_HasFiveParameters()
    {
        var method = InterfaceType.GetMethod("AcquireAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(5, parameters.Length);
    }

    [Fact]
    public void AcquireAsync_FirstParameterIsResourceString()
    {
        var parameters = InterfaceType.GetMethod("AcquireAsync")!.GetParameters();
        Assert.Equal("resource", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void AcquireAsync_SecondParameterIsExpiryTimeSpan()
    {
        var parameters = InterfaceType.GetMethod("AcquireAsync")!.GetParameters();
        Assert.Equal("expiry", parameters[1].Name);
        Assert.Equal(typeof(TimeSpan), parameters[1].ParameterType);
    }

    [Fact]
    public void AcquireAsync_ThirdParameterIsWaitTimeSpan()
    {
        var parameters = InterfaceType.GetMethod("AcquireAsync")!.GetParameters();
        Assert.Equal("wait", parameters[2].Name);
        Assert.Equal(typeof(TimeSpan), parameters[2].ParameterType);
    }

    [Fact]
    public void AcquireAsync_FourthParameterIsRetryTimeSpan()
    {
        var parameters = InterfaceType.GetMethod("AcquireAsync")!.GetParameters();
        Assert.Equal("retry", parameters[3].Name);
        Assert.Equal(typeof(TimeSpan), parameters[3].ParameterType);
    }

    [Fact]
    public void AcquireAsync_FifthParameterIsCancellationToken()
    {
        var parameters = InterfaceType.GetMethod("AcquireAsync")!.GetParameters();
        Assert.Equal("ct", parameters[4].Name);
        Assert.Equal(typeof(CancellationToken), parameters[4].ParameterType);
    }

    [Fact]
    public void AcquireAsync_ReturnsTaskOfNullableIAsyncDisposable()
    {
        var method = InterfaceType.GetMethod("AcquireAsync")!;
        var returnType = method.ReturnType;

        // Return type must be Task<IAsyncDisposable?> — Task<IAsyncDisposable> at runtime
        // (nullable annotations are erased at runtime; we verify the generic argument is IAsyncDisposable)
        Assert.True(returnType.IsGenericType);
        Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());
        Assert.Equal(typeof(IAsyncDisposable), returnType.GetGenericArguments()[0]);
    }

    [Fact]
    public void IDistributedLockService_ExposesExactlyOneMethod()
    {
        var methods = InterfaceType.GetMethods();
        Assert.Single(methods);
    }
}
