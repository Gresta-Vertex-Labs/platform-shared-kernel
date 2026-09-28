# SharedKernel.Compression

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Dependencies: BCL only](https://img.shields.io/badge/third--party%20dependencies-none-success)

> **Brotli and gzip compression for byte arrays, spans and streams behind one `IPayloadCompressor` — where a cut-short
> payload fails instead of silently returning part of the data, and a tiny malicious payload cannot expand until the
> process runs out of memory.**

.NET's `BrotliStream` and `GZipStream` are fast and correct, but on their own a truncated payload decompresses **without
any error** into the first part of the original, and decompression has **no size limit**. This package wraps both
algorithms so neither can happen by accident, and returns a `Result` for bad input instead of throwing.

| You get | So that |
| --- | --- |
| `IPayloadCompressor` with Brotli as the default | One injectable interface for queue messages, blobs and cached values, with the better ratio by default |
| A 13-byte frame on every payload (default) | A payload cut short in transit or storage **fails** with `compression.truncated_payload` instead of returning part of the data |
| `MaxDecompressedSize` (64 MiB by default) | A decompression bomb — 102 bytes that expand to 64 MiB — is refused instead of exhausting memory |
| `Result<byte[]>` / `Result` from every `Decompress` | Corrupt, truncated or oversized input is an expected outcome to handle, never an exception to catch |
| Algorithm recorded in the frame | Reading gzip bytes with the Brotli compressor fails clearly instead of possibly decoding garbage |
| Raw mode (`"Brotli.Raw"`, `"GZip.Raw"`) | Systems outside your platform get ordinary Brotli or `.gz` bytes any tool can read |
| Span, `IBufferWriter<byte>` and stream overloads | Hot paths avoid the extra copy, and large payloads never have to fit in memory |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Compression" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation — reference it from **any** project |
| Depends on | `SharedKernel.Primitives` (`Result`, `Error`), `SharedKernel.Configuration` (validated options); BCL `System.IO.Compression` only |
| Namespaces | `SharedKernel.Compression` (interface, compressors, enums, error codes), `SharedKernel.Compression.Options`, `SharedKernel.Compression.Extensions` (registration) |

## Quick start

```csharp
using SharedKernel.Compression.Extensions;

// Binds SharedKernel:Compression (optional) and validates it when the host starts.
builder.Services.AddSharedKernelCompression(builder.Configuration);
```

```json
{
  "SharedKernel": {
    "Compression": { "Level": "Optimal", "MaxDecompressedSize": 67108864 }
  }
}
```

```csharp
using SharedKernel.Compression;
using SharedKernel.Primitives.Results;

public sealed class OrderArchive(IPayloadCompressor compressor)
{
    public byte[] Pack(byte[] json) => compressor.Compress(json);

    // On failure, result.Error.Code is one of the CompressionErrorCodes, e.g. "compression.truncated_payload".
    public Result<byte[]> Unpack(byte[] stored) => compressor.Decompress(stored);
}
```

`Compress` cannot fail on valid input, so it returns plain bytes. `Decompress` reads data your process did not
necessarily write, so it returns a `Result` and never lets an exception escape for bad input.

## How it works

### Choosing a compressor

`AddSharedKernelCompression` registers five singletons. Inject the unkeyed one unless you have a reason not to.

| Resolve with | Algorithm | Framing | Use for |
| --- | --- | --- | --- |
| `IPayloadCompressor` (unkeyed) | Brotli | Framed | **Anything you write and read back yourself** — queue messages, blobs, cache values |
| `[FromKeyedServices("Brotli")]` | Brotli | Framed | The same compressor, by name |
| `[FromKeyedServices("GZip")]` | gzip | Framed | gzip inside your own system (rarely the right choice) |
| `[FromKeyedServices("Brotli.Raw")]` | Brotli | Raw | Sending Brotli to a system outside your platform |
| `[FromKeyedServices("GZip.Raw")]` | gzip | Raw | Sending an ordinary `.gz` body to a system outside your platform |

The keys are constants on `CompressionServiceCollectionExtensions`. Brotli is the default because on 283 KB of JSON it
produced **15.6 KB against gzip's 35.0 KB**, and gzip inflates tiny payloads (1 byte becomes 21 bytes; Brotli: 5). A
payload can only be read by a compressor matching the one that wrote it — a framed payload fails loudly on a mismatch; a
raw one may not.

### The frame

Both .NET streams treat the end of their input as the end of the data, so a payload that loses its last bytes
decompresses without error into a valid **beginning** of the original — measured on 283 KB, keeping 25% of the
compressed bytes returned 63,898 bytes and reported success. So by default each payload starts with a 13-byte header,
and `Decompress` checks the bytes it produced against the length recorded there.

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 3 | Marker `SKC` (`0x53 0x4B 0x43`) |
| 3 | 1 | Format version, currently `1` |
| 4 | 1 | Algorithm: `1` Brotli, `2` gzip |
| 5 | 8 | Uncompressed length, little-endian signed 64-bit; `-1` when unknown |
| 13 | … | The compressed bytes |

- The layout and the algorithm numbers are a permanent wire format; a future format takes the next version number and
  earlier versions stay readable. An unknown version returns `compression.malformed_payload`.
- The frame is **not** a checksum or a signature. It detects truncation and a wrong-compressor read, not tampering by
  someone who can rewrite the header — encrypt after compressing when a payload must be tamper-proof.
- **Raw mode** writes plain Brotli or gzip with no header. A truncated raw payload decompresses to the start of the
  original and reports success. Raw gzip also decodes concatenated members (`cat a.gz b.gz`) into both originals joined;
  in framed mode that extra output breaks the length check and fails.
- Other bytes after a complete payload are ignored in every mode.

### The size limit

`Decompress` counts the bytes it produces and stops at `MaxDecompressedSize` with `compression.payload_too_large`,
whatever the header claims. A header claiming *more* than the limit is refused before any work; a header claiming *less*
is still stopped by the count. Nothing is pre-allocated from a payload's claimed length.

### Streams

To write the header first, `Compress(Stream, Stream)` needs the length up front. It reads it from a seekable input, or
fills it in afterwards on a seekable output. When neither stream can seek (a network stream piped into another), the
header records `-1` and truncation of that payload cannot be detected. Streams are never disposed by the compressor; it
reads from the input's current position.

## Recipes

### 1. Compress, then encrypt

Encrypted bytes look random and do not compress, so always compress first. With `SharedKernel.Cryptography`'s
`ISymmetricEncryptionService`:

```csharp
byte[] packed = compressor.Compress(plaintext);
EncryptedPayload stored = await encryption.EncryptAsync(packed, associatedData, ct);

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

### 3. Compress a large file without loading it

```csharp
await using var source = File.OpenRead(path);
await using var target = File.Create(path + ".br");
await compressor.CompressAsync(source, target, ct);
```

### 4. Avoid the extra copy on a hot path

`Compress(byte[])` builds its output in a growing buffer and copies it out. Write into a buffer you control instead:

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

Every decompression failure is `ErrorType.Validation`: the input was bad, the service is fine. Map it to HTTP 400 or
dead-letter the message — retrying will not change the bytes.

## Configuration

Section `SharedKernel:Compression` (`CompressionOptions.SectionName`), optional, validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Compression:Level` | `CompressionLevel` | `Optimal` | `Fastest`, `Optimal`, `SmallestSize` or `NoCompression`; applies to both algorithms |
| `SharedKernel:Compression:MaxDecompressedSize` | `long` | `67108864` (64 MiB, `CompressionOptions.DefaultMaxDecompressedSize`) | Most bytes one decompression may produce; at least 1 |

`SmallestSize` selects Brotli's slowest quality level. On 8 MiB of repetitive data: `Fastest` 3,757 bytes in 2 ms,
`Optimal` 1,061 bytes in 10 ms, `SmallestSize` 1,047 bytes in 225 ms — 22× the time for 1.3% less output.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddSharedKernelCompression(IConfiguration)` | `CompressionOptions` (validated); `IPayloadCompressor` unkeyed (framed Brotli) and keyed `"Brotli"`, `"GZip"`, `"Brotli.Raw"`, `"GZip.Raw"` — all singletons, `TryAdd` (a second call is a no-op) |

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

Implementations (`BrotliPayloadCompressor`, `GZipPayloadCompressor`) are stateless, thread-safe, and take
`(IOptions<CompressionOptions> options, CompressionFraming framing = Framed)`.

### Errors

Every decompression failure is `ErrorType.Validation` (`CompressionErrorCodes`):

| Code | When |
| --- | --- |
| `compression.decompression_failed` | The bytes are corrupt, or were written by a different algorithm |
| `compression.truncated_payload` | Decompressed length differs from the header — cut short, or extended by an appended gzip stream |
| `compression.payload_too_large` | Output would exceed `MaxDecompressedSize` |
| `compression.malformed_payload` | Not a framed payload (often raw bytes read by a framed compressor), or an unknown format version |
| `compression.algorithm_mismatch` | The header names a different algorithm than the compressor reading it |

Thrown: `ArgumentNullException` for a null argument; `ArgumentOutOfRangeException` for an undefined `CompressionFraming`
or a `MaxDecompressedSize` below 1 at construction; `OperationCanceledException` on cancellation;
`OptionsValidationException` at host start for invalid settings. An `IOException` from *your* stream propagates.

### Logging

The package does not log.

## Testing

There is no fake: the compressors are pure, in-memory and fast, so use the real ones in unit tests.

```csharp
var compressor = new BrotliPayloadCompressor(
    Microsoft.Extensions.Options.Options.Create(new CompressionOptions()));
```

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
| Compress unbounded caller input on a request path | Limit its size first | Compression is CPU work on data you supply |
| Switch between unframed and framed payloads with messages in flight | Drain or version in-flight messages first | A framed reader rejects unframed bytes with `malformed_payload` |

## Design decisions

**Why a header and not a trailer?** A trailer would let a non-seekable stream write its length afterwards, but it cannot
be read back: both .NET decompressors read ahead past the end of their data — measured, all 8 bytes of a trailer are lost.

**Why is framing the default when it breaks plain-gzip compatibility?** Most payloads are written and read back by your
own services, where silently accepting part of a payload is the worse failure. Interop is one key away.

**Why is every failure `Validation` rather than `Unexpected`?** The problem is always the supplied bytes, not the
service. `Validation` maps to HTTP 400 and tells a message consumer that retrying cannot help.

**Why no async overloads for byte arrays and spans?** Compressing data already in memory is pure CPU work; an async
overload would return a finished task while implying the work moved elsewhere. Offload at the call site if you must.

**Why one package instead of `.Abstractions` plus providers?** Both algorithms come from the .NET base library, the set is
small and closed, and there is nothing to swap — one package with keyed registrations is enough.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Core domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
