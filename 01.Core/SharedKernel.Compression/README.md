# SharedKernel.Compression

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Dependencies: BCL only](https://img.shields.io/badge/third--party%20dependencies-none-success)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Brotli and gzip compression for byte arrays, spans and streams, behind one `IPayloadCompressor` interface — where a
> cut-short payload fails instead of silently returning part of the data, and a tiny malicious payload cannot expand
> until the process runs out of memory.**

.NET's `BrotliStream` and `GZipStream` are fast and correct, but on their own they have two properties that are easy to
miss and expensive to discover in production: a truncated payload decompresses **without any error** into the first part
of the original, and decompression has **no size limit**. This package wraps both algorithms so neither can happen by
accident, and returns a `Result` for bad input instead of throwing.

| You get | So that |
| --- | --- |
| `IPayloadCompressor` with Brotli as the default | One injectable interface for queue messages, blobs and cached values, with the better ratio by default |
| A 13-byte frame on every payload (default) | A payload cut short in transit or storage **fails** with `compression.truncated_payload` instead of returning part of the data |
| `MaxDecompressedSize` (64 MiB by default) | A decompression bomb — 102 bytes that expand to 64 MiB — is refused instead of exhausting memory |
| `Result<byte[]>` / `Result` from every `Decompress` | Corrupt, truncated or oversized input is an expected outcome to handle, never an exception to catch |
| Algorithm recorded in the frame | Reading gzip bytes with the Brotli compressor fails clearly instead of possibly decoding garbage |
| Raw mode (`"Brotli.Raw"`, `"GZip.Raw"`) | Systems outside your platform get ordinary Brotli or `.gz` bytes any tool can read |
| Span, `IBufferWriter<byte>` and stream overloads | Hot paths avoid the extra copy, and large payloads never have to fit in memory |
| Zero third-party dependencies | Only `System.IO.Compression` from the .NET base library |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Choosing a compressor](#choosing-a-compressor)
- [Configuration](#configuration)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Compression
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Depends on | `SharedKernel.Primitives` (`Result`, `Error`), `SharedKernel.Configuration` (validated options) |
| Namespaces | `SharedKernel.Compression` (interface, compressors, enums, error codes), `SharedKernel.Compression.Options` (options), `SharedKernel.Compression.Extensions` (registration) |

## Quick start

```csharp
using SharedKernel.Compression;
using SharedKernel.Compression.Extensions;
using SharedKernel.Primitives.Results;

// Program.cs — binds the SharedKernel:Compression section and validates it when the host starts.
builder.Services.AddSharedKernelCompression(builder.Configuration);

public sealed class OrderArchive(IPayloadCompressor compressor)
{
    public byte[] Pack(byte[] json) => compressor.Compress(json);

    public Result<byte[]> Unpack(byte[] stored)
    {
        var result = compressor.Decompress(stored);

        // result.Error.Code is one of the CompressionErrorCodes constants, e.g. "compression.truncated_payload".
        return result;
    }
}
```

`Compress` cannot fail on valid input, so it returns plain bytes. `Decompress` reads data your process did not
necessarily write, so it returns a `Result` and never lets an exception escape for bad input.

## Choosing a compressor

`AddSharedKernelCompression` registers five singletons. Inject the unkeyed one unless you have a reason not to.

| Resolve with | Algorithm | Framing | Use for |
| --- | --- | --- | --- |
| `IPayloadCompressor` (unkeyed) | Brotli | Framed | **Anything you write and read back yourself** — queue messages, blobs, cache values |
| `[FromKeyedServices("Brotli")]` | Brotli | Framed | The same compressor, by name |
| `[FromKeyedServices("GZip")]` | gzip | Framed | gzip inside your own system (rarely the right choice) |
| `[FromKeyedServices("Brotli.Raw")]` | Brotli | Raw | Sending Brotli to a system outside your platform |
| `[FromKeyedServices("GZip.Raw")]` | gzip | Raw | Sending an ordinary `.gz` body to a system outside your platform |

The keys are constants on `CompressionServiceCollectionExtensions` (`BrotliPayloadCompressorKey`,
`GZipPayloadCompressorKey`, `RawBrotliPayloadCompressorKey`, `RawGZipPayloadCompressorKey`).

**Why Brotli is the default:** on 283 KB of JSON it produced **15.6 KB against gzip's 35.0 KB**. gzip also inflates very
small payloads — 1 byte becomes 21 bytes with gzip and 5 with Brotli.

**A payload can only be read by a compressor matching the one that wrote it.** Store or agree on which one you used. A
framed payload fails loudly on a mismatch; a raw one may not.

## Configuration

```json
{
  "SharedKernel": {
    "Compression": {
      "Level": "Optimal",
      "MaxDecompressedSize": 67108864
    }
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `Level` | `Optimal` | `Fastest`, `Optimal`, `SmallestSize` or `NoCompression`; applies to both algorithms |
| `MaxDecompressedSize` | `67108864` (64 MiB) | The most bytes one decompression may produce; must be at least 1 |

Both are validated when the host starts: an unknown `Level` or a `MaxDecompressedSize` of zero or less throws from
`IHost.StartAsync()`, not from the first request. The section is optional — without it the defaults apply.

**`SmallestSize` costs far more than the name suggests on Brotli**, because it selects Brotli's slowest quality level.
Measured on 8 MiB of repetitive data:

| Level | Output | Time |
| --- | --- | --- |
| `Fastest` | 3,757 bytes | 2 ms |
| `Optimal` | 1,061 bytes | 10 ms |
| `SmallestSize` | 1,047 bytes | 225 ms |

That is 22 times the time of `Optimal` for 1.3% less output. Keep `Optimal` unless you have measured your own data.

## How it works

### Why every payload carries a frame

Both `BrotliStream` and `GZipStream` treat the end of their input as the end of the data. A payload that loses its last
bytes — a dropped connection, a partial upload, a truncated database column — therefore decompresses without error into
the **first part** of the original. Measured on a 283 KB payload with the bare .NET streams:

| Compressed bytes kept | Reported | Data returned |
| --- | --- | --- |
| 25% | success | 63,898 bytes, the start of the original |
| 50% | success | 127,863 bytes, the start of the original |
| 99% | success | 278,932 of 282,775 bytes |

Because what comes back is a valid beginning of the original, nothing downstream can tell it from the complete payload.

So by default each payload starts with a 13-byte header, and `Decompress` checks the number of bytes it produced against
the number recorded there. A mismatch returns `compression.truncated_payload`.

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 3 | Marker `SKC` (`0x53 0x4B 0x43`) |
| 3 | 1 | Format version, currently `1` |
| 4 | 1 | Algorithm: `1` Brotli, `2` gzip |
| 5 | 8 | Uncompressed length, little-endian signed 64-bit; `-1` when unknown |
| 13 | … | The compressed bytes |

The header also records the algorithm, so handing gzip bytes to the Brotli compressor returns
`compression.algorithm_mismatch` — worth having, since Brotli has no marker of its own.

The frame is **not** a checksum and not a signature. It detects truncation and a wrong-compressor read, not deliberate
tampering by someone who can rewrite the header. When a payload must be tamper-proof, encrypt it after compressing: an
authenticated cipher such as AES-GCM then covers the compressed bytes.

### Raw mode

Raw compressors write plain Brotli or gzip with no header, so external tools and services can read the output. The cost
is the protection above: a truncated raw payload decompresses to the start of the original and reports success, because
nothing in either format records the original length. The size limit still applies.

gzip adds one more raw-mode behaviour. The gzip format allows several compressed streams one after another (`cat a.gz
b.gz` is a valid file), and `GZipStream` decodes all of them. So two raw gzip payloads joined together decompress to both
originals joined together. That is correct gzip behaviour, and framed payloads are protected from it: the extra output no
longer matches the recorded length, and decompression fails.

Other bytes after a complete payload — in any mode — are ignored, and the payload still decodes exactly.

### The size limit

A few hundred compressed bytes can expand to gigabytes — measured here, **102 bytes of Brotli expand to 64 MiB** of
zeroes, and specially crafted input goes much further. `Decompress` counts the bytes it produces and stops at
`MaxDecompressedSize` with `compression.payload_too_large`, whatever the header claims. A header that claims *more* than
the limit is refused before any work is done; a header that claims *less* is still stopped by the count.

### Streams: the one case a frame cannot cover

To write the header first, `Compress(Stream, Stream)` needs the length before it starts. It reads it from the input when
the input can seek; otherwise it goes back and fills it in when the *output* can seek. When neither can — a network
stream piped straight into another network stream — the header records `-1` and truncation of that payload cannot be
detected. `MemoryStream` and `FileStream` can both seek, so this only affects genuinely piped pipelines.

## Recipes

### 1. Compress, then encrypt

Always compress first. Encrypted bytes look random and do not compress, so the reverse order wastes CPU and saves nothing.

With `SharedKernel.Cryptography`'s `ISymmetricEncryptionService`:

```csharp
// Write: compress, then encrypt.
byte[] packed = compressor.Compress(plaintext);
EncryptedPayload stored = await encryption.EncryptAsync(packed, associatedData, ct);

// Read: decrypt, then decompress.
Result<byte[]> decrypted = await encryption.DecryptAsync(stored, associatedData, ct);
if (decrypted.IsFailure)
    return decrypted.Error;

Result<byte[]> original = compressor.Decompress(decrypted.Value);
```

### 2. Send a `.gz` body to an external system

```csharp
public sealed class PartnerExport(
    [FromKeyedServices(CompressionServiceCollectionExtensions.RawGZipPayloadCompressorKey)] IPayloadCompressor gzip)
{
    public byte[] BuildBody(byte[] csv) => gzip.Compress(csv);   // standard gzip, starts with 0x1F 0x8B
}
```

Use the raw key: a framed gzip payload starts with the `SKC` header, so it is not a file any gzip tool can open.

### 3. Compress a large file without loading it

```csharp
await using var source = File.OpenRead(path);
await using var target = File.Create(path + ".br");
await compressor.CompressAsync(source, target, ct);
```

Both streams stay open; the compressor never disposes them. It reads from the source's current position.

### 4. Avoid the extra copy on a hot path

`Compress(byte[])` builds its output in a growing buffer and then copies it out, costing about twice the payload. Write
straight into a buffer you control instead:

```csharp
var buffer = new ArrayBufferWriter<byte>();
compressor.Compress(payloadSpan, buffer);
Send(buffer.WrittenMemory);
```

### 5. Allow larger payloads on one path only

Keep the global limit strict and give the one path that needs more its own compressor:

```csharp
builder.Services.AddKeyedSingleton<IPayloadCompressor>("LargeExports", (_, _) =>
    new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(
        new CompressionOptions { MaxDecompressedSize = 1L * 1024 * 1024 * 1024 })));   // 1 GiB
```

### 6. Handle a failure

```csharp
var result = compressor.Decompress(body);
if (result.IsFailure)
{
    // Every decompression failure is ErrorType.Validation: the input was bad, the service is fine.
    // Map it to HTTP 400, or dead-letter the message — retrying will not change the bytes.
    return result.Error;
}
```

## Reference

### `IPayloadCompressor`

```csharp
CompressionAlgorithm Algorithm { get; }   // Brotli or GZip
CompressionFraming   Framing   { get; }   // Framed or Raw

byte[]    Compress(byte[] data);
byte[]    Compress(ReadOnlySpan<byte> data);
void      Compress(ReadOnlySpan<byte> data, IBufferWriter<byte> output);
void      Compress(Stream input, Stream output);
ValueTask CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default);

