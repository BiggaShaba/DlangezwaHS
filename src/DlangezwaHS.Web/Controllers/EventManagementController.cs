using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DlangezwaHS.Web.Controllers;

[Authorize]
public class EventManagementController : Controller
{
    private readonly IEventManagementService _eventSvc;
    private readonly UserManager<ApplicationUser> _userManager;

    public EventManagementController(IEventManagementService eventSvc,
        UserManager<ApplicationUser> userManager)
    {
        _eventSvc = eventSvc;
        _userManager = userManager;
    }

    // ─── Manage Event (Admin) ─────────────────────────────────────────────────
    // GET: /EventManagement/Manage?calendarEventId=5
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Manage(int calendarEventId)
    {
        var existing = await _eventSvc.GetSchoolEventByCalendarIdAsync(calendarEventId);
        var roles = await _eventSvc.GetCoordinatorRolesAsync();
        var candidates = await _eventSvc.GetCoordinatorCandidatesAsync();

        ViewBag.CalendarEventId = calendarEventId;
        ViewBag.SchoolEvent = existing;
        ViewBag.Roles = roles;
        ViewBag.Candidates = candidates;

        return View(existing);
    }

    // POST: Create or update school event details
    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        int calendarEventId,
        string? venue,
        decimal budget,
        int maxParticipants,
        decimal gateTicketPrice,
        string? notes,
        [FromForm] List<string> coordinatorUserIds,
        [FromForm] List<int> coordinatorRoleIds)
    {
        try
        {
            var user = await _userManager.GetUserAsync(User);

            var coordinators = coordinatorUserIds
                .Zip(coordinatorRoleIds, (uid, rid) => new AssignCoordinatorDto(uid, rid))
                .ToList();

            var dto = new CreateSchoolEventDto(
                venue, budget, maxParticipants, gateTicketPrice, notes, coordinators);

            var existing = await _eventSvc.GetSchoolEventByCalendarIdAsync(calendarEventId);
            if (existing == null)
                await _eventSvc.CreateSchoolEventAsync(calendarEventId, dto, user!.Id);
            else
                await _eventSvc.UpdateSchoolEventAsync(existing.Id, dto);

            return Json(new { success = true, message = "Event details saved." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // POST: Publish event → sends coordinator emails
    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int schoolEventId)
    {
        try
        {
            await _eventSvc.PublishEventAsync(schoolEventId);
            return Json(new { success = true, message = "Event published. Coordinator emails sent." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // POST: Add a new coordinator role (Admin)
    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRole(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, message = "Role name required." });
        await _eventSvc.AddCoordinatorRoleAsync(name.Trim());
        var roles = await _eventSvc.GetCoordinatorRolesAsync();
        return Json(new { success = true, roles = roles.Select(r => new { r.Id, r.Name }) });
    }

    // POST: Remove coordinator from event
    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCoordinator(int coordinatorId)
    {
        await _eventSvc.RemoveCoordinatorAsync(coordinatorId);
        return Json(new { success = true });
    }

    // ─── RSVP (Parents & Teachers) ────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RSVP(int schoolEventId)
    {
        try
        {
            var user = await _userManager.GetUserAsync(User);
            var ticket = await _eventSvc.RSVPAsync(
                schoolEventId, user!.Id, $"{user.FirstName} {user.LastName}");

            return Json(new
            {
                success = true,
                message = "RSVP confirmed! Your ticket is ready.",
                ticketHash = ticket.Hash,
                qrCodeUrl = ticket.QrCodeUrl
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRSVP(int schoolEventId)
    {
        var user = await _userManager.GetUserAsync(User);
        await _eventSvc.CancelRSVPAsync(schoolEventId, user!.Id);
        return Json(new { success = true, message = "RSVP cancelled." });
    }

    // GET: My ticket for an event (shows QR code)
    public async Task<IActionResult> MyTicket(int schoolEventId)
    {
        var user = await _userManager.GetUserAsync(User);
        var ticket = await _eventSvc.GetMyTicketAsync(schoolEventId, user!.Id);
        if (ticket == null) return NotFound();

        ViewBag.Ticket = ticket;
        return View(ticket);
    }

    // ─── Scanner Page ─────────────────────────────────────────────────────────
    // Accessible by Admin + assigned coordinators

    public async Task<IActionResult> Scanner(int schoolEventId)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        bool isAdmin = roles.Contains("Admin");
        bool isCoord = await IsCoordinatorAsync(schoolEventId, user.Id);

        if (!isAdmin && !isCoord)
            return Forbid();

        var ev = await _eventSvc.GetSchoolEventAsync(schoolEventId);
        if (ev == null) return NotFound();

        ViewBag.SchoolEventId = schoolEventId;
        ViewBag.EventTitle = ev.CalendarEvent.Title;
        ViewBag.EventDate = ev.CalendarEvent.StartDate.ToString("dd MMMM yyyy");
        return View();
    }

    // POST: Process a scanned hash (AJAX from scanner page)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessScan(int schoolEventId, string hash, string? deviceInfo)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);
        bool isAdmin = roles.Contains("Admin");
        bool isCoord = await IsCoordinatorAsync(schoolEventId, user!.Id);

        if (!isAdmin && !isCoord)
            return Json(new { success = false, message = "Not authorised to scan." });

        var result = await _eventSvc.ScanTicketAsync(schoolEventId, hash, user.Id, deviceInfo);

        return Json(new
        {
            success = result.Result == ScanResult.Valid,
            result = result.Result.ToString(),
            message = result.Message,
            ownerName = result.OwnerName,
            ticketType = result.TicketType,
            issuedAt = result.IssuedAt?.ToString("HH:mm dd MMM yyyy")
        });
    }

    // ─── Event Summary (UC22) ─────────────────────────────────────────────────

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Summary(int schoolEventId)
    {
        var summary = await _eventSvc.GetEventSummaryAsync(schoolEventId);
        return View(summary);
    }

    // ─── Gate QR (shared, shown by Admin/Coordinator) ─────────────────────────

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GateQR(int schoolEventId)
    {
        var ev = await _eventSvc.GetSchoolEventAsync(schoolEventId);
        if (ev == null) return NotFound();
        ViewBag.GateHash = ev.GateTicketHash;
        ViewBag.GateQrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=400x400&data={Uri.EscapeDataString(ev.GateTicketHash)}";
        ViewBag.EventTitle = ev.CalendarEvent.Title;
        return View();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> IsCoordinatorAsync(int schoolEventId, string userId)
    {
        var ev = await _eventSvc.GetSchoolEventAsync(schoolEventId);
        return ev?.Coordinators.Any(c => c.UserId == userId && c.IsActive) ?? false;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyTicketStatus(int schoolEventId)
    {
        var user = await _userManager.GetUserAsync(User);
        var ev = await _eventSvc.GetSchoolEventAsync(schoolEventId);
        if (ev == null) return Json(new { isPublished = false });

        var ticket = await _eventSvc.GetMyTicketAsync(schoolEventId, user!.Id);
        int rsvpCount = ev.RSVPs.Count(r => r.Status == RSVPStatus.Confirmed);
        int spotsLeft = Math.Max(0, ev.MaxParticipants - rsvpCount);

        return Json(new
        {
            isPublished = ev.IsPublished,
            hasTicket = ticket != null,
            ticketStatus = ticket?.Status.ToString(),
            ticketHash = ticket?.Hash,
            qrCodeUrl = ticket?.QrCodeUrl,
            isFull = spotsLeft == 0 && ticket == null,
            spotsLeft
        });
    }
}
