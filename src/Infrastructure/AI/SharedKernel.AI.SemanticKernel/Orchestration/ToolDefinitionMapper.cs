using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SharedKernel.AI.Abstractions.Abstractions;

namespace SharedKernel.AI.SemanticKernel.Orchestration;

/// <summary>Converts the neutral <see cref="ToolDefinition"/> into an SK/OpenAI-connector <see cref="OpenAIFunction"/>.</summary>
/// <remarks>
/// <para>
/// SK's own <see cref="OpenAIFunction"/> is built from a <see cref="KernelFunctionMetadata"/> whose
/// <see cref="KernelFunctionMetadata.Parameters"/> is a flat list of NAMED parameters, each with its own
/// schema — a different shape than <see cref="ToolDefinition.ParametersJsonSchema"/>'s single whole-object
/// JSON Schema document. This mapper decomposes the schema's top-level <c>properties</c> object into one
/// <see cref="KernelParameterMetadata"/> per property (using the schema's <c>required</c> array for
/// <see cref="KernelParameterMetadata.IsRequired"/>), which recombines faithfully for the common flat
/// object-with-properties shape every mainstream tool/function-calling schema uses. This is a documented
/// Core-phase implementation choice, not a neutral-contract guarantee — a schema using <c>$defs</c>,
/// <c>oneOf</c>, or deeply nested structures at the top level does not round-trip through this
/// decomposition.
/// </para>
/// </remarks>
internal static class ToolDefinitionMapper
{
    public static OpenAIFunction ToOpenAIFunction(ToolDefinition tool)
    {
        var parameters = new List<KernelParameterMetadata>();

        using (var document = JsonDocument.Parse(tool.ParametersJsonSchema))
        {
            var root = document.RootElement;
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        requiredNames.Add(item.GetString()!);
                    }
                }
            }

            if (root.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in propertiesElement.EnumerateObject())
                {
                    parameters.Add(new KernelParameterMetadata(property.Name)
                    {
                        IsRequired = requiredNames.Contains(property.Name),
                        Schema = KernelJsonSchema.Parse(property.Value.GetRawText()),
                    });
                }
            }
        }

        var metadata = new KernelFunctionMetadata(tool.Name)
        {
            Description = tool.Description,
            Parameters = parameters,
        };

        return metadata.ToOpenAIFunction();
    }
}
