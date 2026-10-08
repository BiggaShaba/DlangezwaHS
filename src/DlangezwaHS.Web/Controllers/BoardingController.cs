using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize(Roles = "Housemaster")]
public class BoardingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBoardingService _svc;
    private readonly IDietaryService _dietarySvc;
    private readonly IPdfService _pdf;
    private readonly IWebHostEnvironment _env;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<BoardingController> _logger;

    public BoardingController(ApplicationDbContext db, IBoardingService svc, IDietaryService dietarySvc, IPdfService pdf, IWebHostEnvironment env,
        UserManager<ApplicationUser> userManager, ILogger<BoardingController> logger)
    {
        _db = db;
        _svc = svc;
        _dietarySvc = dietarySvc;
        _pdf = pdf;
        _env = env;
        _userManager = userManager;
        _logger = logger;
    }

    private string UserId => _userManager.GetUserId(User)!;

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Dashboard()
        => View(await _svc.GetDashboardAsync());

    // ── Scanner (UC1 / UC2) ──────────────────────────────────────────────────

    // The scanner shows today's in/out numbers alongside the camera
    public async Task<IActionResult> Scanner() => View(await _svc.GetDashboardAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveMealAbsenceAlert(int id)
    {
        var alert = await _db.MealAbsenceAlerts.Include(a => a.Learner).FirstOrDefaultAsync(a => a.Id == id && a.ResolvedAt == null);
        if (alert is not null)
        {
            alert.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Meal absence alert for {alert.Learner.FullName} marked as followed up.";
        }
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessScan(string hash)
    {
        var result = await _svc.ProcessScanAsync(hash);
        return Json(new { isValid = result.IsValid, learnerName = result.LearnerName, status = result.Status, message = result.Message });
    }

    // ── Boarders / QR badges ─────────────────────────────────────────────────

    public async Task<IActionResult> Boarders()
    {
        var boarders = await _db.RoomAllocations
            .Include(r => r.Learner)
            .Where(r => r.IsActive)
            .OrderBy(r => r.Learner.LastName)
            .Select(r => r.Learner)
            .ToListAsync();
        return View(boarders);
    }

    public async Task<IActionResult> LearnerQr(int learnerId)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId);
        if (learner is null) return NotFound();

        ViewBag.QrCode = await _svc.GetOrCreateLearnerQrCodeAsync(learnerId);
        return View(learner);
    }

    // ── Movement Log ─────────────────────────────────────────────────────────

    public async Task<IActionResult> MovementLog(DateTime? date)
    {
        var day = date ?? DateTime.Today;
        ViewBag.Date = day;
        return View(await _svc.GetMovementLogAsync(day));
    }

    // ── Leave Requests (UC2) ─────────────────────────────────────────────────

    public async Task<IActionResult> LeaveRequests(bool showAll = false)
    {
        ViewBag.ShowAll = showAll;
        // Counts for the summary tiles come from every request, whichever tab is showing
        ViewBag.AllRequests = await _svc.GetAllLeaveRequestsAsync();
        return View(showAll ? (IList<DlangezwaHS.Web.ViewModels.LeaveRequestRow>)ViewBag.AllRequests : await _svc.GetPendingLeaveRequestsAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveLeaveRequest(int id, string? notes)
    {
        await _svc.ApproveLeaveRequestAsync(id, UserId, notes);
        TempData["Success"] = "Leave request approved.";
        return RedirectToAction(nameof(LeaveRequests));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectLeaveRequest(int id, string reason)
    {
        await _svc.RejectLeaveRequestAsync(id, UserId, reason);
        TempData["Success"] = "Leave request rejected.";
        return RedirectToAction(nameof(LeaveRequests));
    }

    // ── Dietary Profiles (UC3) — view only; parents/learners add them without review ─

    public async Task<IActionResult> DietaryProfiles()
        => View(await _dietarySvc.GetActiveProfilesAsync());

    public async Task<IActionResult> DietaryProfileReview(int id)
    {
        var profile = await _dietarySvc.GetProfileDetailAsync(id);
        if (profile is null) return NotFound();
        return View(profile);
    }

    public async Task<IActionResult> DietaryDocument(int id)
    {
        var doc = await _dietarySvc.GetDocumentAsync(id);
        if (doc is null) return NotFound();
        return PhysicalFile(doc.Value.path, doc.Value.contentType);
    }

    // ── Meal Compliance Report (UC10) ────────────────────────────────────────

    [Authorize(Roles = "Admin,Housemaster")]
    public async Task<IActionResult> MealComplianceReport(DateTime? from, DateTime? to)
    {
        var fromDate = from ?? DateTime.Today.AddDays(-7);
        var toDate = to ?? DateTime.Today;
        ViewBag.From = fromDate;
        ViewBag.To = toDate;
        return View(await _svc.GenerateComplianceReportAsync(fromDate, toDate));
    }

    [Authorize(Roles = "Admin,Housemaster")]
    public async Task<IActionResult> MealComplianceReportDownload(DateTime from, DateTime to)
    {
        var report = await _svc.GenerateComplianceReportAsync(from, to);
        var user = await _userManager.GetUserAsync(User);
        report.GeneratedByName = user?.FullName ?? "Housemaster";

        var pdfBytes = _pdf.GenerateMealComplianceReportPdf(report);
        await _svc.ArchiveReportAsync(from, to, pdfBytes, UserId, _env.WebRootPath);

        return File(pdfBytes, "application/pdf", $"MealComplianceReport_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");
    }
}
