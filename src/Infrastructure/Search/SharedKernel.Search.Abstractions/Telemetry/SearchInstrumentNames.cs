namespace SharedKernel.Search.Abstractions.Telemetry;

/// <summary>
/// The instrument names of the <c>SharedKernel.Search</c> meter, <c>sharedkernel.search.*</c>. Both providers create these
/// two instruments on the meter their <c>IMeterFactory</c> hands out for that name and tell their measurements apart by
/// the <see cref="SearchTagKeys.Provider"/> tag, so a dashboard keeps working across a provider swap. Declared once
/// here, the names are identical on both providers by construction.
/// </summary>
public static class SearchInstrumentNames
{
    /// <summary>
    /// The operation-duration histogram, unit <c>s</c> (<c>sharedkernel.search.operation.duration</c>). Tags: the three
    /// <see cref="SearchTagKeys"/>, <c>outcome</c> and, on a failure, <c>error.type</c>.
    /// </summary>
    public const string OperationDuration = "sharedkernel.search.operation.duration";

    /// <summary>
    /// The document counter, unit <c>{document}</c> (<c>sharedkernel.search.documents</c>): documents a successful write
    /// or delete was asked to affect. Tags: the three <see cref="SearchTagKeys"/>.
    /// </summary>
    public const string Documents = "sharedkernel.search.documents";
}
