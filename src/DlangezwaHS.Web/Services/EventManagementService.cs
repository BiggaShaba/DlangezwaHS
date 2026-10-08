using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace DlangezwaHS.Web.Services;

public interface IEventManagementService
{
    // School Event CRUD
    Task<SchoolEvent>  CreateSchoolEventAsync(int calendarEventId, CreateSchoolEventDto dto, string userId);
    Task               UpdateSchoolEventAsync(int schoolEventId, CreateSchoolEventDto dto);
    Task<SchoolEvent?> GetSchoolEventAsync(int schoolEventId);
    Task<SchoolEvent?> GetSchoolEventByCalendarIdAsync(int calendarEventId);
    Task               PublishEventAsync(int schoolEventId);
    Task               CancelEventAsync(int schoolEventId);

    // Coordinators
    Task<List<EventCoordinatorRole>> GetCoordinatorRolesAsync();
    Task                             AddCoordinatorRoleAsync(string name);
    Task                             AssignCoordinatorAsync(int schoolEventId, string userId, int roleId);
    Task                             RemoveCoordinatorAsync(int coordinatorId);

    // RSVP + Tickets
    Task<EventTicket>  RSVPAsync(int schoolEventId, string userId, string userName);
    Task               CancelRSVPAsync(int schoolEventId, string userId);
    Task<EventTicket?> GetMyTicketAsync(int schoolEventId, string userId);

    // Gate tickets
    Task<List<EventTicket>> GetGateTicketsAsync(int schoolEventId);

    // Scanning
    Task<ScanResultDto> ScanTicketAsync(int schoolEventId, string hash, string scannedByUserId, string? deviceInfo);

    // Summary (UC22)
    Task<EventSummaryDto> GetEventSummaryAsync(int schoolEventId);

    // Candidate coordinators (teachers + trusted learners)
    Task<List<CoordinatorCandidateDto>> GetCoordinatorCandidatesAsync();
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateSchoolEventDto(
    string?  Venue,
    decimal  Budget,
    int      MaxParticipants,
    decimal  GateTicketPrice,
    string?  Notes,
    List<AssignCoordinatorDto> Coordinators
);

public record AssignCoordinatorDto(string UserId, int RoleId);

public record ScanResultDto(
    ScanResult Result,
    string     Message,
    string?    OwnerName,
    string?    TicketType,
    DateTime?  IssuedAt
);

public record CoordinatorCandidateDto(
    string UserId,
    string Name,
    string Email,
    string Type   // "Teacher" or "Learner"
);

public record EventSummaryDto(
    string         EventTitle,
    DateTime       EventDate,
    string?        Venue,
    decimal        Budget,
    decimal        ActualSpend,
    int            MaxParticipants,
    int            TotalRSVPs,
    int            TotalScanned,
    int            GateTicketsIssued,
    int            GateTicketsScanned,
    double         AttendanceRate,
    List<CoordinatorSummaryRow> Coordinators,
    List<ScanTimelineRow>       ScanTimeline
);

public record CoordinatorSummaryRow(string Name, string Role, string Email);
public record ScanTimelineRow(DateTime Time, string OwnerName, ScanResult Result);

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class EventManagementService : IEventManagementService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventManagementService> _logger;

