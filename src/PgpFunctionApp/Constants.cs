namespace PgpFunctionApp;

internal static class ContainerNames
{
    internal const string Keys = "keys";
    internal const string EncryptInbox = "encryptinbox";
    internal const string EncryptedOutbox = "encryptedoutbox";
    internal const string DecryptInbox = "decryptinbox";
    internal const string DecryptOutbox = "decryptoutbox";
}

internal static class BlobNames
{
    internal const string PublicKey = "public.key";
    internal const string PrivateKey = "private.key";
}
