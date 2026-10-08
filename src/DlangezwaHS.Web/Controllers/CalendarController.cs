using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize]
public class CalendarController : Controller
{
    private readonly ICalendarService             _calendar;
    private readonly ApplicationDbContext         _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public CalendarController(ICalendarService calendar,
        ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _calendar    = calendar;
        _db          = db;
        _userManager = userManager;
    }

    // ── Index — calendar page ─────────────────────────────────────────────────

    public async Task<IActionResult> Index(int? year, int? month)
    {
        var today = DateTime.Today;
        int y = year  ?? today.Year;
        int m = month ?? today.Month;

        // Clamp
        if (m < 1) { m = 12; y--; }
        if (m > 12){ m = 1;  y++; }

        var user   = await _userManager.GetUserAsync(User);
        var roles  = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();
        var role   = roles.FirstOrDefault() ?? "Learner";
        var userId = user?.Id ?? string.Empty;

        var events = await _calendar.GetEventsForMonthAsync(y, m, userId, role);
        var grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync();
        var classes= await _db.Classes.Include(c => c.Grade).OrderBy(c => c.GradeId).ThenBy(c => c.Section).ToListAsync();

        ViewBag.Year       = y;
        ViewBag.Month      = m;
        ViewBag.MonthName  = new DateTime(y, m, 1).ToString("MMMM yyyy");
        ViewBag.IsAdmin    = role == "Admin";
        ViewBag.Role       = role;
        ViewBag.Events     = events;
        ViewBag.Grades     = grades;
        ViewBag.Classes    = classes;
        ViewBag.EventTypes = Enum.GetValues<CalendarEventType>()
                               .Where(t => t != CalendarEventType.Assessment)
                               .ToList();

        // Build a day→events lookup for the view
        var eventsByDay = events.GroupBy(e => e.StartDate.Day)
                                .ToDictionary(g => g.Key, g => g.ToList());
        ViewBag.EventsByDay = eventsByDay;

        return View();
    }

    // ── Get single event (AJAX) ───────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetEvent(int id)
    {
        var ev = await _calendar.GetEventAsync(id);
        if (ev == null) return NotFound();

        // Look up associated SchoolEvent if type is SchoolEvent
        int? schoolEventId = null;
        if (ev.Type == CalendarEventType.SchoolEvent)
        {
            var se = await _db.SchoolEvents
                .FirstOrDefaultAsync(s => s.CalendarEventId == id);
            schoolEventId = se?.Id;
        }

        return Json(new
        {
            ev.Id,
            ev.Title,
            ev.Description,
            startDate = ev.StartDate.ToString("yyyy-MM-dd"),
            endDate = ev.EndDate?.ToString("yyyy-MM-dd"),
            type = (int)ev.Type,
            typeLabel = ev.TypeLabel,
            badgeColor = ev.BadgeColor,
            ev.GradeId,
            ev.ClassId,
            schoolEventId   // null if not a School Event or not set up yet
        });
    }
    

    // ── Get events for month (AJAX — for dynamic month navigation) ────────────

    [HttpGet]
    public async Task<IActionResult> GetMonthEvents(int year, int month)
    {
        var user   = await _userManager.GetUserAsync(User);
        var roles  = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();
        var role   = roles.FirstOrDefault() ?? "Learner";
        var userId = user?.Id ?? string.Empty;

        var events = await _calendar.GetEventsForMonthAsync(year, month, userId, role);

        return Json(events.Select(e => new
        {
            e.Id,
            e.Title,
            e.Description,
            e.BadgeColor,
            e.TypeLabel,
            day        = e.StartDate.Day,
            startDate  = e.StartDate.ToString("yyyy-MM-dd"),
            endDate    = e.EndDate?.ToString("yyyy-MM-dd"),
            type       = (int)e.Type,
            e.GradeId,
            e.ClassId
        }));
    }

    // ── Create (Admin only) ───────────────────────────────────────────────────

    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string title, string? description, DateTime startDate, DateTime? endDate,
        CalendarEventType type, int? gradeId, int? classId)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Json(new { success = false, message = "Title is required." });

        var user = await _userManager.GetUserAsync(User);
        var ev   = new CalendarEvent
        {
            Title       = title.Trim(),
            Description = description?.Trim(),
            StartDate   = startDate,
            EndDate     = endDate,
            Type        = type,
            GradeId     = gradeId == 0 ? null : gradeId,
            ClassId     = classId == 0 ? null : classId
        };

        var created = await _calendar.CreateEventAsync(ev, user!.Id);
        return Json(new
        {
            success     = true,
            message     = "Event added to calendar.",
            id          = created.Id,
            badgeColor  = created.BadgeColor,
            typeLabel   = created.TypeLabel,
            day         = created.StartDate.Day
        });
    }

    // ── Update (Admin only) ───────────────────────────────────────────────────

    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id, string title, string? description, DateTime startDate, DateTime? endDate,
        CalendarEventType type, int? gradeId, int? classId)
    {
        var ev = await _calendar.GetEventAsync(id);
        if (ev == null) return Json(new { success = false, message = "Event not found." });
        if (ev.Type == CalendarEventType.Assessment)
            return Json(new { success = false, message = "Assessment events are managed automatically." });

        ev.Title       = title.Trim();
        ev.Description = description?.Trim();
        ev.StartDate   = startDate;
        ev.EndDate     = endDate;
        ev.Type        = type;
        ev.GradeId     = gradeId == 0 ? null : gradeId;
        ev.ClassId     = classId == 0 ? null : classId;

        await _calendar.UpdateEventAsync(ev);
        return Json(new { success = true, message = "Event updated." });
    }

    // ── Delete (Admin only) ───────────────────────────────────────────────────

    [HttpPost, Authorize(Roles = "Admin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await _calendar.GetEventAsync(id);
        if (ev == null) return Json(new { success = false, message = "Event not found." });
        if (ev.Type == CalendarEventType.Assessment)
            return Json(new { success = false, message = "Assessment events are managed automatically." });

        // If a SchoolEvent is linked, soft-delete it too
        var schoolEvent = await _db.SchoolEvents
            .FirstOrDefaultAsync(s => s.CalendarEventId == id);
        if (schoolEvent != null)
            schoolEvent.Status = SchoolEventStatus.Cancelled;

        await _calendar.DeleteEventAsync(id);
        return Json(new { success = true, message = "Event removed from calendar." });
    }

}
