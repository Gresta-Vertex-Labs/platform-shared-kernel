using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;

namespace SharedKernel.Persistence.EfCore.Encryption.Interception;

/// <summary>
/// Rejects, before a query is compiled, every use of an encrypted property other than materializing its entity.
/// </summary>
/// <remarks>
/// <para>
/// An encrypted column holds ciphertext, and decryption happens only when a whole entity is materialized. So a
/// filter (<c>Where(x =&gt; x.Email == "…")</c>, <c>StartsWith</c>, <c>!=</c>), an ordering or grouping, a projection
/// (<c>Select(x =&gt; x.Email)</c>, or of a complex value containing an encrypted property), a join key or an
/// <c>ExecuteUpdate</c> setter would silently compare, sort, return or write ciphertext and plaintext. Each of these
/// throws here with the property's name and the alternative. Only <c>== null</c> and <c>!= null</c> comparisons
/// are allowed, because a null value is stored as null.
/// </para>
/// <para>
/// The check walks the LINQ expression tree, not SQL text, so it is exact: it knows the entity type of every
/// member access, never matches an unrelated column with the same name, and covers every operator. It runs once
/// per query shape, when EF Core compiles it. SQL written by hand (<c>FromSql</c>, <c>ExecuteSql</c>) is not checked.
/// </para>
/// </remarks>
internal sealed class EncryptedMemberQueryGuard : IQueryExpressionInterceptor
{
    private static readonly MethodInfo PropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (eventData.Context is not { } context)
            return queryExpression;

        var metadata = EncryptionModelMetadata.For(context.Model);
        if (metadata.HasEncryptedMembers)
            new Visitor(metadata, context.Model).Visit(queryExpression);

        return queryExpression;
    }

    private sealed class Visitor(EncryptionModelMetadata metadata, IModel model) : ExpressionVisitor
    {
        private readonly Stack<string> _operators = new();
        private bool _nextIsAccessedThrough;

        public override Expression? Visit(Expression? node)
        {
            if (node is MemberExpression member)
            {
                var accessedThrough = _nextIsAccessedThrough;
                _nextIsAccessedThrough = false;
                Check(member, accessedThrough);

                _nextIsAccessedThrough = true;
                Visit(member.Expression);
                _nextIsAccessedThrough = false;
                return node;
            }

            _nextIsAccessedThrough = false;
            return base.Visit(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            // 'x.Note == null' / 'x.Note != null' is meaningful on ciphertext: a null value is stored as null.
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                && (IsNullConstant(node.Left) || IsNullConstant(node.Right)))
            {
                var other = IsNullConstant(node.Left) ? node.Right : node.Left;
                if (Unwrap(other) is MemberExpression member && Describe(member.Member) is { IsContainer: false })
                {
                    _nextIsAccessedThrough = true;
                    Visit(member.Expression);
                    _nextIsAccessedThrough = false;
                    return node;
                }
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.IsGenericMethod
                && node.Method.GetGenericMethodDefinition() == PropertyMethod
                && node.Arguments[1] is ConstantExpression { Value: string name}
                && model.FindEntityType(Unwrap(node.Arguments[0]).Type) is { } entityType
                && metadata.DescribeGuardedName(entityType, name) is { } description)
            {
                throw Rejected(description);
            }

            var isQueryOperator = node.Method.DeclaringType is { } declaring
                && (declaring == typeof(Queryable) || declaring.Name.Contains("Queryable", StringComparison.Ordinal)
                    || declaring.Name.Contains("Extensions", StringComparison.Ordinal));
            if (isQueryOperator)
                _operators.Push(node.Method.Name);

            try
            {
                return base.VisitMethodCall(node);
            }
            finally
            {
                if (isQueryOperator)
                    _operators.Pop();
            }
        }

        private void Check(MemberExpression node, bool accessedThrough)
        {
            if (Describe(node.Member) is not { } guarded)
                return;

            // A complex value may be navigated through ('x.Address.City'); it may not be used as a whole.
            if (guarded.IsContainer && accessedThrough)
                return;

            throw Rejected(guarded.Description);
        }

        private (string Description, bool IsContainer)? Describe(MemberInfo member) => metadata.DescribeGuardedMember(member);

        private InvalidOperationException Rejected(string description)
        {
            var where = _operators.Count > 0 ? $" (in '{_operators.Peek()}')" : string.Empty;
            return new InvalidOperationException(
                $"This query uses an encrypted property{where}: {description}. Encrypted columns hold ciphertext, so the " +
                "database cannot filter, sort, group, project or update them by value. To find rows by an encrypted " +
                "value, add '.WithBlindIndex()' to the property and use 'WhereEncryptedEquals(x => x.Property, value)'. " +
                "To read the value, select the whole entity; to change it, load the entity, assign the property and save.");
        }

        private static bool IsNullConstant(Expression expression) => Unwrap(expression) is ConstantExpression { Value: null };

        private static Expression Unwrap(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote } unary)
                expression = unary.Operand;
            return expression;
        }
    }
}
