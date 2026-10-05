using System.Text.Json;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Pagination;
using Xunit;

namespace SharedKernel.Contracts.ConsumerVerify;

[IntegrationEvent("verify.order-placed", Version = 3)]
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId, decimal Total) : IIntegrationEvent;

public sealed record Row(DateTimeOffset CreatedOn, Guid Id);

/// <summary>Exercises the packed public API of SharedKernel.Contracts the way a consuming service would.</summary>
public sealed class ConsumerVerifyTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Package_DependsOnPrimitivesOnly()
    {
        var references = typeof(EventEnvelope).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.Contains("SharedKernel.Primitives", references);
        Assert.DoesNotContain(references, name => name!.StartsWith("SharedKernel.", StringComparison.Ordinal)
            && name != "SharedKernel.Primitives");
    }

    [Fact]
    public void Events_WrapSerializeAndReadBack()
    {
        var evt = new OrderPlaced(Guid.CreateVersion7(), DateTimeOffset.UtcNow, Guid.NewGuid(), 59.97m);
        var tenantId = Guid.NewGuid();

        var envelope = EventEnvelope.Wrap(evt, source: "orders-service", subject: "order/1", tenantId: tenantId);
        var json = JsonSerializer.Serialize(envelope, Web);
        var read = JsonSerializer.Deserialize<EventEnvelope<OrderPlaced>>(json, Web)!;

        Assert.Contains("\"specversion\":\"1.0\"", json);
        Assert.Contains("\"type\":\"verify.order-placed\"", json);
        Assert.Equal(envelope, read);
        Assert.Equal(3, read.DataVersion);
        Assert.Equal(tenantId, read.TenantId);
        Assert.Equal("verify.order-placed v3", IntegrationEventDescriptor.For<OrderPlaced>().ToString());
    }

    [Fact]
    public void Events_RejectATamperedDocument()
    {
        var json = JsonSerializer.Serialize(
            EventEnvelope.Wrap(new OrderPlaced(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), 1m), source: "s"), Web);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EventEnvelope<OrderPlaced>>(json.Replace("verify.order-placed", "verify.other"), Web));
    }

    [Fact]
    public void OffsetPaging()
    {
        var request = PageRequest.Create(page: 2, pageSize: 2);
        Assert.True(request.IsValid);

        var page = PagedList<int>.Create([3, 4], request.Value, totalCount: 5_000_000_000).Map(i => i.ToString());
        var read = JsonSerializer.Deserialize<PagedList<string>>(JsonSerializer.Serialize(page, Web), Web);

        Assert.Equal(page, read);
        Assert.Equal(2_500_000_000, page.TotalPages);

        var invalid = PageRequest.Create(page: 0, pageSize: 5000);
        Assert.Equal(
            [PaginationErrorCodes.PageOutOfRange, PaginationErrorCodes.PageSizeOutOfRange],
            invalid.Errors.Select(e => e.Code));
    }

    [Fact]
    public void CursorPaging()
    {
        var rows = new[]
        {
            new Row(DateTimeOffset.UtcNow, Guid.NewGuid()),
            new Row(DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid()),
            new Row(DateTimeOffset.UtcNow.AddMinutes(-2), Guid.NewGuid()),
        };

        var page = CursorPagedList<Row>.FromLookahead(rows, 2, last => PageCursor.Encode(last.CreatedOn, last.Id));
        Assert.True(page.HasMore);

        var request = CursorPageRequest.Create(page.NextCursor, 2);
        Assert.True(request.IsValid);

        var position = PageCursor.Decode<DateTimeOffset, Guid>(request.Value.Cursor);
        Assert.True(position.IsSuccess);
        Assert.Equal(rows[1].Id, position.Value.Id);

        var forged = PageCursor.Decode<DateTimeOffset, Guid>("v1.not-a-cursor");
        Assert.Equal(PaginationErrorCodes.CursorInvalid, forged.Error.Code);
    }
}
