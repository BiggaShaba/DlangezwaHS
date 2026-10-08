using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface IEmailService
{
    Task SendApplicationReceivedAsync(LearnerApplication application);
    Task SendApplicationApprovedAsync(LearnerApplication application);
    Task SendApplicationRejectedAsync(LearnerApplication application);
    Task SendPaymentConfirmationAsync(Payment payment, byte[]? pdfAttachment = null, string? attachmentName = null);
    Task SendRegistrationProofAsync(Enrollment enrollment, byte[] pdfBytes, string attachmentName);
    Task SendTeacherCredentialsAsync(string toEmail, string teacherName, string password);
    Task SendHousemasterCredentialsAsync(string toEmail, string housemasterName, string password);
    Task SendKitchenStaffCredentialsAsync(string toEmail, string staffName, string password);
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, byte[]? attachment = null, string? attachmentName = null);
    Task SendCalendarNotificationAsync(string toEmail, string toName, string eventTitle, string eventType, DateTime eventDate, string? description);

}

// ─────────────────────────────────────────────────────────────────────────────
// CONFIGURATION (bound from appsettings.json)
// ─────────────────────────────────────────────────────────────────────────────

public class SmtpOptions
{
    public const string Section = "Smtp";
    public string Host        { get; set; } = "smtp.mailtrap.io";
    public int    Port        { get; set; } = 587;
    public string UserName    { get; set; } = "";
    public string Password    { get; set; } = "";
    public bool   UseSsl      { get; set; } = false;
    public string FromAddress { get; set; } = "noreply@dlangezwa.edu.za";
    public string FromName    { get; set; } = "Dlangezwa High School";
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class EmailService : IEmailService
{
    private readonly SmtpOptions _opts;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EmailService> _logger;
    private readonly EmailQueue _queue;

    public EmailService(SmtpOptions opts, ApplicationDbContext db, ILogger<EmailService> logger, EmailQueue queue)
    {
        _opts = opts;
        _queue = queue;
        _db = db;
        _logger = logger;
    }

    private async Task<EmailTemplate?> GetTemplateAsync(string key)
        => await _db.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == key);

    private static string FillTemplate(string template, Dictionary<string, string> tokens)
    {
        foreach (var (k, v) in tokens)
            template = template.Replace($"{{{{{k}}}}}", v);
        return template;
    }

    public async Task SendApplicationReceivedAsync(LearnerApplication application)
    {
        var tpl = await GetTemplateAsync("ApplicationReceived");
        if (tpl is null) return;
        var tokens = new Dictionary<string, string>
        {
            ["ParentName"] = application.Parent?.FullName ?? "Parent/Guardian",
            ["LearnerName"] = application.Learner?.FullName ?? "",
            ["ApplicationId"] = application.Id.ToString()
        };
        var body = FillTemplate(tpl.Body, tokens);
        var subject = FillTemplate(tpl.Subject, tokens);
        await SendAsync(application.Parent!.Email!, application.Parent.FullName, subject, body);
    }

    public async Task SendApplicationApprovedAsync(LearnerApplication application)
    {
        var tpl = await GetTemplateAsync("ApplicationApproved");
        if (tpl is null) return;
        var tokens = new Dictionary<string, string>
        {
            ["ParentName"] = application.Parent?.FullName ?? "Parent/Guardian",
            ["LearnerName"] = application.Learner?.FullName ?? ""
        };
        await SendAsync(application.Parent!.Email!, application.Parent.FullName,
            FillTemplate(tpl.Subject, tokens), FillTemplate(tpl.Body, tokens));
    }

    public async Task SendApplicationRejectedAsync(LearnerApplication application)
    {
        var tpl = await GetTemplateAsync("ApplicationRejected");
        if (tpl is null) return;
        var tokens = new Dictionary<string, string>
        {
            ["ParentName"] = application.Parent?.FullName ?? "Parent/Guardian",
            ["LearnerName"] = application.Learner?.FullName ?? "",
            ["RejectionReason"] = application.RejectionReason ?? "No reason provided."
        };
        await SendAsync(application.Parent!.Email!, application.Parent.FullName,
            FillTemplate(tpl.Subject, tokens), FillTemplate(tpl.Body, tokens));
    }

