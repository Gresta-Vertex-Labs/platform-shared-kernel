using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>What one service holds about a data subject, in a machine-readable form (GDPR Article 20).</summary>
/// <param name="Request">The request this export answers.</param>
/// <param name="Source">The service that produced it, for example <c>"orders-api"</c>.</param>
/// <param name="ExportedAt">When the export was produced.</param>
/// <param name="Records">The data, grouped by category; empty when the service holds nothing about the subject.</param>
public sealed record DataSubjectExport(
    DataSubjectRequest Request,
    string Source,
    DateTimeOffset ExportedAt,
    IReadOnlyList<DataSubjectRecord> Records)
{
    /// <summary>
    /// Writes the export as one JSON object with <c>requestId</c>, <c>subjectId</c>, <c>source</c>,
    /// <c>exportedAt</c> and <c>records</c>, each record carrying <c>category</c>, <c>purpose</c> and <c>data</c>.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("requestId", Request.RequestId);
        writer.WriteString("subjectId", Request.SubjectId);
        writer.WriteString("source", Source);
        writer.WriteString("exportedAt", ExportedAt);
        writer.WriteStartArray("records");
        foreach (DataSubjectRecord record in Records)
        {
            writer.WriteStartObject();
            writer.WriteString("category", record.Category);
            if (record.Purpose is not null)
            {
                writer.WriteString("purpose", record.Purpose);
            }

            writer.WritePropertyName("data");
            record.Data.WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>One category of data a service holds about a data subject.</summary>
/// <param name="Category">What the data is, for example <c>"profile"</c> or <c>"orders"</c>.</param>
/// <param name="Data">The data as JSON.</param>
public sealed record DataSubjectRecord(string Category, JsonElement Data)
{
    /// <summary>Gets why the service processes this data (GDPR Article 15(1)(a)), or <see langword="null"/>.</summary>
    public string? Purpose { get; init; }

    /// <summary>Creates a record by serializing <paramref name="value"/> with source-generated metadata.</summary>
    /// <typeparam name="T">The type of the data.</typeparam>
    /// <param name="category">What the data is.</param>
    /// <param name="value">The data.</param>
    /// <param name="typeInfo">The metadata from your <c>JsonSerializerContext</c>.</param>
    /// <returns>The record.</returns>
    public static DataSubjectRecord Create<T>(string category, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(typeInfo);

        return new DataSubjectRecord(category, JsonSerializer.SerializeToElement(value, typeInfo));
    }
}
