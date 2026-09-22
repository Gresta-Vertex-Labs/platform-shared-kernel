namespace SharedKernel.Persistence.Testing;

/// <summary>
/// An in-memory store whose writes a <see cref="FakeUnitOfWork"/> undoes when its transaction rolls back — as the
/// real unit of work's database rollback and change-tracker reset do.
/// </summary>
internal interface IFakeTransactionParticipant
{
    /// <summary>Captures the current contents.</summary>
    object Capture();

    /// <summary>Puts back contents captured by <see cref="Capture"/>.</summary>
    void Restore(object snapshot);
}
