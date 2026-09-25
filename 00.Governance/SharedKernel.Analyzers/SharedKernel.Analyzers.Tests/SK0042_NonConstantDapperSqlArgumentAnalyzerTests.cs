using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0042 <see cref="NonConstantDapperSqlArgumentAnalyzer"/>.</summary>
/// <remarks>
/// Fire path — an interpolated string, a concatenation, or a non-const local passed as the `sql`
/// argument of a matched method: <c>IDbSession.Command</c>, a method on a type implementing
/// <c>IDbSession</c>, or Dapper's own <c>SqlMapper</c> extensions.
/// Pass path — a literal, a `const` field, or a concatenation of only constants; an unrelated method
/// with a coincidentally-named `sql` parameter; every non-`sql` argument.
/// The stubs mirror the shipped shape of <c>SharedKernel.Persistence.Dapper.Sessions</c>
/// (<c>IDbSessionFactory</c> opens an <c>IDbSession</c>, whose <c>Command</c> builds a Dapper
/// <c>CommandDefinition</c>); <c>RealKernelTypeNameTests</c> runs the same rule against the compiled
/// package.
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

            public readonly struct CommandDefinition
            {
            }

            public static class SqlMapper
            {
                public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection cnn, string sql, object? param = null) =>
                    throw new NotImplementedException();

                public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection cnn, CommandDefinition command) =>
                    throw new NotImplementedException();
            }
        }

        namespace SharedKernel.Persistence.Dapper.Sessions
        {
            using System.Data.Common;
            using System.Threading;
            using System.Threading.Tasks;
            using global::Dapper;

            public interface IDbSession
            {
                DbConnection Connection { get; }

                CommandDefinition Command(string sql, object? parameters = null, CancellationToken cancellationToken = default);
            }

            public interface IDbSessionFactory
            {
                Task<IDbSession> OpenAsync(CancellationToken cancellationToken = default);

                Task<IDbSession> OpenReadOnlyAsync(CancellationToken cancellationToken = default);
            }
        }

        """;

    /// <summary>
    /// A consumer-written query object over <c>IDbSessionFactory</c>, as the persistence README shows
    /// it; <paramref name="members"/> supplies the query methods.
    /// </summary>
    private static string OrderQueries(string members) => DapperStubs + $$"""
        namespace Fixture
        {
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Dapper;
            using SharedKernel.Persistence.Dapper.Sessions;

            public sealed class OrderQueries
            {
                private readonly IDbSessionFactory _sessions;

                public OrderQueries(IDbSessionFactory sessions) => _sessions = sessions;

        {{members}}
            }
        }
        """;

    // ---------------------------------------------------------------------------
    // Fire path
    // ---------------------------------------------------------------------------

    /// <summary>An interpolated string passed to <c>IDbSession.Command</c> is always flagged.</summary>
    [Fact]
    public async Task FirePath_DbSessionCommand_InterpolatedSql_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(string status)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command({|SK0042:$"SELECT id FROM orders WHERE status = '{status}'"|}));
                        }

                        public async Task<IEnumerable<int>> FindSafelyAsync(string status)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command("SELECT id FROM orders WHERE status = @status", new { status }));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>String concatenation with a non-constant operand is flagged.</summary>
    [Fact]
    public async Task FirePath_StringConcatenation_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(string status)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command({|SK0042:"SELECT id FROM orders WHERE status = '" + status + "'"|}));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>A non-const local variable built by concatenation is flagged at the call site.</summary>
    [Fact]
    public async Task FirePath_NonConstLocalVariable_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(string status)
                        {
                            string sql = "SELECT id FROM orders WHERE status = '" + status + "'";
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(session.Command({|SK0042:sql|}));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>A named <c>sql:</c> argument out of positional order is still found and flagged.</summary>
    [Fact]
    public async Task FirePath_NamedSqlArgument_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(string table)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command(parameters: null, sql: {|SK0042:$"SELECT id FROM {table}"|}));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A method with a <c>sql</c> parameter on a consumer type implementing <c>IDbSession</c> (a
    /// decorator) is a matched call site too. The decorator's own forwarding call passes its
    /// <c>sql</c> parameter, which is not a constant either, so it is flagged as well: a decorator
    /// suppresses SK0042 on that one line.
    /// </summary>
    [Fact]
    public async Task FirePath_MethodOnTypeImplementingDbSession_Reports()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = DapperStubs + """
                namespace Fixture
                {
                    using System.Data.Common;
                    using System.Threading;
                    using Dapper;
                    using SharedKernel.Persistence.Dapper.Sessions;

                    public sealed class TimedDbSession : IDbSession
                    {
                        private readonly IDbSession _inner;

                        public TimedDbSession(IDbSession inner) => _inner = inner;

                        public DbConnection Connection => _inner.Connection;

                        public CommandDefinition Command(string sql, object? parameters = null, CancellationToken cancellationToken = default) =>
                            _inner.Command({|SK0042:sql|}, parameters, cancellationToken);
                    }

                    public static class Usage
                    {
                        public static CommandDefinition Build(TimedDbSession session, string table) =>
                            session.Command({|SK0042:$"SELECT id FROM {table}"|});
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
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(string status)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command("SELECT id FROM orders WHERE status = @status", new { status }));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>A const field used as the sql argument is compliant.</summary>
    [Fact]
    public async Task PassPath_ConstFieldSql_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = OrderQueries("""
                        private const string Sql = "SELECT id FROM orders WHERE status = @status";

                        public async Task<IEnumerable<int>> FindAsync(string status)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(session.Command(Sql, new { status }));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>Concatenation of two string literals is still a compile-time constant.</summary>
    [Fact]
    public async Task PassPath_ConcatenationOfConstants_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<NonConstantDapperSqlArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(string status)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command("SELECT id " + "FROM orders WHERE status = @status", new { status }));
                        }
                """),
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A non-constant 'sql'-named argument on an unrelated method (not <c>IDbSession</c>, a type
    /// implementing it, or <c>SqlMapper</c>) is not flagged.
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
            TestCode = OrderQueries("""
                        public async Task<IEnumerable<int>> FindAsync(object parameters)
                        {
                            var session = await _sessions.OpenReadOnlyAsync();
                            return await session.Connection.QueryAsync<int>(
                                session.Command("SELECT id FROM orders WHERE status = @status", parameters));
                        }
                """),
        };
        await test.RunAsync();
    }
}
