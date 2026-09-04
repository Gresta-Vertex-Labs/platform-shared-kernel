using System.Reflection;
using SharedKernel.DataPrivacy.Classification;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Classification;

/// <summary>
/// Covers <see cref="DataClassificationAttribute"/>/<see cref="SensitiveDataCategoryAttribute"/>
/// (T-59): proves the attributes apply correctly to a property/field on a test type and are
/// retrievable via <see cref="Attribute.GetCustomAttribute(MemberInfo, Type)"/>.
/// </summary>
/// <remarks>
/// THE REFLECTIVE READS IN THIS FILE ARE A TEST-ONLY DEVICE PROVING THE ATTRIBUTE MECHANICS WORK
/// — they are never a claim that production code reads these attributes this way.
/// <see cref="DataClassificationAttribute"/> and <see cref="SensitiveDataCategoryAttribute"/>'s own
/// XML docs state, IN CAPITALS, that neither attribute is ever read via reflection in production
/// code; their sole sanctioned consumers are a compile-time analyzer and human documentation. A
/// future contributor must not cite this test file as precedent for adding a production reflective
/// read of either attribute.
/// </remarks>
public sealed class ClassificationAttributeTests
{
    [Theory]
    [InlineData(nameof(SampleType.PublicProperty), DataClassification.Public)]
    [InlineData(nameof(SampleType.InternalProperty), DataClassification.Internal)]
    [InlineData(nameof(SampleType.ConfidentialProperty), DataClassification.Confidential)]
    [InlineData(nameof(SampleType.RestrictedProperty), DataClassification.Restricted)]
    public void DataClassificationAttribute_AppliesToProperty_AndIsRetrievableReflectively(
        string propertyName, DataClassification expected)
    {
        PropertyInfo property = typeof(SampleType).GetProperty(propertyName)!;

        var attribute = (DataClassificationAttribute?)Attribute.GetCustomAttribute(
            property, typeof(DataClassificationAttribute));

        Assert.NotNull(attribute);
        Assert.Equal(expected, attribute!.Classification);
    }

    [Fact]
    public void DataClassificationAttribute_AppliesToField_AndIsRetrievableReflectively()
    {
        FieldInfo field = typeof(SampleType).GetField(nameof(SampleType.RestrictedField))!;

        var attribute = (DataClassificationAttribute?)Attribute.GetCustomAttribute(
            field, typeof(DataClassificationAttribute));

        Assert.NotNull(attribute);
        Assert.Equal(DataClassification.Restricted, attribute!.Classification);
    }

    [Fact]
    public void DataClassificationAttribute_UsageIsRestrictedToPropertyAndField()
    {
        var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(DataClassificationAttribute), typeof(AttributeUsageAttribute));

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Property | AttributeTargets.Field, usage!.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Theory]
    [InlineData(nameof(SampleType.PiiProperty), SensitiveDataCategory.Pii)]
    [InlineData(nameof(SampleType.PaymentCardProperty), SensitiveDataCategory.PaymentCard)]
    [InlineData(nameof(SampleType.CredentialProperty), SensitiveDataCategory.Credential)]
    [InlineData(nameof(SampleType.HealthProperty), SensitiveDataCategory.Health)]
    public void SensitiveDataCategoryAttribute_AppliesToProperty_AndIsRetrievableReflectively(
        string propertyName, SensitiveDataCategory expected)
    {
        PropertyInfo property = typeof(SampleType).GetProperty(propertyName)!;

        var attribute = (SensitiveDataCategoryAttribute?)Attribute.GetCustomAttribute(
            property, typeof(SensitiveDataCategoryAttribute));

        Assert.NotNull(attribute);
        Assert.Equal(expected, attribute!.Category);
    }

    [Fact]
    public void SensitiveDataCategoryAttribute_AppliesToField_AndIsRetrievableReflectively()
    {
        FieldInfo field = typeof(SampleType).GetField(nameof(SampleType.CredentialField))!;

        var attribute = (SensitiveDataCategoryAttribute?)Attribute.GetCustomAttribute(
            field, typeof(SensitiveDataCategoryAttribute));

        Assert.NotNull(attribute);
        Assert.Equal(SensitiveDataCategory.Credential, attribute!.Category);
    }

    [Fact]
    public void SensitiveDataCategoryAttribute_UsageIsRestrictedToPropertyAndField()
    {
        var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(SensitiveDataCategoryAttribute), typeof(AttributeUsageAttribute));

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Property | AttributeTargets.Field, usage!.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void BothAttributes_CanApplyToTheSameMember_Independently()
    {
        PropertyInfo property = typeof(SampleType).GetProperty(nameof(SampleType.NationalId))!;

        var classification = (DataClassificationAttribute?)Attribute.GetCustomAttribute(
            property, typeof(DataClassificationAttribute));
        var category = (SensitiveDataCategoryAttribute?)Attribute.GetCustomAttribute(
            property, typeof(SensitiveDataCategoryAttribute));

        Assert.NotNull(classification);
        Assert.NotNull(category);
        Assert.Equal(DataClassification.Restricted, classification!.Classification);
        Assert.Equal(SensitiveDataCategory.Pii, category!.Category);
    }

    [Fact]
    public void IDataSubjectRequestHandler_HasNoDefaultImplementation_RegisteredInThisAssembly()
    {
        // T-59: confirms this package ships no default/reflection-based
        // IDataSubjectRequestHandler implementation — the interface is implemented
        // exclusively by each consuming service against its own data.
        Assembly assembly = typeof(DataSubjectRequests.IDataSubjectRequestHandler).Assembly;

        IEnumerable<Type> implementations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(DataSubjectRequests.IDataSubjectRequestHandler).IsAssignableFrom(t));

        Assert.Empty(implementations);
    }

    private sealed class SampleType
    {
        [DataClassification(DataClassification.Public)]
        public string PublicProperty { get; set; } = string.Empty;

        [DataClassification(DataClassification.Internal)]
        public string InternalProperty { get; set; } = string.Empty;

        [DataClassification(DataClassification.Confidential)]
        public string ConfidentialProperty { get; set; } = string.Empty;

        [DataClassification(DataClassification.Restricted)]
        public string RestrictedProperty { get; set; } = string.Empty;

        [DataClassification(DataClassification.Restricted)]
        public string RestrictedField = string.Empty;

        [SensitiveDataCategory(SensitiveDataCategory.Pii)]
        public string PiiProperty { get; set; } = string.Empty;

        [SensitiveDataCategory(SensitiveDataCategory.PaymentCard)]
        public string PaymentCardProperty { get; set; } = string.Empty;

        [SensitiveDataCategory(SensitiveDataCategory.Credential)]
        public string CredentialProperty { get; set; } = string.Empty;

        [SensitiveDataCategory(SensitiveDataCategory.Health)]
        public string HealthProperty { get; set; } = string.Empty;

        [SensitiveDataCategory(SensitiveDataCategory.Credential)]
        public string CredentialField = string.Empty;

        [DataClassification(DataClassification.Restricted)]
        [SensitiveDataCategory(SensitiveDataCategory.Pii)]
        public string NationalId { get; set; } = string.Empty;
    }
}
