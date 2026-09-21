using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>Applies a recorded <see cref="BulkUpdateSetters{TAggregate}"/> to EF Core's <see cref="UpdateSettersBuilder{TSource}"/>.</summary>
internal static class EfBulkUpdateSetters
{
    /// <summary>Returns the EF Core setter callback: every recorded setter, then <paramref name="extra"/>.</summary>
    public static Action<UpdateSettersBuilder<TAggregate>> ToEfSetters<TAggregate>(
        BulkUpdateSetters<TAggregate> setters,
        Action<UpdateSettersBuilder<TAggregate>>? extra)
        where TAggregate : class =>
        builder =>
        {
            setters.Accept(new Visitor<TAggregate>(builder));
            extra?.Invoke(builder);
        };

    private sealed class Visitor<TAggregate>(UpdateSettersBuilder<TAggregate> builder) : IBulkUpdateSetterVisitor<TAggregate>
        where TAggregate : class
    {
        public void SetValue<TProperty>(Expression<Func<TAggregate, TProperty>> property, TProperty value) =>
            builder.SetProperty(property, value);

        public void SetExpression<TProperty>(
            Expression<Func<TAggregate, TProperty>> property,
            Expression<Func<TAggregate, TProperty>> valueExpression) =>
            builder.SetProperty(property, valueExpression);
    }
}
