using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DlangezwaHS.Web.Models.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// ENUM
// ─────────────────────────────────────────────────────────────────────────────

public enum CalendarEventType
{
    Holiday      = 0,
    Announcement = 1,
    Notice       = 2,
    SchoolEvent  = 3,
    Assessment   = 4   // auto-created when teacher saves an assessment
}

// ─────────────────────────────────────────────────────────────────────────────
// MODEL
// ─────────────────────────────────────────────────────────────────────────────

public class CalendarEvent
{
    public int    Id          { get; set; }

    [Required, MaxLength(200)]
    public string Title       { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    public DateTime   StartDate { get; set; }
    public DateTime?  EndDate   { get; set; }

    [Required]
    public CalendarEventType Type { get; set; } = CalendarEventType.SchoolEvent;

    // ── Visibility scope ─────────────────────────────────────────────────────
    // null = visible to everyone; set for assessment events
    public int? GradeId   { get; set; }
    public int? ClassId   { get; set; }

    // FK to Assessment (only for Type == Assessment)
    public int? AssessmentId { get; set; }

    // ── Email notification flag ───────────────────────────────────────────────
    public bool NotificationSent { get; set; } = false;

    // ── Audit ─────────────────────────────────────────────────────────────────
    public string  CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public bool    IsActive        { get; set; } = true;

    // ── Navigation ───────────────────────────────────────────────────────────
    [ForeignKey(nameof(GradeId))]
    public virtual Grade?      Grade      { get; set; }

    [ForeignKey(nameof(ClassId))]
    public virtual Class?      Class      { get; set; }

    [ForeignKey(nameof(AssessmentId))]
    public virtual Assessment? Assessment { get; set; }

    // ── Helpers ──────────────────────────────────────────────────────────────
    [NotMapped]
    public string BadgeColor => Type switch
    {
        CalendarEventType.Holiday      => "#ef4444",   // red
        CalendarEventType.Announcement => "#f59e0b",   // amber
        CalendarEventType.Notice       => "#3b82f6",   // blue
        CalendarEventType.SchoolEvent  => "#10b981",   // green
        CalendarEventType.Assessment   => "#8b5cf6",   // purple
        _                              => "#6b7280"
    };

    [NotMapped]
    public string TypeLabel => Type switch
    {
        CalendarEventType.Holiday      => "Holiday",
        CalendarEventType.Announcement => "Announcement",
        CalendarEventType.Notice       => "Notice",
        CalendarEventType.SchoolEvent  => "Event",
        CalendarEventType.Assessment   => "Assessment",
        _                              => "Other"
    };
}
