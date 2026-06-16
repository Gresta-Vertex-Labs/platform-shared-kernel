namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>Internal DTO for deserializing RFC 7807 Problem Details JSON responses.</summary>
internal sealed class ProblemDetailsDto
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }
}
