using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

internal static class ResultAssert
{
    public static void Failure<T>(Result<T> result, string expectedCode, ErrorType expectedType)
    {
        Assert.True(result.IsFailure, "Expected a failed result.");
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedType, result.Error.Type);
    }
}