Result<byte[]>    Decompress(byte[] compressed);
Result<byte[]>    Decompress(ReadOnlySpan<byte> compressed);
Result            Decompress(ReadOnlySpan<byte> compressed, IBufferWriter<byte> output);
Result            Decompress(Stream input, Stream output);
ValueTask<Result> DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default);
```

Implementations are stateless and safe to share between threads. The concrete types are `BrotliPayloadCompressor` and
`GZipPayloadCompressor`; both take `(IOptions<CompressionOptions> options, CompressionFraming framing = Framed)`.

### Error codes

Every decompression failure is an `ErrorType.Validation` error with one of these codes (`CompressionErrorCodes`):

| Code | Meaning |
| --- | --- |
| `compression.decompression_failed` | The bytes are corrupt, or were written by a different algorithm |
| `compression.truncated_payload` | Decompressed length differs from the header — cut short, or extended by an appended gzip stream |
| `compression.payload_too_large` | Output would exceed `MaxDecompressedSize` |
| `compression.malformed_payload` | Not a framed payload (often: raw bytes read by a framed compressor), or an unknown format version |
| `compression.algorithm_mismatch` | The header names a different algorithm than the compressor reading it |

### Exceptions

| Thrown | When |
| --- | --- |
| `ArgumentNullException` | A `byte[]`, stream or buffer writer argument is `null` |
| `ArgumentOutOfRangeException` | A compressor is constructed with an undefined `CompressionFraming` or a `MaxDecompressedSize` below 1 |
| `OperationCanceledException` | The token passed to `CompressAsync`/`DecompressAsync` is cancelled |
| `OptionsValidationException` | Invalid `SharedKernel:Compression` settings, at host start |

A failure writing to or reading from *your* stream (an `IOException`, for example) is not bad input and is not converted
to a `Result` — it propagates.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Encrypt, then compress | Compress, then encrypt | Encrypted bytes do not compress |
| Compress an already compressed payload | Compress once | A second pass usually makes it larger |
| Use a `.Raw` compressor for your own queues or storage | Use the unkeyed framed compressor | Raw payloads cannot detect truncation |
| Send framed gzip to an external system | Use `"GZip.Raw"` | The `SKC` header makes it unreadable by gzip tools |
| Read a payload with a different compressor than wrote it | Record or agree on which one was used | Each compressor reads only its own algorithm and framing |
| Keep partial output after a failed stream decompression | Discard the output stream's contents | On failure it may already hold part of the payload |
| Raise `MaxDecompressedSize` globally for one large path | Give that path its own compressor (recipe 5) | The global limit protects every other path |
| Set `Level` to `SmallestSize` on a request path | Keep `Optimal` | 22× slower on Brotli for ~1% smaller output |
| Compress unbounded caller input on a request path | Limit its size first | Compression has no size limit of its own; it is CPU work on data you supply |
| Roll out a change between old unframed and new framed payloads mid-flight | Drain or version in-flight messages first | A framed reader rejects unframed bytes with `malformed_payload` |

## Design decisions

**Why a header and not a trailer?** A trailer would let a non-seekable stream write its length after the fact, but it
cannot be read back: both .NET decompressors read ahead past the end of their data and consume whatever follows —
measured, all 8 bytes of a trailer are lost, on seekable and non-seekable inputs alike.

**Why is framing the default when it breaks plain-gzip compatibility?** Most payloads are written and read back by your
own services, where silently accepting part of a payload is the worse failure. Interop is one key away.

**Why is every failure `Validation` rather than `Unexpected`?** The problem is always the supplied bytes, not the
service. `Validation` maps to HTTP 400 and tells a message consumer that retrying cannot help.

**Why no async overloads for byte arrays and spans?** Compressing data already in memory is pure CPU work. The .NET
compression streams do it inline even through their async methods, so such an overload would return an already-finished
task while implying the work was moved elsewhere. The async members take streams, where there is real I/O to wait for.
If a large in-memory payload must not occupy the current thread, offload it at the call site where that choice is visible.

**Why does appended data not fail?** Both decompressors stop at the end of their own data, and the length check already
rejects anything that changes the output. Rejecting every trailing byte would add a rule that protects nothing further.

**Why one package instead of `.Abstractions` plus providers?** Both algorithms come from the .NET base library, the set is
small and closed, and there is nothing to swap — so one package with keyed registrations is enough.

## AI quick reference

```text
REGISTER     builder.Services.AddSharedKernelCompression(builder.Configuration);   // idempotent, TryAdd
INJECT       IPayloadCompressor                      -> framed Brotli (default; use for data you write AND read back)
             [FromKeyedServices("GZip")]             -> framed gzip
             [FromKeyedServices("Brotli.Raw"|"GZip.Raw")] -> plain Brotli / .gz bytes, external interop ONLY
             Key constants: CompressionServiceCollectionExtensions.{Brotli,GZip,RawBrotli,RawGZip}PayloadCompressorKey
