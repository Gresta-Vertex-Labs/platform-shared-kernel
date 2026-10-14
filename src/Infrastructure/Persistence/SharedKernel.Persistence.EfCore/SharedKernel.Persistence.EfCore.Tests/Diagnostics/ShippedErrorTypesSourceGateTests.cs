using System.Text.RegularExpressions;
using FluentAssertions;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

/// <summary>
/// No shipped Persistence package throws a raw <c>KeyNotFoundException</c> or <c>CryptographicException</c>: not-found,
/// decrypt and data-key faults are classified exceptions (<c>NotFoundException</c>, <c>FieldEncryptionException</c>, ...).
/// The doubles in <c>SharedKernel.Persistence.Testing</c> ship too and are covered.
/// </summary>
public sealed partial class ShippedErrorTypesSourceGateTests
{
    [GeneratedRegex(@"\bnew\s+(System\.Collections\.Generic\.)?KeyNotFoundException\b|\bnew\s+(System\.Security\.Cryptography\.)?CryptographicException\b")]
    private static partial Regex RawFault();

    private static string FindPersistenceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Infrastructure", "Persistence");
            if (File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")) && Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }

    private static bool IsShippedSource(string path)
    {
        var relative = path.Replace('\\', '/');
        return !relative.Contains(".Tests/", StringComparison.Ordinal)
            && !relative.Contains("/obj/", StringComparison.Ordinal)
            && !relative.Contains("/bin/", StringComparison.Ordinal)
            && !relative.Contains("SharedKernel.Persistence.ConsumerVerify/", StringComparison.Ordinal);
    }

    [Fact]
    public void NoShippedPersistenceSource_ThrowsARawKeyNotFoundOrCryptographicException()
    {
        var offenders = Directory.EnumerateFiles(FindPersistenceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(IsShippedSource)
            .Where(file => RawFault().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .ToList();

        offenders.Should().BeEmpty();
    }
}
