using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// A fast <see cref="IOneWayHasher"/> for tests: the production <see cref="OneWayHasher"/> and PHC format, with a
/// PBKDF2 iteration count low enough for test suites.
/// </summary>
/// <remarks>
/// Never use it outside tests: the iteration count is far below the production minimum. Change
/// <see cref="Iterations"/> during a test to exercise <see cref="HashVerificationResult.SuccessRehashNeeded"/>.
/// </remarks>
public sealed class FakeOneWayHasher : IOneWayHasher
{
    private readonly MutableOptions<Pbkdf2Options> _pbkdf2 = new(new Pbkdf2Options { Iterations = 16 });
    private readonly OneWayHasher _inner;

    /// <summary>Creates the hasher.</summary>
    public FakeOneWayHasher() =>
        _inner = new OneWayHasher(
            [new Pbkdf2OneWayHashAlgorithm(_pbkdf2)],
            new MutableOptions<CryptographyOptions>(new CryptographyOptions()));

    /// <summary>The PBKDF2 iteration count for new hashes. Defaults to 16.</summary>
    public int Iterations
    {
        get => _pbkdf2.CurrentValue.Iterations;
        set => _pbkdf2.CurrentValue.Iterations = value;
    }

    /// <inheritdoc />
    public string Hash(string secret) => _inner.Hash(secret);

    /// <inheritdoc />
    public HashVerificationResult Verify(string hash, string secret) => _inner.Verify(hash, secret);

    private sealed class MutableOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
