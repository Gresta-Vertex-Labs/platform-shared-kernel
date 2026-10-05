using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.DataSubjectRequests;

public sealed partial class DataSubjectRequestTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("", "subject")]
    [InlineData(" ", "subject")]
    [InlineData("req-1", "")]
    public void Request_RejectsAnEmptyIdOrSubject(string requestId, string subjectId) =>
        Assert.ThrowsAny<ArgumentException>(() => new DataSubjectRequest(requestId, subjectId, At));

    [Fact]
    public void Request_KeepsItsValues()
    {
        var request = new DataSubjectRequest("req-1", "customer-42", At, "tenant-a");

        Assert.Equal(("req-1", "customer-42", At, "tenant-a"), (request.RequestId, request.SubjectId, request.RequestedAt, request.TenantId));
        Assert.Equal(request, new DataSubjectRequest("req-1", "customer-42", At, "tenant-a"));
    }

    [Fact]
    public void Record_Create_SerializesWithSourceGeneratedMetadata()
    {
        DataSubjectRecord record = DataSubjectRecord.Create("profile", new Profile("Ayşe", "ayse@example.com"), TestJsonContext.Default.Profile)
            with { Purpose = "Account management" };

        Assert.Equal("ayse@example.com", record.Data.GetProperty("email").GetString());
        Assert.Equal("Account management", record.Purpose);
    }

    [Fact]
    public void Export_WriteTo_ProducesOnePortableJsonDocument()
    {
        var request = new DataSubjectRequest("req-1", "customer-42", At);
        var export = new DataSubjectExport(request, "orders-api", At.AddMinutes(5),
        [
            DataSubjectRecord.Create("profile", new Profile("Ayşe", "ayse@example.com"), TestJsonContext.Default.Profile) with { Purpose = "Account management" },
            new DataSubjectRecord("orders", JsonDocument.Parse("""[{"id":"o-1","total":12.5}]""").RootElement),
        ]);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            export.WriteTo(writer);
        }

        JsonElement root = JsonDocument.Parse(stream.ToArray()).RootElement;
        Assert.Equal("req-1", root.GetProperty("requestId").GetString());
        Assert.Equal("customer-42", root.GetProperty("subjectId").GetString());
        Assert.Equal("orders-api", root.GetProperty("source").GetString());
        Assert.Equal(At.AddMinutes(5), root.GetProperty("exportedAt").GetDateTimeOffset());
        Assert.Equal(2, root.GetProperty("records").GetArrayLength());
        Assert.Equal("Account management", root.GetProperty("records")[0].GetProperty("purpose").GetString());
        Assert.False(root.GetProperty("records")[1].TryGetProperty("purpose", out _));
        Assert.Equal("o-1", root.GetProperty("records")[1].GetProperty("data")[0].GetProperty("id").GetString());
        Assert.DoesNotContain("tenant", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_IsComplete_OnlyWhenNothingWasRetained()
    {
        var request = new DataSubjectRequest("req-1", "customer-42", At);
        var complete = new DataSubjectErasureReceipt(request, "orders-api", At, 3, 1, []);
        var partial = complete with
        {
            Retained = [new RetainedData("invoices", "Tax Procedure Law 213, Art. 253", At.AddYears(5))],
        };

        Assert.True(complete.IsComplete);
        Assert.False(partial.IsComplete);
    }

    public sealed record Profile(string Name, string Email);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Profile))]
    private sealed partial class TestJsonContext : JsonSerializerContext;
}
