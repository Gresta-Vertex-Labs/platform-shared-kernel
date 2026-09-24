namespace BillingApi.Api;

/// <summary>The body of a 201 Created answer: the id of the new resource, whose URI is in <c>Location</c>.</summary>
public sealed record ResourceCreated(Guid Id);
