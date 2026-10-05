using FluentAssertions;

namespace SharedKernel.Storage.Abstractions.Tests;

public sealed class StorageValidationTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("invoices/2026/09/inv-1.pdf")]
    [InlineData("folder/")]
    [InlineData("ümlaut/ş.txt")]
    [InlineData("a.b..c")]
    public void ValidateKey_accepts_well_formed_keys(string key) =>
        StorageValidation.ValidateKey(key).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading")]
    [InlineData("a//b")]
    [InlineData("a/../b")]
    [InlineData("../b")]
    [InlineData("a/./b")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void ValidateKey_rejects_keys_that_could_escape_or_confuse_a_prefix(string key) =>
        StorageValidation.ValidateKey(key)!.Code.Should().Be(StorageErrorCodes.InvalidKey);

    [Fact]
    public void ValidateKey_limits_the_utf8_length_not_the_character_count()
    {
        StorageValidation.ValidateKey(new string('a', 1024)).Should().BeNull();
        StorageValidation.ValidateKey(new string('ş', 513))!.Code.Should().Be(StorageErrorCodes.InvalidKey);
    }

    [Fact]
    public void ValidatePrefix_accepts_empty_and_rejects_null() =>
        (StorageValidation.ValidatePrefix(string.Empty), StorageValidation.ValidatePrefix(null)?.Code)
            .Should().Be((null, StorageErrorCodes.InvalidKey));

    [Theory]
    [InlineData("bad key")]
    [InlineData("bad.key")]
    [InlineData("")]
    public void ValidateMetadata_rejects_keys_that_are_not_header_safe(string key) =>
        StorageValidation.ValidateMetadata(new Dictionary<string, string> { [key] = "v" })!.Code
            .Should().Be(StorageErrorCodes.InvalidRequest);

    [Fact]
    public void ValidateMetadata_rejects_non_ascii_values_duplicate_keys_and_oversized_totals()
    {
        StorageValidation.ValidateMetadata(new Dictionary<string, string> { ["name"] = "Çağrı" }).Should().NotBeNull();
        StorageValidation.ValidateMetadata(new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", ["A"] = "2" })
            .Should().NotBeNull();
        StorageValidation.ValidateMetadata(new Dictionary<string, string> { ["big"] = new string('x', 2046) }).Should().NotBeNull();
        StorageValidation.ValidateMetadata(new Dictionary<string, string> { ["big"] = new string('x', 2045) }).Should().BeNull();
    }

    [Fact]
    public void ValidateTags_limits_count_and_lengths()
    {
        StorageValidation.ValidateTags(Enumerable.Range(0, 10).ToDictionary(i => $"k{i}", i => "v")).Should().BeNull();
        StorageValidation.ValidateTags(Enumerable.Range(0, 11).ToDictionary(i => $"k{i}", i => "v")).Should().NotBeNull();
        StorageValidation.ValidateTags(new Dictionary<string, string> { [new string('k', 129)] = "v" }).Should().NotBeNull();
        StorageValidation.ValidateTags(new Dictionary<string, string> { ["k"] = new string('v', 257) }).Should().NotBeNull();
    }

    [Fact]
    public void ValidateUpload_checks_content_type_headers_and_checksum()
    {
        StorageValidation.ValidateUpload(new FileUploadOptions
        {
            ContentType = "application/pdf",
            ContentDisposition = "attachment; filename=\"a.pdf\"",
            ChecksumSha256 = Convert.ToBase64String(new byte[32]),
        }).Should().BeNull();

        StorageValidation.ValidateUpload(new FileUploadOptions { ContentType = "pdf" })!.Code.Should().Be(StorageErrorCodes.InvalidRequest);
        StorageValidation.ValidateUpload(new FileUploadOptions { CacheControl = "no-cache\r\nX-Injected: 1" }).Should().NotBeNull();
        StorageValidation.ValidateUpload(new FileUploadOptions { ChecksumSha256 = Convert.ToBase64String(new byte[20]) }).Should().NotBeNull();
        StorageValidation.ValidateUpload(new FileUploadOptions { ChecksumSha256 = "not base64" }).Should().NotBeNull();
        StorageValidation.ValidateUpload(new FileUploadOptions { Tier = (StorageTier)42 }).Should().NotBeNull();
    }

    [Fact]
    public void ValidateExpiry_requires_a_positive_expiry_within_the_maximum()
    {
        TimeSpan max = TimeSpan.FromHours(1);
        StorageValidation.ValidateExpiry(TimeSpan.FromMinutes(5), max).Should().BeNull();
        StorageValidation.ValidateExpiry(max, max).Should().BeNull();
        StorageValidation.ValidateExpiry(TimeSpan.Zero, max)!.Code.Should().Be(StorageErrorCodes.ExpiryTooLong);
        StorageValidation.ValidateExpiry(max + TimeSpan.FromSeconds(1), max)!.Code.Should().Be(StorageErrorCodes.ExpiryTooLong);
    }

    [Fact]
    public void ValidatePresignedPost_requires_a_bounded_size_and_a_content_type()
    {
        TimeSpan max = TimeSpan.FromHours(1);
        PresignedPostOptions valid = new() { Expiry = TimeSpan.FromMinutes(5), MaxSize = 1024, ContentType = "image/" };

        StorageValidation.ValidatePresignedPost(valid, max).Should().BeNull();
        StorageValidation.ValidatePresignedPost(valid with { ContentType = "image/png" }, max).Should().BeNull();
        StorageValidation.ValidatePresignedPost(valid with { ContentType = "/" }, max).Should().NotBeNull();
        StorageValidation.ValidatePresignedPost(valid with { ContentType = "" }, max).Should().NotBeNull();
        StorageValidation.ValidatePresignedPost(valid with { MaxSize = 0 }, max).Should().NotBeNull();
        StorageValidation.ValidatePresignedPost(valid with { MinSize = 2048 }, max).Should().NotBeNull();
    }

    [Fact]
    public void ValidateList_bounds_the_page_size()
    {
        StorageValidation.ValidateList(new FileListRequest()).Should().BeNull();
        StorageValidation.ValidateList(new FileListRequest { PageSize = 0 }).Should().NotBeNull();
        StorageValidation.ValidateList(new FileListRequest { PageSize = 1001 }).Should().NotBeNull();
    }

    [Theory]
    [InlineData("invoices", true)]
    [InlineData("tenant-docs.v2", true)]
    [InlineData("-leading", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void Store_names_are_restricted_to_config_safe_characters(string name, bool valid) =>
        FileStoreRegistration.IsValidStoreName(name).Should().Be(valid);
}
