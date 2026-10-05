using System.Globalization;
using System.Text;
using Konscious.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Argon2.Tests;

internal static class Argon2TestData
{
    public const int MemorySizeKb = 7_168;
    public const int Iterations = 2;
    public const int Parallelism = 1;

    public static readonly byte[] Password = Encoding.UTF8.GetBytes("correct horse battery staple");

    public static Argon2idOneWayHashAlgorithm CreateAlgorithm(int memory = MemorySizeKb, int iterations = Iterations, int parallelism = Parallelism) =>
        new(new TestOptionsMonitor<Argon2Options>(new Argon2Options
        {
            MemorySizeKb = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        }));

    public static byte[] Derive(byte[] password, byte[] salt, int memory, int iterations, int parallelism, int length)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(length);
    }

    public static PhcHashString Build(
        byte[] salt,
        byte[] hash,
        string memory = "7168",
        string iterations = "2",
        string parallelism = "1",
        int? version = 19,
        string algorithmId = Argon2idOneWayHashAlgorithm.Id) =>
        new(
            algorithmId,
            version,
            [new("m", memory), new("t", iterations), new("p", parallelism)],
            salt,
            hash);

    public static PhcHashString BuildValid(byte[] salt, int memory, int iterations, int parallelism, int length = 32) =>
        Build(
            salt,
            Derive(Password, salt, memory, iterations, parallelism, length),
            memory.ToString(CultureInfo.InvariantCulture),
            iterations.ToString(CultureInfo.InvariantCulture),
            parallelism.ToString(CultureInfo.InvariantCulture));

    public static byte[] Salt(int length) => [.. Enumerable.Range(1, length).Select(i => (byte)i)];
}
