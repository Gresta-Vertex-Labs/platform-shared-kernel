using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Persistence.Dapper.Options;

/// <summary>
/// Settings of Dapper sessions (<c>SharedKernel:Persistence:Dapper</c>).
/// </summary>
public sealed class DapperPersistenceOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Dapper";

    /// <summary>
    /// The command timeout (seconds) of commands built with <c>IDbSession.Command</c>.
    /// <see langword="null"/> keeps the provider default (Npgsql: 30 s).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? DefaultCommandTimeoutSeconds { get; set; }
}
