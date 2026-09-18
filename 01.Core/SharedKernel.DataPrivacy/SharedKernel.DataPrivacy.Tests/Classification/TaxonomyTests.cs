using System.Reflection;
using Microsoft.Extensions.Compliance.Classification;
using SharedKernel.DataPrivacy.Classification;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Classification;

public sealed class TaxonomyTests
{
    private static readonly Type[] AttributeTypes = typeof(PrivacyTaxonomy).Assembly.GetExportedTypes()
        .Where(t => t.IsSubclassOf(typeof(DataClassificationAttribute)))
        .ToArray();

    [Fact]
    public void All_HasTwentyThreeDistinctClassificationsInTheTaxonomy()
    {
        Assert.Equal(23, PrivacyTaxonomy.All.Count);
        Assert.Equal(23, PrivacyTaxonomy.All.Distinct().Count());
        Assert.All(PrivacyTaxonomy.All, c => Assert.Equal(PrivacyTaxonomy.TaxonomyName, c.TaxonomyName));
    }

    [Fact]
    public void All_ListsEveryStaticClassificationProperty()
    {
        var declared = typeof(PrivacyTaxonomy).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(DataClassification))
            .Select(p => (DataClassification)p.GetValue(null)!);

        Assert.Equal(declared.OrderBy(c => c.Value), PrivacyTaxonomy.All.OrderBy(c => c.Value));
    }

    [Fact]
    public void EveryClassification_HasExactlyOneAttributeNamedAfterIt()
    {
        Assert.Equal(PrivacyTaxonomy.All.Count, AttributeTypes.Length);

        foreach (Type type in AttributeTypes)
        {
            var attribute = (DataClassificationAttribute)Activator.CreateInstance(type)!;
            Assert.Equal(attribute.Classification.Value + "DataAttribute", type.Name);
            Assert.Contains(attribute.Classification, PrivacyTaxonomy.All);
        }
    }

    [Fact]
    public void Attributes_CanMarkPropertiesFieldsAndParameters()
    {
        foreach (Type type in AttributeTypes)
        {
            AttributeUsageAttribute usage = type.GetCustomAttribute<AttributeUsageAttribute>()!;
            Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property), type.Name);
            Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Field), type.Name);
            Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Parameter), type.Name);
        }
    }

    [Theory]
    [InlineData("Health")]
    [InlineData("Genetic")]
    [InlineData("Biometric")]
    [InlineData("EthnicOrigin")]
    [InlineData("PoliticalOpinion")]
    [InlineData("Belief")]
    [InlineData("Membership")]
    [InlineData("SexLife")]
    [InlineData("CriminalRecord")]
    [InlineData("Appearance")]
    public void SpecialCategories_AreRecognized(string value)
    {
        var classification = new DataClassification(PrivacyTaxonomy.TaxonomyName, value);

        Assert.True(PrivacyTaxonomy.IsSpecialCategory(classification));
        Assert.Equal(new DataClassificationSet(classification).Union(PrivacyTaxonomy.SpecialCategories), PrivacyTaxonomy.SpecialCategories);
    }

    [Fact]
    public void OrdinaryPersonalData_IsNotASpecialCategory()
    {
        Assert.Equal(10, PrivacyTaxonomy.All.Count(PrivacyTaxonomy.IsSpecialCategory));
        Assert.False(PrivacyTaxonomy.IsSpecialCategory(PrivacyTaxonomy.EmailAddress));
        Assert.False(PrivacyTaxonomy.IsSpecialCategory(new DataClassification("Other", "Health")));
    }
}
