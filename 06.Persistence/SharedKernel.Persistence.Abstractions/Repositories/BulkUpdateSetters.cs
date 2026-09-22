using System.ComponentModel;
using System.Linq.Expressions;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Records the columns a bulk update sets, so the repository can validate them before the statement runs and then
/// apply them to its ORM.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type being updated.</typeparam>
/// <remarks>
/// A target is a member path on the aggregate, including properties of complex types
/// (<c>x =&gt; x.Contact.Email</c>). Navigations, <c>EF.Property</c> and computed expressions are rejected by the
/// repository.
/// </remarks>
public sealed class BulkUpdateSetters<TAggregate>
    where TAggregate : class
{
    private readonly List<Setter> _setters = [];

    /// <summary>Initializes an empty recorder. Repositories create it; application code receives it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public BulkUpdateSetters()
    {
    }

    /// <summary>Gets the recorded targets, in call order.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IReadOnlyList<LambdaExpression> Targets => _setters.ConvertAll(s => s.Target);

    /// <summary>Sets a column to a constant value.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">The target, such as <c>x =&gt; x.Status</c>. Must not be <see langword="null"/>.</param>
    /// <param name="value">The value, sent as a SQL parameter.</param>
    /// <returns>This recorder.</returns>
    public BulkUpdateSetters<TAggregate> SetProperty<TProperty>(Expression<Func<TAggregate, TProperty>> property, TProperty value)
    {
        ArgumentNullException.ThrowIfNull(property);
        _setters.Add(new ValueSetter<TProperty>(property, value));
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
        _setters.Add(new ExpressionSetter<TProperty>(property, valueExpression));
        return this;
    }

    /// <summary>Replays every recorded setter, in call order, to <paramref name="visitor"/>. For repository implementations.</summary>
    /// <param name="visitor">Receives each setter with its property type.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Accept(IBulkUpdateSetterVisitor<TAggregate> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var setter in _setters)
            setter.Accept(visitor);
    }

    private abstract class Setter(LambdaExpression target)
    {
        public LambdaExpression Target { get; } = target;

        public abstract void Accept(IBulkUpdateSetterVisitor<TAggregate> visitor);
    }

    private sealed class ValueSetter<TProperty>(Expression<Func<TAggregate, TProperty>> property, TProperty value) : Setter(property)
    {
        public override void Accept(IBulkUpdateSetterVisitor<TAggregate> visitor) => visitor.SetValue(property, value);
    }

    private sealed class ExpressionSetter<TProperty>(
        Expression<Func<TAggregate, TProperty>> property,
        Expression<Func<TAggregate, TProperty>> valueExpression) : Setter(property)
    {
        public override void Accept(IBulkUpdateSetterVisitor<TAggregate> visitor) => visitor.SetExpression(property, valueExpression);
    }
}

/// <summary>Receives the setters of a <see cref="BulkUpdateSetters{TAggregate}"/>, typed. For repository implementations.</summary>
/// <typeparam name="TAggregate">The aggregate root type being updated.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IBulkUpdateSetterVisitor<TAggregate>
    where TAggregate : class
{
    /// <summary>A setter to a constant value.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">The target.</param>
    /// <param name="value">The value.</param>
    void SetValue<TProperty>(Expression<Func<TAggregate, TProperty>> property, TProperty value);

    /// <summary>A setter to an expression over the row.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">The target.</param>
    /// <param name="valueExpression">The value expression.</param>
    void SetExpression<TProperty>(Expression<Func<TAggregate, TProperty>> property, Expression<Func<TAggregate, TProperty>> valueExpression);
}
