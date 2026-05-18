using System.Reflection;
using SharedKernel.Caching.Abstractions;
using Xunit;

namespace SharedKernel.Caching.Tests.Abstractions;

/// <summary>
/// Design-phase contract verification for <see cref="ICacheService"/>.
/// These tests confirm the interface surface matches the design specification
/// (SK.02.Design D-01) without requiring a live implementation.
/// </summary>
public sealed class ICacheServiceContractTests
{
    private static readonly Type InterfaceType = typeof(ICacheService);

    [Fact]
    public void ICacheService_IsPublicInterface()
    {
        Assert.True(InterfaceType.IsInterface);
        Assert.True(InterfaceType.IsPublic);
    }

    [Fact]
    public void ICacheService_HasGetAsyncMethod()
    {
        // GetAsync<T>(string key, CancellationToken ct) → ValueTask<T?>
        var method = InterfaceType.GetMethod("GetAsync");
        Assert.NotNull(method);
        Assert.True(method!.IsGenericMethodDefinition);
    }

    [Fact]
    public void ICacheService_HasSetAsyncMethod()
    {
        // SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct)
        var method = InterfaceType.GetMethod("SetAsync");
        Assert.NotNull(method);
        Assert.True(method!.IsGenericMethodDefinition);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CachePolicy), parameters[2].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
    }

    [Fact]
    public void ICacheService_HasGetOrSetAsyncMethod()
    {
        // GetOrSetAsync<T>(string key, Func<CancellationToken,Task<T>> factory, CachePolicy) → ValueTask<T>
        var method = InterfaceType.GetMethod("GetOrSetAsync");
        Assert.NotNull(method);
        Assert.True(method!.IsGenericMethodDefinition);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CachePolicy), parameters[2].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
    }

    [Fact]
    public void ICacheService_HasRemoveAsyncMethod()
    {
        // RemoveAsync(string key, CancellationToken ct)
        var method = InterfaceType.GetMethod("RemoveAsync");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void ICacheService_HasRemoveByTagAsyncMethod()
    {
        // RemoveByTagAsync(string tag, CancellationToken ct)
        var method = InterfaceType.GetMethod("RemoveByTagAsync");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void ICacheService_ExposesExactlyFiveMethods()
    {
        var methods = InterfaceType.GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
