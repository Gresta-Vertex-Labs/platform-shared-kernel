using System.Text.Json;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Contracts.Serialization;

namespace SharedKernel.Contracts.Tests;

/// <summary>
/// Unit tests for <see cref="CursorPagedList{T}"/> (WO-052/P-332).
/// </summary>
public sealed class CursorPagedListTests
{
    // ─── Create factory guard clause ──────────────────────────────────────────

    [Fact]
    public void Create_NullItems_ThrowsArgumentNullException()
    {
        var act = () => CursorPagedList<string>.Create(null!, "cursor-1", true);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("items");
    }

    // ─── HasMore / NextCursor combinations ────────────────────────────────────

    [Fact]
    public void Create_HasMoreTrue_WithPopulatedNextCursor_SetsBothCorrectly()
    {
        var list = CursorPagedList<string>.Create(["a", "b"], "cursor-2", true);

        list.HasMore.Should().BeTrue();
        list.NextCursor.Should().Be("cursor-2");
    }

    [Fact]
    public void Create_HasMoreFalse_WithNullNextCursor_IsTerminalPage()
    {
        var list = CursorPagedList<string>.Create(["a", "b"], null, false);

        list.HasMore.Should().BeFalse();
        list.NextCursor.Should().BeNull();
    }

    [Fact]
    public void Create_EmptyItems_WithHasMoreFalse_IsValid()
    {
        var list = CursorPagedList<string>.Create([], null, false);

        list.Items.Should().BeEmpty();
        list.HasMore.Should().BeFalse();
        list.NextCursor.Should().BeNull();
    }

    // ─── Structural equality ───────────────────────────────────────────────────

    [Fact]
    public void TwoInstancesWithSameFields_AreEqual()
    {
        IReadOnlyList<string> items = ["x", "y"];
        var a = CursorPagedList<string>.Create(items, "cursor-3", true);
        var b = CursorPagedList<string>.Create(items, "cursor-3", true);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void TwoInstancesWithDifferentNextCursor_AreNotEqual()
    {
        IReadOnlyList<string> items = ["x"];
        var a = CursorPagedList<string>.Create(items, "cursor-a", true);
        var b = CursorPagedList<string>.Create(items, "cursor-b", true);

        a.Should().NotBe(b);
    }

    // ─── STJ round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void SerjDeserj_CursorPagedListOfString_RoundTripsCorrectly()
    {
        var original = CursorPagedList<string>.Create(["hello", "world"], "cursor-42", true);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var json = JsonSerializer.Serialize(original, options);
        json.Should().Contain("\"nextCursor\":\"cursor-42\"");
        json.Should().Contain("\"hasMore\":true");
        json.Should().Contain("\"items\":");
        json.Should().Contain("hello");
        json.Should().Contain("world");

        var deserialized = JsonSerializer.Deserialize<CursorPagedList<string>>(json, options);
        deserialized.Should().NotBeNull();
        deserialized!.NextCursor.Should().Be(original.NextCursor);
        deserialized.HasMore.Should().Be(original.HasMore);
        deserialized.Items.Should().BeEquivalentTo(original.Items);
    }

    [Fact]
    public void SerjDeserj_TerminalPage_NullNextCursor_RoundTripsCorrectly()
    {
        var original = CursorPagedList<string>.Create(["last"], null, false);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var json = JsonSerializer.Serialize(original, options);

        // The test-level JsonSerializerOptions only sets PropertyNamingPolicy (matching the
        // established pattern) — DefaultIgnoreCondition is not applied here, so a null NextCursor
        // round-trips as a literal JSON null rather than being omitted.
        json.Should().Contain("\"nextCursor\":null");
        json.Should().Contain("\"hasMore\":false");

        var deserialized = JsonSerializer.Deserialize<CursorPagedList<string>>(json, options);
        deserialized.Should().NotBeNull();
        deserialized!.NextCursor.Should().BeNull();
        deserialized.HasMore.Should().BeFalse();
    }
}
