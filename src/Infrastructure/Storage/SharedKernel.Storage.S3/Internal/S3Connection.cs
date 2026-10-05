using Amazon.S3;
using Amazon.S3.Transfer;

namespace SharedKernel.Storage.S3.Internal;

/// <summary>
/// One S3 client and what its endpoint supports, shared by every store on the connection. Stores on the same
/// connection copy between each other server-side.
/// </summary>
internal sealed class S3Connection : IDisposable
{
    public S3Connection(string name, IAmazonS3 client, S3Compatibility compatibility)
    {
        Name = name;
        Client = client;
        Compatibility = compatibility;
        TransferUtility = new TransferUtility(client);
    }

    /// <summary>Gets the connection name, e.g. <c>S3</c> or <c>Obs</c>; the <c>storage.provider</c> telemetry tag.</summary>
    public string Name { get; }

    public IAmazonS3 Client { get; }

    public S3Compatibility Compatibility { get; }

    public ITransferUtility TransferUtility { get; }

    public void Dispose()
    {
        TransferUtility.Dispose();
        Client.Dispose();
    }
}
