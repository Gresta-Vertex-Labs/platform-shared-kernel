namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// The database engine behind the EF Core outbox. It selects the SQL the outbox delivery service uses to lock
/// outbox rows, which differs between engines.
/// </summary>
/// <remarks>
/// MassTransit's own default is SQL Server's lock syntax (<c>SELECT TOP 1 … WITH (UPDLOCK, ROWLOCK, READPAST)</c>),
/// which PostgreSQL rejects with a syntax error on every poll, so nothing is ever delivered.
/// <see cref="OutboxOptions.Database"/> therefore defaults to <see cref="PostgreSql"/>, the platform's database.
/// </remarks>
public enum OutboxDatabase
{
    /// <summary>PostgreSQL (<c>FOR UPDATE SKIP LOCKED</c>). The default.</summary>
    PostgreSql = 0,

    /// <summary>Microsoft SQL Server (<c>WITH (UPDLOCK, ROWLOCK, READPAST)</c>).</summary>
    SqlServer = 1,

    /// <summary>MySQL.</summary>
    MySql = 2,

    /// <summary>SQLite, for tests that check the outbox rows a save writes.</summary>
    Sqlite = 3,
}
