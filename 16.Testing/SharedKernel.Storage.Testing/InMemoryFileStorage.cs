using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage;

namespace SharedKernel.Testing.Storage;

// Inside the namespace so they win over the package's global HotChocolate/GreenDonut usings (Error, Result<T>).
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

/// <summary>
/// In-memory storage provider store for tests: one named store implementing every
/// <see cref="IFileStorage"/> member faithfully — write conditions, SHA-256 checksum verification, range
/// reads, headers/metadata/tags/tier, prefix and folder listing with paging, batch delete, copies,
/// presigned URLs and multipart uploads — without a network or a container.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>raw provider store</em>, the in-memory counterpart of <c>SharedKernel.Storage.S3</c>'s
/// store: register it with <see cref="InMemoryStorageBuilderExtensions.AddInMemoryStore(IStorageBuilder, string, Action{InMemoryFileStorageOptions}?)"/>
/// or <see cref="InMemoryStorageBuilderExtensions.AddInMemoryTenantStore(IStorageBuilder, string, Action{InMemoryFileStorageOptions}?)"/>
/// and application code receives it through the storage registry — which validates every request and
/// applies tenant prefixes — exactly as it receives a real provider's store. It can also be used directly
/// as an <see cref="IFileStorage"/>; it validates its own requests with <see cref="StorageValidation"/>, as
/// the real providers do.
/// </para>
/// <para>
/// The inspection helpers (<see cref="UploadedKeys"/>, <see cref="WasUploaded"/>, <see cref="Seed(string, byte[], string?, IReadOnlyDictionary{string, string}?)"/>,
/// <see cref="GetContent"/>…) take and return <em>store</em> keys. Through a tenant view a key is stored
/// under the tenant's prefix: build it with <see cref="TenantKey"/>.
/// </para>
/// <para>
/// ETags are quoted, generated from an internal sequence (a new one on every write, as for a real
/// object), and times come from <see cref="InMemoryFileStorageOptions.Clock"/> — a fixed instant by
/// default, never real wall-clock time. Content is buffered in memory; upload streams are read to their
/// end and never disposed, per the caller-owned contract.
/// </para>
/// <para>
/// References only <c>SharedKernel.Storage.Abstractions</c>, never a provider package, and no sibling
/// capability folder of this package — including <c>MinioContainerFixture</c>: the
/// in-memory store and the real-provider container fixture are independent test paths.
/// </para>
/// </remarks>
public sealed class InMemoryFileStorage : IFileStorage
{
    /// <summary>The key prefix of a tenant store's tenant views: <c>tenants/{tenantId}/</c>.</summary>
    private const string TenantsFolder = "tenants/";

    private const string UrlScheme = "memory";
    private const string MethodGet = "GET";
    private const string MethodPut = "PUT";
    private const string MethodPost = "POST";
    private const string ContentTypeHeader = "Content-Type";
    private const string IfNoneMatchHeader = "If-None-Match";
    private const string AnyETag = "*";
    private const string ChecksumSha256Header = "x-amz-checksum-sha256";
    private const string MetadataHeaderPrefix = "x-amz-meta-";
    private const string KeyField = "key";
    private const string PolicyField = "policy";

    private const string OperationUpload = "upload";
    private const string OperationCopy = "copy";
    private const string OperationDelete = "delete";
    private const string OperationDeleteMany = "batch delete";
    private const string OperationMultipart = "multipart upload";
    private const string OperationProbe = "health probe";

    private static readonly DateTimeOffset FixedNow = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, string> NoEntries =
        new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();
    private readonly SortedDictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingUpload> _uploads = new(StringComparer.Ordinal);
    private readonly List<string> _uploadedKeys = [];
    private readonly List<string> _deletedKeys = [];
    private readonly List<(string SourceKey, string DestinationStore, string DestinationKey)> _copies = [];
    private readonly List<(string Key, PresignedRequest Request)> _downloadUrls = [];
    private readonly List<(string Key, PresignedRequest Request)> _uploadUrls = [];
    private readonly TimeSpan _maxPresignExpiry;
    private readonly IClock? _clock;
    private long _sequence;

