using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal static class TestCryptographyOptions
{
    public static TestOptionsMonitor<CryptographyOptions> Create(Action<OneWayHashingOptions>? configure = null)
    {
        var options = new CryptographyOptions();
        configure?.Invoke(options.OneWayHashing);
        return new TestOptionsMonitor<CryptographyOptions>(options);
    }

    /// <summary>PBKDF2 settings at the minimum accepted cost, to keep tests fast.</summary>
    public static TestOptionsMonitor<Pbkdf2Options> Pbkdf2(int iterations = Pbkdf2Options.MinimumIterations) =>
        new(new Pbkdf2Options { Iterations = iterations });
}
