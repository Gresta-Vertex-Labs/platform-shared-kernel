# SharedKernel.ArchitectureTests

NetArchTest-based base classes and pre-built layering-rule predicates for SharedKernel architecture tests.

## Important

Reference this package with `PrivateAssets="all"` — it must never become a transitive production dependency:

```xml
<PackageReference Include="SharedKernel.ArchitectureTests" Version="1.0.0" PrivateAssets="all" />
```

## Usage

### Subclassing ArchitectureRuleBase

```csharp
public class MyLayeringTests : ArchitectureRuleBase
{
    [Fact]
    public void Domain_NeverReferencesPersistence()
    {
        var domainAssembly = typeof(MyEntity).Assembly;
        var result = SharedKernelLayeringRules.DomainNeverReferencesPersistence(domainAssembly);
        AssertRule(result);
    }
}
```

### Available Pre-built Rules

| Method | Enforces |
|--------|---------|
| `CoreReferencesNothing` | SharedKernel.Core has no outbound package references |
| `CachingReferencesOnlyCore` | Caching layer only references Core |
| `DomainReferencesOnlyCore` | Domain layer only references Core |
| `ContractsReferencesOnlyCoreAndDomain` | Contracts only references Core and Domain |
| `DomainNeverReferencesPersistence` | Hard rule: Domain must not reference any persistence namespace |
| `DomainNeverReferencesMessaging` | Hard rule: Domain must not reference any messaging namespace |
| `ApplicationNeverReferencesConcreteInfrastructure` | Hard rule: Application must not reference concrete infrastructure |
| `TestingNeverReferencedByProduction` | Hard rule: Testing packages must not be referenced by production code |

See the full usage guide in `00.Governance/README.md`.
