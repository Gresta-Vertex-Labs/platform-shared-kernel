namespace SharedKernel.ArchitectureTests.Helpers;

/// <summary>
/// Thrown by <see cref="ArchitectureRuleBase.AssertRule"/> when an evaluated architecture rule
/// reports one or more violating types.
/// </summary>
/// <remarks>
/// <para>
/// This package deliberately ships no assertion-library dependency, so rule failures surface as
/// this exception rather than through a third-party assertion API. Every xUnit/NUnit/MSTest
/// runner reports an unhandled exception as a test failure, so a rule violation fails the test
/// exactly as an assertion would — and the message carries the same failing-type list.
/// </para>
/// <para>
/// Assert on rules with your own assertion library instead if you prefer: call
/// <c>ruleFactory(assembly).GetResult()</c> directly and inspect
/// <c>IsSuccessful</c>/<c>FailingTypeNames</c>. <see cref="ArchitectureRuleBase.AssertRule"/> is
/// a convenience, never a requirement.
/// </para>
/// </remarks>
public sealed class ArchitectureRuleViolationException : Exception
{
    /// <summary>
    /// Initializes a new instance with an explicit message.
    /// </summary>
    /// <param name="message">The failure description, including the violating type names.</param>
    public ArchitectureRuleViolationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance with an explicit message and an inner exception.
    /// </summary>
    /// <param name="message">The failure description, including the violating type names.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public ArchitectureRuleViolationException(string message, Exception? innerException)
        : base(message, innerException) { }

    /// <summary>
    /// The names of the types that violated the rule, in the order the rule engine reported
    /// them. Empty when the rule failed without attributing specific types.
    /// </summary>
    public IReadOnlyList<string> FailingTypeNames { get; init; } = Array.Empty<string>();
}
