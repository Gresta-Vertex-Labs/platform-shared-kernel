using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SharedKernel.Primitives.Enums;
using Xunit;

namespace SharedKernel.Primitives.Tests.Enums;

/// <summary>
/// Pins the <c>IL2059</c> suppression on <c>SmartEnum.ForceEnumStaticConstructor</c>.
/// </summary>
/// <remarks>
/// <para>
/// This package advertises AOT compatibility in its <c>PackageTags</c> and <c>Description</c>,
/// and <see cref="SmartEnum{TEnum, TValue}"/> forces a subtype's static constructor through
/// <c>RuntimeHelpers.RunClassConstructor(typeof(TEnum).TypeHandle)</c>. ILLink cannot prove the
/// reachability of a static constructor reached through a generic parameter, so without a
/// suppression that call emits:
/// </para>
/// <para>
/// <c>Trim analysis warning IL2059: Unrecognized value passed to the parameter 'type' of method
/// RuntimeHelpers.RunClassConstructor. It's not possible to guarantee the availability of the
/// target static constructor.</c>
/// </para>
/// <para>
/// That warning appears in EVERY consuming build that trims or AOT-compiles, and is a hard failure
/// under <c>TreatWarningsAsErrors</c> — pointing into our code, not theirs. The package shipped in
/// exactly that state while its own XML documentation asserted no annotation was needed.
/// </para>
/// <para>
/// The suppression is sound rather than cosmetic, and that was established by running a
/// self-contained <c>TrimMode=full</c> publish and confirming <c>FromValue</c>, <c>FromName</c>,
/// and <c>List</c> all resolve correctly against a trimmed subclass. This test cannot re-run a
/// trim publish, so it does the next best thing: it fails if the suppression is ever removed,
/// which is the change that would silently push the warning back into every consumer.
/// </para>
/// </remarks>
public sealed class SmartEnumTrimmingContractTests
{
    private const string ForcingMethodName = "ForceEnumStaticConstructor";

    private static MethodInfo ForcingMethod() =>
        typeof(SmartEnum<TrimContractEnum, int>).GetMethod(
            ForcingMethodName,
            BindingFlags.NonPublic | BindingFlags.Static
        )
        ?? throw new InvalidOperationException(
            $"SmartEnum no longer declares a static {ForcingMethodName}. If the "
                + "static-initialization trap is now closed some other way, delete this test "
                + "class deliberately rather than letting it rot."
        );

    [Fact]
    public void ForcingMethod_CarriesAnIl2059Suppression()
    {
        var suppressions = ForcingMethod()
            .GetCustomAttributes<UnconditionalSuppressMessageAttribute>(inherit: false)
            .ToArray();

        Assert.Contains(
            suppressions,
            suppression => suppression.CheckId.StartsWith("IL2059", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ForcingMethod_SuppressionCarriesAJustification()
    {
        // An unjustified suppression is indistinguishable from someone silencing a warning they
        // did not understand, which is the thing this whole pass was correcting.
        var suppression = ForcingMethod()
            .GetCustomAttributes<UnconditionalSuppressMessageAttribute>(inherit: false)
            .Single(attribute => attribute.CheckId.StartsWith("IL2059", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(suppression.Justification));
    }

    [Fact]
    public void InheritedStaticMembers_ResolveRegisteredMembers()
    {
        // Why the suppressed call cannot simply be deleted to silence the warning: these are
        // INHERITED static members, reached on the base type, and reaching one does not itself
        // run TrimContractEnum's static constructor — which is what registers the members.
        //
        // This asserts the outcome, not the ordering. The precise "first touch of the type in the
        // process is an inherited static" case needs an enum type touched by exactly one test and
        // nothing else; SmartEnumTests already owns dedicated FirstTouchOnlyEnumFor* fixtures for
        // each entry point, and those are the authoritative coverage for the trap itself.
        Assert.Equal(2, SmartEnum<TrimContractEnum, int>.List.Count);
        Assert.Same(TrimContractEnum.Alpha, SmartEnum<TrimContractEnum, int>.FromValue(1));
    }

    // Used by this class only, so the "first touch" above really is the first touch.
    private sealed class TrimContractEnum : SmartEnum<TrimContractEnum, int>
    {
        public static readonly TrimContractEnum Alpha = new(nameof(Alpha), 1);
        public static readonly TrimContractEnum Beta = new(nameof(Beta), 2);

        private TrimContractEnum(string name, int value)
            : base(name, value) { }
    }
}
