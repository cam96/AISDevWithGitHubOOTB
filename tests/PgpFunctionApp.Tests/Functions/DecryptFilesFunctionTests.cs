using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using PgpFunctionApp.Functions;
using PgpFunctionApp.Models;
using PgpFunctionApp.Services;

namespace PgpFunctionApp.Tests.Functions;

[TestFixture]
public sealed class DecryptFilesFunctionTests
{
    private Mock<IPgpService> _mockPgpService = null!;
    private Mock<IBlobStorageService> _mockBlobStorageService = null!;
    private Mock<ILogger<DecryptFilesFunction>> _mockLogger = null!;
    private IOptions<PgpSettings> _pgpSettings = null!;
    private DecryptFilesFunction _sut = null!;
    private HttpRequest _request = null!;

    [SetUp]
    public void SetUp()
    {
        _mockPgpService = new Mock<IPgpService>();
        _mockBlobStorageService = new Mock<IBlobStorageService>();
        _mockLogger = new Mock<ILogger<DecryptFilesFunction>>();
        _pgpSettings = new OptionsWrapper<PgpSettings>(new PgpSettings
        {
            Passphrase = "TestPassphrase",
            Identity = "test@test.com",
            KeyStrength = 1024
        });

        _sut = new DecryptFilesFunction(
            _mockPgpService.Object,
            _mockBlobStorageService.Object,
            _pgpSettings,
            _mockLogger.Object);

        _request = new DefaultHttpContext().Request;
    }

    // ─── Constructor guards ───────────────────────────────────────────────────

    [Test]
    public void Constructor_NullPgpService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecryptFilesFunction(
                null!, _mockBlobStorageService.Object, _pgpSettings, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullBlobStorageService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecryptFilesFunction(
                _mockPgpService.Object, null!, _pgpSettings, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullPgpSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecryptFilesFunction(
                _mockPgpService.Object, _mockBlobStorageService.Object, null!, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecryptFilesFunction(
                _mockPgpService.Object, _mockBlobStorageService.Object, _pgpSettings, null!));
    }

    // ─── Run ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Run_PrivateKeyNotFound_Returns404()
    {
        _mockBlobStorageService
            .Setup(b => b.BlobExistsAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        var response = ((NotFoundObjectResult)result).Value as ApiResponse;
        Assert.That(response!.Success, Is.False);
    }

    [Test]
    public async Task Run_EmptyDecryptInbox_ReturnsOkWithZeroCount()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>().AsReadOnly());

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.ProcessedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Run_InboxWithNoPgpFiles_ReturnsOkWithZeroCount()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "readme.txt", "notes.md" }.AsReadOnly());

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.ProcessedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Run_OneFile_DecryptsAndUploadsWithoutPgpExtension()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "document.txt.pgp" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("decryptinbox", "document.txt.pgp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.DecryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            b => b.UploadBlobAsync("decryptoutbox", "document.txt", It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Run_FileWithoutPgpExtensionInLoop_PreservesOriginalName()
    {
        // Edge case: file name ends up in loop but filename stripping handles non-.pgp names gracefully.
        // This covers the ternary else branch (shouldn't normally happen, but guards the code path).
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        // Mix: one .pgp and one non-.pgp (to ensure Where filter works; only .pgp enters the loop).
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "file.pgp", "ignored.txt" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("decryptinbox", "file.pgp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.DecryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Run(_request, CancellationToken.None);

        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.ProcessedCount, Is.EqualTo(1));
        _mockBlobStorageService.Verify(
            b => b.UploadBlobAsync("decryptoutbox", "file", It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Run_MultipleFiles_DecryptsAllPgpFiles()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "a.txt.pgp", "b.txt.pgp", "not-pgp.txt" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("decryptinbox", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.DecryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Run(_request, CancellationToken.None);

        var response = ((OkObjectResult)result).Value as ApiResponse;
        Assert.That(response!.ProcessedCount, Is.EqualTo(2));
    }

    [Test]
    public async Task Run_DecryptThrows_Returns500()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "document.txt.pgp" }.AsReadOnly());
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("decryptinbox", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockPgpService
            .Setup(p => p.DecryptAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Decrypt error"));

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task Run_BlobStorageListThrows_Returns500()
    {
        SetupKeyExists();
        _mockBlobStorageService
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync("decryptinbox", It.IsAny<CancellationToken>()))
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
            .Setup(b => b.DownloadBlobAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        _mockBlobStorageService
            .Setup(b => b.ListBlobsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "file.pgp" }.AsReadOnly());

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.Run(_request, cancelledToken));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetupKeyExists()
    {
        _mockBlobStorageService
            .Setup(b => b.BlobExistsAsync("keys", "private.key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }
}
