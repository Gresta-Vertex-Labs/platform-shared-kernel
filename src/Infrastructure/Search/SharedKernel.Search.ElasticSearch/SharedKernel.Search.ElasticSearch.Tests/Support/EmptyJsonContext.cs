using System.Text.Json.Serialization;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// A source-generated serializer context that knows no document type: the state a trimmed consumer is in when
/// it forgot <c>.WithSourceSerializerContext(...)</c> or left a document type out of the context it registered.
/// </summary>
[JsonSerializable(typeof(string))]
internal sealed partial class EmptyJsonContext : JsonSerializerContext;
