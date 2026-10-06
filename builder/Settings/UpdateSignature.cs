using System;
using System.Security.Cryptography;
using System.Text;

namespace builder.Settings;

/// <summary>Verifies release manifests against the publisher's pinned ECDSA public key.</summary>
internal static class UpdateSignature
{
    // Replace this once with the public PEM produced by New-UpdateSigningKey.ps1.
    // Only the public key belongs in source control; keep the private PEM offline.
    private const string PublicKeyPem = "";

    internal static bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKeyPem);

    internal static string Payload(string version, string url, string sha256, string notes) =>
        $"{version}\n{url}\n{sha256.Trim().ToLowerInvariant()}\n{notes}";

    internal static bool Verify(string version, string url, string sha256, string notes, string signatureBase64)
    {
        if (!IsConfigured) return false;
        try
        {
            byte[] signature = Convert.FromBase64String(signatureBase64);
            byte[] payload = Encoding.UTF8.GetBytes(Payload(version, url, sha256, notes));
            using var key = ECDsa.Create();
            key.ImportFromPem(PublicKeyPem);
            return key.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }
}
