using System.Formats.Asn1;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workslip.Application.Notifications;
using Workslip.Infrastructure.Configuration;

namespace Workslip.Infrastructure.Notifications;

public sealed class VapidKeyMaterial : IVapidPublicKeyProvider
{
    private const string P256ObjectIdentifier = "1.2.840.10045.3.1.7";

    public VapidKeyMaterial(
        IOptions<VapidOptions> options,
        IHostEnvironment? environment = null,
        ILogger<VapidKeyMaterial>? logger = null)
    {
        var configured = options.Value;
        byte[] privateKeyBytes;

        if (string.IsNullOrWhiteSpace(configured.PrivateKey) && environment?.IsDevelopment() == true)
        {
            privateKeyBytes = CreateDevelopmentPrivateKey();
            logger?.LogWarning(
                "VAPID private key is not configured. Using an ephemeral Development key; browser push subscriptions will be reconciled after API restart.");
        }
        else
        {
            privateKeyBytes = DecodeBase64Url(configured.PrivateKey, "Vapid:PrivateKey");
        }

        if (privateKeyBytes.Length != 32)
        {
            throw new InvalidOperationException("Vapid:PrivateKey must be a 32-byte P-256 private key.");
        }

        PublicKey = EncodeBase64Url(DerivePublicKey(privateKeyBytes));
        PrivateKey = EncodeBase64Url(privateKeyBytes);
        Subject = string.IsNullOrWhiteSpace(configured.Subject)
            ? "mailto:push@workslip.app"
            : configured.Subject.Trim();
    }

    public string PublicKey { get; }
    public string PrivateKey { get; }
    public string Subject { get; }

    private static byte[] CreateDevelopmentPrivateKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
        return parameters.D is { Length: 32 } privateKey
            ? privateKey
            : throw new InvalidOperationException("Unable to generate an ephemeral Development VAPID key.");
    }

    private static byte[] DerivePublicKey(byte[] privateKey)
    {
        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey
            });
        }
        catch (CryptographicException)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteInteger(1);
            writer.WriteOctetString(privateKey);

            var parametersTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            writer.PushSequence(parametersTag);
            writer.WriteObjectIdentifier(P256ObjectIdentifier);
            writer.PopSequence(parametersTag);
            writer.PopSequence();

            ecdsa.ImportECPrivateKey(writer.Encode(), out _);
        }

        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
        if (parameters.Q.X is null || parameters.Q.Y is null)
        {
            throw new InvalidOperationException("Unable to derive the VAPID public key.");
        }

        var publicKey = new byte[1 + parameters.Q.X.Length + parameters.Q.Y.Length];
        publicKey[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X, 0, publicKey, 1, parameters.Q.X.Length);
        Buffer.BlockCopy(parameters.Q.Y, 0, publicKey, 1 + parameters.Q.X.Length, parameters.Q.Y.Length);
        return publicKey;
    }

    private static byte[] DecodeBase64Url(string value, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{configurationKey} is required for push notifications.");
        }

        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{configurationKey} is not valid base64url.", exception);
        }
    }

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
