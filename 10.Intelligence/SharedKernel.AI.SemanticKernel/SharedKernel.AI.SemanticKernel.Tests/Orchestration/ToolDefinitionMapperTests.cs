using FluentAssertions;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.SemanticKernel.Orchestration;

namespace SharedKernel.AI.SemanticKernel.Tests.Orchestration;

public sealed class ToolDefinitionMapperTests
{
    [Fact]
    public void ToOpenAIFunction_DecomposesFlatObjectSchema_IntoNamedParameters()
    {
        var tool = new ToolDefinition
        {
            Name = "get_weather",
            Description = "Gets the current weather for a city.",
            ParametersJsonSchema = """
                {
                  "type": "object",
                  "properties": {
                    "city": { "type": "string", "description": "City name" },
                    "unit": { "type": "string", "enum": ["celsius", "fahrenheit"] }
                  },
                  "required": ["city"]
                }
                """,
        };

        var function = ToolDefinitionMapper.ToOpenAIFunction(tool);

        function.FunctionName.Should().Be("get_weather");
        function.Description.Should().Be("Gets the current weather for a city.");
        function.Parameters.Should().HaveCount(2);

        var cityParameter = function.Parameters.Single(p => p.Name == "city");
        cityParameter.IsRequired.Should().BeTrue();

        var unitParameter = function.Parameters.Single(p => p.Name == "unit");
        unitParameter.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void ToOpenAIFunction_NoProperties_ProducesEmptyParameterList()
    {
        var tool = new ToolDefinition
        {
            Name = "ping",
            Description = "No-argument tool.",
            ParametersJsonSchema = """{ "type": "object", "properties": {} }""",
        };

        var function = ToolDefinitionMapper.ToOpenAIFunction(tool);

        function.Parameters.Should().BeEmpty();
    }
}
