using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MsrEdu.Api.Data;

namespace MsrEdu.Api.Services;

/// <summary>
/// Payments stay gateway-agnostic: each provider (bKash, Nagad, SSLCommerz, a card processor...) is one
/// <see cref="IPaymentGateway"/>. Only the sandbox exists until the owner picks a real provider.
/// </summary>
public class PaymentOptions
{
    /// <summary>Key of the gateway used for new checkouts, e.g. "sandbox". Empty = payments off.</summary>
    public string? Gateway { get; set; }
    public SandboxOptions Sandbox { get; set; } = new();

    public class SandboxOptions
    {
        /// <summary>Never honoured in Production.</summary>
        public bool Enabled { get; set; }
        /// <summary>HMAC key for sandbox webhooks; a random per-process key when empty.</summary>
        public string? Secret { get; set; }
    }
}

public record PaymentRequest(Guid TransactionId, string OrderReference, long AmountMinor, string Currency, string Description, string CustomerEmail);
public record GatewayPayment(string ProviderTxnId, string RedirectUrl);

/// <summary>A webhook the gateway has authenticated. <see cref="Outcome"/> is Successful, Failed or Cancelled.</summary>
public record GatewayEvent(string EventId, string EventType, string ProviderTxnId, PaymentState Outcome,
    long AmountMinor, string Currency, string? FailureReason, JsonDocument Redacted);

public class GatewayException(string message) : Exception(message);

public interface IPaymentGateway
{
    string Key { get; }
    string DisplayName { get; }
    PaymentMethod Method { get; }
    bool IsSandbox { get; }

    /// <summary>Registers the payment with the provider and returns where to send the learner.</summary>
    Task<GatewayPayment> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Authenticates a webhook (signature and, where the provider offers one, a server-to-server validation call).
    /// Returns null when it can't be trusted. The browser redirect back from the gateway is never used as proof.
    /// </summary>
    Task<GatewayEvent?> VerifyWebhookAsync(string body, IHeaderDictionary headers, CancellationToken ct = default);

    /// <summary>Returns the provider's refund id. Throws <see cref="GatewayException"/> when the provider refuses.</summary>
    Task<string> RefundAsync(string providerTxnId, long amountMinor, string currency, string reason, CancellationToken ct = default);
}

public sealed class PaymentGateways(IEnumerable<IPaymentGateway> gateways, IOptions<PaymentOptions> options)
{
    private readonly Dictionary<string, IPaymentGateway> _byKey = gateways.ToDictionary(g => g.Key);

    /// <summary>The gateway new checkouts use, or null when payments are not set up.</summary>
    public IPaymentGateway? Default => options.Value.Gateway is { Length: > 0 } key ? Find(key) : null;

    public IPaymentGateway? Find(string key) => _byKey.GetValueOrDefault(key);

    private static readonly string[] SensitiveKeys = ["card", "pan", "cvv", "token", "secret", "signature", "password", "pin", "otp"];

    /// <summary>Webhook bodies are stored for audit with anything card- or credential-like removed.</summary>
    public static JsonDocument Redact(string body)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (JsonException) { return JsonSerializer.SerializeToDocument(new { unparsed = true, length = body.Length }); }
        Strip(node);
        return JsonSerializer.SerializeToDocument(node);

        static void Strip(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                foreach (var key in o.Select(kv => kv.Key).ToList())
                    if (SensitiveKeys.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase))) o[key] = "[redacted]";
                    else Strip(o[key]);
            }
            else if (n is JsonArray a) foreach (var item in a) Strip(item);
        }
    }
}

/// <summary>
/// A fake provider for development and tests: the learner "pays" on our own page, which produces an
/// HMAC-signed webhook exactly like a real gateway would. Disabled in Production.
/// </summary>
public sealed class SandboxGateway(IOptions<PaymentOptions> options) : IPaymentGateway
{
    public const string SignatureHeader = "X-Sandbox-Signature";
    private readonly byte[] _secret = options.Value.Sandbox.Secret is { Length: > 0 } s
        ? Encoding.UTF8.GetBytes(s) : RandomNumberGenerator.GetBytes(32);

    public string Key => "sandbox";
    public string DisplayName => "Sandbox (test payments)";
    public PaymentMethod Method => PaymentMethod.Other;
    public bool IsSandbox => true;

    public Task<GatewayPayment> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var id = $"sbx_{request.TransactionId:N}";
        return Task.FromResult(new GatewayPayment(id, $"/checkout/sandbox/{id}"));
    }

    /// <summary>What the sandbox "provider" posts to our webhook when the learner pays, fails or cancels.</summary>
    public (string Body, string Signature) SignedEvent(string providerTxnId, string outcome, long amountMinor, string currency)
    {
        var body = JsonSerializer.Serialize(new
        {
            event_id = $"evt_{Guid.NewGuid():N}",
            type = $"payment.{outcome}",
            payment_id = providerTxnId,
            amount_minor = amountMinor,
            currency,
            occurred_at = DateTimeOffset.UtcNow,
        });
        return (body, Sign(body));
    }

    public string Sign(string body) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(body)));

    public Task<GatewayEvent?> VerifyWebhookAsync(string body, IHeaderDictionary headers, CancellationToken ct = default)
    {
        var given = headers[SignatureHeader].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(Sign(body))))
            return Task.FromResult<GatewayEvent?>(null);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString()!;
        var outcome = type switch
        {
            "payment.succeeded" => PaymentState.Successful,
            "payment.failed" => PaymentState.Failed,
            "payment.cancelled" => PaymentState.Cancelled,
            _ => throw new FormatException($"unknown sandbox event {type}"),
        };
        return Task.FromResult<GatewayEvent?>(new GatewayEvent(
            root.GetProperty("event_id").GetString()!, type, root.GetProperty("payment_id").GetString()!, outcome,
            root.GetProperty("amount_minor").GetInt64(), root.GetProperty("currency").GetString()!,
            outcome == PaymentState.Failed ? "Declined in the sandbox" : null, PaymentGateways.Redact(body)));
    }

    public Task<string> RefundAsync(string providerTxnId, long amountMinor, string currency, string reason, CancellationToken ct = default) =>
        Task.FromResult($"sbx_rf_{Guid.NewGuid():N}");
}
