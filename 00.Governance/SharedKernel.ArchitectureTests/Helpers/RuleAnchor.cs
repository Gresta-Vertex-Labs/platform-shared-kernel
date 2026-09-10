using System.Reflection;

namespace SharedKernel.ArchitectureTests.Helpers;

/// <summary>
/// Argument validation for caller-supplied anchor types on the rules that no longer bind to a
/// SharedKernel assembly directly.
/// </summary>
/// <remarks>
/// A few rules select the types they judge via <c>.ImplementInterface(anchor)</c> or
/// <c>.Inherit(anchor)</c>. If a caller passes the wrong kind of <see cref="Type"/>, NetArchTest
/// selects zero types and the rule passes VACUOUSLY — the exact hazard once found in the
/// guard-purity rule. These checks convert that silent false-pass into a loud
/// <see cref="ArgumentException"/> at the call site.
/// </remarks>
internal static class RuleAnchor
{
    /// <summary>
    /// Validates that <paramref name="anchor"/> is a non-null interface type.
    /// </summary>
    /// <param name="anchor">The caller-supplied anchor type.</param>
    /// <param name="parameterName">The parameter name to report on failure.</param>
    /// <returns><paramref name="anchor"/>, unchanged, when valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="anchor"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="anchor"/> is not an interface.</exception>
    internal static Type Interface(Type anchor, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(anchor, parameterName);

        if (!anchor.IsInterface)
        {
            throw new ArgumentException(
                $"'{anchor.FullName}' is not an interface. This rule selects the types it judges "
                    + "by interface implementation; passing a non-interface type would select zero "
                    + "types and make the rule pass vacuously.",
                parameterName);
        }

        return anchor;
    }

    /// <summary>
    /// Validates that <paramref name="anchor"/> is a non-null, non-sealed class usable as an
    /// inheritance target.
    /// </summary>
    /// <param name="anchor">The caller-supplied anchor type.</param>
    /// <param name="parameterName">The parameter name to report on failure.</param>
    /// <returns><paramref name="anchor"/>, unchanged, when valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="anchor"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="anchor"/> is not a class, or is sealed so nothing can inherit from it.
    /// </exception>
    internal static Type BaseClass(Type anchor, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(anchor, parameterName);

        if (!anchor.IsClass)
        {
            throw new ArgumentException(
                $"'{anchor.FullName}' is not a class and cannot be an inheritance target.",
                parameterName);
        }

        if (anchor.IsSealed)
        {
            throw new ArgumentException(
                $"'{anchor.FullName}' is sealed, so no type can inherit from it. This rule would "
                    + "fail every selected type.",
                parameterName);
        }

        return anchor;
    }

    /// <summary>
    /// Validates that <paramref name="assembly"/> is not null.
    /// </summary>
    /// <param name="assembly">The caller-supplied assembly under inspection.</param>
    /// <param name="parameterName">The parameter name to report on failure.</param>
    /// <returns><paramref name="assembly"/>, unchanged, when valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    internal static Assembly NotNull(Assembly assembly, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(assembly, parameterName);
        return assembly;
    }
}
