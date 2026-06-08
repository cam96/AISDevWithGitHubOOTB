namespace PgpFunctionApp.Services;

public interface IBlobStorageService
{
    /// <summary>
    /// Lists all blob names in the specified container.
    /// Creates the container if it does not already exist.
    /// </summary>
    Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob and returns its content as a seekable <see cref="MemoryStream"/> positioned at offset 0.
    /// </summary>
    Task<Stream> DownloadBlobAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a stream to the specified blob, overwriting any existing content.
    /// Creates the container if it does not already exist.
    /// </summary>
    Task UploadBlobAsync(string containerName, string blobName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> if the specified blob exists in the container.
    /// </summary>
    Task<bool> BlobExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
}
