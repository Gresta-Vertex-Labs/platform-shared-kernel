using SharedKernel.DataPrivacy.Masking;
using SharedKernel.Testing.DataPrivacy;

namespace SharedKernel.Testing.SelfTests.DataPrivacy;

/// <summary>
/// Proves <see cref="PiiMaskingAssertions"/> against every shipped <see cref="PiiMasking"/>
/// function — no consuming domain has adopted this helper yet, so this self-test is the only
/// behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class PiiMaskingAssertionsTests
{
    [Fact]
    public void ShouldBeMasked_Email_MatchingObservedValue_DoesNotThrow() =>
        PiiMaskingAssertions.ShouldBeMasked("j.doe@example.com", "j***@example.com", PiiMasking.Email);

    [Fact]
    public void ShouldBeMasked_Phone_MatchingObservedValue_DoesNotThrow() =>
        PiiMaskingAssertions.ShouldBeMasked("+1 (555) 123-4567", "+* (***) ***-4567", PiiMasking.Phone);

    [Fact]
    public void ShouldBeMasked_CardNumber_MatchingObservedValue_DoesNotThrow() =>
        PiiMaskingAssertions.ShouldBeMasked("4111-1111-1111-1111", "4111-11**-****-1111", PiiMasking.CardNumber);

    [Fact]
    public void ShouldBeMasked_Suppress_MatchingObservedValue_DoesNotThrow() =>
        PiiMaskingAssertions.ShouldBeMasked("anything", PiiMasking.RedactedSentinel, PiiMasking.Suppress);

    [Fact]
    public void ShouldBeMasked_MismatchedObservedValue_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => PiiMaskingAssertions.ShouldBeMasked("j.doe@example.com", "totally-wrong@example.com", PiiMasking.Email));

    [Fact]
    public void ShouldBeMasked_NullMaskingFunction_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => PiiMaskingAssertions.ShouldBeMasked("value", "masked", null!));

    [Fact]
    public void ShouldBeMasked_NullObservedMasked_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => PiiMaskingAssertions.ShouldBeMasked("value", null!, PiiMasking.Email));
}
