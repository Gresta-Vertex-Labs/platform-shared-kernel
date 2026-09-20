using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// EF Core implementation of <see cref="ISpecificationEvaluator{T}"/>.
/// Translates an <see cref="ISpecification{T}"/> into a composable <see cref="IQueryable{T}"/>
/// pipeline in the following strict order:
/// <list type="number">
/// <item><description>TagWith(spec.GetType().Name) — automatic, applied first</description></item>
/// <item><description>IgnoreQueryFilters(["SoftDelete"]) — only when <c>spec.IncludeDeleted == true</c>; the tenant filter is never dropped this way</description></item>
/// <item><description>Criteria (Where clause — <see langword="null"/> matches all entities)</description></item>
/// <item><description>Keyset seek predicate — <see cref="GetKeysetQuery{TKey}"/> only; skipped on the first page</description></item>
/// <item><description>Includes (expression-based Include / ThenInclude eager loading)</description></item>
/// <item><description>StringIncludes (string-based Include paths — applied after expression includes, before ordering)</description></item>
/// <item><description>AsSplitQuery — only when <c>spec.AsSplitQuery == true</c></description></item>
/// <item><description>Distinct (moved BEFORE ordering)</description></item>
/// <item><description>OrderBy / OrderByDescending (primary sort)</description></item>
/// <item><description>ThenBys (secondary sorts — only when a primary sort is set)</description></item>
/// <item><description>AsNoTracking</description></item>
/// <item><description>Skip / Take — paging; <strong>always the final operation</strong> for the aggregate pipeline; requires a primary sort</description></item>
/// <item><description>Select(spec.Selector) — projection overload only; applied after Skip/Take</description></item>
/// </list>
/// </summary>
/// <typeparam name="T">The entity type this evaluator operates on.</typeparam>
/// <remarks>
/// <para>
/// The paging-last invariant ensures that ordering is stable before any Skip/Take is applied.
/// ThenBy entries are silently ignored when no primary sort has been configured.
/// </para>
/// <para>
/// <strong>Distinct-before-ordering:</strong> applying <c>.Distinct()</c> AFTER
/// <c>.OrderBy(...)</c> — the pre-W2 order — does not reliably produce a correctly-ordered distinct
/// result once translated to SQL; the provider-correct shape computes the distinct set first, then
/// orders it. Every specification's row shape and result set are identical either way; only the
/// generated SQL's structure changes.
/// </para>
/// <para>
/// <strong>Paging requires an order:</strong> <c>Skip</c>/<c>Take</c> without a
/// primary sort produces a database-dependent, unstable row order across identical calls — this
/// evaluator now throws <see cref="InvalidOperationException"/> rather than silently returning
/// whatever order the storage engine happens to produce.
/// </para>
/// <para>
/// <strong>Breaking change:</strong> <c>QueryableExtensions.IgnoreSoftDeleteFilter()</c>
/// has been removed. Use <c>spec.IncludeDeleted = true</c> instead — this evaluator calls
/// <c>.IgnoreQueryFilters(["SoftDelete"])</c> automatically at step 0.
/// </para>
/// </remarks>
public sealed class SpecificationEvaluator<T> : ISpecificationEvaluator<T>
    where T : class
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <strong>Split-query application point:</strong> step 2c calls
    /// <c>.AsSplitQuery()</c> immediately after <c>StringIncludes</c> and before ordering, but only
    /// when <c>spec.</c><see cref="ISpecification{T}.AsSplitQuery"/> is <see langword="true"/>. See
    /// <see cref="ISpecification{T}.AsSplitQuery"/>'s own remarks (declared in
    /// <c>SharedKernel.Domain</c>) for the full Cartesian-product rationale: a single joined query
    /// against a specification with two or more collection <c>Includes</c> duplicates rows across the
    /// included collections, and splitting into separate queries avoids that duplication. Default
    /// <see langword="false"/> — behavior for every existing specification that never opts in is
    /// byte-for-byte unchanged.
    /// </para>
    /// </remarks>
    public IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec)
    {
        var query = inputQuery.TagWith(spec.GetType().Name);

        // 0. IgnoreQueryFilters — SELECTIVE: only the "SoftDelete" named filter is
        // dropped. The "Tenant" named filter is NEVER dropped here — cross-tenant access is only
        // possible via an explicit ICrossTenantScope.
        if (spec.IncludeDeleted)
            query = query.IgnoreQueryFilters([PersistenceFilterNames.SoftDelete]);

        // 1. Criteria
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        // 2. Includes (expression-based)
        query = spec.Includes.Aggregate(query,
            (current, include) => current.Include(include));

        // 2b. StringIncludes — applied after expression includes, before ordering.
        // Intended for deep navigation paths (e.g., "Orders.Items.Product").
        // Empty list is a no-op; existing specs are unaffected.
        foreach (var path in spec.StringIncludes)
        {
            if (!string.IsNullOrWhiteSpace(path))
                query = query.Include(path);
        }

        // 2c. AsSplitQuery — only when the spec declares two or more collection Includes and
        // opts in via ApplySplitQuery(). Default false — behavior is byte-for-byte unchanged
        // for every existing specification.
        if (spec.AsSplitQuery)
            query = query.AsSplitQuery();

        query = ApplyDistinctOrderingTrackingAndPaging(query, spec);

        return query;
    }

    /// <summary>
    /// Applies the full aggregate pipeline (steps 0–7) then projects the result using the
    /// <paramref name="spec"/>'s selector (step 8).
    /// </summary>
    /// <typeparam name="TResult">The projection output type.</typeparam>
    /// <param name="inputQuery">The base queryable to build upon.</param>
    /// <param name="spec">The projection specification supplying both the pipeline and the selector.</param>
    /// <returns>
    /// An <see cref="IQueryable{TResult}"/> with Select applied after all aggregate pipeline steps.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Selector is applied at step 8 — after Skip/Take — to preserve the paging-last invariant.
    /// </para>
    /// <para>
    /// <strong>Split-query application point:</strong> because this method delegates
    /// to <see cref="GetQuery"/> for steps 0–7, <c>spec.</c><see cref="ISpecification{T}.AsSplitQuery"/>
    /// is honored identically for a projected query — see <see cref="GetQuery"/>'s own remarks, and
    /// <see cref="ISpecification{T}.AsSplitQuery"/>'s remarks in <c>SharedKernel.Domain</c>, for the
    /// Cartesian-product rationale.
    /// </para>
    /// </remarks>
    public IQueryable<TResult> GetProjectedQuery<TResult>(
        IQueryable<T> inputQuery,
        IProjectionSpecification<T, TResult> spec)
    {
        // Apply the full aggregate pipeline (steps 0–7).
        var query = GetQuery(inputQuery, spec);

        // Step 8 — Select(spec.Selector): applied after paging.
        return query.Select(spec.Selector);
    }

    /// <inheritdoc />
    /// <remarks>
    /// See <see cref="ISpecificationEvaluator{T}.GetKeysetQuery{TKey}"/> for the full pipeline and
    /// deviation documentation.
    /// </remarks>
    public IQueryable<T> GetKeysetQuery<TKey>(
        IQueryable<T> inputQuery,
        KeysetSpecification<T, TKey> spec)
        where TKey : struct, IComparable<TKey>
    {
        var query = inputQuery.TagWith(spec.GetType().Name);

        // 0. IgnoreQueryFilters — selective, see GetQuery's remarks.
        if (spec.IncludeDeleted)
            query = query.IgnoreQueryFilters([PersistenceFilterNames.SoftDelete]);

        // 1. Criteria
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        // 1b. Keyset seek predicate — skipped entirely on the first page.
        if (spec.AfterKey.HasValue)
            query = query.Where(BuildKeysetSeekPredicate(spec));

        // 2. Includes
        query = spec.Includes.Aggregate(query,
            (current, include) => current.Include(include));

        // 2b. StringIncludes
        foreach (var path in spec.StringIncludes)
        {
            if (!string.IsNullOrWhiteSpace(path))
                query = query.Include(path);
        }

        // 2c. AsSplitQuery
        if (spec.AsSplitQuery)
            query = query.AsSplitQuery();

        // 3. Distinct, 4. OrderBy/ThenBys, 5. AsNoTracking (a KeysetSpecification always declares a
        // primary sort in its own constructor, so the "paging requires order" guard never fires).
        query = ApplyDistinctOrderingTrackingAndPaging(
            query, spec, applyPaging: false);

        // 7. Paging deviation: never Skip (always 0 for a keyset spec); Take one row beyond the
        // declared page size so the caller can compute HasMore without a second round-trip.
        query = query.Take(spec.Take!.Value + 1);

        return query;
    }

    // Applies steps 3-6 (Distinct/OrderBy/ThenBys/AsNoTracking) and, when applyPaging is true, step 7
    // (Skip/Take) — shared by GetQuery and GetKeysetQuery (which applies its own deviated paging step
    // separately). Distinct runs BEFORE ordering; paging without a primary sort
    // throws rather than silently returning a database-dependent row order.
    private static IQueryable<T> ApplyDistinctOrderingTrackingAndPaging(
        IQueryable<T> query,
        ISpecification<T> spec,
        bool applyPaging = true)
    {
        // 3. Distinct — before ordering.
        if (spec.IsDistinct)
            query = query.Distinct();

        // 4. Primary sort
        var hasPrimarySort = false;
        IOrderedQueryable<T>? ordered = null;

        if (spec.OrderBy is not null)
        {
            ordered = query.OrderBy(spec.OrderBy);
            hasPrimarySort = true;
        }
        else if (spec.OrderByDescending is not null)
        {
            ordered = query.OrderByDescending(spec.OrderByDescending);
            hasPrimarySort = true;
        }

        // 4b. ThenBys — only when primary sort is set
        if (hasPrimarySort && ordered is not null)
        {
            foreach (var (keySelector, descending) in spec.ThenBys)
            {
                ordered = descending
                    ? ordered.ThenByDescending(keySelector)
                    : ordered.ThenBy(keySelector);
            }

            query = ordered;
        }

        // 5. AsNoTracking
        if (spec.AsNoTracking)
            query = query.AsNoTracking();

        // 6. Skip / Take — ALWAYS LAST for the aggregate pipeline
        if (applyPaging && (spec.Skip.HasValue || spec.Take.HasValue))
        {
            if (!hasPrimarySort)
            {
                throw new InvalidOperationException(
                    $"'{spec.GetType().Name}' declares Skip/Take paging but no primary sort " +
                    "(OrderBy/OrderByDescending). Paging without a deterministic order produces a " +
                    "database-dependent, unstable row order across identical calls — add " +
                    "ApplyOrderBy/ApplyOrderByDescending to the specification.");
            }

            if (spec.Skip.HasValue)
                query = query.Skip(spec.Skip.Value);

            if (spec.Take.HasValue)
                query = query.Take(spec.Take.Value);
        }

        return query;
    }

    // Builds the standard keyset/seek-pagination tuple-comparison predicate:
    // (OrderKey > @afterKey) OR (OrderKey == @afterKey AND Id > @afterId)
    // flipped to "<" throughout when spec.Descending == true. Rebinds spec.OrderBy/OrderByDescending
    // and spec.ThenBys[0].KeySelector (both Expression<Func<T,object>>, each wrapped in an outer
    // Convert(..., typeof(object)) node) onto one shared parameter, then unwraps each Convert node
    // back to its real typed operand before comparison — CRITICAL so any registered ValueConverter
    // (e.g. StronglyTypedIdValueConverter on the Id) is applied server-side by the LINQ provider —
    // the same precedent already established for GetByIdsAsync's Contains fix.
    private static Expression<Func<T, bool>> BuildKeysetSeekPredicate<TKey>(KeysetSpecification<T, TKey> spec)
        where TKey : struct, IComparable<TKey>
    {
        var keySelectorExpr = spec.Descending ? spec.OrderByDescending! : spec.OrderBy!;
        var idSelectorExpr = spec.ThenBys[0].KeySelector;

        var param = Expression.Parameter(typeof(T), "e");

        var keyBody = new ParameterReplacer(keySelectorExpr.Parameters[0], param).Visit(keySelectorExpr.Body)!;
        var idBody = new ParameterReplacer(idSelectorExpr.Parameters[0], param).Visit(idSelectorExpr.Body)!;

        var keyOperand = UnwrapConvert(keyBody);
        var idOperand = UnwrapConvert(idBody);

        // Parameterized via a closure-shaped field access, not a bare
        // Expression.Constant(value, type) — see ByIdSpecification's remarks for why this matters.
        var keyHolder = new KeyHolder<TKey>(spec.AfterKey!.Value);
        var afterKeyConstant = Expression.Field(Expression.Constant(keyHolder), nameof(KeyHolder<TKey>.Value));

        // AfterId's runtime type is validated/coerced against the tiebreaker's
        // actual CLR type up front, with a clear diagnostic, instead of letting a raw
        // Expression.Constant type mismatch throw an opaque ArgumentException (or, for the closure
        // form, an InvalidCastException deferred to parameter materialization).
        var coercedAfterId = CoerceAfterId(spec.AfterId, idOperand.Type);
        var idHolder = new IdHolder(coercedAfterId);
        var afterIdConstant = Expression.Convert(
            Expression.Field(Expression.Constant(idHolder), nameof(IdHolder.Value)),
            idOperand.Type);

        var keyGreaterOrLess = BuildOrderingComparison(keyOperand, afterKeyConstant, spec.Descending);
        var keyEquals = Expression.Equal(keyOperand, afterKeyConstant);
        var idGreaterOrLess = BuildOrderingComparison(idOperand, afterIdConstant, spec.Descending);

        var tieBreak = Expression.AndAlso(keyEquals, idGreaterOrLess);
        var seekBody = Expression.OrElse(keyGreaterOrLess, tieBreak);

        return Expression.Lambda<Func<T, bool>>(seekBody, param);
    }

    // Validates that `afterId`'s runtime type matches (or can be safely, deterministically coerced
    // to) `targetType` — the aggregate's actual identity-tiebreaker CLR type — throwing a clear,
    // actionable InvalidOperationException instead of letting a type mismatch surface as an opaque
    // exception from deep inside expression-tree construction or query-parameter materialization
    //. A typical cause: a caller decoded a wire cursor with PageCursor.Decode<TKey,
    // TId> using the wrong TId, or hand-built a KeysetSpecification with an AfterId of the wrong type.
    private static object CoerceAfterId(object? afterId, Type targetType)
    {
        if (afterId is null)
        {
            throw new InvalidOperationException(
                "KeysetSpecification.AfterId must not be null when AfterKey is set — both cursor " +
                "components are required together.");
        }

        if (targetType.IsInstanceOfType(afterId))
            return afterId;

        if (targetType == typeof(Guid) && afterId is string guidText && Guid.TryParse(guidText, out var guid))
            return guid;

        if (typeof(IConvertible).IsAssignableFrom(afterId.GetType())
            && typeof(IConvertible).IsAssignableFrom(targetType))
        {
            try
            {
                return Convert.ChangeType(afterId, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                // Falls through to the diagnostic exception below.
            }
        }

        throw new InvalidOperationException(
            $"KeysetSpecification.AfterId has runtime type '{afterId.GetType().Name}' but this " +
            $"aggregate's identity tiebreaker expects '{targetType.Name}'. This usually means a " +
            "wire cursor was decoded with the wrong TId — decode with the same type the cursor was " +
            "encoded with.");
    }

    // Unwraps a single outer Convert(..., typeof(object)) node (inserted by the compiler when a
    // typed lambda body is assigned to an Expression<Func<T,object>>), returning the real typed
    // operand expression. A no-op when the body carries no such node.
    private static Expression UnwrapConvert(Expression expr) =>
        expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : expr;

    // Builds `left.CompareTo(right) > 0` (or "< 0" when descending) — the universal,
    // EF-Core-translatable way to express ordering comparisons for ANY IComparable<T> type,
    // including types with no native `>`/`<` operator overload (e.g. Guid). Falls back to
    // unwrapping a StronglyTypedId<TValue>-shaped explicit conversion when CompareTo isn't found
    // directly on the operand's own type, so a strongly-typed ID used as the mandatory Id
    // tiebreaker still compares correctly via its underlying primitive value.
    private static Expression BuildOrderingComparison(Expression left, Expression right, bool descending)
    {
        var operandType = left.Type;
        var compareToMethod = operandType.GetMethod(nameof(IComparable<object>.CompareTo), [operandType]);

        if (compareToMethod is null)
        {
            // StronglyTypedId<TValue> itself does not implement IComparable<T> — it always wraps a
            // single, real comparable value. Unwrap via its explicit conversion to TValue and retry
            // comparison there. Two reflection pitfalls avoided here, both confirmed empirically:
            // (1) a generic "any op_Explicit" scan via Type.GetMethod on the CONCRETE type (e.g.
            // TestId) never finds it — Type.GetMethod does not return inherited STATIC members
            // without BindingFlags.FlattenHierarchy, and the operator is declared on the
            // StronglyTypedId<TValue> BASE, not the concrete sealed record; (2) even after locating
            // the base type, Expression.Convert(expr, type) alone (the 2-arg overload) still throws
            // "No coercion operator is defined" — its built-in operator search does not consider an
            // operator declared on a base type either. The fix is the 3-arg
            // Expression.Convert(expr, type, method) overload, passing the operator's MethodInfo
            // explicitly (found directly on the closed StronglyTypedId<TValue> base type itself,
            // not via inheritance, so no FlattenHierarchy is needed for that lookup).
            var conversion = FindStronglyTypedIdConversion(operandType);
            if (conversion is { } found)
            {
                var convertedLeft = Expression.Convert(left, found.ValueType, found.ConversionMethod);
                var convertedRight = Expression.Convert(right, found.ValueType, found.ConversionMethod);
                return BuildOrderingComparison(convertedLeft, convertedRight, descending);
            }

            throw new InvalidOperationException(
                $"Type '{operandType.Name}' does not implement IComparable<{operandType.Name}> and is " +
                "not a StronglyTypedId<TValue> — it cannot be used as a KeysetSpecification<T,TKey> " +
                "sort key or Id tiebreaker.");
        }

        var compareCall = Expression.Call(left, compareToMethod, right);
        var zero = Expression.Constant(0);

        return descending
            ? Expression.LessThan(compareCall, zero)
            : Expression.GreaterThan(compareCall, zero);
    }

    // Walks the base-type chain looking for the closed StronglyTypedId<TValue> generic base,
    // returning TValue plus the explicit-conversion MethodInfo (declared directly on that closed
    // base type — e.g. StronglyTypedId<Guid> — not on the concrete sealed record) when found;
    // otherwise null. Query-build-time-only reflection (once per GetKeysetQuery call, never
    // per-row) — the same accepted class of build-time type inspection already used elsewhere in
    // this evaluator/EfReadRepository (e.g. GetByIdsAsync's Contains method lookup).
    private static (Type ValueType, MethodInfo ConversionMethod)? FindStronglyTypedIdConversion(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType
                || current.GetGenericTypeDefinition() != typeof(SharedKernel.Domain.StronglyTypedIds.StronglyTypedId<>))
            {
                continue;
            }

            var valueType = current.GetGenericArguments()[0];
            var conversionMethod = current.GetMethod(
                "op_Explicit", BindingFlags.Public | BindingFlags.Static, [current]);

            if (conversionMethod is not null)
                return (valueType, conversionMethod);
        }

        return null;
    }

    // Rebinds every occurrence of one ParameterExpression onto another — used to combine the
    // key selector and Id selector (each originally over its own parameter instance) onto a single
    // shared parameter for the seek predicate.
    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }

    // Mimics a compiler-generated closure display class so EF Core's query-parameter extraction
    // recognizes and parameterizes the captured sort-key cursor value.
    private sealed class KeyHolder<TKey>(TKey value)
        where TKey : struct, IComparable<TKey>
    {
        public readonly TKey Value = value;
    }

    // Same purpose as KeyHolder<TKey>, for the (already type-checked/coerced) identity cursor value —
    // held as `object` here because the target CLR type is only known at Expression.Convert time.
    private sealed class IdHolder(object value)
    {
        public readonly object Value = value;
    }
}
