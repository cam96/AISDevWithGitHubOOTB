using Org.BouncyCastle.Bcpg;
using PgpCore;

namespace PgpFunctionApp.Services;

public sealed class PgpService : IPgpService
{
    public async Task GenerateKeyPairAsync(
        Stream publicKeyStream,
        Stream privateKeyStream,
        string identity,
        string passphrase,
        int keyStrength = 4096,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicKeyStream);
        ArgumentNullException.ThrowIfNull(privateKeyStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        cancellationToken.ThrowIfCancellationRequested();

        using var pgp = new PGP();
        await Task.Run(
            () => pgp.GenerateKey(publicKeyStream, privateKeyStream, identity, passphrase, strength: keyStrength),
            cancellationToken);

        if (publicKeyStream.CanSeek) publicKeyStream.Seek(0, SeekOrigin.Begin);
        if (privateKeyStream.CanSeek) privateKeyStream.Seek(0, SeekOrigin.Begin)

    public async Task EncryptAsync(
        Stream inputStream,
        Stream outputStream,
        Stream publicKeyStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(publicKeyStream);

        cancellationToken.ThrowIfCancellationRequested();

        var encryptionKeys = new EncryptionKeys(publicKeyStream);
        using var pgp = new PGP(encryptionKeys)
        {
            SymmetricKeyAlgorithm = SymmetricKeyAlgorithmTag.Aes256
        };
        await pgp.EncryptAsync(inputStream, outputStream);
    }

    public async Task DecryptAsync(
        Stream inputStream,
        Stream outputStream,
        Stream privateKeyStream,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(privateKeyStream);
        ArgumentNullException.ThrowIfNull(passphrase);

        cancellationToken.ThrowIfCancellationRequested();

        var encryptionKeys = new EncryptionKeys(privateKeyStream, passphrase);
        using var pgp = new PGP(encryptionKeys);
        await pgp.DecryptAsync(inputStream, outputStream);
    }
}
