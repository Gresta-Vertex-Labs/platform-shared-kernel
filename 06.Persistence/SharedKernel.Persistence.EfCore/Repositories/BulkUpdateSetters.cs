using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Records the columns a bulk update sets, so they can be validated before the statement runs and then applied
/// to EF Core's <c>ExecuteUpdate</c>.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type being updated.</typeparam>
/// <remarks>
/// A target is a member path on the aggregate, including properties of complex types
/// (<c>x =&gt; x.Contact.Email</c>). Navigations, <c>EF.Property</c> and computed expressions are rejected.
/// </remarks>
public sealed class BulkUpdateSetters<TAggregate>
    where TAggregate : class
{
    private readonly List<(LambdaExpression Target, Action<UpdateSettersBuilder<TAggregate>> Apply)> _setters = [];

    internal BulkUpdateSetters()
    {
    }

    /// <summary>Gets the recorded setters, in call order.</summary>
    internal IReadOnlyList<(LambdaExpression Target, Action<UpdateSettersBuilder<TAggregate>> Apply)> Setters => _setters;

    /// <summary>Sets a column to a constant value.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">The target, such as <c>x =&gt; x.Status</c>. Must not be <see langword="null"/>.</param>
    /// <param name="value">The value, sent as a SQL parameter.</param>
    /// <returns>This recorder.</returns>
    public BulkUpdateSetters<TAggregate> SetProperty<TProperty>(Expression<Func<TAggregate, TProperty>> property, TProperty value)
    {
        ArgumentNullException.ThrowIfNull(property);
        _setters.Add((property, builder => builder.SetProperty(property, value)));
        return this;
    }

    /// <summary>Sets a column to an expression over the row, such as <c>x =&gt; x.Stock - 1</c>.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">The target. Must not be <see langword="null"/>.</param>
    /// <param name="valueExpression">The new value, translated to SQL. Must not be <see langword="null"/>.</param>
    /// <returns>This recorder.</returns>
    public BulkUpdateSetters<TAggregate> SetProperty<TProperty>(
        Expression<Func<TAggregate, TProperty>> property,
        Expression<Func<TAggregate, TProperty>> valueExpression)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(valueExpression);
        _setters.Add((property, builder => builder.SetProperty(property, valueExpression)));
        return this;
    }

    /// <summary>Applies every recorded setter, then <paramref name="extra"/>, to EF Core's builder.</summary>
    internal Action<UpdateSettersBuilder<TAggregate>> ToEfSetters(Action<UpdateSettersBuilder<TAggregate>>? extra) =>
        builder =>
        {
            foreach (var (_, apply) in _setters)
                apply(builder);

            extra?.Invoke(builder);
        };
}
