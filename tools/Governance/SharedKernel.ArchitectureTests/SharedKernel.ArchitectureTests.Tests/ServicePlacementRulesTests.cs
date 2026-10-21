using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Validation;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Specifications;
using SharedKernel.Presentation.WebApi;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The nine service placement rules: each has a fire path (a misplaced type in <c>Fixture.Bad</c>) and a pass path
/// (the same kind of type in the right folder in <c>Fixture.Good</c>). Both fixtures are compiled against the real
/// kernel, MassTransit, EF Core and ASP.NET Core assemblies, so a firing rule also proves it recognises the real type.
/// </summary>
public sealed class ServicePlacementRulesTests : ArchitectureRuleBase
{
    private static readonly Assembly[] Good = [ServicePlacementFixtures.Good];
    private static readonly Assembly[] Bad = [ServicePlacementFixtures.Bad];
    private static readonly ServicePlacementOptions GoodOptions = new(ServicePlacementFixtures.GoodService);
    private static readonly ServicePlacementOptions BadOptions = new(ServicePlacementFixtures.BadService);

    private static string[] Failing(ConditionList rule) => rule.GetResult().FailingTypeNames?.ToArray() ?? [];

    private static string[] Named(params string[] suffixes) => suffixes.Select(s => $"{ServicePlacementFixtures.BadService}.{s}").ToArray();

    // ----- 1 domain types --------------------------------------------------------------------------------------------

    [Fact]
    public void DomainTypes_Pass_WhenInTheirFeature() =>
        AssertRule(ServicePlacementRules.DomainTypesLiveInTheirFeature(Good, GoodOptions));

