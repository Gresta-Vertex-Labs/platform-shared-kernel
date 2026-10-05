using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Primitives.Tests.Logging;

public sealed class LoggingEventIdRangesTests
{
    // T-33/T-62: name-to-folder-number table matching the root CLAUDE.md folder map (00 through 20).
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
        yield return new object[] { "Idempotency", 18 };
        yield return new object[] { "Scheduling", 19 };
        yield return new object[] { "Reporting", 20 };
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
        ["Idempotency"] = LoggingEventIdRanges.Idempotency,
        ["Scheduling"] = LoggingEventIdRanges.Scheduling,
        ["Reporting"] = LoggingEventIdRanges.Reporting,
    };

    [Fact]
    public void DomainRangeWidth_Equals1000() => Assert.Equal(1000, LoggingEventIdRanges.DomainRangeWidth);

    [Fact]
    public void PackageSubBlockWidth_Equals100() => Assert.Equal(100, LoggingEventIdRanges.PackageSubBlockWidth);

    [Fact]
    public void AllTwentyOneDomainConstants_ArePairwiseUnique()
    {
        var values = AllDomainValues.Values.ToArray();

        Assert.Equal(21, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    /// <summary>
    /// T-62 regression guard: every one of the 18 pre-existing domain base constants (shipped
    /// under P-249/WO-041) must remain byte-for-byte unchanged now that Idempotency/Scheduling/
    /// Reporting have been added. Each expected value is hardcoded here (never derived from the
    /// same folder-number table the theory cases use) so that if two existing constants were
    /// accidentally swapped — a transposition the pairwise-uniqueness and modulo checks would NOT
    /// catch, since both would still be unique multiples of 1000 — this test fails. A changed base
    /// would silently reassign every already-shipped EventId in that domain's logs.
    /// </summary>
    [Fact]
    public void PreExistingEighteenDomainConstants_AreByteForByteUnchanged()
    {
        Assert.Equal(0, LoggingEventIdRanges.Governance);
        Assert.Equal(1000, LoggingEventIdRanges.Core);
        Assert.Equal(2000, LoggingEventIdRanges.Caching);
        Assert.Equal(3000, LoggingEventIdRanges.Domain);
        Assert.Equal(4000, LoggingEventIdRanges.Contracts);
        Assert.Equal(5000, LoggingEventIdRanges.Application);
        Assert.Equal(6000, LoggingEventIdRanges.Persistence);
        Assert.Equal(7000, LoggingEventIdRanges.Messaging);
        Assert.Equal(8000, LoggingEventIdRanges.Storage);
        Assert.Equal(9000, LoggingEventIdRanges.Search);
        Assert.Equal(10000, LoggingEventIdRanges.Intelligence);
        Assert.Equal(11000, LoggingEventIdRanges.Communication);
        Assert.Equal(12000, LoggingEventIdRanges.Security);
        Assert.Equal(13000, LoggingEventIdRanges.ServiceDefaults);
        Assert.Equal(14000, LoggingEventIdRanges.Presentation);
        Assert.Equal(15000, LoggingEventIdRanges.Integration);
        Assert.Equal(16000, LoggingEventIdRanges.Testing);
        Assert.Equal(17000, LoggingEventIdRanges.Workflows);
    }

    /// <summary>
    /// T-62: the three new domain base constants introduced this phase, matching the root
    /// CLAUDE.md folder map's src/Infrastructure/Idempotency/src/Infrastructure/Scheduling/20.Reporting entries exactly.
    /// </summary>
    [Fact]
    public void NewDomainConstants_MatchRootFolderMap()
    {
        Assert.Equal(18000, LoggingEventIdRanges.Idempotency);
        Assert.Equal(19000, LoggingEventIdRanges.Scheduling);
        Assert.Equal(20000, LoggingEventIdRanges.Reporting);
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
