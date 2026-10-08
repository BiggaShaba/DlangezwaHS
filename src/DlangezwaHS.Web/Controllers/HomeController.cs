using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;

namespace DlangezwaHS.Web.Controllers;

public class HomeController : Controller
{
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IEmailService email, IConfiguration config, ILogger<HomeController> logger)
    {
        _email  = email;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var roles = new[] { "Admin", "Teacher", "Housemaster", "KitchenStaff", "Learner", "Parent" }
                .Where(User.IsInRole).ToList();
            if (roles.Count > 0)
            {
                var (action, controller) = AccountController.HomeFor(roles);
                return RedirectToAction(action, controller);
            }
        }
        return View();
    }

    [HttpGet]
    public IActionResult Contact() => View(new ContactViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(ContactViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var schoolEmail = _config["School:Email"] ?? _config["Smtp:FromAddress"]!;
        var body = $@"
            <h3>New website enquiry: {WebUtility.HtmlEncode(model.Topic)}</h3>
            <p><strong>From:</strong> {WebUtility.HtmlEncode(model.Name)} &lt;{WebUtility.HtmlEncode(model.Email)}&gt;</p>
            <p><strong>Phone:</strong> {WebUtility.HtmlEncode(model.Phone ?? "—")}</p>
            <hr/>
            <p style=""white-space:pre-wrap"">{WebUtility.HtmlEncode(model.Message)}</p>";

        try
        {
            await _email.SendAsync(schoolEmail, "Dlangezwa High School", $"Website enquiry – {model.Topic}", body);
            TempData["Success"] = "Thank you — your message has been sent. The school office will get back to you soon.";
            return RedirectToAction(nameof(Contact));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send contact form message from {Email}", model.Email);
            ModelState.AddModelError("", "We couldn't send your message right now. Please try again or call the school office.");
            return View(model);
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewBag.RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
