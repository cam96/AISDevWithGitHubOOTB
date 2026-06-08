# Plan: Azure Functions PGP Encryption App

## TL;DR
Create a .NET 9 Azure Functions v4 isolated worker app with three HTTP-triggered endpoints for PGP key generation, file encryption, and file decryption using Azure Blob Storage. Services are injected via DI, tested with NUnit 4 + Moq for 100% coverage.

## Technology Stack
- .NET 9 (GA, supported until Nov 2026 — latest fully supported LTS-compatible version in Azure Functions)
- Azure Functions v4 isolated worker with ASP.NET Core HTTP integration
- `PgpCore` 7.1.0 (wraps BouncyCastle, clean async API, AES-256)
- `Azure.Storage.Blobs` 12.x for blob operations
- NUnit 4.6.1 + Moq 4.20.72 + coverlet.collector for testing

## Project Structure
```
AISDevWithGitHub/
├── PgpFunctionSolution.sln
├── src/
│   └── PgpFunctionApp/
│       ├── PgpFunctionApp.csproj
│       ├── Program.cs
│       ├── host.json
│       ├── appsettings.json
│       ├── local.settings.json
│       ├── Functions/
│       │   ├── GenerateKeysFunction.cs
│       │   ├── EncryptFilesFunction.cs
│       │   └── DecryptFilesFunction.cs
│       ├── Services/
│       │   ├── IBlobStorageService.cs
│       │   ├── BlobStorageService.cs
│       │   ├── IPgpService.cs
│       │   └── PgpService.cs
│       └── Models/
│           ├── ApiResponse.cs
│           └── PgpSettings.cs
└── tests/
    └── PgpFunctionApp.Tests/
        ├── PgpFunctionApp.Tests.csproj
        ├── Functions/
        │   ├── GenerateKeysFunctionTests.cs
        │   ├── EncryptFilesFunctionTests.cs
        │   └── DecryptFilesFunctionTests.cs
        └── Services/
            ├── BlobStorageServiceTests.cs
            └── PgpServiceTests.cs
```

## Steps

### Phase 1 — Project Scaffolding
1. Create solution file and project directories
2. Create `PgpFunctionApp.csproj` targeting net9.0 with all required packages
3. Create `PgpFunctionApp.Tests.csproj` targeting net9.0 with NUnit/Moq packages
4. Add both projects to solution

### Phase 2 — Models and Settings
5. Create `Models/PgpSettings.cs` — strongly-typed options class (Passphrase, Identity)
6. Create `Models/ApiResponse.cs` — standardized JSON response { Success, Message, Count? }

### Phase 3 — Services (interfaces + implementations)
7. Create `Services/IBlobStorageService.cs` — interface: ListBlobsAsync, DownloadBlobAsync, UploadBlobAsync, BlobExistsAsync
8. Create `Services/BlobStorageService.cs` — wraps BlobServiceClient injected via DI; calls CreateIfNotExistsAsync before operations
9. Create `Services/IPgpService.cs` — interface: GenerateKeyPairAsync, EncryptAsync, DecryptAsync
10. Create `Services/PgpService.cs` — wraps PgpCore.PGP; enforces AES-256 algorithm override

### Phase 4 — Functions
11. Create `Functions/GenerateKeysFunction.cs` — POST /api/generate-keys; generates RSA-4096 key pair; stores public.key + private.key to "keys" container
12. Create `Functions/EncryptFilesFunction.cs` — POST /api/encrypt-files; lists all blobs in "encryptinbox"; encrypts each with public.key; writes to "encryptedoutbox" with .pgp appended
13. Create `Functions/DecryptFilesFunction.cs` — POST /api/decrypt-files; lists .pgp files in "decryptinbox"; decrypts with private.key; writes to "decryptoutbox" with .pgp removed

### Phase 5 — Configuration
14. Create `Program.cs` — FunctionsApplication.CreateBuilder, ConfigureFunctionsWebApplication, AddAzureClients (BlobServiceClient), Configure<PgpSettings>, AddSingleton services
15. Create `host.json` — v2 schema, logging config
16. Create `appsettings.json` — PgpSettings section with empty defaults
17. Create `local.settings.json` — AzureWebJobsStorage (Azurite), FUNCTIONS_WORKER_RUNTIME

### Phase 6 — Unit Tests
18. Create `Services/BlobStorageServiceTests.cs` — mock BlobServiceClient + BlobContainerClient + BlobClient; test all methods + branches
19. Create `Services/PgpServiceTests.cs` — use real in-memory MemoryStreams (PgpCore is pure .NET, no network); test GenerateKeyPair, Encrypt, Decrypt, round-trip
20. Create `Functions/GenerateKeysFunctionTests.cs` — mock IPgpService + IBlobStorageService; test success, PGP throws, Blob throws
21. Create `Functions/EncryptFilesFunctionTests.cs` — mock services; test files present, empty inbox, key missing, service throws
22. Create `Functions/DecryptFilesFunctionTests.cs` — mock services; test files present, empty inbox, key missing, service throws

## Relevant Files
- `src/PgpFunctionApp/Program.cs` — DI registration hub, single source of truth for service wiring
- `src/PgpFunctionApp/Services/IPgpService.cs` — contract tested in functions via Moq
- `src/PgpFunctionApp/Services/PgpService.cs` — real PgpCore calls; use `SymmetricKeyAlgorithmTag.Aes256`
- `src/PgpFunctionApp/Services/IBlobStorageService.cs` — contract tested in functions via Moq
- `src/PgpFunctionApp/Services/BlobStorageService.cs` — Azure SDK calls; `GetBlobContainerClient()` is virtual → mockable

## Key Design Decisions
- **Passphrase storage**: `PgpSettings:Passphrase` in appsettings.json / app settings; local.settings.json has placeholder; note to use Key Vault reference in production
- **Key naming**: `public.key` and `private.key` in "keys" container
- **Encryption file naming**: add `.pgp` extension on encrypt; strip `.pgp` extension on decrypt
- **PGP algorithm override**: Always set `SymmetricKeyAlgorithmTag.Aes256` (PgpCore default is TripleDes which is NIST-deprecated)
- **Container auto-creation**: BlobStorageService calls CreateIfNotExistsAsync on each operation
- **HTTP integration**: ASP.NET Core model (HttpRequest → IActionResult) not HttpRequestData/HttpResponseData
- **Service lifetimes**: BlobStorageService Singleton (wraps SDK singleton), PgpService Singleton (stateless)

## Verification
1. `dotnet build PgpFunctionSolution.sln` — zero errors, zero warnings
2. `dotnet test --collect:"XPlat Code Coverage"` — all tests pass
3. `reportgenerator` report shows 100% line and branch coverage
4. Manual: start Azurite emulator, run `func start`, call each endpoint with `curl` or HTTP client
5. Confirm key files appear in "keys" container, encrypted file in "encryptedoutbox", decrypted file in "decryptoutbox"

## Scope Boundaries
- Included: HTTP triggers, blob storage, PGP key gen/encrypt/decrypt, unit tests
- Excluded: Azure deployment (azd/bicep), Key Vault integration (noted as production recommendation), file type filtering on encryptinbox (all blobs = plain text per spec), timer-based triggers, message queuing
