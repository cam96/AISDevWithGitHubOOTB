using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PgpFunctionApp.Models;
using PgpFunctionApp.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Register Azure Blob Storage client.
// In production, replace "AzureWebJobsStorage" with a dedicated connection string
// and store secrets in Azure Key Vault via a Key Vault reference.
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddBlobServiceClient(builder.Configuration["AzureWebJobsStorage"]);
});

// Strongly-typed PGP settings bound from configuration.
builder.Services.Configure<PgpSettings>(
    builder.Configuration.GetSection(PgpSettings.SectionName));

// Application services.
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<IPgpService, PgpService>();

builder.Build().Run();
