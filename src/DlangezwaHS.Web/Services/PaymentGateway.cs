namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ABSTRACTION
// ─────────────────────────────────────────────────────────────────────────────

public interface IPaymentGateway
{
    string ProviderName { get; }
    Task<PaymentInitResult> InitiatePaymentAsync(PaymentRequest request);
    Task<PaymentVerifyResult> VerifyPaymentAsync(string providerRef);
}

public record PaymentRequest(
    string  MerchantRef,
    decimal Amount,
    string  Currency,
    string  Description,
    string  CustomerEmail,
    string  CustomerName,
    string  ReturnUrl,
    string  CancelUrl
);

public record PaymentInitResult(
    bool    Success,
    string? ProviderRef,       // transaction / checkout ID from gateway
    string? RedirectUrl,       // URL to send user to for payment
    string? ErrorMessage
);

public record PaymentVerifyResult(
    bool    Success,
    string? Status,            // "COMPLETE", "FAILED", etc.
    string? ProviderRef,
    string? ErrorMessage
);

// ─────────────────────────────────────────────────────────────────────────────
// MOCK / SANDBOX IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class MockPaymentGateway : IPaymentGateway
{
    private readonly MockPaymentOptions            _opts;
    private readonly ILogger<MockPaymentGateway>   _logger;

    public MockPaymentGateway(MockPaymentOptions opts, ILogger<MockPaymentGateway> logger)
    {
        _opts   = opts;
        _logger = logger;
    }

    public string ProviderName => "Mock / Sandbox";

    public Task<PaymentInitResult> InitiatePaymentAsync(PaymentRequest request)
    {
        var providerRef = $"MOCK-{Guid.NewGuid():N}"[..20].ToUpper();
        _logger.LogInformation("[MOCK PAYMENT] Initiated {Ref} for R{Amount} – {Desc}",
            providerRef, request.Amount, request.Description);

        // Simulate a redirect URL (in real gateway this would be the checkout page)
        var result = new PaymentInitResult(
            Success:      true,
            ProviderRef:  providerRef,
            RedirectUrl:  $"{request.ReturnUrl}?ref={providerRef}&status=success",
            ErrorMessage: null
        );
        return Task.FromResult(result);
    }

    public Task<PaymentVerifyResult> VerifyPaymentAsync(string providerRef)
    {
        // In sandbox mode, always mark as successful unless ref starts with "FAIL"
        bool success = !providerRef.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation("[MOCK PAYMENT] Verify {Ref} → {Status}", providerRef, success ? "COMPLETE" : "FAILED");
        return Task.FromResult(new PaymentVerifyResult(
            Success:      success,
            Status:       success ? "COMPLETE" : "FAILED",
            ProviderRef:  providerRef,
            ErrorMessage: success ? null : "Payment declined (mock)"
        ));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// OPTIONS  (bound from appsettings / Key Vault)
// ─────────────────────────────────────────────────────────────────────────────

public class MockPaymentOptions
{
    public const string Section = "Payment:Mock";
    public bool   SandboxMode { get; set; } = true;
    // Add real provider keys here if swapping to real gateway
    // public string ApiKey    { get; set; } = "";
    // public string MerchantId { get; set; } = "";
}
