using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PgpFunctionApp.Models;
using PgpFunctionApp.Services;

namespace PgpFunctionApp.Functions;

public sealed class DecryptFilesFunction
{
    private readonly IPgpService _pgpService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IOptions<PgpSettings> _pgpSettings;
    private readonly ILogger<DecryptFilesFunction> _logger;

    public DecryptFilesFunction(
        IPgpService pgpService,
        IBlobStorageService blobStorageService,
        IOptions<PgpSettings> pgpSettings,
        ILogger<DecryptFilesFunction> logger)
    {
        _pgpService = pgpService ?? throw new ArgumentNullException(nameof(pgpService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _pgpSettings = pgpSettings ?? throw new ArgumentNullException(nameof(pgpSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function(nameof(DecryptFilesFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "decrypt-files")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting file decryption process.");

        try
        {
            if (!await _blobStorageService.BlobExistsAsync(
                    ContainerNames.Keys, BlobNames.PrivateKey, cancellationToken))
            {
                _logger.LogWarning(
                    "Private key not found in container '{Container}'.", ContainerNames.Keys);
                return new NotFoundObjectResult(
                    ApiResponse.Fail("Private key not found. Please generate keys first."));
            }

            using var privateKeyStream = await _blobStorageService.DownloadBlobAsync(
                ContainerNames.Keys, BlobNames.PrivateKey, cancellationToken);

            var allFileNames = await _blobStorageService.ListBlobsAsync(
                ContainerNames.DecryptInbox, cancellationToken);

            var pgpFileNames = allFileNames
                .Where(f => f.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pgpFileNames.Count == 0)
            {
                _logger.LogInformation(
                    "No PGP files found in '{Container}' to decrypt.", ContainerNames.DecryptInbox);
                return new OkObjectResult(ApiResponse.Ok("No PGP files found to decrypt.", 0));
            }

            var passphrase = _pgpSettings.Value.Passphrase;
            var count = 0;
            foreach (var fileName in pgpFileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var inputStream = await _blobStorageService.DownloadBlobAsync(
                    ContainerNames.DecryptInbox, fileName, cancellationToken);
                using var outputStream = new MemoryStream();

                privateKeyStream.Seek(0, SeekOrigin.Begin);
                await _pgpService.DecryptAsync(
                    inputStream, outputStream, privateKeyStream, passphrase, cancellationToken);

                outputStream.Seek(0, SeekOrigin.Begin);
                var decryptedFileName = fileName.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^4]
                    : fileName;

                await _blobStorageService.UploadBlobAsync(
                    ContainerNames.DecryptOutbox, decryptedFileName, outputStream, cancellationToken);

                _logger.LogInformation(
                    "Decrypted '{Source}' to '{Destination}'.", fileName, decryptedFileName);
                count++;
            }

            return new OkObjectResult(ApiResponse.Ok($"Successfully decrypted {count} file(s).", count));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt files.");
            return new ObjectResult(ApiResponse.Fail($"Decryption failed: {ex.Message}"))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
