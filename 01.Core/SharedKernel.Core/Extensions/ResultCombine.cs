using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Combines several independent results into one <see cref="ValidationResult"/> or
/// <see cref="ValidationResult{T}"/> that reports every failure, not just the first.
/// </summary>
/// <remarks>
/// <para>
/// Use it when several checks are independent and the caller should see all of them at once, such as validating
/// each field of a form. Every input is evaluated; there is no short-circuit. To stop at the first failure
/// instead, chain the results with <c>Bind</c>.
/// </para>
/// <para>
/// For guard clauses, <c>Guard.Collect</c> does the same without wrapping each guard in a result.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ValidationResult&lt;IReadOnlyList&lt;LineItem&gt;&gt; lines = ResultCombine.Combine(
///     request.Lines.Select(line =&gt; LineItem.Create(line.Sku, line.Quantity)));
///
/// if (!lines.IsValid)
///     return lines.Errors;   // one error per invalid line, in input order
///
/// Order order = Order.Create(lines.Value);
/// </code>
/// </example>
public static class ResultCombine
{
    /// <summary>Combines non-generic results, keeping every failure.</summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// A successful <see cref="ValidationResult"/> when every input succeeded; otherwise a failed one carrying
    /// every failing <see cref="Error"/>, in input order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An input is an uninitialized <c>default(Result)</c>.</exception>
    public static ValidationResult Combine(params Result[] results)
        => Combine((IEnumerable<Result>)results);

    /// <summary>Combines non-generic results, keeping every failure.</summary>
    /// <remarks>The sequence is enumerated once.</remarks>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// A successful <see cref="ValidationResult"/> when every input succeeded; otherwise a failed one carrying
    /// every failing <see cref="Error"/>, in input order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An input is an uninitialized <c>default(Result)</c>.</exception>
    public static ValidationResult Combine(IEnumerable<Result> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<Error>? errors = null;
        foreach (var result in results)
        {
            if (result.IsFailure)
                (errors ??= []).Add(result.Error);
        }

        return errors is null
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    /// <summary>Combines results that carry values, keeping every failure or, when all succeed, every value.</summary>
    /// <typeparam name="T">The success type shared by every input.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// A successful <see cref="ValidationResult{T}"/> carrying every value in input order when every input
    /// succeeded; otherwise a failed one carrying every failing <see cref="Error"/>, in input order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="results"/> contains a <see langword="null"/> result.</exception>
    public static ValidationResult<IReadOnlyList<T>> Combine<T>(params Result<T>[] results)
        => Combine((IEnumerable<Result<T>>)results);

    /// <summary>Combines results that carry values, keeping every failure or, when all succeed, every value.</summary>
    /// <remarks>The sequence is enumerated once.</remarks>
    /// <typeparam name="T">The success type shared by every input.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// A successful <see cref="ValidationResult{T}"/> carrying every value in input order when every input
    /// succeeded; otherwise a failed one carrying every failing <see cref="Error"/>, in input order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="results"/> contains a <see langword="null"/> result.</exception>
    public static ValidationResult<IReadOnlyList<T>> Combine<T>(IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<Error>? errors = null;
        var values = new List<T>();

        foreach (var result in results)
        {
            if (result is null)
                throw new ArgumentException("The results must not contain a null result.", nameof(results));

            if (result.IsSuccess)
                values.Add(result.Value);
            else
                (errors ??= []).Add(result.Error);
        }

        return errors is null
            ? ValidationResult<IReadOnlyList<T>>.Success(values)
            : ValidationResult<IReadOnlyList<T>>.Failure(errors);
    }
}
