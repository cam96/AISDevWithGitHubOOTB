using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PgpFunctionApp.Models;
using PgpFunctionApp.Services;

namespace PgpFunctionApp.Functions;

public sealed class GenerateKeysFunction
{
    private readonly IPgpService _pgpService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IOptions<PgpSettings> _pgpSettings;
    private readonly ILogger<GenerateKeysFunction> _logger;

    public GenerateKeysFunction(
        IPgpService pgpService,
        IBlobStorageService blobStorageService,
        IOptions<PgpSettings> pgpSettings,
        ILogger<GenerateKeysFunction> logger)
    {
        _pgpService = pgpService ?? throw new ArgumentNullException(nameof(pgpService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _pgpSettings = pgpSettings ?? throw new ArgumentNullException(nameof(pgpSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function(nameof(GenerateKeysFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "generate-keys")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating PGP key pair.");

        try
        {
            var settings = _pgpSettings.Value;

            using var publicKeyStream = new MemoryStream();
            using var privateKeyStream = new MemoryStream();

            await _pgpService.GenerateKeyPairAsync(
                publicKeyStream,
                privateKeyStream,
                settings.Identity,
                settings.Passphrase,
                settings.KeyStrength,
                cancellationToken);

            publicKeyStream.Seek(0, SeekOrigin.Begin);
            privateKeyStream.Seek(0, SeekOrigin.Begin);

            await _blobStorageService.UploadBlobAsync(
                ContainerNames.Keys, BlobNames.PublicKey, publicKeyStream, cancellationToken);
            await _blobStorageService.UploadBlobAsync(
                ContainerNames.Keys, BlobNames.PrivateKey, privateKeyStream, cancellationToken);

            _logger.LogInformation(
                "PGP key pair generated and stored in container '{Container}'.",
                ContainerNames.Keys);

            return new OkObjectResult(ApiResponse.Ok("PGP key pair generated and stored successfully."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate PGP key pair.");
            return new ObjectResult(ApiResponse.Fail($"Failed to generate key pair: {ex.Message}"))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
