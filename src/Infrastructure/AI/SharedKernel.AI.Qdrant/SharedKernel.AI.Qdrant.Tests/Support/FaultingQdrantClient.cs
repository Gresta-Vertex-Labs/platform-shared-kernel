using System.Reflection;
using Qdrant.Client;

namespace SharedKernel.AI.Qdrant.Tests.Support;

/// <summary>
/// An <see cref="IQdrantClient"/> whose every asynchronous member records the call and then fails with the exception
/// the test supplies, so one test body can drive every operation family (including the ones whose signatures carry many
/// optional parameters) without configuring each member. Test-only reflection.
/// </summary>
public class FaultingQdrantClient : DispatchProxy
{
    private readonly List<string> _calls = [];

    /// <summary>Gets or sets the factory of the exception every call faults with.</summary>
    public Func<Exception> Fault { get; set; } = () => new InvalidOperationException("no fault configured");

    /// <summary>
    /// Gets or sets an optional responder: given the member name and the zero-based index of that member's call, it
    /// returns the task result to answer with, or <see langword="null"/> to fault as usual.
    /// </summary>
    public Func<string, int, object?>? Respond { get; set; }

    /// <summary>Gets or sets an optional hook run on every call before it faults (to cancel a token mid-flight).</summary>
    public Action<string>? OnCall { get; set; }

    /// <summary>Gets the names of the members called so far.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Creates a faulting client and returns both the interface view and the controller.</summary>
    public static (IQdrantClient Client, FaultingQdrantClient Controller) Create(Func<Exception> fault)
    {
        var proxy = Create<IQdrantClient, FaultingQdrantClient>();
        var controller = (FaultingQdrantClient)(object)proxy;
        controller.Fault = fault;
        return (proxy, controller);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        int index;
        lock (_calls)
        {
            index = _calls.Count(c => c == targetMethod.Name);
            _calls.Add(targetMethod.Name);
        }

        OnCall?.Invoke(targetMethod.Name);

        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
        {
            return Task.FromException(Fault());
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            if (Respond?.Invoke(targetMethod.Name, index) is { } answer)
            {
                var fromResult = typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(returnType.GetGenericArguments()[0]);
                return fromResult.Invoke(null, [answer]);
            }

            var fromException = typeof(Task)
                .GetMethods()
                .First(m => m.Name == nameof(Task.FromException) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(returnType.GetGenericArguments()[0]);
            return fromException.Invoke(null, [Fault()]);
        }

        return returnType.IsValueType && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
    }
}
