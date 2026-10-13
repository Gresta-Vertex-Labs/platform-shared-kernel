using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SharedKernel.Communication.Tests;

/// <summary>The error-code ledger of the domain: every value is a contract, so every value is pinned by a literal.</summary>
public sealed partial class CommunicationErrorCodesTests
{
    [Fact]
    public void The_communication_codes_are_pinned()
    {
        CommunicationErrorCodes.Unavailable.Should().Be("communication.unavailable");
        CommunicationErrorCodes.Timeout.Should().Be("communication.timeout");
        CommunicationErrorCodes.CircuitOpen.Should().Be("communication.circuit_open");
        CommunicationErrorCodes.AccessTokenUnavailable.Should().Be("communication.access_token_unavailable");
        CommunicationErrorCodes.EmptyBody.Should().Be("communication.empty_body");
        CommunicationErrorCodes.InvalidBody.Should().Be("communication.invalid_body");
        CommunicationErrorCodes.GrpcInvalidMoney.Should().Be("communication.grpc.invalid_money");
    }

    [Fact]
    public void The_grpc_status_names_are_byte_identical_to_the_names_14_presentation_gives_them()
    {
        CommunicationErrorCodes.GrpcStatus.Ok.Should().Be("grpc.ok");
        CommunicationErrorCodes.GrpcStatus.Cancelled.Should().Be("grpc.cancelled");
        CommunicationErrorCodes.GrpcStatus.Unknown.Should().Be("grpc.unknown");
        CommunicationErrorCodes.GrpcStatus.InvalidArgument.Should().Be("grpc.invalid_argument");
        CommunicationErrorCodes.GrpcStatus.DeadlineExceeded.Should().Be("grpc.deadline_exceeded");
        CommunicationErrorCodes.GrpcStatus.NotFound.Should().Be("grpc.not_found");
        CommunicationErrorCodes.GrpcStatus.AlreadyExists.Should().Be("grpc.already_exists");
        CommunicationErrorCodes.GrpcStatus.PermissionDenied.Should().Be("grpc.permission_denied");
        CommunicationErrorCodes.GrpcStatus.ResourceExhausted.Should().Be("grpc.resource_exhausted");
        CommunicationErrorCodes.GrpcStatus.FailedPrecondition.Should().Be("grpc.failed_precondition");
        CommunicationErrorCodes.GrpcStatus.Aborted.Should().Be("grpc.aborted");
        CommunicationErrorCodes.GrpcStatus.OutOfRange.Should().Be("grpc.out_of_range");
        CommunicationErrorCodes.GrpcStatus.Unimplemented.Should().Be("grpc.unimplemented");
        CommunicationErrorCodes.GrpcStatus.Internal.Should().Be("grpc.internal");
        CommunicationErrorCodes.GrpcStatus.Unavailable.Should().Be("grpc.unavailable");
        CommunicationErrorCodes.GrpcStatus.DataLoss.Should().Be("grpc.data_loss");
        CommunicationErrorCodes.GrpcStatus.Unauthenticated.Should().Be("grpc.unauthenticated");
        CommunicationErrorCodes.GrpcStatus.NumberPrefix.Should().Be("grpc.");
        ConstantsOf(typeof(CommunicationErrorCodes.GrpcStatus)).Where(c => !c.Value.EndsWith('.')).Should().HaveCount(17);
    }

    [Fact]
    public void The_http_status_family_is_pinned()
    {
        CommunicationErrorCodes.HttpStatusPrefix.Should().Be("http.");
        CommunicationErrorCodes.ForHttpStatus(404).Should().Be("http.404");
        CommunicationErrorCodes.ForHttpStatus(503).Should().Be("http.503");
        CommunicationErrorCodes.ForHttpStatus(599).Should().Be("http.599");
    }

    [Fact]
    public void Every_code_is_lowercase_domain_area_snake_reason()
    {
        List<KeyValuePair<string, string>> codes =
        [
            .. ConstantsOf(typeof(CommunicationErrorCodes)),
            .. ConstantsOf(typeof(CommunicationErrorCodes.GrpcStatus)),
        ];

        // A bare prefix is not a code: the two prefixes are internal, and the status number is appended by a helper.
        codes.Should().NotContain(c => c.Value.EndsWith('.'));
        foreach ((string name, string value) in codes.Where(c => !c.Value.EndsWith('.')))
        {
            CodePattern().IsMatch(value).Should().BeTrue($"{name} = '{value}' must be lowercase {{domain}}[.{{area}}].{{snake_reason}}");
        }

        codes.Select(c => c.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_retired_code_names_survive_nowhere_in_the_compiled_assembly()
    {
        Assembly assembly = typeof(CommunicationOptions).Assembly;

        AssemblyContains(assembly, "communication.timeout").Should().BeTrue("the scan must find a literal that is there");
        AssemblyContains(assembly, "communication.unreachable").Should().BeFalse();
        AssemblyContains(assembly, "money.proto.invalid").Should().BeFalse();
    }

    [Fact]
    public void The_retired_code_names_survive_in_no_readme()
    {
        string root = RepositoryRoot();
        string[] readmes = Directory.GetFiles(Path.Combine(root, "src", "Infrastructure", "Communication"), "README.md", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        readmes.Should().NotBeEmpty();
        foreach (string readme in readmes)
        {
            string text = File.ReadAllText(readme);
            text.Should().NotContain("communication.unreachable", $"{Path.GetFileName(Path.GetDirectoryName(readme))} README");
            text.Should().NotContain("money.proto.invalid", $"{Path.GetFileName(Path.GetDirectoryName(readme))} README");
        }
    }

    private static List<KeyValuePair<string, string>> ConstantsOf(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => new KeyValuePair<string, string>(f.Name, (string)f.GetRawConstantValue()!))];

    /// <summary>The string literals compiled into an assembly: they sit in the user-string heap as UTF-16.</summary>
    internal static bool AssemblyContains(Assembly assembly, string literal) =>
        File.ReadAllBytes(assembly.Location).AsSpan().IndexOf(Encoding.Unicode.GetBytes(literal)) >= 0;

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Platform.SharedKernel.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*(_[a-z0-9]+)*)+$")]
    private static partial Regex CodePattern();
}
