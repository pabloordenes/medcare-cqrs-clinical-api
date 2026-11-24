using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Infrastructure.Configuration;
using MedCareOS.Infrastructure.Notifications.Webhooks;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.UnitTests.Notifications;

public class WebhookSignatureVerifierTests
{
    [Fact]
    public void MetaVerifier_ValidSignaturePasses_AndTamperedOrMissingFailsClosed()
    {
        var appSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var verifier = new MetaWebhookSignatureVerifier(
            Options.Create(new MetaWhatsAppOptions { AppSecret = appSecret }));

        var body = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\",\"entry\":[]}");
        var validSig = ComputeMetaSignature(appSecret, body);

        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = validSig },
            DateTimeOffset.UtcNow)).Should().BeTrue();

        // Tampered body
        var tamperedBody = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\",\"entry\":[1]}");
        verifier.Verify(new WebhookSignatureContext(
            tamperedBody,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = validSig },
            DateTimeOffset.UtcNow)).Should().BeFalse();

        // Wrong secret
        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = ComputeMetaSignature("wrong", body) },
            DateTimeOffset.UtcNow)).Should().BeFalse();

        // Missing sha256= prefix
        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = validSig.Replace("sha256=", "") },
            DateTimeOffset.UtcNow)).Should().BeFalse();

        // Empty configured secret fails closed
        var unconfigured = new MetaWebhookSignatureVerifier(Options.Create(new MetaWhatsAppOptions { AppSecret = "" }));
        unconfigured.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = validSig },
            DateTimeOffset.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public void ResendSvixVerifier_ValidSignaturePasses_AndExpiredOrTamperedFails()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var webhookSecret = "whsec_" + Convert.ToBase64String(keyBytes);

        var verifier = new ResendWebhookSignatureVerifier(
            Options.Create(new ResendOptions { WebhookSecret = webhookSecret }));

        var now = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        var svixId = "msg_2abc123";
        var svixTs = now.ToUnixTimeSeconds().ToString();
        var body = Encoding.UTF8.GetBytes("{\"type\":\"email.delivered\",\"data\":{\"email_id\":\"re_1\"}}");
        var validSig = ComputeSvixSignature(keyBytes, svixId, svixTs, body);

        // Single valid signature
        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["svix-id"] = svixId,
                ["svix-timestamp"] = svixTs,
                ["svix-signature"] = validSig
            },
            now.AddSeconds(30))).Should().BeTrue();

        // Multiple rotated signatures where second is valid
        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["svix-id"] = svixId,
                ["svix-timestamp"] = svixTs,
                ["svix-signature"] = $"v1,aW52YWxpZA== {validSig}"
            },
            now)).Should().BeTrue();

        // Tampered body
        verifier.Verify(new WebhookSignatureContext(
            Encoding.UTF8.GetBytes("{\"type\":\"email.bounced\"}"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["svix-id"] = svixId,
                ["svix-timestamp"] = svixTs,
                ["svix-signature"] = validSig
            },
            now)).Should().BeFalse();

        // Expired (> 300s old)
        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["svix-id"] = svixId,
                ["svix-timestamp"] = svixTs,
                ["svix-signature"] = validSig
            },
            now.AddSeconds(301))).Should().BeFalse();

        // Future skewed (> 300s ahead)
        verifier.Verify(new WebhookSignatureContext(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["svix-id"] = svixId,
                ["svix-timestamp"] = svixTs,
                ["svix-signature"] = validSig
            },
            now.AddSeconds(-301))).Should().BeFalse();
    }

    private static string ComputeMetaSignature(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static string ComputeSvixSignature(byte[] keyBytes, string svixId, string svixTimestamp, byte[] body)
    {
        var payload = $"{svixId}.{svixTimestamp}.{Encoding.UTF8.GetString(body)}";
        using var hmac = new HMACSHA256(keyBytes);
        return "v1," + Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}
