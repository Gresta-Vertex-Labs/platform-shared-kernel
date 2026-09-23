namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>Messages shared by the authorization attributes.</summary>
internal static class AuthorizeDataMessages
{
    public const string Fixed =
        "The policy and roles of a SharedKernel authorization attribute are fixed by its constructor. Use [Authorize(Policy = ...)] for a named policy and [RequireRole] for roles.";
}
