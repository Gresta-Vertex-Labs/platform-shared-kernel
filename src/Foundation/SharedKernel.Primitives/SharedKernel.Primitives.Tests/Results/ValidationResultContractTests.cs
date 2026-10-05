using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Results;

/// <summary>
/// Pins the two contracts a <c>record</c> declaration promises but the compiler does not deliver
/// for an <see cref="IReadOnlyList{T}"/> member: value equality, and immutability.
/// </summary>
/// <remarks>
/// <para>
/// <b>Equality.</b> The compiler-generated <c>Equals</c> compares <c>Errors</c> with
/// <see cref="EqualityComparer{T}"/> for <see cref="IReadOnlyList{T}"/>, which is reference
/// equality for a <see cref="List{T}"/> or array. Two failures built from equal errors therefore
/// compared unequal, while two successes compared EQUAL — not by design, but because both reuse
/// the single static empty array. Equality that holds or breaks depending on the outcome is worse
/// than equality that is uniformly absent, because a passing test on the success path implies
/// nothing about the failure path.
/// </para>
/// <para>
/// <b>Immutability.</b> <c>Failure</c> stored the caller's own list by reference.
/// <see cref="IReadOnlyList{T}"/> is a read-only VIEW, not an immutable collection — a
/// <see cref="List{T}"/> satisfies it — so the caller could keep mutating it afterwards. Clearing
/// it produced a failed result carrying zero errors: exactly the state <c>Failure</c>'s own
/// <see cref="ArgumentException"/> guard rejects at construction, reached by going around the
/// guard rather than through it.
/// </para>
/// </remarks>
public sealed class ValidationResultContractTests
{
    private static Error SampleError() => Error.Validation("field.required", "Field is required.");

    // ---- Value equality ----

    [Fact]
    public void NonGeneric_FailuresWithEqualErrors_AreEqual()
    {
        var left = ValidationResult.Failure(new[] { SampleError() });
        var right = ValidationResult.Failure(new[] { SampleError() });

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void NonGeneric_FailuresBuiltFromDifferentCollectionTypes_AreStillEqual()
    {
        // The fix must compare CONTENT, not the collection instance or its concrete type.
        var fromArray = ValidationResult.Failure(new[] { SampleError() });
        var fromList = ValidationResult.Failure(new List<Error> { SampleError() });

        Assert.Equal(fromArray, fromList);
    }

    [Fact]
    public void NonGeneric_FailuresWithDifferentErrors_AreNotEqual()
    {
        var left = ValidationResult.Failure(new[] { SampleError() });
        var right = ValidationResult.Failure(
            new[] { Error.Validation("other.code", "Something else.") }
        );

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void NonGeneric_FailuresDifferingOnlyInErrorOrder_AreNotEqual()
    {
        var first = SampleError();
        var second = Error.Validation("second.code", "Second problem.");

        var forward = ValidationResult.Failure(new[] { first, second });
        var reversed = ValidationResult.Failure(new[] { second, first });

        Assert.NotEqual(forward, reversed);
    }

    [Fact]
    public void NonGeneric_SuccessAndFailure_AreNotEqual()
    {
        Assert.NotEqual(ValidationResult.Success(), ValidationResult.Failure(new[] { SampleError() }));
    }

    [Fact]
    public void NonGeneric_Successes_AreEqual()
    {
        Assert.Equal(ValidationResult.Success(), ValidationResult.Success());
    }

    [Fact]
    public void Generic_FailuresWithEqualErrors_AreEqual()
    {
        var left = ValidationResult<int>.Failure(new[] { SampleError() });
        var right = ValidationResult<int>.Failure(new[] { SampleError() });

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Generic_SuccessesWithEqualValues_AreEqual()
    {
        Assert.Equal(ValidationResult<int>.Success(42), ValidationResult<int>.Success(42));
        Assert.Equal(
            ValidationResult<int>.Success(42).GetHashCode(),
            ValidationResult<int>.Success(42).GetHashCode()
        );
    }

    [Fact]
    public void Generic_SuccessesWithDifferentValues_AreNotEqual()
    {
        // The value must stay part of equality — it is the whole point of the generic overload.
        Assert.NotEqual(ValidationResult<int>.Success(1), ValidationResult<int>.Success(2));
    }

    // ---- Immutability by snapshot ----

    [Fact]
    public void NonGeneric_Failure_SnapshotsTheCallerList_Addition()
    {
        var live = new List<Error> { SampleError() };
        var result = ValidationResult.Failure(live);

        live.Add(Error.Validation("added.later", "Added after construction."));

        Assert.Single(result.Errors);
    }

    [Fact]
    public void NonGeneric_Failure_SnapshotsTheCallerList_Clear()
    {
        // The impossible state: IsValid false with an empty Errors collection, which Failure's own
        // guard exists to prevent.
        var live = new List<Error> { SampleError() };
        var result = ValidationResult.Failure(live);

        live.Clear();

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Generic_Failure_SnapshotsTheCallerList()
    {
        var live = new List<Error> { SampleError() };
        var result = ValidationResult<int>.Failure(live);

        live.Clear();

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void NonGeneric_Failure_DoesNotExposeTheCallerList_Instance()
    {
        var live = new List<Error> { SampleError() };

        var result = ValidationResult.Failure(live);

        Assert.NotSame(live, result.Errors);
    }

    // ---- Argument validation ----

    [Fact]
    public void NonGeneric_Failure_NullCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationResult.Failure(null!));
    }

    [Fact]
    public void NonGeneric_Failure_NullElement_ThrowsNamingTheIndex()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ValidationResult.Failure(new Error[] { null! })
        );

        Assert.Contains("index 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_Failure_NullElement_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ValidationResult<int>.Failure(new[] { SampleError(), null! })
        );
    }
}
