using System.Linq.Expressions;
using FluentAssertions;
using SharedKernel.Persistence.PostgreSQL.Vector;
using PgVector = Pgvector.Vector;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Vector;

/// <summary>
/// WO-053/P-339 (C-144/C-145): <see cref="VectorDistanceMetric"/> and
/// <see cref="VectorOrderingExpressions.ByDistance{TAggregate}"/>. These are pure expression-tree
/// construction tests — no PostgreSQL/Testcontainer is required, since
/// <c>Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions</c>' distance methods are EF-Core
/// query-translation placeholders never meaningfully invoked client-side; only the SHAPE of the
/// built <see cref="Expression"/> tree is asserted.
/// </summary>
public sealed class VectorOrderingExpressionsTests
{
    private sealed class VectorTestEntity
    {
        public int Id { get; set; }
        public PgVector Embedding { get; set; } = new(new float[3]);
    }

    [Fact]
    public void VectorDistanceMetric_HasExactlyCosineAndL2Members()
    {
        var values = Enum.GetValues<VectorDistanceMetric>();
        values.Should().BeEquivalentTo([VectorDistanceMetric.Cosine, VectorDistanceMetric.L2]);
    }

    [Fact]
    public void ByDistance_ReturnsLambdaExpression_WithSingleParameterMatchingSelector()
    {
        // Arrange
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        // Act
        var expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, VectorDistanceMetric.Cosine);

        // Assert
        expr.Should().NotBeNull();
        expr.Parameters.Should().ContainSingle();
        expr.Parameters[0].Type.Should().Be(typeof(VectorTestEntity));
        expr.ReturnType.Should().Be(typeof(object));
    }

    [Fact]
    public void ByDistance_Cosine_BuildsExpressionCallingCosineDistance()
    {
        // Arrange
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        // Act
        var expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, VectorDistanceMetric.Cosine);

        // Assert — body is Convert(Call(CosineDistance, memberAccess, constant), object)
        var convert = expr.Body.Should().BeOfType<UnaryExpression>().Subject;
        convert.NodeType.Should().Be(ExpressionType.Convert);
        convert.Type.Should().Be(typeof(object));

        var call = convert.Operand.Should().BeAssignableTo<MethodCallExpression>().Subject;
        call.Method.Name.Should().Be("CosineDistance");
        call.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void ByDistance_L2_BuildsExpressionCallingL2Distance()
    {
        // Arrange
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        // Act
        var expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, VectorDistanceMetric.L2);

        // Assert
        var convert = expr.Body.Should().BeOfType<UnaryExpression>().Subject;
        var call = convert.Operand.Should().BeAssignableTo<MethodCallExpression>().Subject;
        call.Method.Name.Should().Be("L2Distance");
    }

    [Fact]
    public void ByDistance_FirstCallArgument_IsTheVectorSelectorMemberAccess()
    {
        // Arrange
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        // Act
        var expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, VectorDistanceMetric.Cosine);

        // Assert
        var convert = (UnaryExpression)expr.Body;
        var call = (MethodCallExpression)convert.Operand;

        var firstArg = call.Arguments[0].Should().BeAssignableTo<MemberExpression>().Subject;
        firstArg.Member.Name.Should().Be(nameof(VectorTestEntity.Embedding));

        // Second argument is the boxed query vector constant.
        var secondArg = call.Arguments[1].Should().BeAssignableTo<Expression>().Subject;
        secondArg.Type.Should().Be(typeof(PgVector));
    }

    [Fact]
    public void ByDistance_NullVectorSelector_ThrowsArgumentNullException()
    {
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        Action act = () => VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            null!, queryVector, VectorDistanceMetric.Cosine);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ByDistance_NullQueryVector_ThrowsArgumentNullException()
    {
        Action act = () => VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, null!, VectorDistanceMetric.Cosine);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ByDistance_InvalidMetric_ThrowsArgumentOutOfRangeException()
    {
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        Action act = () => VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, (VectorDistanceMetric)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
