using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Primitives.Tests.Logging;

public sealed class LoggingEventIdRangesTests
{
    // T-33: name-to-folder-number table matching the root CLAUDE.md folder map (00 through 17).
    public static IEnumerable<object[]> DomainNameToFolderNumber()
    {
        yield return new object[] { "Governance", 0 };
        yield return new object[] { "Core", 1 };
        yield return new object[] { "Caching", 2 };
        yield return new object[] { "Domain", 3 };
        yield return new object[] { "Contracts", 4 };
        yield return new object[] { "Application", 5 };
        yield return new object[] { "Persistence", 6 };
        yield return new object[] { "Messaging", 7 };
        yield return new object[] { "Storage", 8 };
        yield return new object[] { "Search", 9 };
        yield return new object[] { "Intelligence", 10 };
        yield return new object[] { "Communication", 11 };
        yield return new object[] { "Security", 12 };
        yield return new object[] { "ServiceDefaults", 13 };
        yield return new object[] { "Presentation", 14 };
        yield return new object[] { "Integration", 15 };
        yield return new object[] { "Testing", 16 };
        yield return new object[] { "Workflows", 17 };
    }

    private static readonly IReadOnlyDictionary<string, int> AllDomainValues = new Dictionary<string, int>
    {
        ["Governance"] = LoggingEventIdRanges.Governance,
        ["Core"] = LoggingEventIdRanges.Core,
        ["Caching"] = LoggingEventIdRanges.Caching,
        ["Domain"] = LoggingEventIdRanges.Domain,
        ["Contracts"] = LoggingEventIdRanges.Contracts,
        ["Application"] = LoggingEventIdRanges.Application,
        ["Persistence"] = LoggingEventIdRanges.Persistence,
        ["Messaging"] = LoggingEventIdRanges.Messaging,
        ["Storage"] = LoggingEventIdRanges.Storage,
        ["Search"] = LoggingEventIdRanges.Search,
        ["Intelligence"] = LoggingEventIdRanges.Intelligence,
        ["Communication"] = LoggingEventIdRanges.Communication,
        ["Security"] = LoggingEventIdRanges.Security,
        ["ServiceDefaults"] = LoggingEventIdRanges.ServiceDefaults,
        ["Presentation"] = LoggingEventIdRanges.Presentation,
        ["Integration"] = LoggingEventIdRanges.Integration,
        ["Testing"] = LoggingEventIdRanges.Testing,
        ["Workflows"] = LoggingEventIdRanges.Workflows,
    };

    [Fact]
    public void DomainRangeWidth_Equals1000() => Assert.Equal(1000, LoggingEventIdRanges.DomainRangeWidth);

    [Fact]
    public void PackageSubBlockWidth_Equals100() => Assert.Equal(100, LoggingEventIdRanges.PackageSubBlockWidth);

    [Fact]
    public void AllEighteenDomainConstants_ArePairwiseUnique()
    {
        var values = AllDomainValues.Values.ToArray();

        Assert.Equal(18, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(DomainNameToFolderNumber))]
    public void EachDomainConstant_IsMultipleOfDomainRangeWidth(string domainName, int _)
    {
        var value = AllDomainValues[domainName];

        Assert.Equal(0, value % LoggingEventIdRanges.DomainRangeWidth);
    }

    [Theory]
    [MemberData(nameof(DomainNameToFolderNumber))]
    public void EachDomainConstant_EqualsFolderNumberTimesDomainRangeWidth(string domainName, int folderNumber)
    {
        var expected = folderNumber * LoggingEventIdRanges.DomainRangeWidth;

        Assert.Equal(expected, AllDomainValues[domainName]);
    }

    [Fact]
    public void AllDomainNames_AreCoveredByTheTable()
        => Assert.Equal(AllDomainValues.Count, DomainNameToFolderNumber().Count());
}