    public async Task SendPaymentConfirmationAsync(Payment payment, byte[]? pdfAttachment = null, string? attachmentName = null)
    {
        if (payment.Parent?.Email is null) return;
        var tpl = await GetTemplateAsync("PaymentConfirmation");
        if (tpl is null) return;
        var tokens = new Dictionary<string, string>
        {
            ["ParentName"] = payment.Parent?.FullName ?? "Parent/Guardian",
            ["LearnerName"] = payment.Learner?.FullName ?? "",
            ["Amount"] = payment.Amount.ToString("N2"),
            ["PaymentType"] = payment.Type.ToString(),
            ["PaymentRef"] = payment.ProviderRef ?? payment.BankRef ?? payment.Id.ToString()
        };
        await SendAsync(payment.Parent!.Email, payment.Parent.FullName,
            FillTemplate(tpl.Subject, tokens), FillTemplate(tpl.Body, tokens),
            pdfAttachment, attachmentName);
    }

    public async Task SendRegistrationProofAsync(Enrollment enrollment, byte[] pdfBytes, string attachmentName)
    {
        var parent = enrollment.Learner?.Parent;
        if (parent?.Email is null) return;
        var tpl = await GetTemplateAsync("RegistrationProof");
        if (tpl is null) return;
        var subjects = string.Join(", ", enrollment.EnrollmentSubjects.Select(es => es.Subject?.Name ?? ""));
        var tokens = new Dictionary<string, string>
        {
            ["ParentName"] = parent.FullName,
            ["LearnerName"] = enrollment.Learner?.FullName ?? "",
            ["ClassName"] = enrollment.Class?.DisplayName ?? "",
            ["Subjects"] = subjects
        };
        await SendAsync(parent.Email, parent.FullName,
            FillTemplate(tpl.Subject, tokens), FillTemplate(tpl.Body, tokens),
            pdfBytes, attachmentName);
    }

    public async Task SendTeacherCredentialsAsync(string toEmail, string teacherName, string password)
    {
        var tpl = await GetTemplateAsync("TeacherWelcome");
        string subject, body;

        if (tpl is not null)
        {
            var tokens = new Dictionary<string, string>
            {
                ["TeacherName"] = teacherName,
                ["Email"] = toEmail,
                ["Password"] = password
            };
            subject = FillTemplate(tpl.Subject, tokens);
            body = FillTemplate(tpl.Body, tokens);
        }
        else
        {
            // Fallback if template not seeded yet
            subject = "Your Dlangezwa High School Teacher Account";
            body = $@"<p>Dear {teacherName},</p>
<p>Your teacher account has been created on the Dlangezwa High School portal.</p>
<p><strong>Email:</strong> {toEmail}<br/>
<strong>Temporary Password:</strong> <code>{password}</code></p>
<p>Please log in at <strong>/Account/Login</strong> and change your password on first use.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>";
        }

        await SendAsync(toEmail, teacherName, subject, body);
    }

    public async Task SendHousemasterCredentialsAsync(string toEmail, string housemasterName, string password)
    {
        var tpl = await GetTemplateAsync("HousemasterWelcome");
        string subject, body;

        if (tpl is not null)
        {
            var tokens = new Dictionary<string, string>
            {
                ["HousemasterName"] = housemasterName,
                ["Email"] = toEmail,
                ["Password"] = password
            };
            subject = FillTemplate(tpl.Subject, tokens);
            body = FillTemplate(tpl.Body, tokens);
        }
        else
        {
            subject = "Your Dlangezwa High School Housemaster Account";
            body = $@"<p>Dear {housemasterName},</p>
<p>Your housemaster account has been created on the Dlangezwa High School portal.</p>
<p><strong>Email:</strong> {toEmail}<br/>
<strong>Temporary Password:</strong> <code>{password}</code></p>
<p>Please log in at <strong>/Account/Login</strong> and change your password on first use.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>";
        }

        await SendAsync(toEmail, housemasterName, subject, body);
    }

