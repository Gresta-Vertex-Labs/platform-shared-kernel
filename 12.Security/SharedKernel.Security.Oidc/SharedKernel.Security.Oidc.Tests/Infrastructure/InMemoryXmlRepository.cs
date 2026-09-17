using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace SharedKernel.Security.Oidc.Tests.Infrastructure;

// Keeps Data Protection keys in memory so tests never read or write the machine's key ring.
internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> _elements = [];
    private readonly Lock _gate = new();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_gate)
        {
            return [.. _elements.Select(element => new XElement(element))];
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_gate)
        {
            _elements.Add(new XElement(element));
        }
    }
}