COMPRESS     byte[] Compress(byte[]|ReadOnlySpan<byte>); Compress(span, IBufferWriter<byte>); Compress(Stream, Stream);
             ValueTask CompressAsync(Stream, Stream, ct). Never throws for valid input. Streams are not disposed.
DECOMPRESS   Result<byte[]> Decompress(byte[]|span); Result Decompress(span, IBufferWriter<byte>|Stream in, Stream out);
             ValueTask<Result> DecompressAsync(Stream, Stream, ct). Never throws for bad input; returns Error.Validation.
ERROR CODES  compression.decompression_failed | truncated_payload | payload_too_large | malformed_payload | algorithm_mismatch
CONFIG       Section SharedKernel:Compression. Level (Optimal), MaxDecompressedSize (64 MiB, >= 1). Validated at startup.
FRAME        13 bytes: "SKC", version 1, algorithm (1 Brotli, 2 gzip), int64 LE uncompressed length (-1 unknown).
ORDER        Compress THEN encrypt. Decrypt THEN decompress. Never compress twice.
LIMITS       Raw mode cannot detect truncation. Raw gzip decodes concatenated members. Non-seekable in AND out -> length -1.
FORBIDDEN    Raw compressors for internal storage; SmallestSize on hot paths; raising MaxDecompressedSize globally for one path.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`, and every public member is documented.
- **Stable payload format.** The frame layout and the algorithm numbers are permanent. A future format takes the next
  version number, and payloads from earlier versions stay readable.
- **Decompression never throws for bad input** and never produces more than `MaxDecompressedSize` bytes.
- **Validated at startup.** Invalid settings fail host start, never the first request.
- **Thread-safe.** Every compressor is stateless and registered as a singleton.
- **No third-party dependencies.** Only `System.IO.Compression` from the .NET base library.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
