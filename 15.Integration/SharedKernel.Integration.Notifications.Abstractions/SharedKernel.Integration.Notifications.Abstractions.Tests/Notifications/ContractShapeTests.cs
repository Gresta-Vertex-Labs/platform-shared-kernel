using System.Reflection;
using FluentAssertions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Storage;

namespace SharedKernel.Integration.Notifications.Abstractions.Tests.Notifications;

/// <summary>
/// Compile-time/reflection-shape assertions over this package's pure-DTO surface — mirrors
/// <c>08.Storage.Abstractions.Tests/ContractShapeTests.cs</c>'s precedent.
/// </summary>
public sealed class ContractShapeTests
{
    [Fact]
    public void NotificationAttachment_HasNoByteArrayOrStreamMember()
    {
        var members = typeof(NotificationAttachment)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(typeof(NotificationAttachment)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType))
            .ToList();

        members.Should().NotContain(typeof(byte[]));
        members.Should().NotContain(t => typeof(Stream).IsAssignableFrom(t));
    }

    [Fact]
    public void NotificationAttachment_FileReferenceIsTheOnlyStorageHandle()
    {
        var fileReferenceProperty = typeof(NotificationAttachment).GetProperty(nameof(NotificationAttachment.FileReference));

        fileReferenceProperty.Should().NotBeNull();
        fileReferenceProperty!.PropertyType.Should().Be(typeof(FileReference));
    }

    [Fact]
    public void NotificationMessage_HasNoByteArrayOrStreamMember()
    {
        var openType = typeof(NotificationMessage<>);
        var closedType = openType.MakeGenericType(typeof(object));

        var propertyTypes = closedType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Where(t => t != typeof(object)) // exclude the TemplateModel slot itself (caller-supplied, opaque)
            .ToList();

        propertyTypes.Should().NotContain(typeof(byte[]));
        propertyTypes.Should().NotContain(t => typeof(Stream).IsAssignableFrom(t));
    }

    [Fact]
    public void NotificationMessage_NotificationDeliveryIdIsRequired()
    {
        var property = typeof(NotificationMessage<object>).GetProperty(nameof(NotificationMessage<object>.NotificationDeliveryId));

        property.Should().NotBeNull();
        property!.GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>().Should().NotBeNull(
            "NotificationDeliveryId must be caller-supplied and required — never generated internally by a sender.");
    }
}
