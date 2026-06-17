using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.GraphQL.Extensions;
using SharedKernel.Communication.GraphQL.Options;
using SharedKernel.Communication.GraphQL.Types;

namespace SharedKernel.Communication.GraphQL.Tests;

// ── Test domain types (public — HC reflection requires accessible types) ──────

public sealed class SchemaProduct
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SchemaProductFilterType : FilterBase<SchemaProduct>
{
    protected override void Configure(IFilterInputTypeDescriptor<SchemaProduct> descriptor)
    {
        descriptor.Field(p => p.Name);
        descriptor.Field(p => p.Price);
        descriptor.Field(p => p.CreatedAt);
    }
}

public sealed class SchemaProductSortType : SortBase<SchemaProduct>
{
    protected override void Configure(ISortInputTypeDescriptor<SchemaProduct> descriptor)
    {
        descriptor.Field(p => p.Name);
        descriptor.Field(p => p.Price);
    }
}

public sealed class SchemaQueryType
{
    [UseFiltering(typeof(SchemaProductFilterType))]
    [UseSorting(typeof(SchemaProductSortType))]
    public IQueryable<SchemaProduct> GetProducts() =>
        new List<SchemaProduct>
        {
            new() { Name = "Widget", Price = 9.99m, CreatedAt = DateTime.UtcNow }
        }.AsQueryable();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Schema-level tests using HotChocolate's in-process executor.
/// No real HTTP server or network calls.
/// </summary>
public sealed class SchemaConventionTests
{
    private static IRequestExecutorBuilder BuildSchema(Action<GraphQLOptions>? opts = null)
    {
        var services = new ServiceCollection();
        return services
            .AddSharedKernelGraphQL(opts)
            .AddQueryType<SchemaQueryType>();
    }

    private static async Task<IRequestExecutor> GetExecutorAsync(Action<GraphQLOptions>? opts = null)
    {
        var builder = BuildSchema(opts);
        return await builder.BuildRequestExecutorAsync();
    }

    // ── FilterConvention ──────────────────────────────────────────────────────

    [Fact]
    public async Task FilterConvention_RegistersProductFilterType_InSchema()
    {
        var executor = await GetExecutorAsync();
        var schema = executor.Schema;

        schema.Types.Should().Contain(t => t.Name.Contains("SchemaProductFilter"),
            "SchemaProductFilter input type should be registered by SharedKernelFilterConvention");
    }

    [Fact]
    public async Task FilterConvention_StringFilter_HasSnakeCaseOperationNames()
    {
        var executor = await GetExecutorAsync();
        var schema = executor.Schema;

        var stringFilter = schema.Types
            .OfType<InputObjectType>()
            .FirstOrDefault(t => t.Name.Contains("String") && t.Name.Contains("Filter"));

        stringFilter.Should().NotBeNull("String filter type should be registered in schema");

        var fieldNames = stringFilter!.Fields.Select(f => f.Name).ToList();

        fieldNames.Should().Contain("eq", "snake_case 'eq' operation should be present");
        fieldNames.Should().Contain("contains", "snake_case 'contains' operation should be present");
        fieldNames.Should().Contain("starts_with", "snake_case 'starts_with' operation should be present");
        fieldNames.Should().Contain("ends_with", "snake_case 'ends_with' operation should be present");
        fieldNames.Should().Contain("neq", "snake_case 'neq' operation should be present");
    }

    // ── SortConvention ────────────────────────────────────────────────────────

    [Fact]
    public async Task SortConvention_RegistersProductSortType_InSchema()
    {
        var executor = await GetExecutorAsync();
        var schema = executor.Schema;

        var sortType = schema.Types
            .OfType<InputObjectType>()
            .FirstOrDefault(t => t.Name.Contains("SchemaProduct") && t.Name.Contains("Sort"));

        sortType.Should().NotBeNull("SchemaProductSortInput should be present in schema");
    }

    // ── Error filter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ErrorFilter_IsRegistered_SchemaBuildsSuccessfully()
    {
        var act = async () => await GetExecutorAsync();
        await act.Should().NotThrowAsync();
    }

    // ── Introspection ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AllowIntrospection_True_IntrospectionQuerySucceeds()
    {
        var executor = await GetExecutorAsync(o => o.AllowIntrospection = true);
        var result = await executor.ExecuteAsync("{ __schema { queryType { name } } }");

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().BeNullOrEmpty(
            "introspection should succeed when AllowIntrospection = true");
    }

    [Fact]
    public async Task AllowIntrospection_False_IntrospectionQueryReturnsError()
    {
        var executor = await GetExecutorAsync(o => o.AllowIntrospection = false);
        var result = await executor.ExecuteAsync("{ __schema { queryType { name } } }");

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().NotBeNullOrEmpty(
            "introspection should be rejected when AllowIntrospection = false");
    }

    // ── Paging ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schema_BuildsWithPagingEnabled_DefaultOptions()
    {
        var act = async () => await GetExecutorAsync(o => o.EnablePaging = true);
        await act.Should().NotThrowAsync();
    }
}
