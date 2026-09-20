using System.Linq.Expressions;
using FluentAssertions;
using SharedKernel.Persistence.PostgreSQL.Vector;
using PgVector = Pgvector.Vector;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Vector;

/// <summary>
/// <see cref="VectorDistanceMetric"/> and
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
    public void VectorDistanceMetric_HasExactlyCosineL2L1AndInnerProductMembers()
    {
        // Widened from the original two (Cosine/L2) to the four metrics pgvector's own
        // index operator classes support.
        var values = Enum.GetValues<VectorDistanceMetric>();
        values.Should().BeEquivalentTo([
            VectorDistanceMetric.Cosine,
            VectorDistanceMetric.L2,
            VectorDistanceMetric.L1,
            VectorDistanceMetric.InnerProduct,
        ]);
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
    public void ByDistance_L1_BuildsExpressionCallingL1Distance()
    {
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        var expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, VectorDistanceMetric.L1);

        var convert = expr.Body.Should().BeOfType<UnaryExpression>().Subject;
        var call = convert.Operand.Should().BeAssignableTo<MethodCallExpression>().Subject;
        call.Method.Name.Should().Be("L1Distance");
    }

    [Fact]
    public void ByDistance_InnerProduct_BuildsExpressionCallingMaxInnerProduct()
    {
        var queryVector = new PgVector(new float[] { 1f, 2f, 3f });

        var expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
            e => e.Embedding, queryVector, VectorDistanceMetric.InnerProduct);

        var convert = expr.Body.Should().BeOfType<UnaryExpression>().Subject;
        var call = convert.Operand.Should().BeAssignableTo<MethodCallExpression>().Subject;
        call.Method.Name.Should().Be("MaxInnerProduct");
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

    // -------------------------------------------------------------------------
    // T-121 — zero-reflection-at-runtime behavioral proof. True static-
    // analysis enforcement of the reflection-elimination rule remains 00.Governance's
    // jurisdiction (SK0xxx MakeGenericMethod/Invoke rule) — this is the practical, behavioral
    // proxy available at this layer: repeated calls across multiple TAggregate/metric
    // combinations behave correctly and with stable performance characteristics consistent with
    // the compile-time-resolved MethodInfo (a statically-typed delegate-cast, this package's own
    // "((Func<Vector,Vector,double>)VectorDbFunctionsExtensions.CosineDistance).Method" technique)
    // rather than a per-call Type.GetMethod/MakeGenericMethod runtime lookup.
    // -------------------------------------------------------------------------

    private sealed class AnotherVectorTestEntity
    {
        public int Id { get; set; }
        public PgVector Embedding { get; set; } = new(new float[2]);
    }

    [Fact]
    public void ByDistance_RepeatedCallsAcrossMultipleAggregateTypesAndMetrics_BehaveCorrectly()
    {
        var queryVector3D = new PgVector(new float[] { 1f, 2f, 3f });
        var queryVector2D = new PgVector(new float[] { 0.1f, 0.2f });

        for (var i = 0; i < 500; i++)
        {
            var cosineExpr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
                e => e.Embedding, queryVector3D, VectorDistanceMetric.Cosine);
            var l2Expr = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
                e => e.Embedding, queryVector3D, VectorDistanceMetric.L2);
            var otherEntityExpr = VectorOrderingExpressions.ByDistance<AnotherVectorTestEntity>(
                e => e.Embedding, queryVector2D, i % 2 == 0 ? VectorDistanceMetric.Cosine : VectorDistanceMetric.L2);

            // Cheap, allocation-light correctness spot-checks every iteration (no FluentAssertions
            // inside the hot loop, to keep this a fair timing proxy in the next test).
            if (cosineExpr.Parameters.Count != 1 || l2Expr.Parameters.Count != 1 || otherEntityExpr.Parameters.Count != 1)
                throw new InvalidOperationException("ByDistance's expression shape regressed mid-loop.");

            var cosineCall = (MethodCallExpression)((UnaryExpression)cosineExpr.Body).Operand;
            var l2Call = (MethodCallExpression)((UnaryExpression)l2Expr.Body).Operand;
            if (cosineCall.Method.Name != "CosineDistance" || l2Call.Method.Name != "L2Distance")
                throw new InvalidOperationException("ByDistance's method-resolution regressed mid-loop.");
        }
    }

    [Fact]
    public void ByDistance_RepeatedCalls_StablePerformance_NoPerCallReflectionGrowthAcrossDifferentAggregateTypes()
    {
        const int iterations = 2000;
        var queryVector3D = new PgVector(new float[] { 1f, 2f, 3f });
        var queryVector2D = new PgVector(new float[] { 0.1f, 0.2f });

        // Warm up the JIT before measuring, so both timed batches reflect steady-state cost only.
        for (var i = 0; i < 200; i++)
        {
            _ = VectorOrderingExpressions.ByDistance<VectorTestEntity>(e => e.Embedding, queryVector3D, VectorDistanceMetric.Cosine);
            _ = VectorOrderingExpressions.ByDistance<AnotherVectorTestEntity>(e => e.Embedding, queryVector2D, VectorDistanceMetric.L2);
        }

        var firstBatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = VectorOrderingExpressions.ByDistance<VectorTestEntity>(
                e => e.Embedding, queryVector3D, i % 2 == 0 ? VectorDistanceMetric.Cosine : VectorDistanceMetric.L2);
        }
        firstBatch.Stop();

        // A DIFFERENT closed-generic TAggregate — the case a per-call Type.GetMethod/
        // MakeGenericMethod runtime lookup (rather than a compile-time-resolved MethodInfo) would
        // most plausibly penalize via a cache miss.
        var secondBatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = VectorOrderingExpressions.ByDistance<AnotherVectorTestEntity>(
                e => e.Embedding, queryVector2D, i % 2 == 0 ? VectorDistanceMetric.Cosine : VectorDistanceMetric.L2);
        }
        secondBatch.Stop();

        // Generous tolerance (a practical behavioral proxy, not a strict CI-timing assertion prone
        // to flakiness): the second batch must not be dramatically slower than the first, and both
        // must complete well within a wide ceiling — consistent with zero per-call reflection cost.
        firstBatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        secondBatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        secondBatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(Math.Max(500, firstBatch.Elapsed.TotalMilliseconds * 5)));
    }
}
