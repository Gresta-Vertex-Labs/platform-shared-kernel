namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Zero-member marker interface for domain value objects.
/// Types implementing this interface use structural equality — two instances are equal
/// if and only if all their equality components are equal.
/// </summary>
public interface IValueObject
{
}
