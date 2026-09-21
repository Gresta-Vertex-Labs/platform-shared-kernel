using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.EfCore.Auditing.Format;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Format;

/// <summary>
/// The AUDITv3 implementation must reproduce, byte for byte, every test vector published in
/// AUDIT-FORMAT.md (read from the shipped file itself, so the document and the code cannot drift).
/// </summary>
public sealed partial class AuditFormatVectorTests
{
    [GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z0-9.\-_]+)`\s*\|\s*`(?<hex>[0-9a-f]+)`\s*\|", RegexOptions.Multiline)]
    private static partial Regex VectorRow { get; }

    private static Dictionary<string, string> PublishedVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AUDIT-FORMAT.md");
        var markdown = File.ReadAllText(path);
        return VectorRow.Matches(markdown).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["hex"].Value);
    }

    private static Dictionary<string, string> ComputedVectors()
    {
        var signer = new HmacSha256Signer();
        var r1 = AuditFormatVectors.Record1;
        var r2 = AuditFormatVectors.Record2;

        var payload1 = AuditV3Format.EncodePayload("{\"status\":\"pending\"}", "{\"status\":\"approved\"}");
        var message1 = AuditV3Format.EncodeLinkMessage(r1, 1, [], false, AuditFormatVectors.KeyId, AuditV3Format.HmacSha256);
        var mac1 = signer.Sign(message1, AuditFormatVectors.Key);

        var payload2 = AuditV3Format.EncodePayload(null, null);
        var message2 = AuditV3Format.EncodeLinkMessage(r2, 2, mac1, true, AuditFormatVectors.KeyId, AuditV3Format.HmacSha256);
        var mac2 = signer.Sign(message2, AuditFormatVectors.Key);

        var checkpoint = AuditV3Format.EncodeCheckpoint(
            AuditFormatVectors.CheckpointId, r1.TenantId, r1.ResourceType, 1, mac1, AuditFormatVectors.CheckpointCreatedOn, "checkpoints-2026");

        return new Dictionary<string, string>
        {
            ["v1.payload"] = Convert.ToHexStringLower(payload1),
            ["v1.commitment"] = Convert.ToHexStringLower(r1.PayloadHash),
            ["v1.message"] = Convert.ToHexStringLower(message1),
            ["v1.mac"] = Convert.ToHexStringLower(mac1),
            ["v2.payload"] = Convert.ToHexStringLower(payload2),
            ["v2.commitment"] = Convert.ToHexStringLower(r2.PayloadHash),
            ["v2.message"] = Convert.ToHexStringLower(message2),
            ["v2.mac"] = Convert.ToHexStringLower(mac2),
            ["cp.message"] = Convert.ToHexStringLower(checkpoint),
            ["cp.message.sha256"] = Convert.ToHexStringLower(SHA256.HashData(checkpoint)),
        };
    }

    [Fact]
    public void EveryPublishedVector_IsReproducedExactly()
    {
        var computed = ComputedVectors();
        var published = PublishedVectors();

        if (Environment.GetEnvironmentVariable("AUDIT_FORMAT_DUMP") is { Length: > 0 } dumpPath)
            File.WriteAllLines(dumpPath, computed.Select(kv => $"| `{kv.Key}` | `{kv.Value}` |"));

        published.Keys.Should().BeEquivalentTo(computed.Keys, "the spec publishes every vector the code computes");
        foreach (var (name, hex) in computed)
            published[name].Should().Be(hex, $"vector {name} must match AUDIT-FORMAT.md");
    }

    [Fact]
    public void Guid_IsEncodedInRfc9562ByteOrder()
    {
        var writer = new CanonicalWriter(16);
        writer.WriteGuid(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
        Convert.ToHexStringLower(writer.ToArray()).Should().Be("00112233445566778899aabbccddeeff");
    }

    [Fact]
    public void Timestamp_IsTruncatedToWholeMicroseconds()
    {
        var value = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)).AddTicks(1_234_567);
        var truncated = AuditTimestamp.Truncate(value);

        truncated.Offset.Should().Be(TimeSpan.Zero);
        (truncated.UtcTicks % 10).Should().Be(0);
        AuditTimestamp.ToUnixMicroseconds(truncated).Should().Be(AuditTimestamp.ToUnixMicroseconds(value));
    }

    [Fact]
    public void NullAndEmptyOptionalStrings_EncodeDifferently()
    {
        var a = AuditV3Format.EncodePayload(null, null);
        var b = AuditV3Format.EncodePayload(string.Empty, null);
        a.Should().NotEqual(b);
    }

    [Fact]
    public void KeyIdAndAlgorithm_AreBoundIntoTheMessage()
    {
        var r = AuditFormatVectors.Record1;
        var baseline = AuditV3Format.EncodeLinkMessage(r, 1, [], false, "k1", AuditV3Format.HmacSha256);

        AuditV3Format.EncodeLinkMessage(r, 1, [], false, "k2", AuditV3Format.HmacSha256).Should().NotEqual(baseline);
        AuditV3Format.EncodeLinkMessage(r, 1, [], false, "k1", "HMAC-SHA512").Should().NotEqual(baseline);
    }
}
