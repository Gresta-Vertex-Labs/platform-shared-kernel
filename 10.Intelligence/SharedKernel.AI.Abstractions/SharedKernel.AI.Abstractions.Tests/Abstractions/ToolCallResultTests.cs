using FluentAssertions;
using SharedKernel.AI.Abstractions.Abstractions;

namespace SharedKernel.AI.Abstractions.Tests.Abstractions;

/// <summary>
/// <see cref="ToolCallResult.ToMessage"/> mapping tests — the produced <see cref="ChatMessage"/> has
/// <see cref="ChatRole.Tool"/>, <see cref="ChatMessage.Name"/> set to <see cref="ToolCallResult.CallId"/>,
/// and <see cref="ChatMessage.Content"/> set to <see cref="ToolCallResult.ResultJson"/>.
/// </summary>
public sealed class ToolCallResultTests
{
    [Fact]
    public void ToMessage_MapsCallIdToName_AndResultJsonToContent_WithToolRole()
    {
        var result = new ToolCallResult
        {
            CallId = "call-1",
            ResultJson = """{"temperature":72}""",
        };

        var message = result.ToMessage();

        message.Role.Should().Be(ChatRole.Tool);
        message.Name.Should().Be("call-1");
        message.Content.Should().Be("""{"temperature":72}""");
    }
}
