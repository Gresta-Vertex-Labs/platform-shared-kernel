// Proves the observability rules reach a consumer from the packed analyzer (WO-087). The kernel types are stubbed under
// their real metadata names, because the analyzer matches by name and this harness references no kernel assembly.
// Expected: SK0043 on NotDerivingException, and SK0044 on the literal EventId only because .editorconfig opts in
// (the rule ships disabled).
using System;

namespace SharedKernel.Primitives.Errors
{
    public sealed class Error { }
}

namespace Microsoft.Extensions.Logging
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LoggerMessageAttribute : Attribute
    {
        public LoggerMessageAttribute(int eventId, int level, string message) { }
    }
}

namespace AnalyzerConsumer
{
    public sealed class NotDerivingException : Exception
    {
        public SharedKernel.Primitives.Errors.Error Error { get; } = new();
    }

    public static class Log
    {
        [Microsoft.Extensions.Logging.LoggerMessage(1234, 2, "Done.")]
        public static void Done() { }
    }
}
