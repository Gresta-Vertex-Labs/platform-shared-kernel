using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0201 <see cref="TenantedDbContextOnModelCreatingAnalyzer"/>.
/// </summary>
public class SK0201_TenantedDbContextOnModelCreatingAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-73 — Fire path: TenantedDbContext subclass missing tenant call
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-73: A <c>TenantedDbContext</c> subclass that overrides <c>OnModelCreating</c>
    /// without calling <c>base.OnModelCreating</c> or <c>ApplyTenantFilters</c> fires SK0201.
    /// </summary>
    [Fact]
    public async Task FirePath_SubclassOverridesOnModelCreating_WithoutTenantCall_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<TenantedDbContextOnModelCreatingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ModelBuilder { }

                public class TenantedDbContext
                {
                    public virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                    protected void ApplyTenantFilters(ModelBuilder mb) { }
                }

                public class OrderDbContext : TenantedDbContext
                {
                    public override void {|SK0201:OnModelCreating|}(ModelBuilder modelBuilder)
                    {
                        // Deliberately omits base.OnModelCreating and ApplyTenantFilters
                        modelBuilder.ToString();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: A body that only calls unrelated methods should still fire SK0201.
    /// </summary>
    [Fact]
    public async Task FirePath_SubclassCallsUnrelatedMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<TenantedDbContextOnModelCreatingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ModelBuilder { }

                public class TenantedDbContext
                {
                    public virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                }

                public class ProductDbContext : TenantedDbContext
                {
                    private void ConfigureEntities(ModelBuilder mb) { }

                    public override void {|SK0201:OnModelCreating|}(ModelBuilder modelBuilder)
                    {
                        ConfigureEntities(modelBuilder);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-74 — Pass path: subclass calls base.OnModelCreating
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-74: A <c>TenantedDbContext</c> subclass that calls <c>base.OnModelCreating(modelBuilder)</c>
    /// does not trigger SK0201.
    /// </summary>
    [Fact]
    public async Task PassPath_SubclassCallsBaseOnModelCreating_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<TenantedDbContextOnModelCreatingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ModelBuilder { }

                public class TenantedDbContext
                {
                    public virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                }

                public class OrderDbContext : TenantedDbContext
                {
                    public override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        base.OnModelCreating(modelBuilder);
                        // Additional configuration is fine
                        modelBuilder.ToString();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-75 — Pass path: subclass calls ApplyTenantFilters explicitly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-75: A <c>TenantedDbContext</c> subclass that calls <c>this.ApplyTenantFilters(modelBuilder)</c>
    /// (instead of <c>base.OnModelCreating</c>) does not trigger SK0201.
    /// </summary>
    [Fact]
    public async Task PassPath_SubclassCallsApplyTenantFilters_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<TenantedDbContextOnModelCreatingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ModelBuilder { }

                public class TenantedDbContext
                {
                    public virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                    protected void ApplyTenantFilters(ModelBuilder mb) { }
                }

                public class ShopDbContext : TenantedDbContext
                {
                    public override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        this.ApplyTenantFilters(modelBuilder);
                        modelBuilder.ToString();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: A non-TenantedDbContext subclass overriding OnModelCreating should
    /// not trigger SK0201 (the rule scopes to TenantedDbContext ancestry only).
    /// </summary>
    [Fact]
    public async Task PassPath_NonTenantedDbContextSubclass_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<TenantedDbContextOnModelCreatingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ModelBuilder { }

                public class DbContext
                {
                    public virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                }

                // Inherits from DbContext, not TenantedDbContext — SK0201 must not fire
                public class ReportDbContext : DbContext
                {
                    public override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        // Does not call base or ApplyTenantFilters — but that is fine here
                        modelBuilder.ToString();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: A simple invocation of <c>ApplyTenantFilters(...)</c> (without explicit
    /// receiver) satisfies the requirement and suppresses SK0201.
    /// </summary>
    [Fact]
    public async Task PassPath_ApplyTenantFiltersSimpleCall_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<TenantedDbContextOnModelCreatingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ModelBuilder { }

                public class TenantedDbContext
                {
                    public virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                    protected void ApplyTenantFilters(ModelBuilder mb) { }
                }

                public class InventoryDbContext : TenantedDbContext
                {
                    public override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        ApplyTenantFilters(modelBuilder);
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
