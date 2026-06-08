using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace PgpFunctionApp.Services;

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public BlobStorageService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient
            ?? throw new ArgumentNullException(nameof(blobServiceClient));
    }

    public async Task<IReadOnlyList<string>> ListBlobsAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(
            publicAccessType: PublicAccessType.None,
            cancellationToken: cancellationToken);

        var blobNames = new List<string>();
        await foreach (var blobItem in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            blobNames.Add(blobItem.Name);
        }

        return blobNames.AsReadOnly();
    }

    public async Task<Stream> DownloadBlobAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return memoryStream;
    }

    public async Task UploadBlobAsync(
        string containerName,
        string blobName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(content);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(
            publicAccessType: PublicAccessType.None,
            cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite: true, cancellationToken: cancellationToken);
    }

    public async Task<bool> BlobExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }
}
