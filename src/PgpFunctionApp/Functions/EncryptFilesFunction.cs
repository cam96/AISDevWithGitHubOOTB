using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PgpFunctionApp.Models;
using PgpFunctionApp.Services;

namespace PgpFunctionApp.Functions;

public sealed class EncryptFilesFunction
{
    private readonly IPgpService _pgpService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<EncryptFilesFunction> _logger;

    public EncryptFilesFunction(
        IPgpService pgpService,
        IBlobStorageService blobStorageService,
        ILogger<EncryptFilesFunction> logger)
    {
        _pgpService = pgpService ?? throw new ArgumentNullException(nameof(pgpService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function(nameof(EncryptFilesFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "encrypt-files")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting file encryption process.");

        try
        {
            if (!await _blobStorageService.BlobExistsAsync(
                    ContainerNames.Keys, BlobNames.PublicKey, cancellationToken))
            {
                _logger.LogWarning(
                    "Public key not found in container '{Container}'.", ContainerNames.Keys);
                return new NotFoundObjectResult(
                    ApiResponse.Fail("Public key not found. Please generate keys first."));
            }

            using var publicKeyStream = await _blobStorageService.DownloadBlobAsync(
                ContainerNames.Keys, BlobNames.PublicKey, cancellationToken);

            var fileNames = await _blobStorageService.ListBlobsAsync(
                ContainerNames.EncryptInbox, cancellationToken);

            if (fileNames.Count == 0)
            {
                _logger.LogInformation(
                    "No files found in '{Container}' to encrypt.", ContainerNames.EncryptInbox);
                return new OkObjectResult(ApiResponse.Ok("No files found to encrypt.", 0));
            }

            var count = 0;
            foreach (var fileName in fileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var inputStream = await _blobStorageService.DownloadBlobAsync(
                    ContainerNames.EncryptInbox, fileName, cancellationToken);
                using var outputStream = new MemoryStream();

                publicKeyStream.Seek(0, SeekOrigin.Begin);
                await _pgpService.EncryptAsync(inputStream, outputStream, publicKeyStream, cancellationToken);

                outputStream.Seek(0, SeekOrigin.Begin);
                var encryptedFileName = $"{fileName}.pgp";
                await _blobStorageService.UploadBlobAsync(
                    ContainerNames.EncryptedOutbox, encryptedFileName, outputStream, cancellationToken);

                _logger.LogInformation(
                    "Encrypted '{Source}' to '{Destination}'.", fileName, encryptedFileName);
                count++;
            }

            return new OkObjectResult(ApiResponse.Ok($"Successfully encrypted {count} file(s).", count));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt files.");
            return new ObjectResult(ApiResponse.Fail($"Encryption failed: {ex.Message}"))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