    [Fact]
    public void DomainTypes_Fire_ForEachMisplacedKind() =>
        Failing(ServicePlacementRules.DomainTypesLiveInTheirFeature(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(
                Named(
                    "Domain.LooseAggregate",
                    "Domain.Common.SharedKindAggregate",
                    "Domain.Orders.MisplacedRule",
                    "Domain.Orders.MisplacedSpecification",
                    "Domain.Orders.MisplacedEvent",
                    "Domain.Orders.RulesEngine.LookalikeRule"
                ),
                "an aggregate outside a feature, a rule outside Rules (and in RulesEngine, which is not Rules), a specification and an event outside their folders"
            );

    // ----- 2 use cases -----------------------------------------------------------------------------------------------

    [Fact]
    public void UseCases_Pass_WhenEachLivesInItsOwnNamespace() =>
        AssertRule(ServicePlacementRules.UseCasesLiveInTheirOwnNamespace(Good, GoodOptions));

    [Fact]
    public void UseCases_Fire_ForMisplacedRequestsHandlersAndValidators() =>
        Failing(ServicePlacementRules.UseCasesLiveInTheirOwnNamespace(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(
                Named(
                    "Application.Orders.Commands.WrongPlaceCommand",
                    "Application.Orders.Commands.Cancel.CancelOrderCommand",
                    "Application.Orders.Commands.Cancel.RefundOrderCommand",
                    "Application.Orders.Queries.Find.FindOrder",
                    "Application.Orders.Handlers.CancelOrderHandler",
                    "Application.Orders.Handlers.ShipOrderValidator"
                )
            );

    // ----- 3 domain-event handlers -----------------------------------------------------------------------------------

    [Fact]
    public void DomainEventHandlers_Pass_InEventHandlers() =>
        AssertRule(ServicePlacementRules.DomainEventHandlersLiveInEventHandlers(Good, GoodOptions));

    [Fact]
    public void DomainEventHandlers_Fire_OutsideEventHandlers() =>
        Failing(ServicePlacementRules.DomainEventHandlersLiveInEventHandlers(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Application.Orders.Handlers.StrayEventHandler"));

    // ----- 4-6 configurations, consumers, endpoint modules -----------------------------------------------------------

    [Fact]
    public void EntityConfigurations_Pass_InPersistenceConfigurations() =>
        AssertRule(ServicePlacementRules.EntityConfigurationsLiveInConfigurations(Good, GoodOptions));

    [Fact]
    public void EntityConfigurations_Fire_Elsewhere() =>
        Failing(ServicePlacementRules.EntityConfigurationsLiveInConfigurations(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Infrastructure.Data.StrayConfiguration"));

    [Fact]
    public void Consumers_Pass_InMessagingConsumers() =>
        AssertRule(ServicePlacementRules.ConsumersLiveInConsumers(Good, GoodOptions));

    [Fact]
    public void Consumers_Fire_Elsewhere() =>
        Failing(ServicePlacementRules.ConsumersLiveInConsumers(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Infrastructure.Consumers.StrayConsumer"));

    [Fact]
    public void EndpointModules_Pass_InEndpoints() =>
        AssertRule(ServicePlacementRules.EndpointModulesLiveInEndpoints(Good, GoodOptions));

    [Fact]
    public void EndpointModules_Fire_Elsewhere() =>
        Failing(ServicePlacementRules.EndpointModulesLiveInEndpoints(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Api.Modules.StrayEndpoints"));

    [Fact]
    public void EndpointModules_FollowTheHostSuffix()
    {
        var worker = new ServicePlacementOptions(ServicePlacementFixtures.GoodService) { HostSuffix = "Worker" };

        Failing(ServicePlacementRules.EndpointModulesLiveInEndpoints(Good, worker))
            .Should()
            .BeEquivalentTo([$"{ServicePlacementFixtures.GoodService}.Api.Endpoints.OrderEndpoints"]);
    }

    // ----- 7 middleware and filters ----------------------------------------------------------------------------------

    [Fact]
    public void MiddlewareAndFilters_Pass_InTheirFolders() =>
        AssertRule(ServicePlacementRules.MiddlewareAndFiltersLiveInTheirFolders(Good, GoodOptions));

    [Fact]
    public void MiddlewareAndFilters_Fire_ForFactoryConventionAndFilter() =>
        Failing(ServicePlacementRules.MiddlewareAndFiltersLiveInTheirFolders(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Api.Modules.StrayMiddleware", "Api.Modules.StrayConventionMiddleware", "Api.Modules.StrayFilter"));

    // ----- 8 generic folder names ------------------------------------------------------------------------------------

    [Fact]
    public void GenericFolderNames_Pass_WhenEveryFolderNamesWhatItHolds() =>
        AssertRule(ServicePlacementRules.NoGenericFolderNames(Good, GoodOptions));

    [Fact]
    public void GenericFolderNames_Fire_ForTheDefaultBannedSegments_InterfacesIncluded() =>
        Failing(ServicePlacementRules.NoGenericFolderNames(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Domain.Helpers.Thing", "Application.Utils.IUtil"));

    [Fact]
    public void GenericFolderNames_MatchWholeSegments_NotPrefixes()
    {
        var options = new ServicePlacementOptions(ServicePlacementFixtures.BadService) { BannedSegments = ["Rules"] };

        // 'Rules' bans the Rules folder; the RulesEngine folder is a different segment and stays legal.
        Failing(ServicePlacementRules.NoGenericFolderNames(Bad, options))
            .Should()
            .BeEquivalentTo(Named("Domain.Orders.Rules.WellPlacedRule"));
    }

    [Fact]
    public void DefaultBannedSegments_AreTheServiceTemplateList() =>
        ServicePlacementOptions.DefaultBannedSegments
            .Should()
            .Equal("Managers", "Helpers", "Utils", "Extensions", "Exceptions", "Shared", "Misc");

    // ----- 9 exceptions ----------------------------------------------------------------------------------------------

    [Fact]
    public void ExceptionTypes_Pass_WhenNoTypeDerivesFromException() =>
        AssertRule(ServicePlacementRules.NoExceptionTypes(Good, GoodOptions));

    [Fact]
    public void ExceptionTypes_Fire_ForEveryExceptionByDefault() =>
        Failing(ServicePlacementRules.NoExceptionTypes(Bad, BadOptions))
            .Should()
            .BeEquivalentTo(Named("Errors.PlainFailure", "Errors.ClassifiedFailure"));

    [Fact]
    public void ExceptionTypes_PermitTheCallersBaseTypes()
    {
        var options = new ServicePlacementOptions(ServicePlacementFixtures.BadService)
        {
            PermittedExceptionBaseTypes = [typeof(SharedKernelException).FullName!],
        };

        Failing(ServicePlacementRules.NoExceptionTypes(Bad, options)).Should().BeEquivalentTo(Named("Errors.PlainFailure"));
    }

    // ----- construction ----------------------------------------------------------------------------------------------

    [Fact]
    public void Rules_RejectNoAssemblies() =>
        FluentActions
            .Invoking(() => ServicePlacementRules.NoExceptionTypes([], GoodOptions))
            .Should()
            .Throw<ArgumentException>();

    [Fact]
    public void Options_RejectABlankServicePrefix() =>
        FluentActions.Invoking(() => new ServicePlacementOptions(" ")).Should().Throw<ArgumentException>();

    // ----- the metadata names match the real types -------------------------------------------------------------------

    [Fact]
    public void KernelTypeNames_EqualTheMetadataNamesOfTheRealTypes()
    {
        KernelTypeNames.AggregateRoot.Should().Be(typeof(IAggregateRoot<>).FullName);
        KernelTypeNames.BusinessRule.Should().Be(typeof(IBusinessRule).FullName);
        KernelTypeNames.Specification.Should().Be(typeof(ISpecification<>).FullName);
        KernelTypeNames.DomainEvent.Should().Be(typeof(IDomainEvent).FullName);
        KernelTypeNames.Command.Should().Be(typeof(ICommandBase).FullName);
        KernelTypeNames.Query.Should().Be(typeof(IQueryBase).FullName);
        KernelTypeNames.RequestHandler.Should().Be(typeof(IRequestHandler<,>).FullName);
        KernelTypeNames.RequestValidator.Should().Be(typeof(IRequestValidator<>).FullName);
        KernelTypeNames.DomainEventHandler.Should().Be(typeof(IDomainEventHandler<>).FullName);
        KernelTypeNames.EndpointModule.Should().Be(typeof(IEndpointModule).FullName);
        KernelTypeNames.EntityTypeConfiguration.Should().Be(typeof(IEntityTypeConfiguration<>).FullName);
        KernelTypeNames.Consumer.Should().Be(typeof(MassTransit.IConsumer<>).FullName);
        KernelTypeNames.Middleware.Should().Be(typeof(IMiddleware).FullName);
        KernelTypeNames.EndpointFilter.Should().Be(typeof(IEndpointFilter).FullName);
        KernelTypeNames.HttpContext.Should().Be(typeof(HttpContext).FullName);
        KernelTypeNames.Exception.Should().Be(typeof(Exception).FullName);
    }
}
