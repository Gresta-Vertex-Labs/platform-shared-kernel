using System.Text.Json.Serialization;

namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// STJ source-generated serialization context for <see cref="ProblemDetailsDto"/>.
/// Provides AOT-safe deserialization of <c>application/problem+json</c> responses.
/// </summary>
[JsonSerializable(typeof(ProblemDetailsDto))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class ProblemDetailsJsonContext : JsonSerializerContext;
