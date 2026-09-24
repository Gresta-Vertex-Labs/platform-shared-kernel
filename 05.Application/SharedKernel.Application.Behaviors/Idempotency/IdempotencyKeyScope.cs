using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using SharedKernel.Application.Context;

namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Derives the key <see cref="IdempotencyBehavior{TRequest,TResponse}"/> reserves in
/// <see cref="IRequestIdempotencyStore"/>: the command's own idempotency key, scoped to the tenant and
/// the caller that sent it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> An idempotency key is chosen by the client and travels in plain sight, so it is not a
/// secret. If every caller of a tenant shared one reservation per key, anyone who learned another
/// caller's key and sent the same body would be handed that caller's stored response, including
/// whatever the server generated for them. Scoped to the caller, a key only ever replays to the caller
/// that used it.
/// </para>
/// <para>
/// <b>What identifies the caller.</b> Everything <see cref="IRequestContext"/> reports about who is
/// calling that stays the same across a genuine retry: the tenant (<see cref="IRequestContext.TenantId"/>),
/// the actor kind (<see cref="IRequestContext.ActorKind"/>), the subject (<see cref="IRequestContext.UserId"/>:
/// a user's id, a service's subject, a <see cref="SystemRequestContext"/>'s identity), the OAuth client
/// the caller came through (<see cref="IRequestContext.ClientId"/>) and whoever acts on the caller's
/// behalf (<see cref="IRequestContext.ImpersonatorId"/>). The session id is left out on purpose: it
/// changes when the caller signs in again, and a retry after a fresh sign-in must find its first
/// attempt instead of running a second time.
/// </para>
/// <para>
/// <b>Encoding.</b> SHA-256 over seven fields in this order: a label naming this layout
/// (<see cref="LayoutLabel"/>), the tenant (<c>"D"</c> format), the actor kind (its numeric value,
/// invariant culture), the subject, the client, the impersonator, and the idempotency key exactly as the
/// command supplied it. An absent value is the single byte <c>0x00</c>. A present one is <c>0x01</c>, its
/// length in UTF-16 code units as a 32-bit big-endian integer, then the code units, little-endian. Every
/// field delimits itself and "absent" differs from every string, the empty one included, so the encoding
/// is injective: two different (tenant, caller, key) tuples always hash different bytes, and no part can
/// smuggle in a separator that makes one caller's key read as another's. Code units rather than UTF-8
/// keep that true for a string holding an unpaired surrogate, which UTF-8 would replace.
/// </para>
/// <para>
/// <b>Output.</b> The digest as 64 lowercase hexadecimal characters. The length is fixed however long the
/// key or the identities are, so it fits every store (the EF Core store's key column holds 512
/// characters); lowercase hex holds no separator a store uses and compares the same under a
/// case-insensitive collation. Neither the raw key nor the caller's identifiers reach the store.
/// </para>
/// <para>
/// <b>Why a digest is safe.</b> The key only has to be unique per scope, and finding two inputs with the
/// same SHA-256 digest is computationally infeasible, so two scopes never share a reservation. The
/// request fingerprint is stored and compared on its own, exactly as before, so a replay still needs the
/// same body (or the same <see cref="IIdempotentRequest.Fingerprint"/>).
/// </para>
/// <para>
/// <b>The layout is a stored format.</b> Stores find reservations by this digest. Changing a field, the
/// order or the encoding orphans every reservation already stored, so a retry that spans the deploy runs
/// again. A new layout gets a new label and an operational note, never a silent edit.
/// </para>
/// </remarks>
internal static class IdempotencyKeyScope
{
    /// <summary>
    /// Names this layout. Hashed first, so a digest of a future layout can never equal one of this layout.
    /// </summary>
    internal const string LayoutLabel = "SharedKernel.Application.Behaviors.Idempotency.KeyScope.v1";

    private const byte Absent = 0x00;
    private const byte Present = 0x01;

    /// <summary>The number of UTF-16 code units hashed per chunk.</summary>
    private const int CharsPerChunk = 128;

    /// <summary>
    /// Scopes <paramref name="idempotencyKey"/> to the tenant and the caller <paramref name="caller"/> reports.
    /// </summary>
    /// <param name="caller">The caller of the current request.</param>
    /// <param name="idempotencyKey">The key the command supplied, used exactly as given.</param>
    /// <returns>The key to hand to <see cref="IRequestIdempotencyStore"/>: 64 lowercase hexadecimal characters.</returns>
    internal static string Create(IRequestContext caller, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendField(hash, LayoutLabel);
        AppendField(hash, caller.TenantId?.ToString("D", CultureInfo.InvariantCulture));
        AppendField(hash, ((int)caller.ActorKind).ToString(CultureInfo.InvariantCulture));
        AppendField(hash, caller.UserId);
        AppendField(hash, caller.ClientId);
        AppendField(hash, caller.ImpersonatorId);
        AppendField(hash, idempotencyKey);

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static void AppendField(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            ReadOnlySpan<byte> absent = [Absent];
            hash.AppendData(absent);
            return;
        }

        Span<byte> header = stackalloc byte[1 + sizeof(int)];
        header[0] = Present;
        BinaryPrimitives.WriteInt32BigEndian(header[1..], value.Length);
        hash.AppendData(header);

        // Written explicitly as little-endian so the digest is the same on every platform.
        Span<byte> chunk = stackalloc byte[CharsPerChunk * sizeof(char)];
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var count = Math.Min(remaining.Length, CharsPerChunk);
            for (var i = 0; i < count; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(chunk[(i * sizeof(char))..], remaining[i]);

            hash.AppendData(chunk[..(count * sizeof(char))]);
            remaining = remaining[count..];
        }
    }
}
