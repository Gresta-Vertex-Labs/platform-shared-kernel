using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Contracts.Tests;

public sealed record SgItem(Guid Id, string Name);

[IntegrationEvent("sourcegen.item-created", Version = 4)]
public sealed record SgItemCreated(Guid EventId, DateTimeOffset OccurredOn, Guid ItemId) : IIntegrationEvent;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PagedList<SgItem>))]
[JsonSerializable(typeof(CursorPagedList<SgItem>))]
[JsonSerializable(typeof(EventEnvelope<SgItemCreated>))]
internal sealed partial class WebContext : JsonSerializerContext;

[JsonSerializable(typeof(PagedList<SgItem>))]
[JsonSerializable(typeof(CursorPagedList<SgItem>))]
[JsonSerializable(typeof(EventEnvelope<SgItemCreated>))]
internal sealed partial class DefaultContext : JsonSerializerContext;

/// <summary>
/// The test assembly stands in for a consuming service: the source generator runs here, not in the package, so it
/// cannot call a constructor the package keeps internal.
/// </summary>
public sealed class SourceGeneratedContextTests
{
    private static readonly JsonSerializerOptions WebReflection = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions DefaultReflection = new();

    private static readonly SgItem[] Items = [new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "a"), new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "b")];

    public static TheoryData<string> Policies => new() { "web", "default" };

    private static (JsonSerializerContext Context, JsonSerializerOptions Reflection) For(string policy) =>
        policy == "web" ? (WebContext.Default, WebReflection) : (DefaultContext.Default, DefaultReflection);

    [Theory]
    [MemberData(nameof(Policies))]
    public void PagedList_RoundTripsAndMatchesReflection(string policy)
    {
        var (context, reflection) = For(policy);
        var page = PagedList<SgItem>.Create(Items, 2, 2, 5_000_000_000);

        var json = JsonSerializer.Serialize(page, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<PagedList<SgItem>>)context.GetTypeInfo(typeof(PagedList<SgItem>))!);
        var read = JsonSerializer.Deserialize(json, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<PagedList<SgItem>>)context.GetTypeInfo(typeof(PagedList<SgItem>))!);

        read.Should().Be(page);
        json.Should().Be(JsonSerializer.Serialize(page, reflection));
        JsonNode.Parse(json)!.AsObject().Select(p => p.Key)
            .Should().BeEquivalentTo("items", "page", "pageSize", "totalCount", "totalPages", "hasNextPage", "hasPreviousPage");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void CursorPagedList_RoundTripsAndMatchesReflection(string policy)
    {
        var (context, reflection) = For(policy);
        var page = CursorPagedList<SgItem>.Create(Items, "v1.abc");
        var info = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<CursorPagedList<SgItem>>)context.GetTypeInfo(typeof(CursorPagedList<SgItem>))!;

        var json = JsonSerializer.Serialize(page, info);
        var read = JsonSerializer.Deserialize(json, info);

        read.Should().Be(page);
        json.Should().Be(JsonSerializer.Serialize(page, reflection));
        JsonNode.Parse(json)!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("items", "nextCursor", "hasMore");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Envelope_RoundTripsAndMatchesReflection(string policy)
    {
        var (context, reflection) = For(policy);
        var evt = new SgItemCreated(Guid.CreateVersion7(), new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), Guid.NewGuid());
        var envelope = EventEnvelope.Wrap(evt, "items-service", subject: "item/1", tenantId: Guid.NewGuid(), correlationId: "c", causationId: "k");
        var info = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<EventEnvelope<SgItemCreated>>)context.GetTypeInfo(typeof(EventEnvelope<SgItemCreated>))!;

        var json = JsonSerializer.Serialize(envelope, info);
        var read = JsonSerializer.Deserialize(json, info);

        read.Should().Be(envelope);
        json.Should().Be(JsonSerializer.Serialize(envelope, reflection));
    }

    private static T? Read<T>(string policy, string json) =>
        (T?)JsonSerializer.Deserialize(json, For(policy).Context.GetTypeInfo(typeof(T))!);

    [Theory]
    [InlineData("{\"page\":1,\"pageSize\":2,\"totalCount\":0}")]
    [InlineData("{\"items\":null,\"page\":1,\"pageSize\":2,\"totalCount\":0}")]
    [InlineData("{\"items\":[],\"page\":0,\"pageSize\":2,\"totalCount\":0}")]
    [InlineData("{\"items\":[],\"page\":1,\"pageSize\":0,\"totalCount\":0}")]
    [InlineData("{\"items\":[],\"page\":1,\"pageSize\":2,\"totalCount\":-1}")]
    [InlineData("{\"items\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"a\"},{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"b\"},{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"c\"}],\"page\":1,\"pageSize\":2,\"totalCount\":3}")]
    public void PagedList_InvalidDocumentThrowsJsonException(string json)
    {
        foreach (var policy in new[] { "web", "default" })
            FluentActions.Invoking(() => Read<PagedList<SgItem>>(policy, json)).Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("{\"nextCursor\":\"v1.a\"}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[],\"nextCursor\":\"\"}")]
    [InlineData("{\"items\":[],\"nextCursor\":\"   \"}")]
    public void CursorPagedList_InvalidDocumentThrowsJsonException(string json) =>
        FluentActions.Invoking(() => Read<CursorPagedList<SgItem>>("web", json)).Should().Throw<JsonException>();

    [Fact]
    public void CursorPagedList_OverlongCursorThrowsJsonException() =>
        FluentActions.Invoking(() =>
                Read<CursorPagedList<SgItem>>("web", $"{{\"items\":[],\"nextCursor\":\"{new string('a', PageCursor.MaxLength + 1)}\"}}"))
            .Should().Throw<JsonException>();

    [Theory]
    [MemberData(nameof(Policies))]
    public void PagedLists_IgnoreComputedMembersOnRead(string policy)
    {
        var paged = Read<PagedList<SgItem>>(policy, "{\"items\":[],\"page\":1,\"pageSize\":2,\"totalCount\":9,\"totalPages\":99,\"hasNextPage\":false,\"hasPreviousPage\":true}")!;
        paged.TotalPages.Should().Be(5);
        paged.HasNextPage.Should().BeTrue();
        paged.HasPreviousPage.Should().BeFalse();

        var cursor = Read<CursorPagedList<SgItem>>(policy, "{\"items\":[],\"nextCursor\":\"v1.a\",\"hasMore\":false}")!;
        cursor.HasMore.Should().BeTrue();
    }

    [Theory]
    [InlineData("type", "sourcegen.other")]
    [InlineData("specversion", "2.0")]
    [InlineData("source", " ")]
    [InlineData("dataversion", 0)]
    [InlineData("datacontenttype", "text/plain")]
    [InlineData("tenantid", "00000000-0000-0000-0000-000000000000")]
    [InlineData("id", "00000000-0000-0000-0000-000000000001")]
    public void Envelope_InvalidDocumentThrowsJsonException(string member, object value)
    {
        var evt = new SgItemCreated(Guid.CreateVersion7(), new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), Guid.NewGuid());
        var json = JsonNode.Parse(JsonSerializer.Serialize(EventEnvelope.Wrap(evt, "items-service", tenantId: Guid.NewGuid()), WebContext.Default.EventEnvelopeSgItemCreated))!.AsObject();
        json[member] = JsonValue.Create(value);

        FluentActions.Invoking(() => Read<EventEnvelope<SgItemCreated>>("web", json.ToJsonString())).Should().Throw<JsonException>();
    }

    [Fact]
    public void PublicConstructors_MatchTheFactories()
    {
        var created = PagedList<SgItem>.Create(Items, 2, 2, 5);
        new PagedList<SgItem>(Items, 2, 2, 5).Should().Be(created);
        new PagedList<SgItem>(Items.ToList(), 2, 2, 5).Items.Should().BeEquivalentTo(created.Items);
        FluentActions.Invoking(() => new PagedList<SgItem>(Items, 0, 2, 5)).Should().Throw<JsonException>();
        FluentActions.Invoking(() => new PagedList<SgItem>(null!, 1, 2, 5)).Should().Throw<JsonException>();

        new CursorPagedList<SgItem>(Items, "v1.a").Should().Be(CursorPagedList<SgItem>.Create(Items, "v1.a"));
        FluentActions.Invoking(() => new CursorPagedList<SgItem>(Items, " ")).Should().Throw<JsonException>();

        var evt = new SgItemCreated(Guid.CreateVersion7(), new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), Guid.NewGuid());
        var wrapped = EventEnvelope.Wrap(evt, "items-service", subject: "item/1");
        new EventEnvelope<SgItemCreated>(
                wrapped.SpecVersion, wrapped.Id, wrapped.Source, wrapped.Type, wrapped.DataVersion, wrapped.Time,
                wrapped.Subject, wrapped.DataContentType, wrapped.TenantId, wrapped.CorrelationId, wrapped.CausationId, wrapped.Data)
            .Should().Be(wrapped);
        FluentActions.Invoking(() => new EventEnvelope<SgItemCreated>(
                "1.0", evt.EventId, "items-service", "sourcegen.other", 4, evt.OccurredOn, null, "application/json", null, null, null, evt))
            .Should().Throw<JsonException>();
    }
}