    public EventManagementService(ApplicationDbContext db, IEmailService email,
        UserManager<ApplicationUser> userManager, IServiceScopeFactory scopeFactory,
        ILogger<EventManagementService> logger)
    {
        _db = db;
        _email = email;
        _userManager = userManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ─── Create School Event ──────────────────────────────────────────────────

    public async Task<SchoolEvent> CreateSchoolEventAsync(
        int calendarEventId, CreateSchoolEventDto dto, string userId)
    {
        // Gate ticket = one shared hash for all physical gate sales
        var gateHash = GenerateHash();

        var ev = new SchoolEvent
        {
            CalendarEventId = calendarEventId,
            Venue = dto.Venue,
            Budget = dto.Budget,
            MaxParticipants = dto.MaxParticipants,
            GateTicketHash = gateHash,
            GateTicketPrice = dto.GateTicketPrice,
            Notes = dto.Notes,
            Status = SchoolEventStatus.Draft,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.SchoolEvents.Add(ev);
        await _db.SaveChangesAsync();

        // Assign coordinators
        foreach (var c in dto.Coordinators)
            await AssignCoordinatorAsync(ev.Id, c.UserId, c.RoleId);

        return ev;
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    public async Task UpdateSchoolEventAsync(int id, CreateSchoolEventDto dto)
    {
        var ev = await _db.SchoolEvents.FindAsync(id)
            ?? throw new InvalidOperationException("Event not found.");

        ev.Venue = dto.Venue;
        ev.Budget = dto.Budget;
        ev.MaxParticipants = dto.MaxParticipants;
        ev.GateTicketPrice = dto.GateTicketPrice;
        ev.Notes = dto.Notes;
        await _db.SaveChangesAsync();
    }


    public async Task<SchoolEvent?> GetSchoolEventByCalendarIdAsync(int calendarEventId)
    {
        var ev = await _db.SchoolEvents
            .Include(e => e.CalendarEvent)
            .Include(e => e.Coordinators).ThenInclude(c => c.Role)
            .Include(e => e.Tickets)
            .Include(e => e.RSVPs)
            .FirstOrDefaultAsync(e => e.CalendarEventId == calendarEventId);

        if (ev == null) return null;

        foreach (var coord in ev.Coordinators.Where(c => c.IsActive))
        {
            var user = await _userManager.FindByIdAsync(coord.UserId);
            coord.CoordinatorName = user != null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            coord.CoordinatorEmail = user?.Email ?? string.Empty;
            var roles = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();
            coord.CoordinatorType = roles.Contains("Teacher") ? "Teacher" : "Learner";
        }

        return ev;
    }

    // ─── Publish ──────────────────────────────────────────────────────────────

    public async Task PublishEventAsync(int schoolEventId)
    {
        var ev = await _db.SchoolEvents
            .Include(e => e.CalendarEvent)
            .Include(e => e.Coordinators).ThenInclude(c => c.Role)
            .FirstOrDefaultAsync(e => e.Id == schoolEventId)
            ?? throw new InvalidOperationException("Event not found.");

        ev.Status = SchoolEventStatus.Published;
        ev.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Run all emails in background so publish doesn't time out
        _ = SendPublishEmailsAsync(ev.Id);
    }

    private async Task SendPublishEmailsAsync(int schoolEventId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var ev = await db.SchoolEvents
                .Include(e => e.CalendarEvent)
                .Include(e => e.Coordinators).ThenInclude(c => c.Role)
                .FirstOrDefaultAsync(e => e.Id == schoolEventId);
            if (ev is null) return;

            var calEv = ev.CalendarEvent;
            var sentEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── 1. Coordinators — personalised role email ─────────────────────
            foreach (var coord in ev.Coordinators.Where(c => c.IsActive))
            {
                var user = await userManager.FindByIdAsync(coord.UserId);
                if (user?.Email == null || !sentEmails.Add(user.Email)) continue;

                await email.SendCalendarNotificationAsync(
                    user.Email,
                    $"{user.FirstName} {user.LastName}",
                    calEv.Title,
                    $"You are assigned as {coord.Role.Name} for this event",
                    calEv.StartDate,
                    $"Venue: {ev.Venue ?? "TBC"} | Budget: R{ev.Budget:N0} | Capacity: {ev.MaxParticipants} people.");
            }

            // ── 2. All Teachers ───────────────────────────────────────────────
            var teachers = await db.Teachers
                .Where(t => t.IsActive && t.UserId != null)
                .ToListAsync();

            foreach (var t in teachers)
            {
                var user = await userManager.FindByIdAsync(t.UserId!);
                if (user?.Email == null || !sentEmails.Add(user.Email)) continue;

                await email.SendCalendarNotificationAsync(
                    user.Email,
                    $"{t.FirstName} {t.LastName}",
                    calEv.Title,
                    "School Event",
                    calEv.StartDate,
                    $"Venue: {ev.Venue ?? "TBC"}. {calEv.Description ?? string.Empty}");
            }

            // ── 3. All Parents ────────────────────────────────────────────────
            var parents = await userManager.GetUsersInRoleAsync("Parent");
            foreach (var p in parents.Where(p => p.IsActive))
            {
                if (p.Email == null || !sentEmails.Add(p.Email)) continue;

                await email.SendCalendarNotificationAsync(
                    p.Email,
                    $"{p.FirstName} {p.LastName}",
                    calEv.Title,
                    "School Event",
                    calEv.StartDate,
                    $"Venue: {ev.Venue ?? "TBC"}. {calEv.Description ?? string.Empty}");
            }

            // ── 4. Learners with accounts ─────────────────────────────────────
            var learners = await userManager.GetUsersInRoleAsync("Learner");
            foreach (var l in learners.Where(l => l.IsActive))
            {
                if (l.Email == null || !sentEmails.Add(l.Email)) continue;

                await email.SendCalendarNotificationAsync(
                    l.Email,
                    $"{l.FirstName} {l.LastName}",
                    calEv.Title,
                    "School Event",
                    calEv.StartDate,
                    $"Venue: {ev.Venue ?? "TBC"}. {calEv.Description ?? string.Empty}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send publish emails for school event {SchoolEventId}", schoolEventId);
        }
    }


    public async Task CancelEventAsync(int schoolEventId)
    {
        var ev = await _db.SchoolEvents.FindAsync(schoolEventId)
            ?? throw new InvalidOperationException("Event not found.");
        ev.Status = SchoolEventStatus.Cancelled;
        await _db.SaveChangesAsync();
    }

    // ─── Coordinator Roles ────────────────────────────────────────────────────

    public async Task<List<EventCoordinatorRole>> GetCoordinatorRolesAsync()
        => await _db.EventCoordinatorRoles.Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();

    public async Task AddCoordinatorRoleAsync(string name)
    {
        if (await _db.EventCoordinatorRoles.AnyAsync(r => r.Name == name))
            return;
        _db.EventCoordinatorRoles.Add(new EventCoordinatorRole { Name = name, IsActive = true });
        await _db.SaveChangesAsync();
    }

    // ─── Assign Coordinator ───────────────────────────────────────────────────

    public async Task AssignCoordinatorAsync(int schoolEventId, string userId, int roleId)
    {
        if (await _db.EventCoordinators.AnyAsync(c =>
                c.SchoolEventId == schoolEventId && c.UserId == userId))
            return;

        _db.EventCoordinators.Add(new EventCoordinator
        {
            SchoolEventId = schoolEventId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();
    }

    public async Task RemoveCoordinatorAsync(int coordinatorId)
    {
        var c = await _db.EventCoordinators.FindAsync(coordinatorId);
        if (c != null) { c.IsActive = false; await _db.SaveChangesAsync(); }
    }

    // ─── RSVP ─────────────────────────────────────────────────────────────────

    public async Task<EventTicket> RSVPAsync(int schoolEventId, string userId, string userName)
    {
        var ev = await _db.SchoolEvents
            .Include(e => e.CalendarEvent)
            .Include(e => e.RSVPs)
            .FirstOrDefaultAsync(e => e.Id == schoolEventId)
            ?? throw new InvalidOperationException("Event not found.");

        if (!ev.IsPublished)
            throw new InvalidOperationException("Event is not open for RSVP.");

        if (ev.RSVPs.Count(r => r.Status == RSVPStatus.Confirmed) >= ev.MaxParticipants)
            throw new InvalidOperationException("Event is fully booked.");

        // Check existing RSVP
        var existing = await _db.EventRSVPs
            .Include(r => r.Ticket)
            .FirstOrDefaultAsync(r => r.SchoolEventId == schoolEventId && r.UserId == userId);
        if (existing != null && existing.Status == RSVPStatus.Confirmed)
            return existing.Ticket!;

        // Generate unique ticket hash
        var hash = GenerateHash();
        var ticket = new EventTicket
        {
            SchoolEventId = schoolEventId,
            Hash = hash,
            TicketType = TicketType.RSVP,
            OwnerUserId = userId,
            OwnerName = userName,
            Status = TicketStatus.Unused,
            IssuedAt = DateTime.UtcNow
        };
        _db.EventTickets.Add(ticket);
        await _db.SaveChangesAsync();

        var rsvp = new EventRSVP
        {
            SchoolEventId = schoolEventId,
            UserId = userId,
            TicketId = ticket.Id,
            RSVPedAt = DateTime.UtcNow,
            Status = RSVPStatus.Confirmed
        };
        _db.EventRSVPs.Add(rsvp);
        await _db.SaveChangesAsync();

        // Email ticket confirmation
        var user = await _userManager.FindByIdAsync(userId);
        if (user?.Email != null)
        {
            await _email.SendCalendarNotificationAsync(
                user.Email, userName,
                ev.CalendarEvent.Title,
                "RSVP Confirmed — your unique QR ticket is ready in the portal",
                ev.CalendarEvent.StartDate,
                $"Venue: {ev.Venue ?? "TBC"}. Your QR code is unique and single-use.");
        }

        return ticket;
    }

    public async Task CancelRSVPAsync(int schoolEventId, string userId)
    {
        var rsvp = await _db.EventRSVPs.FirstOrDefaultAsync(
            r => r.SchoolEventId == schoolEventId && r.UserId == userId);
        if (rsvp == null) return;

        rsvp.Status = RSVPStatus.Cancelled;
        if (rsvp.TicketId.HasValue)
        {
            var ticket = await _db.EventTickets.FindAsync(rsvp.TicketId.Value);
            if (ticket != null) ticket.Status = TicketStatus.Cancelled;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<EventTicket?> GetMyTicketAsync(int schoolEventId, string userId)
        => await _db.EventTickets.FirstOrDefaultAsync(
            t => t.SchoolEventId == schoolEventId && t.OwnerUserId == userId
              && t.Status != TicketStatus.Cancelled);

    public async Task<List<EventTicket>> GetGateTicketsAsync(int schoolEventId)
        => await _db.EventTickets
            .Where(t => t.SchoolEventId == schoolEventId && t.TicketType == TicketType.Gate)
            .ToListAsync();

    // ─── Scan ─────────────────────────────────────────────────────────────────

    public async Task<ScanResultDto> ScanTicketAsync(
        int schoolEventId, string hash, string scannedByUserId, string? deviceInfo)
    {
        ScanResult result;
        string message;
        string? ownerName = null;
        string? ticketType = null;
        DateTime? issuedAt = null;

        // Check if it's the gate hash
        var schoolEvent = await _db.SchoolEvents.FindAsync(schoolEventId);
        if (schoolEvent?.GateTicketHash == hash)
        {
            // Gate ticket — always valid (physical tickets sold at gate)
            // Record as a new gate ticket entry each time (track count)
            var gateTkt = new EventTicket
            {
                SchoolEventId = schoolEventId,
                Hash = GenerateHash(), // unique record per gate entry
                TicketType = TicketType.Gate,
                Status = TicketStatus.Used,
                IssuedAt = DateTime.UtcNow,
                UsedAt = DateTime.UtcNow,
                ScannedByUserId = scannedByUserId
            };
            _db.EventTickets.Add(gateTkt);
            result = ScanResult.Valid;
            message = "✅ Gate ticket valid. Entry recorded.";
            ticketType = "Gate";
        }
        else
        {
            var ticket = await _db.EventTickets.FirstOrDefaultAsync(
                t => t.Hash == hash && t.SchoolEventId == schoolEventId);

            if (ticket == null)
            {
                result = ScanResult.Invalid;
                message = "❌ Invalid QR code. Ticket not recognised.";
            }
            else if (ticket.Status == TicketStatus.Used)
            {
                result = ScanResult.AlreadyUsed;
                message = $"⚠️ Ticket already used at {ticket.UsedAt:HH:mm dd MMM}.";
                ownerName = ticket.OwnerName;
                issuedAt = ticket.IssuedAt;
                ticketType = "RSVP";
            }
            else if (ticket.Status == TicketStatus.Cancelled)
            {
                result = ScanResult.Invalid;
                message = "❌ This ticket has been cancelled.";
            }
            else
            {
                ticket.Status = TicketStatus.Used;
                ticket.UsedAt = DateTime.UtcNow;
                ticket.ScannedByUserId = scannedByUserId;
                result = ScanResult.Valid;
                message = $"✅ Valid. Welcome, {ticket.OwnerName}!";
                ownerName = ticket.OwnerName;
                issuedAt = ticket.IssuedAt;
                ticketType = "RSVP";
            }
        }

        // Log the scan
        _db.EventScans.Add(new EventScan
        {
            SchoolEventId = schoolEventId,
            Hash = hash,
            ScannedByUserId = scannedByUserId,
            ScannedAt = DateTime.UtcNow,
            Result = result,
            DeviceInfo = deviceInfo
        });
        await _db.SaveChangesAsync();

        return new ScanResultDto(result, message, ownerName, ticketType, issuedAt);
    }

    // ─── Summary (UC22) ───────────────────────────────────────────────────────

    public async Task<EventSummaryDto> GetEventSummaryAsync(int schoolEventId)
    {
        var ev = await _db.SchoolEvents
            .Include(e => e.CalendarEvent)
            .Include(e => e.Coordinators).ThenInclude(c => c.Role)
            .Include(e => e.Tickets)
            .Include(e => e.RSVPs)
            .Include(e => e.Scans)
            .FirstOrDefaultAsync(e => e.Id == schoolEventId)
            ?? throw new InvalidOperationException("Event not found.");

        var rsvpTickets = ev.Tickets.Where(t => t.TicketType == TicketType.RSVP).ToList();
        var gateTickets = ev.Tickets.Where(t => t.TicketType == TicketType.Gate).ToList();
        int totalScanned = rsvpTickets.Count(t => t.Status == TicketStatus.Used)
                           + gateTickets.Count;
        int totalRSVPs = ev.RSVPs.Count(r => r.Status == RSVPStatus.Confirmed);
        double rate = ev.MaxParticipants > 0
                           ? totalScanned * 100.0 / ev.MaxParticipants : 0;

        // Enrich coordinator names
        var coordRows = new List<CoordinatorSummaryRow>();
        foreach (var c in ev.Coordinators.Where(c => c.IsActive))
        {
            var user = await _userManager.FindByIdAsync(c.UserId);
            coordRows.Add(new CoordinatorSummaryRow(
                $"{user?.FirstName} {user?.LastName}",
                c.Role.Name,
                user?.Email ?? "—"));
        }

        var timeline = ev.Scans
            .OrderByDescending(s => s.ScannedAt)
            .Take(50)
            .Select(s =>
            {
                var tkt = ev.Tickets.FirstOrDefault(t => t.Hash == s.Hash);
                return new ScanTimelineRow(s.ScannedAt, tkt?.OwnerName ?? "Gate", s.Result);
            }).ToList();

        return new EventSummaryDto(
            ev.CalendarEvent.Title,
            ev.CalendarEvent.StartDate,
            ev.Venue,
            ev.Budget,
            ev.ActualSpend,
            ev.MaxParticipants,
            totalRSVPs,
            totalScanned,
            gateTickets.Count,
            gateTickets.Count,
            Math.Round(rate, 1),
            coordRows,
            timeline);
    }

    // ─── Candidates ───────────────────────────────────────────────────────────

    public async Task<List<CoordinatorCandidateDto>> GetCoordinatorCandidatesAsync()
    {
        var result = new List<CoordinatorCandidateDto>();

        // Teachers
        var teachers = await _db.Teachers.Where(t => t.IsActive && t.UserId != null).ToListAsync();
        foreach (var t in teachers)
        {
            var u = await _userManager.FindByIdAsync(t.UserId!);
            if (u != null)
                result.Add(new CoordinatorCandidateDto(
                    t.UserId!, $"{t.FirstName} {t.LastName}", u.Email ?? "", "Teacher"));
        }

        // Learners with user accounts (trusted learners)
        var learnerUsers = await _userManager.GetUsersInRoleAsync("Learner");
        foreach (var u in learnerUsers.Where(u => u.IsActive))
        {
            result.Add(new CoordinatorCandidateDto(
                u.Id, $"{u.FirstName} {u.LastName}", u.Email ?? "", "Learner"));
        }

        return result.OrderBy(c => c.Name).ToList();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string GenerateHash()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLower();

    public async Task<SchoolEvent?> GetSchoolEventAsync(int id)
    {
        var ev = await _db.SchoolEvents
            .Include(e => e.CalendarEvent)
            .Include(e => e.Coordinators).ThenInclude(c => c.Role)
            .Include(e => e.Tickets)
            .Include(e => e.RSVPs)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null) return null;

        // Populate the NotMapped display properties
        foreach (var coord in ev.Coordinators.Where(c => c.IsActive))
        {
            var user = await _userManager.FindByIdAsync(coord.UserId);
            coord.CoordinatorName = user != null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            coord.CoordinatorEmail = user?.Email ?? string.Empty;

            // Determine if teacher or learner
            var roles = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();
            coord.CoordinatorType = roles.Contains("Teacher") ? "Teacher" : "Learner";
        }

        return ev;
    }
    
}
