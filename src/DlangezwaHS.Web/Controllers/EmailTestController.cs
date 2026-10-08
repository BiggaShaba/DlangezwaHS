using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DlangezwaHS.Web.Controllers;

/// <summary>
/// Admin-only controller to test email configuration.
/// Access via: /EmailTest  (only visible in Development or when logged in as Admin)
/// REMOVE THIS CONTROLLER BEFORE GOING LIVE IN PRODUCTION.
/// </summary>
[Authorize(Roles = "Admin")]
public class EmailTestController : Controller
{
    private readonly IEmailService        _email;
    private readonly SmtpOptions          _smtp;
    private readonly ILogger<EmailTestController> _logger;

    public EmailTestController(IEmailService email, SmtpOptions smtp,
        ILogger<EmailTestController> logger)
    {
        _email  = email;
        _smtp   = smtp;
        _logger = logger;
    }

    /// GET /EmailTest
    /// Shows current SMTP config and a form to send a test email.
    public IActionResult Index()
    {
        ViewBag.Host        = _smtp.Host;
        ViewBag.Port        = _smtp.Port;
        ViewBag.UserName    = _smtp.UserName;
        ViewBag.HasPassword = !string.IsNullOrEmpty(_smtp.Password);
        ViewBag.UseSsl      = _smtp.UseSsl;
        ViewBag.FromAddress = _smtp.FromAddress;
        ViewBag.Configured  = !string.IsNullOrEmpty(_smtp.UserName)
                           && !string.IsNullOrEmpty(_smtp.Password)
                           && _smtp.Host != "smtp.mailtrap.io"
                              || (_smtp.Host == "sandbox.smtp.mailtrap.io" && !string.IsNullOrEmpty(_smtp.UserName));
        return View();
    }

    /// POST /EmailTest/Send
    /// Sends a real test email to the address you specify.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(string toEmail)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            TempData["Error"] = "Please enter an email address.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _email.SendAsync(
                toEmail,
                "Test Recipient",
                "Test Email from Dlangezwa High School System",
                $@"<h2 style='color:#0B2A4A;'>Email Test Successful!</h2>
                   <p>This is a test email sent from the <strong>Dlangezwa High School</strong> system.</p>
                   <p><strong>SMTP Host:</strong> {_smtp.Host}<br/>
                   <strong>Port:</strong> {_smtp.Port}<br/>
                   <strong>From:</strong> {_smtp.FromAddress}<br/>
                   <strong>Sent at:</strong> {DateTime.Now:dd MMM yyyy HH:mm:ss}</p>
                   <p>If you received this, email notifications are working correctly.</p>"
            );

            TempData["Success"] = $"Test email sent to {toEmail}. Check your inbox (or Mailtrap inbox).";
            _logger.LogInformation("Test email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Email failed: {ex.Message}";
            _logger.LogError(ex, "Test email failed");
        }

        return RedirectToAction(nameof(Index));
    }
}
