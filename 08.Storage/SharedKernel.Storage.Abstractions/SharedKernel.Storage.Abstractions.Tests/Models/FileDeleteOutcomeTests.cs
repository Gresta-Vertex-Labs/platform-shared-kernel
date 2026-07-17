using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>
/// T-02: <see cref="FileDeleteOutcome"/> — the <c>Succeeded == true ⇒ Error == null</c> invariant,
/// enforced by its private constructor, plus value equality by <see cref="FileDeleteOutcome.Key"/>
/// (and the record's other backing fields).
/// </summary>
public sealed class FileDeleteOutcomeTests
{
    [Fact]
    public void Success_SetsSucceededTrue_AndErrorNull()
    {
        var outcome = FileDeleteOutcome.Success("my-key");

        outcome.Key.Should().Be("my-key");
        outcome.Succeeded.Should().BeTrue();
        outcome.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_SetsSucceededFalse_AndCarriesTheGivenError()
    {
        var error = Error.NotFound("storage.not_found", "not found");

        var outcome = FileDeleteOutcome.Failure("my-key", error);

        outcome.Key.Should().Be("my-key");
        outcome.Succeeded.Should().BeFalse();
        outcome.Error.Should().Be(error);
    }

    [Fact]
    public void TwoSuccessOutcomes_ForTheSameKey_AreEqual()
    {
        var first = FileDeleteOutcome.Success("key-1");
        var second = FileDeleteOutcome.Success("key-1");

        first.Should().Be(second);
    }

    [Fact]
    public void TwoSuccessOutcomes_ForDifferentKeys_AreNotEqual()
    {
        var first = FileDeleteOutcome.Success("key-1");
        var second = FileDeleteOutcome.Success("key-2");

        first.Should().NotBe(second);
    }

    [Fact]
    public void SuccessOutcome_And_FailureOutcome_ForTheSameKey_AreNotEqual()
    {
        var success = FileDeleteOutcome.Success("key-1");
        var failure = FileDeleteOutcome.Failure("key-1", Error.NotFound("storage.not_found", "not found"));

        success.Should().NotBe(failure);
    }

    [Fact]
    public void TwoFailureOutcomes_WithTheSameKeyAndError_AreEqual()
    {
        var error = Error.Unauthorized("storage.access_denied", "denied");

        var first = FileDeleteOutcome.Failure("key-1", error);
        var second = FileDeleteOutcome.Failure("key-1", error);

        first.Should().Be(second);
    }
}
