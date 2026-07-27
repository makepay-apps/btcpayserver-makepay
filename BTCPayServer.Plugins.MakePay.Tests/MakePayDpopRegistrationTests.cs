#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MakePay.PaymentHandler;
using BTCPayServer.Plugins.MakePay.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.Tests;

public class MakePayDpopRegistrationTests
{
    [Fact]
    public void DisconnectKeepsTheRegisteredKeyForAProvableReconnect()
    {
        var config = new MakePayPaymentMethodConfig
        {
            AccessToken = "access",
            ClientId = "client",
            DpopJkt = "registered-thumbprint",
            DpopPrivateKeyPem = "registered-private-key",
            RefreshToken = "refresh",
            WebhookSecret = "webhook"
        };

        config.ClearConnectionForReconnect();

        Assert.Null(config.AccessToken);
        Assert.Null(config.ClientId);
        Assert.Null(config.RefreshToken);
        Assert.Null(config.WebhookSecret);
        Assert.Equal("registered-thumbprint", config.DpopJkt);
        Assert.Equal("registered-private-key", config.DpopPrivateKeyPem);
    }

    [Fact]
    public async Task NativeRegistrationIncludesProofBoundToSubmittedKeyAndEndpoint()
    {
        var keyPair = MakePayDpopService.GenerateKeyPair();
        var handler = new RecordingHandler();
        var client = new MakePayApiClient(
            new HttpClient(handler),
            NullLogger<MakePayApiClient>.Instance);
        var config = new MakePayPaymentMethodConfig
        {
            ApiBaseUrl = "https://www.makecrypto.io"
        };

        await client.RegisterNativeInstallation(
            config,
            "https://merchant.example",
            "https://merchant.example/plugins/store/makepay/oauth/callback",
            keyPair.Thumbprint,
            keyPair.PrivateKeyPem,
            null,
            null,
            "2.3.9");

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "https://www.makecrypto.io/oauth/native/installations",
            handler.Request.RequestUri?.ToString());
        Assert.True(handler.Request.Headers.TryGetValues("DPoP", out var values));

        var proof = Assert.Single(values);
        var verified = VerifyProof(proof);
        Assert.Equal("dpop+jwt", verified.Header["typ"]?.Value<string>());
        Assert.Equal("ES256", verified.Header["alg"]?.Value<string>());
        Assert.Equal("POST", verified.Payload["htm"]?.Value<string>());
        Assert.Equal(
            "https://www.makecrypto.io/oauth/native/installations",
            verified.Payload["htu"]?.Value<string>());
        Assert.False(string.IsNullOrWhiteSpace(verified.Payload["jti"]?.Value<string>()));
        Assert.InRange(
            verified.Payload["iat"]?.Value<long>() ?? 0,
            DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
        Assert.Equal(keyPair.Thumbprint, Thumbprint((JObject)verified.Header["jwk"]!));
    }

    [Fact]
    public async Task NativeRegistrationProvesBothKeysDuringRotation()
    {
        var nextKey = MakePayDpopService.GenerateKeyPair();
        var previousKey = MakePayDpopService.GenerateKeyPair();
        var handler = new RecordingHandler();
        var client = new MakePayApiClient(
            new HttpClient(handler),
            NullLogger<MakePayApiClient>.Instance);

        await client.RegisterNativeInstallation(
            new MakePayPaymentMethodConfig(),
            "https://merchant.example",
            "https://merchant.example/plugins/store/makepay/oauth/callback",
            nextKey.Thumbprint,
            nextKey.PrivateKeyPem,
            previousKey.Thumbprint,
            previousKey.PrivateKeyPem,
            "2.3.9");

        Assert.NotNull(handler.Request);
        Assert.True(handler.Request!.Headers.TryGetValues("DPoP", out var nextValues));
        Assert.True(handler.Request.Headers.TryGetValues("DPoP-Previous", out var previousValues));
        Assert.Equal(
            nextKey.Thumbprint,
            Thumbprint((JObject)VerifyProof(Assert.Single(nextValues)).Header["jwk"]!));
        Assert.Equal(
            previousKey.Thumbprint,
            Thumbprint((JObject)VerifyProof(Assert.Single(previousValues)).Header["jwk"]!));
    }

    private static (JObject Header, JObject Payload) VerifyProof(string proof)
    {
        var parts = proof.Split('.');
        Assert.Equal(3, parts.Length);
        var header = JObject.Parse(
            Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
        var payload = JObject.Parse(
            Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
        var jwk = (JObject)header["jwk"]!;
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64UrlDecode(jwk["x"]!.Value<string>()!),
                Y = Base64UrlDecode(jwk["y"]!.Value<string>()!)
            }
        };
        using var publicKey = ECDsa.Create(parameters);
        Assert.True(publicKey.VerifyData(
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return (header, payload);
    }

    private static string Thumbprint(JObject publicJwk)
    {
        var canonical = new JObject
        {
            ["crv"] = publicJwk["crv"],
            ["kty"] = publicJwk["kty"],
            ["x"] = publicJwk["x"],
            ["y"] = publicJwk["y"]
        }.ToString(Newtonsoft.Json.Formatting.None);
        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"client_id":"mco_app_test"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
