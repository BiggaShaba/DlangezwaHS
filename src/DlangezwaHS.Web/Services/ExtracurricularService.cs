using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DlangezwaHS.Web.Services;

public interface IExtracurricularService
{
    // Schedule Activities (UC6)
    Task<Activity> SaveActivityAsync(int? id, ActivityData data, string userId);
    Task<List<Activity>> GetActivitiesAsync(ActivityType? type = null, bool activeOnly = true);
    Task<Activity?> GetActivityAsync(int id);
    Task<List<Activity>> GetActivitiesForLearnerAsync(int learnerId);
    Task RegisterLearnerAsync(int activityId, int learnerId);
    Task<bool> IsLearnerRegisteredAsync(int activityId, int learnerId);

    // Assign Coaches (UC7)
    Task AssignCoachAsync(int activityId, int teacherId, CoachRole role, AssignmentPeriod period, string userId);
    Task<List<Activity>> GetActivitiesForCoachAsync(int teacherId);
    Task<bool> IsCoachForActivityAsync(int teacherId, int activityId);

    // Attendance Register (UC8)
    Task<ActivitySession> GetOrCreateSessionAsync(int activityId, DateTime date);
    Task<List<ActivityAttendanceRow>> GetSessionRosterAsync(int sessionId);
    Task SubmitAttendanceAsync(int sessionId, List<(int LearnerId, ActivityAttendanceStatus Status, string? Note)> marks, string userId);
    Task<List<ActivityAttendance>> GetAttendanceHistoryAsync(int learnerId, DateTime from, DateTime to);

    // Achievements (UC9)
    Task<Achievement> AddAchievementAsync(AchievementData data, IFormFile? document, string userId);
    Task<List<Achievement>> GetAchievementsForLearnerAsync(int learnerId);
    Task<List<Achievement>> GetAllAchievementsAsync();

    // Activity Report (UC10)
    Task<ActivityReportDto> GetReportAsync(ActivityReportFilter filter);
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record ActivityData(
    string Name, ActivityType Type, string? Description, string? Venue,
    TimeSpan StartTime, TimeSpan EndTime, int MaxParticipants,
    int? MinGradeLevel, int? MaxGradeLevel, bool RegistrationOpen
);

public record ActivityAttendanceRow(int LearnerId, string FullName, ActivityAttendanceStatus Status, string? Note);

public record AchievementData(
    int LearnerId, int ActivityId, AchievementType Type, string Description,
    DateTime DateAchieved, AchievementLevel Level
);

public record ActivityReportFilter(ActivityType? Type, DateTime? From, DateTime? To, int? GradeLevel, int? ActivityId, int? CoachTeacherId);

public record ActivityReportDto(
    int TotalActivities, int TotalParticipants,
    double AverageAttendancePct, List<(string ActivityName, int SessionCount, double AttendancePct)> AttendanceByActivity,
    int TotalAchievements, List<(string LearnerName, int Count)> TopAchievers,
    List<(string CoachName, List<string> Activities)> CoachSummary,
    List<(string LearnerName, int ActivityCount)> ParticipationSummary,
    List<(string TypeName, int Count)> ActivitiesByType,
    List<(string ActivityName, int Participants)> ParticipantsByActivity,
    List<(string ActivityName, int Count)> AchievementsByActivity
);

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class ExtracurricularService : IExtracurricularService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly IAuditService _audit;
    private readonly IDocumentService _docs;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtracurricularService> _logger;