    /// <summary>Initializes a new instance of the <see cref="InMemoryFileStorage"/> class.</summary>
    /// <param name="storeName">The store name; must match the name it is registered under.</param>
    /// <param name="options">The store options; <see langword="null"/> uses the defaults.</param>
    public InMemoryFileStorage(string storeName = "default", InMemoryFileStorageOptions? options = null)
    {
        if (!FileStoreRegistration.IsValidStoreName(storeName))
        {
            throw new ArgumentException(
                $"Store name '{storeName}' is invalid: use 1 to 64 characters from A-Z, a-z, 0-9, '.', '_' and '-', starting with a letter or digit.",
                nameof(storeName));
        }

        StoreName = storeName;
        _maxPresignExpiry = options?.MaxPresignExpiry ?? new InMemoryFileStorageOptions().MaxPresignExpiry;
        _clock = options?.Clock;
    }

    /// <inheritdoc />
    public string StoreName { get; }

    /// <inheritdoc />
    /// <remarks>Always <see langword="null"/>: this is the whole store; tenant views are applied by the registry.</remarks>
    public TenantId? TenantId => null;

    /// <summary>
    /// Gets or sets a value indicating whether write operations simulate an unavailable provider. When
    /// <see langword="true"/>, uploads, copies, deletes, and starting or completing a multipart upload return
    /// <see cref="StorageErrorCodes.Unavailable"/> without changing anything (a batch delete reports every key
    /// in <see cref="BatchDeleteResult.Failed"/>, as the S3 provider does). Reads, listing, presigning and the
    /// health probe are unaffected.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="ProbeAsync"/> — the store's readiness probe —
    /// reports <see cref="StorageErrorCodes.Unavailable"/>.
    /// </summary>
    public bool SimulateUnavailable { get; set; }

    /// <summary>Gets every key ever successfully written by an upload, copy or completed multipart upload, in order; never pruned on delete.</summary>
    public IReadOnlyList<string> UploadedKeys => Snapshot(_uploadedKeys);

    /// <summary>Gets every key ever successfully deleted by <see cref="DeleteAsync"/> or <see cref="DeleteManyAsync"/>, in order.</summary>
    public IReadOnlyList<string> DeletedKeys => Snapshot(_deletedKeys);

    /// <summary>Gets every successful copy out of this store: source key, destination store name and destination key.</summary>
    public IReadOnlyList<(string SourceKey, string DestinationStore, string DestinationKey)> CopiedPairs => Snapshot(_copies);

    /// <summary>Gets every download URL issued by <see cref="CreateDownloadUrlAsync"/>, with the key it was issued for.</summary>
    public IReadOnlyList<(string Key, PresignedRequest Request)> IssuedDownloadUrls => Snapshot(_downloadUrls);

    /// <summary>Gets every upload URL issued by <see cref="CreateUploadUrlAsync"/>, with the key it was issued for.</summary>
    public IReadOnlyList<(string Key, PresignedRequest Request)> IssuedUploadUrls => Snapshot(_uploadUrls);

