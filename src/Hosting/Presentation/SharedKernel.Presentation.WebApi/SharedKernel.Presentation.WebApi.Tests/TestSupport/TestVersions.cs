using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>
/// Entity versions as 06.Persistence sends them since P-562 X4: opaque tokens (28 characters of Base64Url), never the
/// raw row version. Any well-formed token parses; only the persistence layer can tell whether it names a version.
/// </summary>
public static class TestVersions
{
    /// <summary>The text of the version the test endpoints treat as current.</summary>
    public const string Current = "ASoqKioqKioqKioqKioqKioqKioq";

    /// <summary>The text of another, stale version.</summary>
    public const string Stale = "ASkpKSkpKSkpKSkpKSkpKSkpKSkp";

    /// <summary>The current version.</summary>
    public static readonly EntityVersion CurrentVersion = EntityVersion.Parse(Current);
}
