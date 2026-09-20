using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0042 <see cref="NonConstantDapperSqlArgumentAnalyzer"/>.</summary>
/// <remarks>
/// Fire path — an interpolated string, a concatenation, or a non-const local passed as the `sql`
/// argument of a matched method.
/// Pass path — a literal, a `const` field, or a concatenation of only constants; an unrelated method
/// with a coincidentally-named `sql` parameter; every non-`sql` argument.
/// </remarks>
public class SK0042_NonConstantDapperSqlArgumentAnalyzerTests
{
    private const string DapperStubs = """
        namespace Dapper
        {
            using System;
            using System.Collections.Generic;
            using System.Data;
            using System.Threading.Tasks;

            public static class SqlMapper
            {
                public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection cnn, string sql, object? param = null) =>
                    throw new NotImplementedException();
            }
        }

        namespace SharedKernel.Persistence.Abstractions.Connections
        {
            using System.Data.Common;
            using System.Threading;
            using System.Threading.Tasks;

            public interface IDbConnectionFactory
            {
                Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default);
            }
        }

        namespace SharedKernel.Persistence.Dapper.ReadModels
        {
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using SharedKernel.Persistence.Abstractions.Connections;

            public abstract class DapperReadService
            {
                protected DapperReadService(IDbConnectionFactory factory) { }

                protected Task<IReadOnlyList<TResult>> QueryAsync<TResult>(
                    string sql, object? parameters, int? commandTimeout = null, CancellationToken ct = default) =>
                    throw new System.NotImplementedException();
            }

            public abstract class DapperCommandService
            {
                protected DapperCommandService(IDbConnectionFactory factory) { }

                protected Task<int> ExecuteAsync(
                    string sql, object? parameters, int? commandTimeout = null, CancellationToken ct = default) =>
                    throw new System.NotImplementedException();
            }
        }

        """;

    // ---------------------------------------------------------------------------
    // Fire path
    // ---------------------------------------------------------------------------

    /// <summary>An interpolated string sql argument is always flagged.</summary>
    [Fact]
    public async Task FirePath_InterpolatedStringSql_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(string status) =>
                            QueryAsync<int>({|SK0042:$"SELECT id FROM orders WHERE status = '{status}'"|}, null);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>String concatenation with a non-constant operand is flagged.</summary>
    [Fact]
    public async Task FirePath_StringConcatenation_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(string status) =>
                            QueryAsync<int>({|SK0042:"SELECT id FROM orders WHERE status = '" + status + "'"|}, null);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A non-const local variable built by concatenation is flagged at the call site.</summary>
    [Fact]
    public async Task FirePath_NonConstLocalVariable_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(string status)
                        {
                            string sql = "SELECT id FROM orders WHERE status = '" + status + "'";
                            return QueryAsync<int>({|SK0042:sql|}, null);
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A raw Dapper SqlMapper.QueryAsync call with a non-constant sql argument is flagged.</summary>
    [Fact]
    public async Task FirePath_RawSqlMapperCall_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using System.Data;
                    using Dapper;

                    public static class RawQueries
                    {
                        public static System.Threading.Tasks.Task Run(IDbConnection connection, string table) =>
                            connection.QueryAsync<int>({|SK0042:$"SELECT id FROM {table}"|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Pass path
    // ---------------------------------------------------------------------------

    /// <summary>A literal sql argument with a genuine parameter placeholder is compliant.</summary>
    [Fact]
    public async Task PassPath_LiteralSqlWithParameter_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(string status) =>
                            QueryAsync<int>("SELECT id FROM orders WHERE status = @status", new { status });
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A const field used as the sql argument is compliant.</summary>
    [Fact]
    public async Task PassPath_ConstFieldSql_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        private const string Sql = "SELECT id FROM orders WHERE status = @status";

                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(string status) =>
                            QueryAsync<int>(Sql, new { status });
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>Concatenation of two string literals is still a compile-time constant.</summary>
    [Fact]
    public async Task PassPath_ConcatenationOfConstants_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(string status) =>
                            QueryAsync<int>("SELECT id " + "FROM orders WHERE status = @status", new { status });
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A non-constant 'sql'-named argument on an unrelated method (not DapperReadService/
    /// DapperCommandService/SqlMapper) is not flagged.
    /// </summary>
    [Fact]
    public async Task PassPath_UnrelatedMethodWithSqlParameter_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    public static class Logger
                    {
                        public static void Log(string sql) { }

                        public static void Run(string table)
                        {
                            Log($"unrelated {table}");
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A non-constant 'parameters' argument (not 'sql') is not flagged.</summary>
    [Fact]
    public async Task PassPath_NonConstantParametersArgument_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using SharedKernel.Persistence.Abstractions.Connections;
                    using SharedKernel.Persistence.Dapper.ReadModels;

                    public sealed class OrderReadService : DapperReadService
                    {
                        public OrderReadService(IDbConnectionFactory factory) : base(factory) { }

                        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> FindAsync(object parameters) =>
                            QueryAsync<int>("SELECT id FROM orders WHERE status = @status", parameters);
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
