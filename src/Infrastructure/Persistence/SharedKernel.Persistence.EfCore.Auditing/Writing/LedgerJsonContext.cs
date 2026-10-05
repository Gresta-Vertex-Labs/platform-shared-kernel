using System.Text.Json.Serialization;

namespace SharedKernel.Persistence.EfCore.Auditing.Writing;

/// <summary>Source-generated JSON for the details of the ledger's own records.</summary>
[JsonSerializable(typeof(IReadOnlyDictionary<string, string?>))]
internal sealed partial class LedgerJsonContext : JsonSerializerContext;
