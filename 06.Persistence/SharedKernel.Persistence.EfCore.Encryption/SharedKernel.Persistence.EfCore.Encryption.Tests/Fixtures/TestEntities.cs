using SharedKernel.Domain.Abstractions;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

public sealed class BankAccount
{
    public string Iban { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;
}

public sealed class Address
{
    public string City { get; set; } = string.Empty;

    // Nested complex type: the encrypted property sits two levels below the entity (finding A9).
    public BankAccount Bank { get; set; } = new();
}

public sealed class Customer : IHasTenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public TenantId TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Note { get; set; }

    public Address Billing { get; set; } = new();
}

/// <summary>Non-tenanted, integer key assigned by the client.</summary>
[SharedKernel.Domain.Abstractions.TenantShared]
public sealed class Document
{
    public int Id { get; set; }

    public string Body { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}

// TPH: two sibling types map a same-named property to one shared column, with one purpose.
public abstract class Animal : IHasTenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public TenantId TenantId { get; set; }
}

public sealed class Dog : Animal
{
    public string ChipCode { get; set; } = string.Empty;
}

public sealed class Cat : Animal
{
    public string ChipCode { get; set; } = string.Empty;
}

// TPT: the tenant column lives only in the base table.
public class Vehicle : IHasTenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public TenantId TenantId { get; set; }

    public string Vin { get; set; } = string.Empty;
}

public sealed class Truck : Vehicle
{
    public string Permit { get; set; } = string.Empty;
}