    public async Task SendKitchenStaffCredentialsAsync(string toEmail, string staffName, string password)
    {
        var tpl = await GetTemplateAsync("KitchenStaffWelcome");
        string subject, body;

        if (tpl is not null)
        {
            var tokens = new Dictionary<string, string>
            {
                ["StaffName"] = staffName,
                ["Email"] = toEmail,
                ["Password"] = password
            };
            subject = FillTemplate(tpl.Subject, tokens);
            body = FillTemplate(tpl.Body, tokens);
        }
        else
        {
            subject = "Your Dlangezwa High School Kitchen Staff Account";
            body = $@"<p>Dear {staffName},</p>
<p>Your kitchen staff account has been created on the Dlangezwa High School portal.</p>
<p><strong>Email:</strong> {toEmail}<br/>
<strong>Temporary Password:</strong> <code>{password}</code></p>
<p>Please log in at <strong>/Account/Login</strong> and change your password on first use.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>";
        }

        await SendAsync(toEmail, staffName, subject, body);
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody,
        byte[]? attachment = null, string? attachmentName = null)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment is not null && attachmentName is not null)
                builder.Attachments.Add(attachmentName, attachment, new ContentType("application", "pdf"));

            message.Body = builder.ToMessageBody();

            // Sent by EmailSenderJob in the background so the caller doesn't wait on SMTP
            _queue.Enqueue(message);
            await Task.CompletedTask;

            _logger.LogInformation("Email queued for {Email} – Subject: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue email to {Email}", toEmail);
            // Don't throw — email failure should not break the main workflow
        }
    }

    public async Task SendCalendarNotificationAsync(
    string toEmail, string toName, string eventTitle,
    string eventType, DateTime eventDate, string? description)
    {
        var tpl = await GetTemplateAsync("CalendarNotification");

        string body;
        if (tpl != null)
        {
            body = tpl.Body
                .Replace("{{RecipientName}}", toName)
                .Replace("{{EventTitle}}", eventTitle)
                .Replace("{{EventType}}", eventType)
                .Replace("{{EventDate}}", eventDate.ToString("dddd, dd MMMM yyyy"))
                .Replace("{{Description}}", description ?? string.Empty);
        }
        else
        {
            // Fallback hardcoded template
            body = $@"
        <div style='font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:auto;border:1px solid #e5e7eb;border-radius:8px;overflow:hidden'>
          <div style='background:#1e3a5f;padding:24px 32px'>
            <h1 style='color:#fff;margin:0;font-size:20px'>Dlangezwa High School</h1>
            <p style='color:#93c5fd;margin:4px 0 0;font-size:13px'>School Calendar Notification</p>
          </div>
          <div style='padding:32px'>
            <p style='color:#374151'>Dear {toName},</p>
            <p style='color:#374151'>A new <strong>{eventType}</strong> has been added to the school calendar:</p>
            <div style='background:#f3f4f6;border-left:4px solid #1e3a5f;border-radius:4px;padding:16px 20px;margin:20px 0'>
              <p style='margin:0 0 4px;font-size:18px;font-weight:700;color:#111827'>{eventTitle}</p>
              <p style='margin:0;color:#6b7280;font-size:14px'>📅 {eventDate:dddd, dd MMMM yyyy}</p>
              {(string.IsNullOrEmpty(description) ? "" : $"<p style='margin:12px 0 0;color:#374151;font-size:14px'>{description}</p>")}
            </div>
            <p style='color:#374151'>Log in to the portal to view the full calendar.</p>
          </div>
          <div style='background:#f9fafb;padding:16px 32px;border-top:1px solid #e5e7eb'>
            <p style='color:#9ca3af;font-size:12px;margin:0'>Dlangezwa High School &mdash; Administration System</p>
          </div>
        </div>";
        }

        string subject = $"[{eventType}] {eventTitle} — {eventDate:dd MMM yyyy}";
        await SendAsync(toEmail, toName, subject, body);
    }


}
