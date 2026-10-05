namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>What one service erased, anonymized and kept for a data subject.</summary>
/// <param name="Request">The request this receipt answers.</param>
/// <param name="Source">The service that carried it out, for example <c>"orders-api"</c>.</param>
/// <param name="CompletedAt">When the erasure finished.</param>
/// <param name="ErasedRecords">How many records were deleted.</param>
/// <param name="AnonymizedRecords">How many records were kept with the personal data irreversibly removed.</param>
/// <param name="Retained">Data kept because a law or a legal hold requires it; empty when nothing was kept.</param>
public sealed record DataSubjectErasureReceipt(
    DataSubjectRequest Request,
    string Source,
    DateTimeOffset CompletedAt,
    int ErasedRecords,
    int AnonymizedRecords,
    IReadOnlyList<RetainedData> Retained)
{
    /// <summary>Gets whether nothing about the subject was kept.</summary>
    public bool IsComplete => Retained.Count == 0;
}

/// <summary>Data kept despite an erasure request, and why (GDPR Article 17(3), KVKK Article 7).</summary>
/// <param name="Category">What was kept, for example <c>"invoices"</c>.</param>
/// <param name="LegalBasis">Why it must be kept, for example <c>"Tax Procedure Law 213, Art. 253: 5 years"</c>.</param>
/// <param name="RetainUntil">When it will be erased, or <see langword="null"/> when that is not yet known (a legal hold).</param>
public sealed record RetainedData(string Category, string LegalBasis, DateTimeOffset? RetainUntil);
