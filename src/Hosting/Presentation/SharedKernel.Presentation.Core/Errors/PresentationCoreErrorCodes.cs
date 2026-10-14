namespace SharedKernel.Presentation;

/// <summary>
/// The error codes this package answers with that no other package owns. The public alias a client branches on is
/// <c>SharedKernel.Presentation.WebApi</c>'s <c>PresentationErrorCodes</c>.
/// </summary>
internal static class PresentationCoreErrorCodes
{
    /// <summary><c>unauthorized.step_up_required</c>: the caller must authenticate again, more recently or more strongly.</summary>
    public const string StepUpRequired = "unauthorized.step_up_required";
}
