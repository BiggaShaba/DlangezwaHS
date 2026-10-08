using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface ICalendarService
{
    Task<List<CalendarEvent>> GetEventsForMonthAsync(int year, int month, string userId, string role);
    Task<CalendarEvent?>GetEventAsync(int id);
    Task<CalendarEvent>CreateEventAsync(CalendarEvent ev, string createdByUserId);
    Task UpdateEventAsync(CalendarEvent ev);
    Task DeleteEventAsync(int id);

    /// <summary>Called automatically when a teacher creates/updates an Assessment.</summary>
    Task SyncAssessmentEventAsync(Assessment assessment);

    /// <summary>Called automatically when an Assessment is deleted.</summary>
    Task RemoveAssessmentEventAsync(int assessmentId);
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class CalendarService : ICalendarService
{
    private readonly ApplicationDbContext         _db;
    private readonly IEmailService                _email;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IServiceScopeFactory         _scopeFactory;
    private readonly ILogger<CalendarService>     _logger;

    public CalendarService(ApplicationDbContext db, IEmailService email,
        UserManager<ApplicationUser> userManager, IServiceScopeFactory scopeFactory,
        ILogger<CalendarService> logger)
    {
        _db          = db;
        _email       = email;
        _userManager = userManager;
        _scopeFactory = scopeFactory;
        _logger      = logger;
    }

    // ── Get events for a month, filtered by viewer role ───────────────────────

    public async Task<List<CalendarEvent>> GetEventsForMonthAsync(
    int year, int month, string userId, string role)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1).AddTicks(-1);

        // Base query — all active events in range
        var baseQuery = _db.CalendarEvents
            .Include(e => e.Grade)
            .Include(e => e.Class)
            .Where(e => e.IsActive
                     && e.StartDate <= to
                     && (e.EndDate == null ? e.StartDate >= from : e.EndDate >= from));

        // Admin sees everything
        if (role == "Admin")
            return await baseQuery.OrderBy(e => e.StartDate).ToListAsync();

        // ── Teacher ───────────────────────────────────────────────────────────────
        if (role == "Teacher")
        {
            // Get all classIds this teacher is assigned to
            var teacher = await _db.Teachers
                .FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive);

            var teacherClassIds = teacher == null
                ? new List<int>()
                : await _db.ClassTeachers
                    .Where(ct => ct.TeacherId == teacher.Id)
                    .Select(ct => ct.ClassId)
                    .ToListAsync();

            var teacherGradeIds = await _db.Classes
                .Where(c => teacherClassIds.Contains(c.Id))
                .Select(c => c.GradeId)
                .Distinct()
                .ToListAsync();

            return await baseQuery.Where(e =>
                // Global events (no class/grade scope)
                (e.ClassId == null && e.GradeId == null)
                // Grade-scoped events for this teacher's grades
                || (e.ClassId == null && e.GradeId != null && teacherGradeIds.Contains(e.GradeId.Value))
                // Class-scoped events (assessments etc) for this teacher's classes
                || (e.ClassId != null && teacherClassIds.Contains(e.ClassId.Value))
            ).OrderBy(e => e.StartDate).ToListAsync();
        }

        // ── Parent ────────────────────────────────────────────────────────────────
        if (role == "Parent")
        {
            // To this — also check Applications table where parent submitted the application:
            var childLearnerIds = await _db.Learners
                .Where(l => l.ParentId == userId)
                .Select(l => l.Id)
                .ToListAsync();

            // If no direct ParentId link, fall back to Applications
            if (!childLearnerIds.Any())
            {
                childLearnerIds = await _db.Applications
                    .Where(a => a.ParentId == userId)
                    .Select(a => a.LearnerId)
                    .ToListAsync();
            }

            var childClassIds = await _db.Enrollments
                .Where(en => en.IsActive && childLearnerIds.Contains(en.LearnerId))
                .Select(en => en.ClassId)
                .Distinct()
                .ToListAsync();

            var childGradeIds = await _db.Classes
                .Where(c => childClassIds.Contains(c.Id))
                .Select(c => c.GradeId)
                .Distinct()
                .ToListAsync();

            return await baseQuery.Where(e =>
                // Global events
                (e.ClassId == null && e.GradeId == null)
                // Grade-scoped events for children's grades
                || (e.ClassId == null && e.GradeId != null && childGradeIds.Contains(e.GradeId.Value))
                // Class-scoped events (assessments) for children's classes
                || (e.ClassId != null && childClassIds.Contains(e.ClassId.Value))
            ).OrderBy(e => e.StartDate).ToListAsync();
        }

        // ── Learner ───────────────────────────────────────────────────────────────
        if (role == "Learner")
        {
            var learner = await _db.Learners
                .FirstOrDefaultAsync(l => l.UserId == userId);

            var learnerClassIds = new List<int>();
            var learnerGradeIds = new List<int>();

            if (learner != null)
            {
                learnerClassIds = await _db.Enrollments
                    .Where(en => en.IsActive && en.LearnerId == learner.Id)
                    .Select(en => en.ClassId)
                    .Distinct()
                    .ToListAsync();

                learnerGradeIds = await _db.Classes
                    .Where(c => learnerClassIds.Contains(c.Id))
                    .Select(c => c.GradeId)
                    .Distinct()
                    .ToListAsync();
            }

            return await baseQuery.Where(e =>
                (e.ClassId == null && e.GradeId == null)
                || (e.ClassId == null && e.GradeId != null && learnerGradeIds.Contains(e.GradeId.Value))
                || (e.ClassId != null && learnerClassIds.Contains(e.ClassId.Value))
            ).OrderBy(e => e.StartDate).ToListAsync();
        }

        // Fallback — global events only
        return await baseQuery
            .Where(e => e.ClassId == null && e.GradeId == null)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<CalendarEvent?> GetEventAsync(int id)
        => await _db.CalendarEvents
            .Include(e => e.Grade)
            .Include(e => e.Class)
            .FirstOrDefaultAsync(e => e.Id == id && e.IsActive);

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<CalendarEvent> CreateEventAsync(CalendarEvent ev, string createdByUserId)
    {
        ev.CreatedByUserId  = createdByUserId;
        ev.CreatedAt        = DateTime.UtcNow;
        ev.IsActive         = true;
        ev.NotificationSent = false;

        _db.CalendarEvents.Add(ev);
        await _db.SaveChangesAsync();

        // Fire notifications (don't await so it doesn't block the response)
        _ = SendNotificationsAsync(ev.Id);

        return ev;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task UpdateEventAsync(CalendarEvent ev)
    {
        var existing = await _db.CalendarEvents.FindAsync(ev.Id)
            ?? throw new InvalidOperationException("Event not found.");

        existing.Title       = ev.Title;
        existing.Description = ev.Description;
        existing.StartDate   = ev.StartDate;
        existing.EndDate     = ev.EndDate;
        existing.Type        = ev.Type;
        existing.GradeId     = ev.GradeId;
        existing.ClassId     = ev.ClassId;

        await _db.SaveChangesAsync();
    }

    // ── Delete (soft) ─────────────────────────────────────────────────────────

    public async Task DeleteEventAsync(int id)
    {
        var ev = await _db.CalendarEvents.FindAsync(id);
        if (ev != null)
        {
            ev.IsActive = false;
            await _db.SaveChangesAsync();
        }
    }

    // ── Sync Assessment → Calendar ────────────────────────────────────────────

    public async Task SyncAssessmentEventAsync(Assessment assessment)
    {
        // Find grade from class
        var cls = await _db.Classes.Include(c => c.Grade)
            .FirstOrDefaultAsync(c => c.Id == assessment.ClassId);

        var existing = await _db.CalendarEvents
            .FirstOrDefaultAsync(e => e.AssessmentId == assessment.Id);

        if (existing == null)
        {
            var ev = new CalendarEvent
            {
                Title          = $"[{assessment.Type}] {assessment.Name}",
                Description    = $"{cls?.Grade?.Name} {cls?.Section} — {assessment.Subject?.Name ?? ""}. Total: {assessment.TotalMarks} marks.",
                StartDate      = assessment.Date,
                Type           = CalendarEventType.Assessment,
                GradeId        = cls?.GradeId,
                ClassId        = assessment.ClassId,
                AssessmentId   = assessment.Id,
                CreatedByUserId = "system",
                CreatedAt      = DateTime.UtcNow,
                IsActive       = true,
                NotificationSent = false
            };
            _db.CalendarEvents.Add(ev);
            await _db.SaveChangesAsync();

            _ = SendNotificationsAsync(ev.Id);
        }
        else
        {
            existing.Title     = $"[{assessment.Type}] {assessment.Name}";
            existing.StartDate = assessment.Date;
            existing.ClassId   = assessment.ClassId;
            existing.GradeId   = cls?.GradeId;
            existing.IsActive  = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task RemoveAssessmentEventAsync(int assessmentId)
    {
        var ev = await _db.CalendarEvents
            .FirstOrDefaultAsync(e => e.AssessmentId == assessmentId);
        if (ev != null) { ev.IsActive = false; await _db.SaveChangesAsync(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EMAIL NOTIFICATIONS
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SendNotificationsAsync(int calendarEventId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var ev = await db.CalendarEvents.FindAsync(calendarEventId);
            if (ev is null) return;

            var recipients = await BuildRecipientListAsync(db, userManager, ev);

            foreach (var (name, recipientEmail) in recipients)
            {
                await email.SendCalendarNotificationAsync(
                    recipientEmail, name, ev.Title, ev.TypeLabel,
                    ev.StartDate, ev.Description);
            }

            ev.NotificationSent = true;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send calendar notifications for event {CalendarEventId}", calendarEventId);
        }
    }

    private static async Task<List<(string Name, string Email)>> BuildRecipientListAsync(
        ApplicationDbContext db, UserManager<ApplicationUser> userManager, CalendarEvent ev)
    {
        var result = new List<(string, string)>();

        // Assessment events: only that class's learners + their parents
        if (ev.Type == CalendarEventType.Assessment && ev.ClassId.HasValue)
        {
            // Learners in that class (those that have a UserId)
            var learners = await db.Enrollments
                .Include(en => en.Learner)
                .Where(en => en.ClassId == ev.ClassId && en.IsActive
                          && en.Learner.UserId != null)
                .Select(en => en.Learner)
                .ToListAsync();

            foreach (var l in learners)
            {
                if (!string.IsNullOrEmpty(l.UserId))
                {
                    var user = await userManager.FindByIdAsync(l.UserId);
                    if (user?.Email != null)
                        result.Add(($"{l.FirstName} {l.LastName}", user.Email));
                }

                // Parent
                if (!string.IsNullOrEmpty(l.ParentId))
                {
                    var parent = await userManager.FindByIdAsync(l.ParentId);
                    if (parent?.Email != null)
                        result.Add(($"{parent.FirstName} {parent.LastName}", parent.Email));
                }
            }
            return result;
        }

        // Global events (Holiday, Announcement, Notice, SchoolEvent):
        // All active users — teachers, learners (with accounts), parents

        // Teachers
        var teachers = await db.Teachers
            .Where(t => t.IsActive && t.UserId != null)
            .ToListAsync();
        foreach (var t in teachers)
        {
            var user = await userManager.FindByIdAsync(t.UserId!);
            if (user?.Email != null)
                result.Add(($"{t.FirstName} {t.LastName}", user.Email));
        }

        // Parents (Role = Parent)
        var parentRole = await userManager.GetUsersInRoleAsync("Parent");
        foreach (var p in parentRole)
        {
            if (p.Email != null && p.IsActive)
                result.Add(($"{p.FirstName} {p.LastName}", p.Email));
        }

        // Learners with accounts
        var learnerRole = await userManager.GetUsersInRoleAsync("Learner");
        foreach (var l in learnerRole)
        {
            if (l.Email != null && l.IsActive && !LearnerAccountService.IsPlaceholderEmail(l.Email))
                result.Add(($"{l.FirstName} {l.LastName}", l.Email));
        }

        // Deduplicate by email
        return result.GroupBy(r => r.Item2).Select(g => g.First()).ToList();
    }

}
