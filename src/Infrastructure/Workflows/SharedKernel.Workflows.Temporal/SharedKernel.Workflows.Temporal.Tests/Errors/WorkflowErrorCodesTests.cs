using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Failures;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Tests.Errors;

/// <summary>
/// The public <see cref="WorkflowErrorCodes"/> and the internal <see cref="WorkflowErrors"/> factory behind them: one
/// row per code with its <see cref="ErrorType"/>, a format guard over every public constant, a producer for every code,
/// and the round trip through the failure mapper.
/// </summary>
public sealed partial class WorkflowErrorCodesTests
{
    public static TheoryData<string, Error, ErrorType> AllFactories() => new()
    {
        { nameof(WorkflowErrorCodes.NotFound), WorkflowErrors.NotFound(), ErrorType.NotFound },
        { nameof(WorkflowErrorCodes.AlreadyStarted), WorkflowErrors.AlreadyStarted(), ErrorType.Conflict },
        { nameof(WorkflowErrorCodes.Cancelled), WorkflowErrors.Cancelled(), ErrorType.Unexpected },
        { nameof(WorkflowErrorCodes.Terminated), WorkflowErrors.Terminated(), ErrorType.Unexpected },
        { nameof(WorkflowErrorCodes.TimedOut), WorkflowErrors.TimedOut(), ErrorType.Timeout },
        { nameof(WorkflowErrorCodes.RequestTimedOut), WorkflowErrors.RequestTimedOut(), ErrorType.Timeout },
        { nameof(WorkflowErrorCodes.RpcFailed), WorkflowErrors.RpcFailed(), ErrorType.Unexpected },
        { nameof(WorkflowErrorCodes.QueryFailed), WorkflowErrors.QueryFailed("the reason."), ErrorType.Unexpected },
        { nameof(WorkflowErrorCodes.ServiceUnavailable), WorkflowErrors.ServiceUnavailable(), ErrorType.Unavailable },
        { nameof(WorkflowErrorCodes.NamespaceNotFound), WorkflowErrors.NamespaceNotFound("ns"), ErrorType.Unexpected },
        { nameof(WorkflowErrorCodes.TenantScopeMissing), WorkflowErrors.TenantScopeMissing(), ErrorType.Unauthorized },
        { nameof(WorkflowErrorCodes.InvalidWorkflowId), WorkflowErrors.InvalidWorkflowId("the id is blank."), ErrorType.Validation },
        { nameof(WorkflowErrorCodes.PayloadCodecFailure), WorkflowErrors.PayloadCodecFailure(), ErrorType.Unexpected },
        { nameof(WorkflowErrorCodes.UnmappedFailure), WorkflowErrors.UnmappedFailure(), ErrorType.Unexpected },
    };

    private static IEnumerable<FieldInfo> PublicCodes() =>
        typeof(WorkflowErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral);

    [Theory]
    [MemberData(nameof(AllFactories))]
    public void Factory_ReturnsItsCodeAndErrorType(string codeName, Error error, ErrorType expectedType)
    {
        string expectedCode = (string)typeof(WorkflowErrorCodes).GetField(codeName)!.GetRawConstantValue()!;

        error.Code.Should().Be(expectedCode);
        error.Type.Should().Be(expectedType, because: $"{codeName} is fixed as {expectedType}");
        error.Message.Should().NotBeNullOrWhiteSpace().And.EndWith(".");
    }

    [Theory]
    [MemberData(nameof(AllFactories))]
    public void Factory_NeverReturnsNoneOrBusinessRule(string codeName, Error error, ErrorType _)
    {
        error.Should().NotBe(Error.None, because: codeName);
        error.Type.Should().NotBe(ErrorType.None).And.NotBe(ErrorType.BusinessRule);
    }

    [Fact]
    public void EveryPublicConstant_IsLowercaseWorkflowSnakeReason()
    {
        PublicCodes().Should().NotBeEmpty();
        foreach (FieldInfo field in PublicCodes())
        {
            ((string)field.GetRawConstantValue()!).Should().MatchRegex(CodeFormat().ToString(), because: field.Name);
        }
    }

    [Fact]
    public void EveryPublicConstant_HasAProducer()
    {
        // ApplicationFailure is produced by the mapper for a failure that carries no error type; every other code has a factory row.
        HashSet<string> producedByFactories = [.. AllFactories().Select(row => ((Error)row[1]!).Code)];
        ApplicationFailureException untyped = new("message", errorType: null);

        string[] producerLess = PublicCodes()
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(code => !producedByFactories.Contains(code))
            .ToArray();

        producerLess.Should().BeEquivalentTo([WorkflowErrorCodes.ApplicationFailure]);
        WorkflowFailureMapper.ToError(untyped).Code.Should().Be(WorkflowErrorCodes.ApplicationFailure);
    }

    [Fact]
    public void EveryFactoryCode_IsAPublicConstant()
    {
        HashSet<string> publicCodes = [.. PublicCodes().Select(field => (string)field.GetRawConstantValue()!)];

        foreach (var row in AllFactories())
        {
            publicCodes.Should().Contain(((Error)row[1]!).Code);
        }
    }

    [Theory]
    [MemberData(nameof(AllFactories))]
    public void Factory_RoundTripsThroughToFailureAndToError(string codeName, Error error, ErrorType _)
    {
        ApplicationFailureException failure = WorkflowFailureMapper.ToFailure(error);

        Error back = WorkflowFailureMapper.ToError(failure);

        back.Code.Should().Be(error.Code, because: codeName);
        back.Type.Should().Be(error.Type, because: codeName);
    }

    [Fact]
    public void Messages_NameNoPlaceholderOrExceptionText()
    {
        foreach (var row in AllFactories())
        {
            ((Error)row[1]!).Message.Should().NotContain("{").And.NotContain("Exception");
        }
    }

    [GeneratedRegex(@"^workflow\.[a-z]+(_[a-z]+)*$")]
    private static partial Regex CodeFormat();
}
