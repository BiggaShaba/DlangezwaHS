using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DlangezwaHS.Web.Models.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// ENUMS
// ─────────────────────────────────────────────────────────────────────────────

public enum ActivityType { Sport, Cultural, AcademicClub, Other }
public enum CoachRole { HeadCoach, AssistantCoach, Mentor, Supervisor }
public enum AssignmentPeriod { Term1, Term2, Term3, Term4, FullYear }
public enum RegistrationStatus { Registered, Withdrawn }
public enum ActivityAttendanceStatus { Present, Absent, Late, Excused }
public enum AchievementType { Certificate, Award, Trophy, Position, Other }
public enum AchievementLevel { School, District, Provincial, National }

// ─────────────────────────────────────────────────────────────────────────────
// ACTIVITY
// ─────────────────────────────────────────────────────────────────────────────

public class Activity
{
    public int Id { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = "";
    public ActivityType Type { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
    [StringLength(150)] public string? Venue { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int MaxParticipants { get; set; }
    public int? MinGradeLevel { get; set; } // null + null = "All grades"
    public int? MaxGradeLevel { get; set; }
    public bool RegistrationOpen { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ActivityCoach> Coaches { get; set; } = new List<ActivityCoach>();
    public ICollection<ActivityRegistration> Registrations { get; set; } = new List<ActivityRegistration>();
    public ICollection<ActivitySession> Sessions { get; set; } = new List<ActivitySession>();
    public ICollection<Achievement> Achievements { get; set; } = new List<Achievement>();

    [NotMapped]
    public string TargetGroupLabel =>
        MinGradeLevel is null && MaxGradeLevel is null ? "All Grades"
        : MinGradeLevel == MaxGradeLevel ? $"Grade {MinGradeLevel}"
        : $"Grade {MinGradeLevel}-{MaxGradeLevel}";
}

public class ActivityCoach
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    [ForeignKey(nameof(ActivityId))] public Activity Activity { get; set; } = null!;
    public int TeacherId { get; set; }
    [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;
    public CoachRole Role { get; set; }
    public AssignmentPeriod Period { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class ActivityRegistration
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    [ForeignKey(nameof(ActivityId))] public Activity Activity { get; set; } = null!;
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Registered;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

// One dated session of an activity — the register is taken per session
public class ActivitySession
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    [ForeignKey(nameof(ActivityId))] public Activity Activity { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Today;
    public string? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ActivityAttendance> Attendances { get; set; } = new List<ActivityAttendance>();
}

public class ActivityAttendance
{
    public int Id { get; set; }
    public int ActivitySessionId { get; set; }
    [ForeignKey(nameof(ActivitySessionId))] public ActivitySession ActivitySession { get; set; } = null!;
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public ActivityAttendanceStatus Status { get; set; } = ActivityAttendanceStatus.Present;
    [StringLength(300)] public string? Note { get; set; }
}

public class Achievement
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public int ActivityId { get; set; }
    [ForeignKey(nameof(ActivityId))] public Activity Activity { get; set; } = null!;
    public AchievementType Type { get; set; }
    [Required, StringLength(500)] public string Description { get; set; } = "";
    public DateTime DateAchieved { get; set; } = DateTime.Today;
    public AchievementLevel Level { get; set; }
    public string? DocumentPath { get; set; }
    public string RecordedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
