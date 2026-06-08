using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;
using NUnit.Framework;
using PgpFunctionApp.Services;
using PgpFunctionApp.Tests.Helpers;
using System.Text;

namespace PgpFunctionApp.Tests.Services;

[TestFixture]
public sealed class BlobStorageServiceTests
{
    private Mock<BlobServiceClient> _mockBlobServiceClient = null!;
    private Mock<BlobContainerClient> _mockContainerClient = null!;
    private Mock<BlobClient> _mockBlobClient = null!;
    private BlobStorageService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockContainerClient = new Mock<BlobContainerClient>();
        _mockBlobClient = new Mock<BlobClient>();

        _mockBlobServiceClient
            .Setup(s => s.GetBlobContainerClient(It.IsAny<string>()))
            .Returns(_mockContainerClient.Object);

        _mockContainerClient
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockBlobClient.Object);

        _sut = new BlobStorageService(_mockBlobServiceClient.Object);
    }

    // ─── Constructor ─────────────────────────────────────────────────────────

    [Test]
    public void Constructor_NullBlobServiceClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BlobStorageService(null!));
    }

    // ─── ListBlobsAsync ───────────────────────────────────────────────────────

    [Test]
    public async Task ListBlobsAsync_ContainerWithBlobs_ReturnsAllBlobNames()
    {
        // Arrange
        var blobItems = new[]
        {
            BlobsModelFactory.BlobItem(name: "file1.txt"),
            BlobsModelFactory.BlobItem(name: "file2.txt")
        };
        SetupCreateIfNotExists();
        SetupGetBlobsAsync(blobItems);

        // Act
        var result = await _sut.ListBlobsAsync("encryptinbox");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("file1.txt"));
        Assert.That(result, Does.Contain("file2.txt"));
    }

    [Test]
    public async Task ListBlobsAsync_EmptyContainer_ReturnsEmptyList()
    {
        SetupCreateIfNotExists();
        SetupGetBlobsAsync(Array.Empty<BlobItem>());

        var result = await _sut.ListBlobsAsync("encryptinbox");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ListBlobsAsync_NullContainerName_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.ListBlobsAsync(null!));
    }

    [Test]
    public void ListBlobsAsync_EmptyContainerName_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.ListBlobsAsync(""));
    }

    [Test]
    public void ListBlobsAsync_WhitespaceContainerName_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.ListBlobsAsync("   "));
    }

    // ─── DownloadBlobAsync ────────────────────────────────────────────────────

    [Test]
    public async Task DownloadBlobAsync_ExistingBlob_ReturnsSeekableStreamWithContent()
    {
        const string expectedContent = "Hello, World!";
        _mockBlobClient
            .Setup(b => b.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((stream, _) =>
            {
                var bytes = Encoding.UTF8.GetBytes(expectedContent);
                stream.Write(bytes, 0, bytes.Length);
            })
            .ReturnsAsync(Mock.Of<Response>());

        var result = await _sut.DownloadBlobAsync("container", "blob.txt");

        Assert.That(result.Position, Is.EqualTo(0), "Returned stream must be positioned at offset 0.");
        Assert.That(result.Length, Is.GreaterThan(0));
        var content = new StreamReader(result).ReadToEnd();
        Assert.That(content, Is.EqualTo(expectedContent));
    }

    [Test]
    public void DownloadBlobAsync_NullContainerName_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.DownloadBlobAsync(null!, "blob.txt"));
    }

    [Test]
    public void DownloadBlobAsync_EmptyContainerName_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.DownloadBlobAsync("", "blob.txt"));
    }

    [Test]
    public void DownloadBlobAsync_NullBlobName_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.DownloadBlobAsync("container", null!));
    }

    [Test]
    public void DownloadBlobAsync_EmptyBlobName_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.DownloadBlobAsync("container", ""));
    }

    // ─── UploadBlobAsync ──────────────────────────────────────────────────────

    [Test]
    public async Task UploadBlobAsync_ValidInput_UploadsToCorrectContainerAndBlob()
    {
        SetupCreateIfNotExists();
        _mockBlobClient
            .Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("upload content"));
        await _sut.UploadBlobAsync("container", "blob.txt", content);

        _mockBlobClient.Verify(
            b => b.UploadAsync(content, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void UploadBlobAsync_NullContainerName_ThrowsArgumentNullException()
    {
        using var content = new MemoryStream();
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.UploadBlobAsync(null!, "blob.txt", content));
    }

    [Test]
    public void UploadBlobAsync_EmptyContainerName_ThrowsArgumentException()
    {
        using var content = new MemoryStream();
        Assert.ThrowsAsync<ArgumentException>(() => _sut.UploadBlobAsync("", "blob.txt", content));
    }

    [Test]
    public void UploadBlobAsync_NullBlobName_ThrowsArgumentNullException()
    {
        using var content = new MemoryStream();
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.UploadBlobAsync("container", null!, content));
    }

    [Test]
    public void UploadBlobAsync_EmptyBlobName_ThrowsArgumentException()
    {
        using var content = new MemoryStream();
        Assert.ThrowsAsync<ArgumentException>(() => _sut.UploadBlobAsync("container", "", content));
    }

    [Test]
    public void UploadBlobAsync_NullContent_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.UploadBlobAsync("container", "blob.txt", null!));
    }

    // ─── BlobExistsAsync ──────────────────────────────────────────────────────

    [Test]
    public async Task BlobExistsAsync_WhenBlobExists_ReturnsTrue()
    {
        _mockBlobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var result = await _sut.BlobExistsAsync("container", "blob.txt");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task BlobExistsAsync_WhenBlobDoesNotExist_ReturnsFalse()
    {
        _mockBlobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        var result = await _sut.BlobExistsAsync("container", "missing.txt");

        Assert.That(result, Is.False);
    }

    [Test]
    public void BlobExistsAsync_NullContainerName_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.BlobExistsAsync(null!, "blob.txt"));
    }

    [Test]
    public void BlobExistsAsync_EmptyContainerName_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.BlobExistsAsync("", "blob.txt"));
    }

    [Test]
    public void BlobExistsAsync_NullBlobName_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.BlobExistsAsync("container", null!));
    }

    [Test]
    public void BlobExistsAsync_EmptyBlobName_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.BlobExistsAsync("container", ""));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetupCreateIfNotExists()
    {
        _mockContainerClient
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());
    }

    private void SetupGetBlobsAsync(IEnumerable<BlobItem> items)
    {
        _mockContainerClient
            .Setup(c => c.GetBlobsAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(new FakeAsyncPageable<BlobItem>(items));
    }
}
