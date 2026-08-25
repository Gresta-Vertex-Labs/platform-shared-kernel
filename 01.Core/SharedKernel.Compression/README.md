# SharedKernel.Compression

Generic payload compression for the Platform.SharedKernel ecosystem. Pure BCL `System.IO.Compression` — zero third-party NuGet dependencies, AOT-compatible. Depends on `SharedKernel.Primitives` and `SharedKernel.Configuration`.

## Included

**`IPayloadCompressor`** — compress and decompress arbitrary payloads.

```csharp
byte[]         Compress(byte[] data);
Result<byte[]> Decompress(byte[] compressed);
Result         Decompress(Stream input, Stream output);
```

| Implementation | Registration |
|---|---|
| `BrotliPayloadCompressor` | default |
| `GZipPayloadCompressor` | keyed alternate |

`Decompress` returns `Result` rather than throwing on corrupt or truncated input, mirroring `ISymmetricEncryptionService.Decrypt`'s failure shape. Failure codes live in `CompressionErrorCodes`.

## Quick Start

```csharp
// Register (Program.cs)
builder.Services.AddSharedKernelCompression(builder.Configuration);

public class EventPublisher(IPayloadCompressor compressor)
{
    public byte[] Pack(byte[] payload) => compressor.Compress(payload);

    public Result<byte[]> Unpack(byte[] compressed) => compressor.Decompress(compressed);
}
```

## Rules

**Always compress-then-encrypt, never the reverse.** Encrypted output is high-entropy, so compressing it wastes CPU for no size benefit — and compressing after encryption can leak plaintext length information. Consumers that do both (`07.Messaging`'s payload transform, `15.Integration`'s webhook encryption, `02.Caching`'s cache-value encryption) enforce this order structurally.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
