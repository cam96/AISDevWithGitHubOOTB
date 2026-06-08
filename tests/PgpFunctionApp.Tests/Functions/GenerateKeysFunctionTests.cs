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
public sealed class GenerateKeysFunctionTests
{
    private Mock<IPgpService> _mockPgpService = null!;
    private Mock<IBlobStorageService> _mockBlobStorageService = null!;
    private Mock<ILogger<GenerateKeysFunction>> _mockLogger = null!;
    private IOptions<PgpSettings> _pgpSettings = null!;
    private GenerateKeysFunction _sut = null!;
    private HttpRequest _request = null!;

    [SetUp]
    public void SetUp()
    {
        _mockPgpService = new Mock<IPgpService>();
        _mockBlobStorageService = new Mock<IBlobStorageService>();
        _mockLogger = new Mock<ILogger<GenerateKeysFunction>>();
        _pgpSettings = new OptionsWrapper<PgpSettings>(new PgpSettings
        {
            Passphrase = "TestPassphrase",
            Identity = "test@test.com",
            KeyStrength = 1024
        });

        _sut = new GenerateKeysFunction(
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
            new GenerateKeysFunction(
                null!, _mockBlobStorageService.Object, _pgpSettings, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullBlobStorageService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GenerateKeysFunction(
                _mockPgpService.Object, null!, _pgpSettings, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullPgpSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GenerateKeysFunction(
                _mockPgpService.Object, _mockBlobStorageService.Object, null!, _mockLogger.Object));
    }

    [Test]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GenerateKeysFunction(
                _mockPgpService.Object, _mockBlobStorageService.Object, _pgpSettings, null!));
    }

    // ─── Run ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Run_Success_ReturnsOkResultWithSuccessResponse()
    {
        _mockPgpService
            .Setup(p => p.GenerateKeyPairAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as ApiResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Success, Is.True);
    }

    [Test]
    public async Task Run_Success_UploadsBothKeys()
    {
        _mockPgpService
            .Setup(p => p.GenerateKeyPairAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.Run(_request, CancellationToken.None);

        _mockBlobStorageService.Verify(
            b => b.UploadBlobAsync("keys", "public.key", It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockBlobStorageService.Verify(
            b => b.UploadBlobAsync("keys", "private.key", It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Run_PgpServiceThrows_Returns500WithFailResponse()
    {
        _mockPgpService
            .Setup(p => p.GenerateKeyPairAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PGP error"));

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));
        var response = objectResult.Value as ApiResponse;
        Assert.That(response!.Success, Is.False);
        Assert.That(response.Message, Is.EqualTo("Failed to generate key pair."));
    }

    [Test]
    public async Task Run_BlobStorageThrows_Returns500WithFailResponse()
    {
        _mockPgpService
            .Setup(p => p.GenerateKeyPairAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockBlobStorageService
            .Setup(b => b.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        var result = await _sut.Run(_request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public void Run_OperationCanceledException_Rethrows()
    {
        _mockPgpService
            .Setup(p => p.GenerateKeyPairAsync(
                It.IsAny<Stream>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.Run(_request, CancellationToken.None));
    }
}
