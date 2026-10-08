using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// DASHBOARD
// ─────────────────────────────────────────────────────────────────────────────

public class ExtracurricularDashboardViewModel
{
    public bool IsCoach { get; set; }
    public int TotalActivities { get; set; }
    public int TotalRegistrations { get; set; }
    public int TotalAchievements { get; set; }
    public IList<Activity> MyActivities { get; set; } = new List<Activity>();
}

// ─────────────────────────────────────────────────────────────────────────────
// SCHEDULE ACTIVITIES (UC6)
// ─────────────────────────────────────────────────────────────────────────────

public class ActivityCreateViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = "";
    public ActivityType Type { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
    [StringLength(150)] public string? Venue { get; set; }
    public TimeSpan StartTime { get; set; } = new(14, 0, 0);
    public TimeSpan EndTime { get; set; } = new(15, 30, 0);
    public int MaxParticipants { get; set; } = 30;
    public int? MinGradeLevel { get; set; }
    public int? MaxGradeLevel { get; set; }
    public bool RegistrationOpen { get; set; } = true;

    public IList<Grade> Grades { get; set; } = new List<Grade>();
}

// ─────────────────────────────────────────────────────────────────────────────
// ASSIGN COACHES (UC7)
// ─────────────────────────────────────────────────────────────────────────────

public class AssignCoachViewModel
{
    [Required] public int ActivityId { get; set; }
    [Required] public int TeacherId { get; set; }
    public CoachRole Role { get; set; } = CoachRole.HeadCoach;
    public AssignmentPeriod Period { get; set; } = AssignmentPeriod.FullYear;

    public IList<Activity> Activities { get; set; } = new List<Activity>();
    public IList<Teacher> Teachers { get; set; } = new List<Teacher>();
}

// ─────────────────────────────────────────────────────────────────────────────
// ATTENDANCE REGISTER (UC8)
// ─────────────────────────────────────────────────────────────────────────────

public class ActivityAttendanceViewModel
{
    public int ActivityId { get; set; }
    public int SessionId { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string ActivityName { get; set; } = "";
    public IList<ActivityAttendanceMarkRow> Learners { get; set; } = new List<ActivityAttendanceMarkRow>();
}

public class ActivityAttendanceMarkRow
{
    public int LearnerId { get; set; }
    public string FullName { get; set; } = "";
    public ActivityAttendanceStatus Status { get; set; }
    public string? Note { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// ACHIEVEMENTS (UC9)
// ─────────────────────────────────────────────────────────────────────────────

public class AchievementCreateViewModel
{
    [Required] public int LearnerId { get; set; }
    [Required] public int ActivityId { get; set; }
    public AchievementType Type { get; set; }
    [Required, StringLength(500)] public string Description { get; set; } = "";
    public DateTime DateAchieved { get; set; } = DateTime.Today;
    public AchievementLevel Level { get; set; }
    public IFormFile? Document { get; set; }

    public IList<Learner> Learners { get; set; } = new List<Learner>();
    public IList<Activity> Activities { get; set; } = new List<Activity>();
}

// ─────────────────────────────────────────────────────────────────────────────
// ACTIVITY REPORT (UC10)
// ─────────────────────────────────────────────────────────────────────────────

public class ActivityReportViewModel
{
    public ActivityType? Type { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? GradeLevel { get; set; }
    public int? ActivityId { get; set; }

    public IList<Activity> Activities { get; set; } = new List<Activity>();
    public ActivityReportDto? Report { get; set; }
}
