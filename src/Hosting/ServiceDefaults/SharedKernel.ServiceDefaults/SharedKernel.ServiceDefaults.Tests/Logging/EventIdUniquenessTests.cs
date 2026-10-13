using System.Reflection;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.ServiceDefaults.Tests.Logging;

/// <summary>
/// Every <c>[LoggerMessage]</c> of the domain's five logging assemblies has a distinct EventId, inside the sub-block of its
/// own package. The ownership table is the one in the domain <c>CLAUDE.md</c> (<c>## Logging</c>), so this test and the
/// platform report share one source. Reflection over the attribute is test-side only; production stays reflection-free.
/// </summary>
public sealed class EventIdUniquenessTests
{
    /// <summary>The domain base, named explicitly: <c>Security.Mtls</c> is 12300 in 12.Security and 13300 here.</summary>
    private const int DomainBase = LoggingEventIdRanges.ServiceDefaults;

    private static List<(string Assembly, string Method, int Id)> Messages() =>
    [
        .. OwnershipTable.Read().Keys.SelectMany(name => Assembly.Load(name).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .Select(pair => (name, $"{pair.Method.DeclaringType!.Name}.{pair.Method.Name}", pair.Attribute!.EventId))),
    ];

    [Fact]
    public void TheOwnershipTable_NamesTheFiveLoggingPackages_OneSubBlockEach()
    {
        var table = OwnershipTable.Read();

        Assert.Equal(5, table.Count);
        Assert.Equal(5, table.Values.Distinct().Count());
        Assert.Equal(1, table["SharedKernel.MultiTenancy"]);
        Assert.Equal(0, table["SharedKernel.ServiceDefaults"]);
        Assert.Equal(3, table["SharedKernel.ServiceDefaults.Security.Mtls"]);
    }

    [Fact]
    public void EveryLoggerMessage_IsFound()
    {
        // 13000, 13001, 13100, 13101, 13200, 13201, 13300, 13301, 13302, 13303 and 13400.
        Assert.True(Messages().Count == 11, "Expected the domain's 11 [LoggerMessage] methods to be discoverable.");
    }

    [Fact]
    public void EveryEventId_IsDistinctAcrossTheDomain()
    {
        var duplicates = Messages()
            .GroupBy(message => message.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(m => m.Method))}")
            .ToList();

        Assert.True(duplicates.Count == 0, "Duplicate EventIds: " + string.Join("; ", duplicates));
    }

    [Fact]
    public void EveryEventId_LiesInTheSubBlockOfItsPackage()
    {
        var table = OwnershipTable.Read();

        var outside = Messages()
            .Where(message => !LoggingEventIdRanges.IsInSubBlock(message.Id, DomainBase, table[message.Assembly]))
            .Select(message => $"{message.Assembly} {message.Method} = {message.Id}")
            .ToList();

        Assert.True(outside.Count == 0, "EventIds outside their package's sub-block: " + string.Join("; ", outside));
    }

    [Fact]
    public void MultiTenancy_KeepsItsSubBlock()
    {
        var tenancy = Messages().Where(message => message.Assembly == "SharedKernel.MultiTenancy").Select(m => m.Id).Order().ToList();

        Assert.Equal([13100, 13101], tenancy);
    }

    [Fact]
    public void ASecurityDomainMtlsId_FailsTheDomainBaseCheck()
    {
        // SharedKernel.Security.Mtls logs in 12300-12399 (12.Security): index 3 of a different domain base.
        Assert.False(LoggingEventIdRanges.IsInSubBlock(12300, DomainBase, 3));
        Assert.True(LoggingEventIdRanges.IsInSubBlock(13300, DomainBase, 3));
    }
}
