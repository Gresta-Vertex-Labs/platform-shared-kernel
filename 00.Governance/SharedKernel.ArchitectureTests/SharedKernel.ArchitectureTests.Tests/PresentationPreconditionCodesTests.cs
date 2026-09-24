using FluentAssertions;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Storage;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// P-562 R7: <c>SharedKernel.Presentation.WebApi</c> answers a version conflict of a conditional request with 412,
/// recognizing it by error code (<c>Problems:PreconditionFailedErrorCodes</c>).
/// </summary>
/// <remarks>
/// The codes are owned by 06.Persistence (<see cref="ConcurrencyVersion.ConflictErrorCode"/>) and 08.Storage
/// (<see cref="StorageErrorCodes"/>), which 14.Presentation may not reference, so the WebApi defaults are literals.
/// This test pins them to the owning constants: renaming a code in either domain fails here instead of silently
/// turning every stale-version 412 back into a 409.
/// </remarks>
public sealed class PresentationPreconditionCodesTests
{
    [Fact]
    public void DefaultPreconditionFailedErrorCodes_AreTheOwningDomainsConstants()
    {
        var defaults = new SharedKernelWebApiOptions().Problems.PreconditionFailedErrorCodes;

        defaults.Should().BeEquivalentTo(
            [
                ConcurrencyVersion.ConflictErrorCode,
                StorageErrorCodes.PreconditionFailed,
                StorageErrorCodes.AlreadyExists,
            ],
            because: "each default must be the owning domain's constant, character for character");
    }
}
