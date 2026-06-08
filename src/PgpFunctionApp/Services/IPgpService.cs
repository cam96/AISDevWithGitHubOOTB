namespace PgpFunctionApp.Services;

public interface IPgpService
{
    /// <summary>
    /// Generates a new PGP key pair and writes them to the provided streams.
    /// Both streams will be positioned at offset 0 after this call.
    /// </summary>
    Task GenerateKeyPairAsync(
        Stream publicKeyStream,
        Stream privateKeyStream,
        string identity,
        string passphrase,
        int keyStrength = 4096,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts <paramref name="inputStream"/> using the public key read from
    /// <paramref name="publicKeyStream"/> and writes ciphertext to <paramref name="outputStream"/>.
    /// <paramref name="publicKeyStream"/> must be positioned at offset 0 before calling.
    /// </summary>
    Task EncryptAsync(
        Stream inputStream,
        Stream outputStream,
        Stream publicKeyStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts <paramref name="inputStream"/> using the private key read from
    /// <paramref name="privateKeyStream"/> and writes plaintext to <paramref name="outputStream"/>.
    /// <paramref name="privateKeyStream"/> must be positioned at offset 0 before calling.
    /// </summary>
    Task DecryptAsync(
        Stream inputStream,
        Stream outputStream,
        Stream privateKeyStream,
        string passphrase,
        CancellationToken cancellationToken = default);
}