    public ExtracurricularService(ApplicationDbContext db, IEmailService email, IAuditService audit,
        IDocumentService docs, UserManager<ApplicationUser> userManager,
        IServiceScopeFactory scopeFactory, ILogger<ExtracurricularService> logger)
    {
        _db = db;
        _email = email;
        _audit = audit;
        _docs = docs;
        _userManager = userManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ─── Schedule Activities (UC6) ───────────────────────────────────────────

    public async Task<Activity> SaveActivityAsync(int? id, ActivityData data, string userId)
    {
        Activity activity;
        bool isNew = !id.HasValue;
        if (id.HasValue)
        {
            activity = await _db.Activities.FindAsync(id.Value) ?? throw new InvalidOperationException("Activity not found.");
        }
        else
        {
            activity = new Activity { CreatedByUserId = userId };
            _db.Activities.Add(activity);
        }

        activity.Name = data.Name;
        activity.Type = data.Type;
        activity.Description = data.Description;
        activity.Venue = data.Venue;
        activity.StartTime = data.StartTime;
        activity.EndTime = data.EndTime;
        activity.MaxParticipants = data.MaxParticipants;
        activity.MinGradeLevel = data.MinGradeLevel;
        activity.MaxGradeLevel = data.MaxGradeLevel;
        activity.RegistrationOpen = data.RegistrationOpen;

        await _db.SaveChangesAsync();

        if (isNew)
            _ = SendActivityPublishedEmailsAsync(activity.Id);

        return activity;
    }

    private async Task SendActivityPublishedEmailsAsync(int activityId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var activity = await db.Activities.FindAsync(activityId);
            if (activity is null) return;

            var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var teachers = await db.Teachers.Where(t => t.IsActive && t.UserId != null).ToListAsync();
            foreach (var t in teachers)
            {
                var user = await userManager.FindByIdAsync(t.UserId!);
                if (user?.Email is null || !sent.Add(user.Email)) continue;
                await email.SendCalendarNotificationAsync(user.Email, user.FullName,
                    activity.Name, "New Extracurricular Activity", DateTime.Today,
                    $"{activity.Type} · {activity.TargetGroupLabel} · {activity.Venue}");
            }

            var parents = await userManager.GetUsersInRoleAsync("Parent");
            foreach (var p in parents.Where(p => p.IsActive))
            {
                if (p.Email is null || !sent.Add(p.Email)) continue;
                await email.SendCalendarNotificationAsync(p.Email, p.FullName,
                    activity.Name, "New Extracurricular Activity", DateTime.Today,
                    $"{activity.Type} · {activity.TargetGroupLabel} · {activity.Venue}. Registration is now open.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send activity-published emails for activity {ActivityId}", activityId);
        }
    }

    public async Task<List<Activity>> GetActivitiesAsync(ActivityType? type = null, bool activeOnly = true)
    {
        var q = _db.Activities
            .Include(a => a.Coaches).ThenInclude(c => c.Teacher)
            .Include(a => a.Registrations)
            .AsQueryable();
        if (activeOnly) q = q.Where(a => a.IsActive);
        if (type.HasValue) q = q.Where(a => a.Type == type.Value);
        return await q.OrderBy(a => a.Name).ToListAsync();
    }

    public async Task<Activity?> GetActivityAsync(int id)
        => await _db.Activities
            .Include(a => a.Coaches).ThenInclude(c => c.Teacher)
            .Include(a => a.Registrations).ThenInclude(r => r.Learner)
            .FirstOrDefaultAsync(a => a.Id == id);

    public async Task<List<Activity>> GetActivitiesForLearnerAsync(int learnerId)
        => await _db.ActivityRegistrations
            .Where(r => r.LearnerId == learnerId && r.Status == RegistrationStatus.Registered)
            .Include(r => r.Activity)
            .Select(r => r.Activity)
            .ToListAsync();

    public async Task RegisterLearnerAsync(int activityId, int learnerId)
    {
        var activity = await _db.Activities.Include(a => a.Registrations)
            .FirstOrDefaultAsync(a => a.Id == activityId)
            ?? throw new InvalidOperationException("Activity not found.");

        if (!activity.RegistrationOpen)
            throw new InvalidOperationException("Registration is closed for this activity.");

        var existing = activity.Registrations.FirstOrDefault(r => r.LearnerId == learnerId);
        if (existing is not null)
        {
            if (existing.Status == RegistrationStatus.Registered) return;
            existing.Status = RegistrationStatus.Registered;
            existing.RegisteredAt = DateTime.UtcNow;
        }
        else
        {
            var activeCount = activity.Registrations.Count(r => r.Status == RegistrationStatus.Registered);
            if (activity.MaxParticipants > 0 && activeCount >= activity.MaxParticipants)
                throw new InvalidOperationException("This activity is fully booked.");

            _db.ActivityRegistrations.Add(new ActivityRegistration { ActivityId = activityId, LearnerId = learnerId });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsLearnerRegisteredAsync(int activityId, int learnerId)
        => await _db.ActivityRegistrations.AnyAsync(r =>
            r.ActivityId == activityId && r.LearnerId == learnerId && r.Status == RegistrationStatus.Registered);

    // ─── Assign Coaches (UC7) ────────────────────────────────────────────────

    public async Task AssignCoachAsync(int activityId, int teacherId, CoachRole role, AssignmentPeriod period, string userId)
    {
        var teacher = await _db.Teachers.FindAsync(teacherId) ?? throw new InvalidOperationException("Teacher not found.");
        var activity = await _db.Activities.FindAsync(activityId) ?? throw new InvalidOperationException("Activity not found.");

        var existing = await _db.ActivityCoaches.FirstOrDefaultAsync(c => c.ActivityId == activityId && c.TeacherId == teacherId);
        if (existing is not null)
        {
            existing.Role = role;
            existing.Period = period;
            existing.IsActive = true;
        }
        else
        {
            _db.ActivityCoaches.Add(new ActivityCoach
            {
                ActivityId = activityId,
                TeacherId = teacherId,
                Role = role,
                Period = period
            });
        }

        if (!teacher.IsCoach) teacher.IsCoach = true;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, null, "CoachAssigned", "Activity", activityId.ToString(),
            $"Teacher {teacherId} as {role} for {period}");

        if (teacher.UserId is not null)
        {
            var user = await _userManager.FindByIdAsync(teacher.UserId);
            if (user?.Email is not null)
                await _email.SendAsync(user.Email, teacher.FullName,
                    $"Coaching Assignment — {activity.Name}",
                    $"<p>Dear {teacher.FullName},</p><p>You have been assigned as <strong>{role}</strong> for <strong>{activity.Name}</strong> ({period}).</p>");
        }
    }

    public async Task<List<Activity>> GetActivitiesForCoachAsync(int teacherId)
        => await _db.ActivityCoaches
            .Where(c => c.TeacherId == teacherId && c.IsActive)
            .Include(c => c.Activity)
            .Select(c => c.Activity)
            .ToListAsync();

    public async Task<bool> IsCoachForActivityAsync(int teacherId, int activityId)
        => await _db.ActivityCoaches.AnyAsync(c => c.TeacherId == teacherId && c.ActivityId == activityId && c.IsActive);

    // ─── Attendance Register (UC8) ───────────────────────────────────────────

    public async Task<ActivitySession> GetOrCreateSessionAsync(int activityId, DateTime date)
    {
        date = date.Date;
        var session = await _db.ActivitySessions.FirstOrDefaultAsync(s => s.ActivityId == activityId && s.Date == date);
        if (session is null)
        {
            session = new ActivitySession { ActivityId = activityId, Date = date };
            _db.ActivitySessions.Add(session);
            await _db.SaveChangesAsync();
        }
        return session;
    }

    public async Task<List<ActivityAttendanceRow>> GetSessionRosterAsync(int sessionId)
    {
        var session = await _db.ActivitySessions.FindAsync(sessionId)
            ?? throw new InvalidOperationException("Session not found.");

        var roster = await _db.ActivityRegistrations
            .Where(r => r.ActivityId == session.ActivityId && r.Status == RegistrationStatus.Registered)
            .Include(r => r.Learner)
            .OrderBy(r => r.Learner.LastName).ThenBy(r => r.Learner.FirstName)
            .ToListAsync();

        var existing = await _db.ActivityAttendances.Where(a => a.ActivitySessionId == sessionId).ToListAsync();

        return roster.Select(r =>
        {
            var rec = existing.FirstOrDefault(a => a.LearnerId == r.LearnerId);
            return new ActivityAttendanceRow(r.LearnerId, r.Learner.FullName,
                rec?.Status ?? ActivityAttendanceStatus.Present, rec?.Note);
        }).ToList();
    }

    public async Task SubmitAttendanceAsync(int sessionId, List<(int LearnerId, ActivityAttendanceStatus Status, string? Note)> marks, string userId)
    {
        var session = await _db.ActivitySessions.FindAsync(sessionId)
            ?? throw new InvalidOperationException("Session not found.");

        var existing = await _db.ActivityAttendances.Where(a => a.ActivitySessionId == sessionId).ToListAsync();
        foreach (var (learnerId, status, note) in marks)
        {
            var rec = existing.FirstOrDefault(a => a.LearnerId == learnerId);
            if (rec is null)
            {
                rec = new ActivityAttendance { ActivitySessionId = sessionId, LearnerId = learnerId };
                _db.ActivityAttendances.Add(rec);
            }
            rec.Status = status;
            rec.Note = note;
        }

        session.SubmittedByUserId = userId;
        session.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<ActivityAttendance>> GetAttendanceHistoryAsync(int learnerId, DateTime from, DateTime to)
        => await _db.ActivityAttendances
            .Include(a => a.ActivitySession).ThenInclude(s => s.Activity)
            .Where(a => a.LearnerId == learnerId
                     && a.ActivitySession.Date >= from.Date && a.ActivitySession.Date <= to.Date)
            .OrderByDescending(a => a.ActivitySession.Date)
            .ToListAsync();

    // ─── Achievements (UC9) ──────────────────────────────────────────────────

    public async Task<Achievement> AddAchievementAsync(AchievementData data, IFormFile? document, string userId)
    {
        var learner = await _db.Learners.FindAsync(data.LearnerId) ?? throw new InvalidOperationException("Learner not found.");
        var activity = await _db.Activities.FindAsync(data.ActivityId) ?? throw new InvalidOperationException("Activity not found.");

        var achievement = new Achievement
        {
            LearnerId = data.LearnerId,
            ActivityId = data.ActivityId,
            Type = data.Type,
            Description = data.Description,
            DateAchieved = data.DateAchieved,
            Level = data.Level,
            DocumentPath = await _docs.SaveDocumentAsync(document, "achievements"),
            RecordedByUserId = userId
        };
        _db.Achievements.Add(achievement);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, null, "AchievementRecorded", "Achievement", achievement.Id.ToString(),
            $"Learner {data.LearnerId}: {data.Description}");

        if (learner.ParentId is not null)
        {
            var parent = await _userManager.FindByIdAsync(learner.ParentId);
            if (parent?.Email is not null)
                await _email.SendAsync(parent.Email, parent.FullName,
                    $"Achievement — {learner.FullName}",
                    $"<p>Dear {parent.FullName},</p><p>Congratulations! Your child <strong>{learner.FullName}</strong> received a <strong>{data.Type}</strong> in <strong>{activity.Name}</strong>:</p><p>{data.Description}</p><p>Level: {data.Level}</p>");
        }

        return achievement;
    }

    public async Task<List<Achievement>> GetAchievementsForLearnerAsync(int learnerId)
        => await _db.Achievements
            .Include(a => a.Activity)
            .Include(a => a.Learner)
            .Where(a => a.LearnerId == learnerId)
            .OrderByDescending(a => a.DateAchieved)
            .ToListAsync();

    public async Task<List<Achievement>> GetAllAchievementsAsync()
        => await _db.Achievements
            .Include(a => a.Activity)
            .Include(a => a.Learner)
            .OrderByDescending(a => a.DateAchieved)
            .ToListAsync();

    // ─── Activity Report (UC10) ──────────────────────────────────────────────

    public async Task<ActivityReportDto> GetReportAsync(ActivityReportFilter filter)
    {
        var fromDate = (filter.From ?? DateTime.Today.AddMonths(-6)).Date;
        var toDate = (filter.To ?? DateTime.Today).Date;

        var activitiesQ = _db.Activities.Where(a => a.IsActive).AsQueryable();
        if (filter.Type.HasValue) activitiesQ = activitiesQ.Where(a => a.Type == filter.Type.Value);
        if (filter.ActivityId.HasValue) activitiesQ = activitiesQ.Where(a => a.Id == filter.ActivityId.Value);
        if (filter.GradeLevel.HasValue)
            activitiesQ = activitiesQ.Where(a =>
                (a.MinGradeLevel == null && a.MaxGradeLevel == null) ||
                (a.MinGradeLevel <= filter.GradeLevel.Value && a.MaxGradeLevel >= filter.GradeLevel.Value));
        if (filter.CoachTeacherId.HasValue)
            activitiesQ = activitiesQ.Where(a => a.Coaches.Any(c => c.TeacherId == filter.CoachTeacherId.Value && c.IsActive));

        var activities = await activitiesQ.Include(a => a.Coaches).ThenInclude(c => c.Teacher).ToListAsync();
        var activityIds = activities.Select(a => a.Id).ToList();

        var totalParticipants = await _db.ActivityRegistrations
            .Where(r => activityIds.Contains(r.ActivityId) && r.Status == RegistrationStatus.Registered)
            .Select(r => r.LearnerId).Distinct().CountAsync();

        var sessions = await _db.ActivitySessions
            .Include(s => s.Attendances)
            .Where(s => activityIds.Contains(s.ActivityId) && s.Date >= fromDate && s.Date <= toDate)
            .ToListAsync();

        var attendanceByActivity = new List<(string, int, double)>();
        foreach (var a in activities)
        {
            var actSessions = sessions.Where(s => s.ActivityId == a.Id).ToList();
            if (actSessions.Count == 0) { attendanceByActivity.Add((a.Name, 0, 0)); continue; }
            var totalMarks = actSessions.Sum(s => s.Attendances.Count);
            var presentMarks = actSessions.Sum(s => s.Attendances.Count(x => x.Status == ActivityAttendanceStatus.Present));
            var pct = totalMarks > 0 ? Math.Round(presentMarks * 100.0 / totalMarks, 1) : 0;
            attendanceByActivity.Add((a.Name, actSessions.Count, pct));
        }
        var avgAttendance = attendanceByActivity.Count(x => x.Item2 > 0) > 0
            ? Math.Round(attendanceByActivity.Where(x => x.Item2 > 0).Average(x => x.Item3), 1) : 0;

        var achievements = await _db.Achievements
            .Include(a => a.Learner)
            .Where(a => activityIds.Contains(a.ActivityId) && a.DateAchieved >= fromDate && a.DateAchieved <= toDate)
            .ToListAsync();
        var topAchievers = achievements
            .GroupBy(a => a.Learner.FullName)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .Take(10)
            .ToList();

        var coachSummary = activities
            .SelectMany(a => a.Coaches.Where(c => c.IsActive).Select(c => (Coach: c.Teacher.FullName, Activity: a.Name)))
            .GroupBy(x => x.Coach)
            .Select(g => (g.Key, g.Select(x => x.Activity).Distinct().ToList()))
            .ToList();

        var registrations = await _db.ActivityRegistrations
            .Include(r => r.Learner)
            .Where(r => activityIds.Contains(r.ActivityId) && r.Status == RegistrationStatus.Registered)
            .ToListAsync();

        var participation = registrations
            .GroupBy(r => r.Learner.FullName)
            .Select(g => (Name: g.Key, Count: g.Select(r => r.ActivityId).Distinct().Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var activitiesByType = activities
            .GroupBy(a => a.Type.ToString())
            .Select(g => (TypeName: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var participantsByActivity = activities
            .Select(a => (a.Name, Participants: registrations.Count(r => r.ActivityId == a.Id)))
            .OrderByDescending(x => x.Participants)
            .ToList();

        var achievementsByActivity = activities
            .Select(a => (a.Name, Count: achievements.Count(x => x.ActivityId == a.Id)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToList();

        return new ActivityReportDto(
            activities.Count,
            totalParticipants,
            avgAttendance,
            attendanceByActivity,
            achievements.Count,
            topAchievers,
            coachSummary,
            participation,
            activitiesByType,
            participantsByActivity,
            achievementsByActivity
        );
    }
}
