using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Validation.Extensions;
using SharedKernel.Validation.NationalId;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class ValidationServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSharedKernelValidation_RegistersRegistry_PreSeededWithTckn()
    {
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => services.AddSharedKernelValidation())
            .Build();

        await host.StartAsync();

        INationalIdValidatorRegistry registry = host.Services.GetRequiredService<INationalIdValidatorRegistry>();

        Assert.True(registry.TryGetValidator("TR", out INationalIdValidator? validator));
        Assert.IsType<TckNationalIdValidator>(validator);

        await host.StopAsync();
    }

    [Fact]
    public async Task AddSharedKernelValidation_RegistryIsSingleton()
    {
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => services.AddSharedKernelValidation())
            .Build();

        await host.StartAsync();

        var first = host.Services.GetRequiredService<INationalIdValidatorRegistry>();
        var second = host.Services.GetRequiredService<INationalIdValidatorRegistry>();

        Assert.Same(first, second);

        await host.StopAsync();
    }

    [Fact]
    public async Task AddNationalIdValidator_AdditionalCountry_ResolvesAlongsideDefault()
    {
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => services
                .AddSharedKernelValidation()
                .AddNationalIdValidator<FakeUsNationalIdValidator>())
            .Build();

        await host.StartAsync();

        INationalIdValidatorRegistry registry = host.Services.GetRequiredService<INationalIdValidatorRegistry>();

        // The pre-seeded default is still present...
        Assert.True(registry.TryGetValidator("TR", out INationalIdValidator? trValidator));
        Assert.IsType<TckNationalIdValidator>(trValidator);

        // ...alongside the newly-registered country.
        Assert.True(registry.TryGetValidator("US", out INationalIdValidator? usValidator));
        Assert.IsType<FakeUsNationalIdValidator>(usValidator);

        await host.StopAsync();
    }

    [Fact]
    public async Task AddNationalIdValidator_CalledBeforeAddSharedKernelValidation_StillResolves()
    {
        // Registration order between the two extension calls must not matter — resolution is
        // deferred until the registry singleton is first constructed.
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddNationalIdValidator<FakeUsNationalIdValidator>();
                services.AddSharedKernelValidation();
            })
            .Build();

        await host.StartAsync();

        INationalIdValidatorRegistry registry = host.Services.GetRequiredService<INationalIdValidatorRegistry>();

        Assert.True(registry.TryGetValidator("US", out _));

        await host.StopAsync();
    }

    private sealed class FakeUsNationalIdValidator : INationalIdValidator
    {
        public string CountryCode => "US";

        public bool IsValid(string idNumber) => idNumber.Length == 9;
    }
}
