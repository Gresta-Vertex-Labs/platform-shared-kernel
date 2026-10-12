using System.Reflection;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.ServiceDefaults.Tests.Logging;

/// <summary>
/// Every <c>[LoggerMessage]</c> of the domain's seven production assemblies has a distinct EventId inside the domain
/// block. Reflection over the attribute is test-side only; production stays reflection-free.
/// </summary>
public sealed class EventIdUniquenessTests
{
    private static readonly string[] Assemblies =
    [
        "SharedKernel.ServiceDefaults",
        "SharedKernel.ServiceDefaults.Security",
        "SharedKernel.ServiceDefaults.Security.Mtls",
        "SharedKernel.ServiceDefaults.Localization",
        "SharedKernel.MultiTenancy",
    ];

    private static List<(string Assembly, string Method, int Id)> Messages() =>
    [
        .. Assemblies.SelectMany(name => Assembly.Load(name).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .Select(pair => (name, $"{pair.Method.DeclaringType!.Name}.{pair.Method.Name}", pair.Attribute!.EventId))),
    ];

    [Fact]
    public void EveryLoggerMessage_IsFound()
    {
        // 13000, 13001, 13003, 13004, 13005, 13006, 13007, 13008, 13100, 13101 and 13002.
        Assert.True(Messages().Count >= 11, "Expected the domain's [LoggerMessage] methods to be discoverable.");
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
    public void EveryEventId_LiesInTheServiceDefaultsBlock()
    {
        var outside = Messages()
            .Where(message => message.Id < LoggingEventIdRanges.ServiceDefaults || message.Id >= LoggingEventIdRanges.ServiceDefaults + 1000)
            .Select(message => $"{message.Method} = {message.Id}")
            .ToList();

        Assert.True(outside.Count == 0, "EventIds outside 13000-13999: " + string.Join("; ", outside));
    }

    [Fact]
    public void MultiTenancy_KeepsItsOwnSubBlock()
    {
        var tenancy = Messages().Where(message => message.Assembly == "SharedKernel.MultiTenancy").ToList();

        Assert.All(tenancy, message => Assert.InRange(message.Id, 13100, 13199));
        Assert.All(
            Messages().Where(message => message.Assembly != "SharedKernel.MultiTenancy"),
            message => Assert.InRange(message.Id, 13000, 13099));
    }
}
