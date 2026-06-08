namespace PgpFunctionApp.Models;

public sealed class PgpSettings
{
    public const string SectionName = "PgpSettings";

    public string Passphrase { get; init; } = string.Empty;
    public string Identity { get; init; } = "pgp@example.com";
    public int KeyStrength { get; init; } = 4096;
}
