using Mono.Cecil;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>One <c>[LoggerMessage]</c> method found in IL.</summary>
/// <param name="Method">The method.</param>
/// <param name="EventId">The explicit EventId, or <see langword="null"/> when the attribute carries none.</param>
/// <param name="Message">The message template, or <see langword="null"/> when the attribute carries none.</param>
internal sealed record LoggerMessageInfo(MethodDefinition Method, int? EventId, string? Message);

/// <summary>Reads <c>[LoggerMessage]</c> attributes (EventId and message template) from a module, nested types included.</summary>
internal static class LoggerMessageReader
{
    private const string AttributeFullName = "Microsoft.Extensions.Logging.LoggerMessageAttribute";

    /// <summary>Every <c>[LoggerMessage]</c> method of the module.</summary>
    public static IEnumerable<LoggerMessageInfo> Read(ModuleDefinition module)
    {
        foreach (var type in IlReader.AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasCustomAttributes)
                    continue;

                var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == AttributeFullName);
                if (attribute is null)
                    continue;

                yield return new LoggerMessageInfo(method, EventIdOf(attribute), MessageOf(attribute));
            }
        }
    }

    private static int? EventIdOf(CustomAttribute attribute)
    {
        foreach (var property in attribute.Properties)
        {
            if (property.Name == "EventId" && property.Argument.Value is int named)
                return named;
        }

        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Type.FullName == "System.Int32" && argument.Value is int positional)
                return positional;
        }

        return null;
    }

    private static string? MessageOf(CustomAttribute attribute)
    {
        foreach (var property in attribute.Properties)
        {
            if (property.Name == "Message" && property.Argument.Value is string named)
                return named;
        }

        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Type.FullName == "System.String" && argument.Value is string positional)
                return positional;
        }

        return null;
    }
}