    /// <summary>Gets the keys of every stored object, in ordinal order.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return [.. _objects.Keys];
            }
        }
    }

    private DateTimeOffset Now => _clock?.UtcNow ?? FixedNow;

    /// <summary>Returns the store key a tenant view writes <paramref name="key"/> to: <c>tenants/{tenantId}/{key}</c>.</summary>
    /// <param name="tenantId">The tenant. Must not be <see langword="default"/>.</param>
    /// <param name="key">The key relative to the tenant.</param>
    /// <returns>The store key, for the inspection helpers.</returns>
    public static string TenantKey(TenantId tenantId, string key)
    {
        if (tenantId.IsDefault)
        {
            throw new ArgumentException("The tenant identifier must not be default(TenantId).", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(key);
        return $"{TenantsFolder}{tenantId}/{key}";
    }

    /// <inheritdoc />
    public async Task<Result<FileReference>> UploadAsync(
        string key,
        Stream content,
        FileUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        options ??= new FileUploadOptions();

        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidateUpload(options)) is Error invalid)
        {
            return invalid;
        }

        byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);
        if (SimulateFailure)
        {
            return StorageErrors.Unavailable(StoreName, OperationUpload);
        }

        string checksum = Sha256(bytes);
        if (options.ChecksumSha256 is { } expected && !string.Equals(expected, checksum, StringComparison.Ordinal))
        {
            return StorageErrors.ChecksumMismatch(StoreName, key);
        }

        lock (_gate)
        {
            if (CheckCondition(options.Condition, key) is Error conditionFailed)
            {
                return conditionFailed;
            }

            StoredObject stored = Store(
                key,
                bytes,
                checksum,
                new ObjectHeaders(options.ContentType, options.ContentDisposition, options.CacheControl, options.ContentEncoding),
                options.Metadata,
                options.Tags,
                options.Tier);
            return Reference(key, stored);
        }
    }

    /// <inheritdoc />
    public Task<Result<FileDownload>> DownloadAsync(
        string key,
        FileDownloadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FileDownloadOptions();
        if (StorageValidation.ValidateKey(key) is Error invalid)
        {
            return Task.FromResult(Result<FileDownload>.Failure(invalid));
        }

        StoredObject? stored;
        lock (_gate)
        {
            _objects.TryGetValue(key, out stored);
        }

        if (stored is null || (options.VersionId is not null && !string.Equals(options.VersionId, stored.VersionId, StringComparison.Ordinal)))
        {
            return Task.FromResult(Result<FileDownload>.Failure(StorageErrors.NotFound(StoreName, key)));
        }

        if (options.IfMatch is not null && !ETagEquals(options.IfMatch, stored.ETag))
        {
            return Task.FromResult(Result<FileDownload>.Failure(StorageErrors.PreconditionFailed(StoreName, key)));
        }

        long length = stored.Content.Length;
        if (options.Range is not { } range)
        {
            return Task.FromResult(Result<FileDownload>.Success(
                new FileDownload(new MemoryStream(stored.Content, writable: false), Properties(key, stored), length)));
        }

        if (range.From >= length)
        {
            return Task.FromResult(Result<FileDownload>.Failure(StorageErrors.InvalidRange(StoreName, key)));
        }

        long last = Math.Min(range.To ?? length - 1, length - 1);
        int count = checked((int)(last - range.From + 1));
        var slice = new MemoryStream(stored.Content, checked((int)range.From), count, writable: false);
        return Task.FromResult(Result<FileDownload>.Success(
            new FileDownload(slice, Properties(key, stored), count, new ByteRange(range.From, last))));
    }

    /// <inheritdoc />
    public Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidateKey(key) is Error invalid)
        {
            return Task.FromResult(Result<FileProperties>.Failure(invalid));
        }

        lock (_gate)
        {
            return Task.FromResult(_objects.TryGetValue(key, out StoredObject? stored)
                ? Result<FileProperties>.Success(Properties(key, stored))
                : Result<FileProperties>.Failure(StorageErrors.NotFound(StoreName, key)));
        }
    }

    /// <inheritdoc />
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidateKey(key) is Error invalid)
        {
            return Task.FromResult(Result<bool>.Failure(invalid));
        }

        lock (_gate)
        {
            return Task.FromResult(Result<bool>.Success(_objects.ContainsKey(key)));
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new FileDeleteOptions();
        if (StorageValidation.ValidateKey(key) is Error invalid)
        {
            return Task.FromResult(Result.Failure(invalid));
        }

        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(StorageErrors.Unavailable(StoreName, OperationDelete)));
        }

        lock (_gate)
        {
            _objects.TryGetValue(key, out StoredObject? stored);
            bool matches = stored is not null
                && (options.IfMatch is null || ETagEquals(options.IfMatch, stored.ETag))
                && (options.VersionId is null || string.Equals(options.VersionId, stored.VersionId, StringComparison.Ordinal));

            if (!matches)
            {
                // Deleting a missing object succeeds, unless the caller required a specific version of it.
                return Task.FromResult(options.IfMatch is null
                    ? Result.Success()
                    : Result.Failure(StorageErrors.PreconditionFailed(StoreName, key)));
            }

            _objects.Remove(key);
            _deletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    public Task<Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        string[] distinct = [.. keys.Distinct(StringComparer.Ordinal)];
        foreach (string key in distinct)
        {
            if (StorageValidation.ValidateKey(key) is Error invalid)
            {
                return Task.FromResult(Result<BatchDeleteResult>.Failure(invalid));
            }
        }

        if (SimulateFailure)
        {
            Error unavailable = StorageErrors.Unavailable(StoreName, OperationDeleteMany);
            return Task.FromResult(Result<BatchDeleteResult>.Success(new BatchDeleteResult
            {
                Deleted = [],
                Failed = [.. distinct.Select(k => new FileDeleteFailure(k, unavailable))],
            }));
        }

        lock (_gate)
        {
            foreach (string key in distinct)
            {
                // Idempotent per key, as S3's DeleteObjects: a missing key is reported deleted.
                if (_objects.Remove(key))
                {
                    _deletedKeys.Add(key);
                }
            }
        }

        return Task.FromResult(Result<BatchDeleteResult>.Success(new BatchDeleteResult { Deleted = distinct, Failed = [] }));
    }

    /// <inheritdoc />
    public Task<Result<FileReference>> CopyAsync(
        string sourceKey,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CopyToAsync(sourceKey, this, destinationKey, options, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Into another <see cref="InMemoryFileStorage"/> the copy happens in memory, like a provider's
    /// server-side copy; into any other store the content is streamed through
    /// <see cref="IFileStorage.UploadAsync"/> of that store.
    /// </remarks>
    public async Task<Result<FileReference>> CopyToAsync(
        string sourceKey,
        IFileStorage destination,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new FileCopyOptions();

        if ((StorageValidation.ValidateKey(sourceKey)
            ?? StorageValidation.ValidateKey(destinationKey)
            ?? StorageValidation.ValidateCopy(options)) is Error invalid)
        {
            return invalid;
        }

        if (SimulateFailure)
        {
            return StorageErrors.Unavailable(StoreName, OperationCopy);
        }

        Result<FileReference> result = destination is InMemoryFileStorage target
            ? CopyInMemory(sourceKey, target, destinationKey, options)
            : await StreamCopyAsync(sourceKey, destination, destinationKey, options, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            lock (_gate)
            {
                _copies.Add((sourceKey, destination.StoreName, destinationKey));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidatePrefix(prefix) is Error invalid)
        {
            throw new StorageException(invalid);
        }

        return ListCoreAsync(prefix, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Objects and folders are returned together in key order, and a folder counts toward
    /// <see cref="FileListRequest.PageSize"/>, as on S3. The continuation token is opaque; a token this
    /// store did not issue returns <see cref="StorageErrorCodes.InvalidRequest"/>.
    /// </remarks>
    public Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (StorageValidation.ValidateList(request) is Error invalid)
        {
            return Task.FromResult(Result<FileListPage>.Failure(invalid));
        }

        string? startAfter = null;
        if (request.ContinuationToken is not null && !TryDecodeToken(request.ContinuationToken, out startAfter))
        {
            return Task.FromResult(Result<FileListPage>.Failure(
                StorageErrors.InvalidRequest("The continuation token was not issued by this store.")));
        }

        var entries = new List<(string SortKey, FileListItem? Item)>();
        lock (_gate)
        {
            string? lastFolder = null;
            foreach ((string key, StoredObject stored) in _objects)
            {
                if (!key.StartsWith(request.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                int slash = request.Recursive ? -1 : key.IndexOf('/', request.Prefix.Length);
                if (slash >= 0)
                {
                    string folder = key[..(slash + 1)];
                    if (!string.Equals(folder, lastFolder, StringComparison.Ordinal))
                    {
                        entries.Add((folder, null));
                        lastFolder = folder;
                    }

                    continue;
                }

                entries.Add((key, ListItem(key, stored)));
            }
        }

        var page = entries
            .Where(e => startAfter is null || string.CompareOrdinal(e.SortKey, startAfter) > 0)
            .Take(request.PageSize + 1)
            .ToList();
        bool hasMore = page.Count > request.PageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Task.FromResult(Result<FileListPage>.Success(new FileListPage
        {
            Items = [.. page.Where(e => e.Item is not null).Select(e => e.Item!)],
            Folders = [.. page.Where(e => e.Item is null).Select(e => e.SortKey)],
            ContinuationToken = hasMore ? EncodeToken(page[^1].SortKey) : null,
        }));
    }

    /// <inheritdoc />
    /// <remarks>The URL is a deterministic <c>memory://{store}/{key}?method=GET&amp;expires={unix seconds}</c>.</remarks>
    public Task<Result<PresignedRequest>> CreateDownloadUrlAsync(
        string key,
        PresignedDownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidatePresignedDownload(options, _maxPresignExpiry)) is Error invalid)
        {
            return Task.FromResult(Result<PresignedRequest>.Failure(invalid));
        }

        PresignedRequest request = Presign(key, MethodGet, options.Expiry, NoEntries);
        lock (_gate)
        {
            _downloadUrls.Add((key, request));
        }

        return Task.FromResult(Result<PresignedRequest>.Success(request));
    }

    /// <inheritdoc />
    public Task<Result<PresignedRequest>> CreateUploadUrlAsync(
        string key,
        PresignedUploadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidatePresignedUpload(options, _maxPresignExpiry)) is Error invalid)
        {
            return Task.FromResult(Result<PresignedRequest>.Failure(invalid));
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ContentTypeHeader] = options.ContentType };
        if (options.CreateOnly)
        {
            headers[IfNoneMatchHeader] = AnyETag;
        }

        if (options.ChecksumSha256 is { } checksum)
        {
            headers[ChecksumSha256Header] = checksum;
        }

        foreach ((string name, string value) in options.Metadata ?? NoEntries)
        {
            headers[MetadataHeaderPrefix + name.ToLowerInvariant()] = value;
        }

        PresignedRequest request = Presign(key, MethodPut, options.Expiry, headers);
        lock (_gate)
        {
            _uploadUrls.Add((key, request));
        }

        return Task.FromResult(Result<PresignedRequest>.Success(request));
    }

    /// <inheritdoc />
    public Task<Result<PresignedPost>> CreateUploadFormAsync(
        string key,
        PresignedPostOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidatePresignedPost(options, _maxPresignExpiry)) is Error invalid)
        {
            return Task.FromResult(Result<PresignedPost>.Failure(invalid));
        }

        DateTimeOffset expiresAt = Now + options.Expiry;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyField] = key,
            [ContentTypeHeader] = options.ContentType,
            [PolicyField] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"size={options.MinSize}-{options.MaxSize};content-type={options.ContentType};expires={expiresAt.ToUnixTimeSeconds()}"))),
        };
        foreach ((string name, string value) in options.Metadata ?? NoEntries)
        {
            fields[MetadataHeaderPrefix + name.ToLowerInvariant()] = value;
        }

        return Task.FromResult(Result<PresignedPost>.Success(new PresignedPost
        {
            Url = new Uri(string.Create(CultureInfo.InvariantCulture, $"{UrlScheme}://{StoreName}/?method={MethodPost}")),
            Fields = fields,
            ExpiresAt = expiresAt,
        }));
    }

    /// <inheritdoc />
    /// <remarks>Upload parts with <see cref="UploadPart"/>, which stands in for a client <c>PUT</c> to a part URL.</remarks>
    public Task<Result<MultipartUpload>> StartMultipartUploadAsync(
        string key,
        MultipartUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MultipartUploadOptions();
        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidateMultipart(options)) is Error invalid)
        {
            return Task.FromResult(Result<MultipartUpload>.Failure(invalid));
        }

        if (SimulateFailure)
        {
            return Task.FromResult(Result<MultipartUpload>.Failure(StorageErrors.Unavailable(StoreName, OperationMultipart)));
        }

        lock (_gate)
        {
            string uploadId = string.Create(CultureInfo.InvariantCulture, $"upload-{++_sequence}");
            _uploads[uploadId] = new PendingUpload(key, options);
            return Task.FromResult(Result<MultipartUpload>.Success(new MultipartUpload(key, uploadId)));
        }
    }

    /// <inheritdoc />
    public Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(
        MultipartUpload upload,
        int partNumber,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if ((StorageValidation.ValidateKey(upload.Key)
            ?? StorageValidation.ValidatePartNumber(partNumber)
            ?? StorageValidation.ValidateExpiry(expiry, _maxPresignExpiry)) is Error invalid)
        {
            return Task.FromResult(Result<PresignedRequest>.Failure(invalid));
        }

        PresignedRequest request = Presign(
            upload.Key,
            MethodPut,
            expiry,
            NoEntries,
            string.Create(CultureInfo.InvariantCulture, $"&uploadId={Uri.EscapeDataString(upload.UploadId)}&partNumber={partNumber}"));
        return Task.FromResult(Result<PresignedRequest>.Success(request));
    }

    /// <inheritdoc />
    public Task<Result<FileReference>> CompleteMultipartUploadAsync(
        MultipartUpload upload,
        IReadOnlyCollection<UploadedPart> parts,
        WriteCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(parts);
        if (StorageValidation.ValidateKey(upload.Key) is Error invalid)
        {
            return Task.FromResult(Result<FileReference>.Failure(invalid));
        }

        if (SimulateFailure)
        {
            return Task.FromResult(Result<FileReference>.Failure(StorageErrors.Unavailable(StoreName, OperationMultipart)));
        }

        lock (_gate)
        {
            if (!_uploads.TryGetValue(upload.UploadId, out PendingUpload? pending)
                || !string.Equals(pending.Key, upload.Key, StringComparison.Ordinal))
            {
                return Task.FromResult(Result<FileReference>.Failure(StorageErrors.NotFound(StoreName, upload.Key)));
            }

            bool complete = parts.Count > 0
                && parts.All(p => pending.Parts.TryGetValue(p.PartNumber, out (byte[] Content, string ETag) part) && ETagEquals(p.ETag, part.ETag));
            if (!complete)
            {
                return Task.FromResult(Result<FileReference>.Failure(StorageErrors.InvalidRequest(
                    "The parts do not complete the upload: every part must be uploaded with the ETag given.")));
            }

            if (CheckCondition(condition, upload.Key) is Error conditionFailed)
            {
                return Task.FromResult(Result<FileReference>.Failure(conditionFailed));
            }

            byte[] bytes = [.. parts.OrderBy(p => p.PartNumber).SelectMany(p => pending.Parts[p.PartNumber].Content)];
            MultipartUploadOptions options = pending.Options;
            StoredObject stored = Store(
                upload.Key,
                bytes,
                Sha256(bytes),
                new ObjectHeaders(options.ContentType, options.ContentDisposition, options.CacheControl, ContentEncoding: null),
                options.Metadata,
                options.Tags,
                options.Tier);
            _uploads.Remove(upload.UploadId);
            return Task.FromResult(Result<FileReference>.Success(Reference(upload.Key, stored)));
        }
    }

    /// <inheritdoc />
    public Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        lock (_gate)
        {
            _uploads.Remove(upload.UploadId);
        }

        return Task.FromResult(Result.Success());
    }

    /// <summary>The store's readiness probe; fails only while <see cref="SimulateUnavailable"/> is set.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>Success, or <see cref="StorageErrorCodes.Unavailable"/>.</returns>
    public Task<Result> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SimulateUnavailable
            ? Result.Failure(StorageErrors.Unavailable(StoreName, OperationProbe))
            : Result.Success());
    }

    /// <summary>
    /// Uploads one part of a multipart upload — what a client does with a URL from
    /// <see cref="CreateUploadPartUrlAsync"/>. Replaces an earlier upload of the same part.
    /// </summary>
    /// <param name="upload">The upload returned by <see cref="StartMultipartUploadAsync"/> (a store key).</param>
    /// <param name="partNumber">The part number, 1 to 10,000.</param>
    /// <param name="content">The part's bytes.</param>
    /// <returns>The part's ETag, to pass to <see cref="CompleteMultipartUploadAsync"/>.</returns>
    /// <exception cref="InvalidOperationException">The upload is unknown, completed or aborted.</exception>
    public string UploadPart(MultipartUpload upload, int partNumber, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(partNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(partNumber, StorageValidation.MaxPartNumber);

        lock (_gate)
        {
            if (!_uploads.TryGetValue(upload.UploadId, out PendingUpload? pending))
            {
                throw new InvalidOperationException($"No multipart upload '{upload.UploadId}' is in progress in store '{StoreName}'.");
            }

            string eTag = NextETag();
            pending.Parts[partNumber] = ([.. content], eTag);
            return eTag;
        }
    }

    /// <summary>Returns whether <paramref name="key"/> was ever successfully written.</summary>
    /// <param name="key">The store key.</param>
    /// <returns><see langword="true"/> when it appears in <see cref="UploadedKeys"/>.</returns>
    public bool WasUploaded(string key) => UploadedKeys.Contains(key, StringComparer.Ordinal);

    /// <summary>Returns whether <paramref name="key"/> was ever successfully deleted.</summary>
    /// <param name="key">The store key.</param>
    /// <returns><see langword="true"/> when it appears in <see cref="DeletedKeys"/>.</returns>
    public bool WasDeleted(string key) => DeletedKeys.Contains(key, StringComparer.Ordinal);

    /// <summary>Returns whether <paramref name="sourceKey"/> was ever successfully copied to <paramref name="destinationKey"/>, in any store.</summary>
    /// <param name="sourceKey">The source store key.</param>
    /// <param name="destinationKey">The destination store key.</param>
    /// <returns><see langword="true"/> when the pair appears in <see cref="CopiedPairs"/>.</returns>
    public bool WasCopied(string sourceKey, string destinationKey) =>
        CopiedPairs.Any(p =>
            string.Equals(p.SourceKey, sourceKey, StringComparison.Ordinal)
            && string.Equals(p.DestinationKey, destinationKey, StringComparison.Ordinal));

    /// <summary>Returns the content stored under <paramref name="key"/>.</summary>
    /// <param name="key">The store key.</param>
    /// <returns>A copy of the object's bytes.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">No object has that key.</exception>
    public byte[] GetContent(string key)
    {
        lock (_gate)
        {
            return _objects.TryGetValue(key, out StoredObject? stored)
                ? [.. stored.Content]
                : throw new System.Collections.Generic.KeyNotFoundException($"No object '{key}' is stored in store '{StoreName}'.");
        }
    }

    /// <summary>
    /// Stores an object without going through <see cref="UploadAsync"/> — a test-setup helper. The key is
    /// not recorded in <see cref="UploadedKeys"/>.
    /// </summary>
    /// <param name="key">The store key.</param>
    /// <param name="content">The object's bytes.</param>
    /// <param name="contentType">The object's MIME type.</param>
    /// <param name="metadata">Optional user metadata.</param>
    /// <returns>The stored object's reference.</returns>
    public FileReference Seed(
        string key,
        byte[] content,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (StorageValidation.ValidateKey(key) is Error invalid)
        {
            throw new ArgumentException(invalid.Message, nameof(key));
        }

        byte[] bytes = [.. content];
        lock (_gate)
        {
            StoredObject stored = new(
                bytes,
                Sha256(bytes),
                new ObjectHeaders(contentType, null, null, null),
                CopyEntries(metadata, lowerCaseKeys: true),
                NoEntries,
                StorageTier.Default,
                NextETag(),
                VersionId: null,
                Now);
            _objects[key] = stored;
            return Reference(key, stored);
        }
    }

    /// <summary>
    /// Stores an object read from <paramref name="content"/> (from its current position; the stream is not
    /// disposed) without going through <see cref="UploadAsync"/>.
    /// </summary>
    /// <param name="key">The store key.</param>
    /// <param name="content">The object's content.</param>
    /// <param name="contentType">The object's MIME type.</param>
    /// <param name="metadata">Optional user metadata.</param>
    /// <returns>The stored object's reference.</returns>
    public FileReference Seed(
        string key,
        Stream content,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return Seed(key, buffer.ToArray(), contentType, metadata);
    }

    /// <summary>Removes every object and in-progress multipart upload, clears every recording and both simulation flags.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _objects.Clear();
            _uploads.Clear();
            _uploadedKeys.Clear();
            _deletedKeys.Clear();
            _copies.Clear();
            _downloadUrls.Clear();
            _uploadUrls.Clear();
        }

        SimulateFailure = false;
        SimulateUnavailable = false;
    }

    private Result<FileReference> CopyInMemory(string sourceKey, InMemoryFileStorage target, string destinationKey, FileCopyOptions options)
    {
        StoredObject? source;
        lock (_gate)
        {
            _objects.TryGetValue(sourceKey, out source);
        }

        if (source is null)
        {
            return StorageErrors.NotFound(StoreName, sourceKey);
        }

        if (options.SourceIfMatch is not null && !ETagEquals(options.SourceIfMatch, source.ETag))
        {
            return StorageErrors.PreconditionFailed(StoreName, sourceKey);
        }

        lock (target._gate)
        {
            if (target.CheckCondition(options.Condition, destinationKey) is Error conditionFailed)
            {
                return conditionFailed;
            }

            StoredObject stored = target.Store(
                destinationKey,
                source.Content,
                source.ChecksumSha256,
                options.ContentType is null ? source.Headers : source.Headers with { ContentType = options.ContentType },
                options.Metadata ?? source.Metadata,
                source.Tags,
                options.Tier);
            return target.Reference(destinationKey, stored);
        }
    }

    private async Task<Result<FileReference>> StreamCopyAsync(
        string sourceKey,
        IFileStorage destination,
        string destinationKey,
        FileCopyOptions options,
        CancellationToken cancellationToken)
    {
        Result<FileDownload> download = await DownloadAsync(
                sourceKey,
                new FileDownloadOptions { IfMatch = options.SourceIfMatch },
                cancellationToken)
            .ConfigureAwait(false);
        if (download.IsFailure)
        {
            return download.Error;
        }

        await using FileDownload source = download.Value;
        FileProperties properties = source.Properties;
        return await destination.UploadAsync(
                destinationKey,
                source.Content,
                new FileUploadOptions
                {
                    ContentType = options.ContentType ?? properties.ContentType,
                    CacheControl = properties.CacheControl,
                    ContentDisposition = properties.ContentDisposition,
                    ContentEncoding = properties.ContentEncoding,
                    Metadata = options.Metadata ?? properties.Metadata,
                    Tier = options.Tier,
                    Condition = options.Condition,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<FileListItem> ListCoreAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? continuationToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            Result<FileListPage> page = await ListPageAsync(
                    new FileListRequest { Prefix = prefix, ContinuationToken = continuationToken },
                    cancellationToken)
                .ConfigureAwait(false);
            if (page.IsFailure)
            {
                throw new StorageException(page.Error);
            }

            foreach (FileListItem item in page.Value.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }

            continuationToken = page.Value.ContinuationToken;
        }
        while (continuationToken is not null);
    }

    /// <summary>Checks a write condition against the current object. Call under <see cref="_gate"/>.</summary>
    private Error? CheckCondition(WriteCondition? condition, string key)
    {
        if (condition is null)
        {
            return null;
        }

        bool exists = _objects.TryGetValue(key, out StoredObject? existing);
        if (condition.MustNotExist)
        {
            return exists ? StorageErrors.AlreadyExists(StoreName, key) : null;
        }

        // S3 answers If-Match on a missing object with 404.
        return !exists ? StorageErrors.NotFound(StoreName, key)
            : ETagEquals(condition.ETag!, existing!.ETag) ? null
            : StorageErrors.PreconditionFailed(StoreName, key);
    }

    /// <summary>Writes an object and records it. Call under <see cref="_gate"/>.</summary>
    private StoredObject Store(
        string key,
        byte[] content,
        string checksum,
        ObjectHeaders headers,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, string>? tags,
        StorageTier tier)
    {
        var stored = new StoredObject(
            content,
            checksum,
            headers,
            CopyEntries(metadata, lowerCaseKeys: true),
            CopyEntries(tags, lowerCaseKeys: false),
            tier,
            NextETag(),
            VersionId: null,
            Now);
        _objects[key] = stored;
        _uploadedKeys.Add(key);
        return stored;
    }

    private PresignedRequest Presign(
        string key,
        string method,
        TimeSpan expiry,
        IReadOnlyDictionary<string, string> headers,
        string extraQuery = "")
    {
        DateTimeOffset expiresAt = Now + expiry;
        string path = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
        return new PresignedRequest
        {
            Url = new Uri(string.Create(
                CultureInfo.InvariantCulture,
                $"{UrlScheme}://{StoreName}/{path}?method={method}{extraQuery}&expires={expiresAt.ToUnixTimeSeconds()}")),
            Method = method,
            Headers = headers,
            ExpiresAt = expiresAt,
        };
    }

    private FileReference Reference(string key, StoredObject stored) => new()
    {
        Store = StoreName,
        Key = key,
        ETag = stored.ETag,
        VersionId = stored.VersionId,
    };

    private static FileProperties Properties(string key, StoredObject stored) => new()
    {
        Key = key,
        ContentLength = stored.Content.Length,
        ContentType = stored.Headers.ContentType,
        ContentDisposition = stored.Headers.ContentDisposition,
        CacheControl = stored.Headers.CacheControl,
        ContentEncoding = stored.Headers.ContentEncoding,
        LastModified = stored.LastModified,
        ETag = stored.ETag,
        VersionId = stored.VersionId,
        ChecksumSha256 = stored.ChecksumSha256,
        Tier = stored.Tier,
        Metadata = stored.Metadata,
    };

    private static FileListItem ListItem(string key, StoredObject stored) => new()
    {
        Key = key,
        ContentLength = stored.Content.Length,
        LastModified = stored.LastModified,
        ETag = stored.ETag,
    };

    private string NextETag() => string.Create(CultureInfo.InvariantCulture, $"\"{++_sequence:x16}\"");

    private IReadOnlyList<T> Snapshot<T>(List<T> list)
    {
        lock (_gate)
        {
            return [.. list];
        }
    }

    private static bool ETagEquals(string left, string right) =>
        string.Equals(left.Trim('"'), right.Trim('"'), StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> CopyEntries(IReadOnlyDictionary<string, string>? entries, bool lowerCaseKeys) =>
        entries is null || entries.Count == 0
            ? NoEntries
            : entries.ToDictionary(e => lowerCaseKeys ? e.Key.ToLowerInvariant() : e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);

    private static string Sha256(byte[] content) => Convert.ToBase64String(SHA256.HashData(content));

    private static string EncodeToken(string startAfter) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(startAfter));

    private static bool TryDecodeToken(string token, out string? startAfter)
    {
        startAfter = null;
        var buffer = new byte[token.Length];
        if (!Convert.TryFromBase64String(token, buffer, out int written) || written == 0)
        {
            return false;
        }

        startAfter = Encoding.UTF8.GetString(buffer, 0, written);
        return true;
    }

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private sealed record ObjectHeaders(string? ContentType, string? ContentDisposition, string? CacheControl, string? ContentEncoding);

    private sealed record StoredObject(
        byte[] Content,
        string ChecksumSha256,
        ObjectHeaders Headers,
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyDictionary<string, string> Tags,
        StorageTier Tier,
        string ETag,
        string? VersionId,
        DateTimeOffset LastModified);

    private sealed record PendingUpload(string Key, MultipartUploadOptions Options)
    {
        public Dictionary<int, (byte[] Content, string ETag)> Parts { get; } = [];
    }
}
