using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize]
public class ExtracurricularController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IExtracurricularService _svc;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPdfService _pdf;
    private readonly ILogger<ExtracurricularController> _logger;

    public ExtracurricularController(ApplicationDbContext db, IExtracurricularService svc,
        UserManager<ApplicationUser> userManager, IPdfService pdf, ILogger<ExtracurricularController> logger)
    {
        _db = db;
        _svc = svc;
        _userManager = userManager;
        _pdf = pdf;
        _logger = logger;
    }

    private string UserId => _userManager.GetUserId(User)!;

    private async Task<Teacher?> GetCurrentTeacherAsync()
        => await _db.Teachers.FirstOrDefaultAsync(t => t.UserId == UserId);

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> Index()
    {
        var teacher = User.IsInRole("Teacher") ? await GetCurrentTeacherAsync() : null;
        var myActivities = teacher is not null ? await _svc.GetActivitiesForCoachAsync(teacher.Id) : new List<Activity>();

        return View(new ExtracurricularDashboardViewModel
        {
            IsCoach = teacher?.IsCoach ?? false,
            TotalActivities = (await _svc.GetActivitiesAsync()).Count,
            TotalRegistrations = await _db.ActivityRegistrations.CountAsync(r => r.Status == RegistrationStatus.Registered),
            TotalAchievements = await _db.Achievements.CountAsync(),
            MyActivities = myActivities
        });
    }

    // ── Schedule Activities (UC6) ────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher,Parent,Learner")]
    public async Task<IActionResult> Activities(ActivityType? type)
    {
        var activities = await _svc.GetActivitiesAsync(type);

        if (User.IsInRole("Parent"))
        {
            ViewBag.MyLearners = await _db.Learners.Where(l => l.ParentId == UserId).ToListAsync();
            var learnerIds = ((List<Learner>)ViewBag.MyLearners).Select(l => l.Id).ToList();
            var registered = await _db.ActivityRegistrations
                .Where(r => learnerIds.Contains(r.LearnerId) && r.Status == RegistrationStatus.Registered)
                .Select(r => new { r.ActivityId, r.LearnerId })
                .ToListAsync();
            ViewBag.Registrations = registered.Select(r => (r.ActivityId, r.LearnerId)).ToList();
        }

        ViewBag.TypeFilter = type;
        return View(activities);
    }

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> ActivityCreate(int? id)
    {
        var vm = new ActivityCreateViewModel { Grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync() };
        if (id.HasValue)
        {
            var a = await _svc.GetActivityAsync(id.Value);
            if (a is null) return NotFound();
            vm.Id = a.Id;
            vm.Name = a.Name;
            vm.Type = a.Type;
            vm.Description = a.Description;
            vm.Venue = a.Venue;
            vm.StartTime = a.StartTime;
            vm.EndTime = a.EndTime;
            vm.MaxParticipants = a.MaxParticipants;
            vm.MinGradeLevel = a.MinGradeLevel;
            vm.MaxGradeLevel = a.MaxGradeLevel;
            vm.RegistrationOpen = a.RegistrationOpen;
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> ActivityCreate(ActivityCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync();
            return View(vm);
        }

        try
        {
            var data = new ActivityData(vm.Name, vm.Type, vm.Description, vm.Venue, vm.StartTime, vm.EndTime,
                vm.MaxParticipants, vm.MinGradeLevel, vm.MaxGradeLevel, vm.RegistrationOpen);
            var activity = await _svc.SaveActivityAsync(vm.Id, data, UserId);
            TempData["Success"] = $"Activity '{activity.Name}' saved and published.";
            return RedirectToAction(nameof(Activities));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving activity");
            TempData["Error"] = ex.Message;
            vm.Grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync();
            return View(vm);
        }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Parent")]
    public async Task<IActionResult> Register(int activityId, int learnerId)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == UserId);
        if (learner is null) return Forbid();

        try
        {
            await _svc.RegisterLearnerAsync(activityId, learnerId);
            TempData["Success"] = $"{learner.FullName} registered for the activity.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Activities));
    }

    // ── Assign Coaches (UC7) ─────────────────────────────────────────────────

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignCoach()
    {
        return View(new AssignCoachViewModel
        {
            Activities = await _svc.GetActivitiesAsync(),
            Teachers = await _db.Teachers.Where(t => t.IsActive).OrderBy(t => t.LastName).ToListAsync()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignCoach(AssignCoachViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Activities = await _svc.GetActivitiesAsync();
            vm.Teachers = await _db.Teachers.Where(t => t.IsActive).OrderBy(t => t.LastName).ToListAsync();
            return View(vm);
        }

        try
        {
            await _svc.AssignCoachAsync(vm.ActivityId, vm.TeacherId, vm.Role, vm.Period, UserId);
            TempData["Success"] = "Coach assigned successfully.";
            return RedirectToAction(nameof(AssignCoach));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning coach");
            TempData["Error"] = ex.Message;
            vm.Activities = await _svc.GetActivitiesAsync();
            vm.Teachers = await _db.Teachers.Where(t => t.IsActive).OrderBy(t => t.LastName).ToListAsync();
            return View(vm);
        }
    }

    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> MyAssignments()
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher is null || !teacher.IsCoach) return Forbid();

        var activities = await _svc.GetActivitiesForCoachAsync(teacher.Id);
        return View(activities);
    }

    // ── Attendance Register (UC8) ────────────────────────────────────────────

    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AttendanceRegister(int activityId, DateTime? date = null)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher is null) return NotFound();

        if (!await _svc.IsCoachForActivityAsync(teacher.Id, activityId)) return Forbid();

        var activity = await _svc.GetActivityAsync(activityId);
        if (activity is null) return NotFound();

        var session = await _svc.GetOrCreateSessionAsync(activityId, date ?? DateTime.Today);
        var roster = await _svc.GetSessionRosterAsync(session.Id);

        return View(new ActivityAttendanceViewModel
        {
            ActivityId = activityId,
            SessionId = session.Id,
            Date = session.Date,
            ActivityName = activity.Name,
            Learners = roster.Select(r => new ActivityAttendanceMarkRow
            {
                LearnerId = r.LearnerId,
                FullName = r.FullName,
                Status = r.Status,
                Note = r.Note
            }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Teacher")]
    public async Task<IActionResult> SubmitAttendance(ActivityAttendanceViewModel vm)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher is null || !await _svc.IsCoachForActivityAsync(teacher.Id, vm.ActivityId)) return Forbid();

        try
        {
            var marks = vm.Learners.Select(l => (l.LearnerId, l.Status, l.Note)).ToList();
            await _svc.SubmitAttendanceAsync(vm.SessionId, marks, UserId);
            TempData["Success"] = "Attendance register saved.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving activity attendance");
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(AttendanceRegister), new { activityId = vm.ActivityId, date = vm.Date.ToString("yyyy-MM-dd") });
    }

    [Authorize(Roles = "Parent,Learner")]
    public async Task<IActionResult> LearnerAttendanceHistory(int learnerId, string? from = null, string? to = null)
    {
        // A learner only ever sees their own history
        var learner = User.IsInRole("Learner")
            ? await _db.Learners.FirstOrDefaultAsync(l => l.UserId == UserId)
            : await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == UserId);
        if (learner is null) return NotFound();

        var fromDate = from is not null ? DateTime.Parse(from) : DateTime.Today.AddDays(-90);
        var toDate = to is not null ? DateTime.Parse(to) : DateTime.Today;

        var history = await _svc.GetAttendanceHistoryAsync(learnerId, fromDate, toDate);
        ViewBag.LearnerName = learner.FullName;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;
        return View(history);
    }

    // ── Activities Achievement (UC9) ─────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher,Parent,Learner")]
    public async Task<IActionResult> Achievements(int? learnerId)
    {
        if (User.IsInRole("Learner"))
        {
            var me = await _db.Learners.FirstOrDefaultAsync(l => l.UserId == UserId);
            return View(me is null ? new List<Achievement>() : await _svc.GetAchievementsForLearnerAsync(me.Id));
        }

        if (User.IsInRole("Parent"))
        {
            if (!learnerId.HasValue)
            {
                var learners = await _db.Learners.Where(l => l.ParentId == UserId).ToListAsync();
                return View(learners.Count > 0
                    ? await _svc.GetAchievementsForLearnerAsync(learners[0].Id)
                    : new List<Achievement>());
            }
            var owned = await _db.Learners.AnyAsync(l => l.Id == learnerId.Value && l.ParentId == UserId);
            if (!owned) return Forbid();
            return View(await _svc.GetAchievementsForLearnerAsync(learnerId.Value));
        }

        return View(await _svc.GetAllAchievementsAsync());
    }

    [HttpGet, Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> AchievementCreate()
    {
        var vm = new AchievementCreateViewModel { Learners = await _db.Learners.OrderBy(l => l.LastName).ToListAsync() };

        if (User.IsInRole("Teacher"))
        {
            var teacher = await GetCurrentTeacherAsync();
            vm.Activities = teacher is not null ? await _svc.GetActivitiesForCoachAsync(teacher.Id) : new List<Activity>();
        }
        else
        {
            vm.Activities = await _svc.GetActivitiesAsync();
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> AchievementCreate(AchievementCreateViewModel vm)
    {
        if (User.IsInRole("Teacher"))
        {
            var teacher = await GetCurrentTeacherAsync();
            if (teacher is null || !await _svc.IsCoachForActivityAsync(teacher.Id, vm.ActivityId)) return Forbid();
        }

        if (!ModelState.IsValid)
        {
            vm.Learners = await _db.Learners.OrderBy(l => l.LastName).ToListAsync();
            vm.Activities = await _svc.GetActivitiesAsync();
            return View(vm);
        }

        try
        {
            var data = new AchievementData(vm.LearnerId, vm.ActivityId, vm.Type, vm.Description, vm.DateAchieved, vm.Level);
            await _svc.AddAchievementAsync(data, vm.Document, UserId);
            TempData["Success"] = "Achievement recorded and the family has been notified.";
            return RedirectToAction(nameof(Achievements));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording achievement");
            TempData["Error"] = ex.Message;
            vm.Learners = await _db.Learners.OrderBy(l => l.LastName).ToListAsync();
            vm.Activities = await _svc.GetActivitiesAsync();
            return View(vm);
        }
    }

    // ── Activity Report (UC10) ───────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> Report(ActivityType? type, DateTime? from, DateTime? to, int? gradeLevel, int? activityId)
    {
        int? coachTeacherId = null;
        if (User.IsInRole("Teacher"))
        {
            var teacher = await GetCurrentTeacherAsync();
            coachTeacherId = teacher?.Id;
        }

        var filter = new ActivityReportFilter(type, from, to, gradeLevel, activityId, coachTeacherId);
        var report = await _svc.GetReportAsync(filter);

        return View(new ActivityReportViewModel
        {
            Type = type,
            From = from,
            To = to,
            GradeLevel = gradeLevel,
            ActivityId = activityId,
            Activities = await _svc.GetActivitiesAsync(),
            Report = report
        });
    }

    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> ReportPdf(ActivityType? type, DateTime? from, DateTime? to, int? gradeLevel, int? activityId)
    {
        int? coachTeacherId = null;
        if (User.IsInRole("Teacher"))
        {
            var teacher = await GetCurrentTeacherAsync();
            coachTeacherId = teacher?.Id;
        }

        var report = await _svc.GetReportAsync(new ActivityReportFilter(type, from, to, gradeLevel, activityId, coachTeacherId));
        var bytes = _pdf.GenerateActivityReportPdf(report);
        return File(bytes, "application/pdf", $"activity-report-{DateTime.Now:yyyyMMdd}.pdf");
    }

    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> ReportPrint(ActivityType? type, DateTime? from, DateTime? to, int? gradeLevel, int? activityId)
    {
        int? coachTeacherId = null;
        if (User.IsInRole("Teacher"))
        {
            var teacher = await GetCurrentTeacherAsync();
            coachTeacherId = teacher?.Id;
        }

        var report = await _svc.GetReportAsync(new ActivityReportFilter(type, from, to, gradeLevel, activityId, coachTeacherId));
        return View(new ActivityReportViewModel { Type = type, From = from, To = to, GradeLevel = gradeLevel, ActivityId = activityId, Report = report });
    }
}
