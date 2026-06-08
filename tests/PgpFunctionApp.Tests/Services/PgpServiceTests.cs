using NUnit.Framework;
using PgpFunctionApp.Services;

namespace PgpFunctionApp.Tests.Services;

[TestFixture]
public sealed class PgpServiceTests
{
    private PgpService _sut = null!;

    private const string TestPassphrase = "TestPassphrase123!";
    private const string TestIdentity = "test@example.com";
    private const int TestKeyStrength = 1024; // Low strength for test speed

    [SetUp]
    public void SetUp()
    {
        _sut = new PgpService();
    }

    // ─── GenerateKeyPairAsync ────────────────────────────────────────────────

    [Test]
    public async Task GenerateKeyPairAsync_ValidParameters_WritesBothKeyStreams()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        await _sut.GenerateKeyPairAsync(
            publicKeyStream, privateKeyStream, TestIdentity, TestPassphrase, TestKeyStrength);

        Assert.That(publicKeyStream.Length, Is.GreaterThan(0), "Public key stream must not be empty.");
        Assert.That(privateKeyStream.Length, Is.GreaterThan(0), "Private key stream must not be empty.");
    }

    [Test]
    public void GenerateKeyPairAsync_NullPublicKeyStream_ThrowsArgumentNullException()
    {
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateKeyPairAsync(null!, privateKeyStream, TestIdentity, TestPassphrase, TestKeyStrength));
    }

    [Test]
    public void GenerateKeyPairAsync_NullPrivateKeyStream_ThrowsArgumentNullException()
    {
        using var publicKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateKeyPairAsync(publicKeyStream, null!, TestIdentity, TestPassphrase, TestKeyStrength));
    }

    [Test]
    public void GenerateKeyPairAsync_NullIdentity_ThrowsArgumentNullException()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateKeyPairAsync(publicKeyStream, privateKeyStream, null!, TestPassphrase, TestKeyStrength));
    }

    [Test]
    public void GenerateKeyPairAsync_EmptyIdentity_ThrowsArgumentException()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GenerateKeyPairAsync(publicKeyStream, privateKeyStream, "", TestPassphrase, TestKeyStrength));
    }

    [Test]
    public void GenerateKeyPairAsync_NullPassphrase_ThrowsArgumentNullException()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateKeyPairAsync(publicKeyStream, privateKeyStream, TestIdentity, null!, TestKeyStrength));
    }

    [Test]
    public void GenerateKeyPairAsync_EmptyPassphrase_ThrowsArgumentException()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GenerateKeyPairAsync(publicKeyStream, privateKeyStream, TestIdentity, "", TestKeyStrength));
    }

    [Test]
    public void GenerateKeyPairAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();
        var cancelledToken = new CancellationToken(canceled: true);

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.GenerateKeyPairAsync(
                publicKeyStream, privateKeyStream, TestIdentity, TestPassphrase, TestKeyStrength, cancelledToken));
    }

    // ─── EncryptAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task EncryptAsync_ValidInput_ProducesNonEmptyEncryptedOutput()
    {
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();
        await _sut.GenerateKeyPairAsync(
            publicKeyStream, privateKeyStream, TestIdentity, TestPassphrase, TestKeyStrength);
        publicKeyStream.Seek(0, SeekOrigin.Begin);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, PGP!");
        using var inputStream = new MemoryStream(plaintext);
        using var outputStream = new MemoryStream();

        await _sut.EncryptAsync(inputStream, outputStream, publicKeyStream);

        Assert.That(outputStream.Length, Is.GreaterThan(0), "Encrypted output must not be empty.");
        Assert.That(outputStream.ToArray(), Is.Not.EqualTo(plaintext),
            "Encrypted output must differ from plaintext.");
    }

    [Test]
    public void EncryptAsync_NullInputStream_ThrowsArgumentNullException()
    {
        using var outputStream = new MemoryStream();
        using var publicKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.EncryptAsync(null!, outputStream, publicKeyStream));
    }

    [Test]
    public void EncryptAsync_NullOutputStream_ThrowsArgumentNullException()
    {
        using var inputStream = new MemoryStream();
        using var publicKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.EncryptAsync(inputStream, null!, publicKeyStream));
    }

    [Test]
    public void EncryptAsync_NullPublicKeyStream_ThrowsArgumentNullException()
    {
        using var inputStream = new MemoryStream();
        using var outputStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.EncryptAsync(inputStream, outputStream, null!));
    }

    [Test]
    public void EncryptAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var inputStream = new MemoryStream();
        using var outputStream = new MemoryStream();
        using var publicKeyStream = new MemoryStream();
        var cancelledToken = new CancellationToken(canceled: true);

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.EncryptAsync(inputStream, outputStream, publicKeyStream, cancelledToken));
    }

    // ─── DecryptAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task DecryptAsync_AfterEncrypt_ReturnsOriginalContent()
    {
        // Generate keys
        using var publicKeyStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();
        await _sut.GenerateKeyPairAsync(
            publicKeyStream, privateKeyStream, TestIdentity, TestPassphrase, TestKeyStrength);
        publicKeyStream.Seek(0, SeekOrigin.Begin);

        // Encrypt
        const string originalContent = "Round-trip test message.";
        using var plaintextStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(originalContent));
        using var encryptedStream = new MemoryStream();
        await _sut.EncryptAsync(plaintextStream, encryptedStream, publicKeyStream);

        // Decrypt
        encryptedStream.Seek(0, SeekOrigin.Begin);
        privateKeyStream.Seek(0, SeekOrigin.Begin);
        using var decryptedStream = new MemoryStream();
        await _sut.DecryptAsync(encryptedStream, decryptedStream, privateKeyStream, TestPassphrase);

        decryptedStream.Seek(0, SeekOrigin.Begin);
        var result = new StreamReader(decryptedStream).ReadToEnd();
        Assert.That(result, Is.EqualTo(originalContent));
    }

    [Test]
    public void DecryptAsync_NullInputStream_ThrowsArgumentNullException()
    {
        using var outputStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DecryptAsync(null!, outputStream, privateKeyStream, TestPassphrase));
    }

    [Test]
    public void DecryptAsync_NullOutputStream_ThrowsArgumentNullException()
    {
        using var inputStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DecryptAsync(inputStream, null!, privateKeyStream, TestPassphrase));
    }

    [Test]
    public void DecryptAsync_NullPrivateKeyStream_ThrowsArgumentNullException()
    {
        using var inputStream = new MemoryStream();
        using var outputStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DecryptAsync(inputStream, outputStream, null!, TestPassphrase));
    }

    [Test]
    public void DecryptAsync_NullPassphrase_ThrowsArgumentNullException()
    {
        using var inputStream = new MemoryStream();
        using var outputStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DecryptAsync(inputStream, outputStream, privateKeyStream, null!));
    }

    [Test]
    public void DecryptAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var inputStream = new MemoryStream();
        using var outputStream = new MemoryStream();
        using var privateKeyStream = new MemoryStream();
        var cancelledToken = new CancellationToken(canceled: true);

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.DecryptAsync(inputStream, outputStream, privateKeyStream, TestPassphrase, cancelledToken));
    }
}
