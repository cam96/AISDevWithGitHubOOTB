using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using PgpFunctionApp.Functions;
using PgpFunctionApp.Models;
using PgpFunctionApp.Services;

namespace PgpFunctionApp.Tests.Functions;

[TestFixture]
public sealed class EncryptFilesFunctionTests
{
    private Mock<IPgpService> _mockPgpService = null!;
    private Mock<IBlobStorageService> _mockBlobStorageService = null!;
    private Mock<ILogger<EncryptFilesFunction>> _mockLogger = null!;
    private EncryptFilesFunction _sut = null!;
    private HttpRequest _request = null!;

    [SetUp]
    public void SetUp()
    {
        _mockPgpService = new Mock<IPgpService>();
        _mockBlobStorageService = new Mock<IBlobStorageService>();
        _mockLogger = new Mock<ILogger<EncryptFilesFunction>>();

        _sut = new EncryptFilesFunction(
            _mockPgpService.Object,
            _mockBlobStorageService.Object,
            _mockLogger.Object);

        _request = new DefaultHttpContext().Request;
    }

    // ─── Constructor guards ───────────────────────────────────────────────────

    [Test]
    public void Constructor_NullPgpService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EncryptFilesFunction(null!, _mockBlobStorageService.Object, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullBlobStorageService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EncryptFilesFunction(_mockPgpService.Object, null!, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EncryptFilesFunction(_mockPgpService.Object, _mockBlobStorageService.Object, null!));
    }

    // ─── Run ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Run_PublicKeyNotFound_Returns404()
    {
        _mockBlobStorageService
            .Setup(b => b.BlobExistsAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        var response = ((NotFoundObjectResult)result).Value as ApiResponse;
        Assert.That(response!.Success, Is.False);
    }

    [Test]
    public async Task Run_EmptyInbox_ReturnsOkWithZeroCount()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("encryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>().AsReadOnly());

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.Success, Is.True);
        Assert.That(response.ProcessedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Run_OneFile_EncryptsAndUploadsWithPgpExtension()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("encryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "document.txt" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("encryptinbox", "document.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.EncryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.Success, Is.True);
        Assert.That(response.ProcessedCount, Is.EqualTo(1));

        _mockBlobStorageService.Verify(
            b => b.UploadBlobAsync("encryptedoutbox", "document.txt.pgp", It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Run_MultipleFiles_EncryptsAllFiles()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("encryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "file1.txt", "file2.txt", "file3.csv" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("encryptinbox", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.EncryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Run(_request, CancellationToken.None);

        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.ProcessedCount, Is.EqualTo(3));
    }

    [Test]
    public async Task Run_EncryptThrows_Returns500()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("encryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "document.txt" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("encryptinbox", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.EncryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Encrypt error"));

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task Run_BlobStorageListThrows_Returns500()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("encryptinbox", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public void Run_OperationCanceledException_Rethrows()
    {
        _mockBlobStorageService
            .Setup(b => b.BlobExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.Run(_request, CancellationToken.None));
    }

    [Test]
    public void Run_CancelledInsideLoop_Rethrows()
    {
        var cancelledToken = new CancellationToken(canceled: true);

        _mockBlobStorageService
            .Setup(b => b.BlobExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "file1.txt" }.AsReadOnly());

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.Run(_request, cancelledToken));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetupKeyExists()
    {
        _mockBlobStorageService
            .Setup(b => b.BlobExistsAsync("keys", "public.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }
}
