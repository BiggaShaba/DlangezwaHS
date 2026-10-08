using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;
using static DlangezwaHS.Web.Models.Domain.QuestionPaper;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// SCHOOL DAY CONSTANTS
// ─────────────────────────────────────────────────────────────────────────────

public static class SchoolDay
{
    public static readonly string[] DayNames = { "", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

    /// <summary>All instructional period slots (breaks excluded).</summary>
    public static readonly PeriodInfo[] Periods =
    {
        new(1, "Period 1", "09:00", "09:50", allDays: true),
        new(2, "Period 2", "09:50", "10:40", allDays: true),
        new(3, "Period 3", "10:55", "11:45", allDays: true),
        new(4, "Period 4", "11:45", "12:35", allDays: true),
        new(5, "Period 5", "12:35", "13:25", allDays: true),
        new(6, "Period 6", "13:40", "14:30", allDays: true),
        new(7, "Period 7", "14:30", "15:00", monThuOnly: true),
    };

    /// <summary>Returns false for Period 7 on Friday.</summary>
    public static bool IsPeriodAvailable(int periodNumber, int day) =>
        periodNumber != 7 || day != 5;

    /// <summary>Break rows injected between periods in the UI.</summary>
    public static readonly BreakInfo[] Breaks =
    {
        new("Break 1", "10:40", "10:55", AfterPeriod: 2),
        new("Break 2", "13:25", "13:40", AfterPeriod: 5),
    };

    /// <summary>Assembly rows rendered at the top on Mon and Fri.</summary>
    public const string AssemblyTime = "08:30 – 09:00";

    // Subject colour palette (10 colours, cycles by subject ID)
    private static readonly string[] Palette =
    {
        "#4A90D9", "#27AE60", "#E67E22", "#8E44AD", "#E74C3C",
        "#16A085", "#2980B9", "#F39C12", "#D35400", "#1ABC9C"
    };
    public static string DefaultColor(int subjectId) => Palette[subjectId % Palette.Length];
}

public record PeriodInfo(int Number, string Label, string Start, string End,
    bool allDays = true, bool monThuOnly = false);
public record BreakInfo(string Label, string Start, string End, int AfterPeriod);

// ─────────────────────────────────────────────────────────────────────────────
// VIEW MODELS
// ─────────────────────────────────────────────────────────────────────────────

public class TimetableGridVm
{
    /// <summary>Rows indexed by [day 1-5][period 1-7] → slot (or null)</summary>
    public TimetableSlot?[,] Grid { get; set; } = new TimetableSlot?[6, 8];
    public List<TimetableSlot> AllSlots { get; set; } = new();
    public Class? SelectedClass { get; set; }
    public string Term { get; set; } = "Term 1";
    public int AcademicYear { get; set; } = DateTime.UtcNow.Year;
    public Dictionary<int, string> SubjectColors { get; set; } = new();
    /// <summary>Available subject-teacher assignments for the selected class (left-panel cards)</summary>
    public List<TeacherClassSubject> AvailableAssignments { get; set; } = new();
}

public class TeacherDayVm
{
    public Teacher Teacher { get; set; } = null!;
    public string Term { get; set; } = "Term 1";
    public int AcademicYear { get; set; }
    /// <summary>Slots for a full week grouped by day then period</summary>
    public List<TimetableSlot> Slots { get; set; } = new();
    public Dictionary<int, string> SubjectColors { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface ITimetableService
{
    Task<TimetableGridVm> GetGridAsync(int classId, string term, int year);
    Task<TeacherDayVm> GetTeacherWeekAsync(int teacherId, string term, int year);
    Task<TimetableGridVm> GetLearnerTimetableAsync(int enrollmentId, string term, int year);
    Task<TimetableSlot> SaveSlotAsync(int classId, int day, int period, int subjectId, int teacherId, string term, int year, string adminUserId);
    Task RemoveSlotAsync(int slotId, string adminUserId);
    Task<Dictionary<int, string>> GetColorsAsync(IEnumerable<int> subjectIds);
    Task SetColorAsync(int subjectId, string hex);
    Task<List<Class>> GetClassListAsync();
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class TimetableService : ITimetableService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public TimetableService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── Grid (Admin + Learner) ────────────────────────────────────────────────

    public async Task<TimetableGridVm> GetGridAsync(int classId, string term, int year)
    {
        var slots = await _db.TimetableSlots
            .Include(s => s.Subject)
            .Include(s => s.Teacher)
            .Include(s => s.Class).ThenInclude(c => c.Grade)
            .Where(s => s.ClassId == classId && s.Term == term && s.AcademicYear == year)
            .ToListAsync();

        var cls = await _db.Classes
            .Include(c => c.Grade)
            .FirstOrDefaultAsync(c => c.Id == classId);

        var assignments = await _db.TeacherClassSubjects
            .Include(t => t.Teacher)
            .Include(t => t.Subject)
            .Include(t => t.Class).ThenInclude(c => c.Grade)
            .Where(t => t.ClassId == classId)
            .ToListAsync();

        var subjectIds = slots.Select(s => s.SubjectId)
            .Union(assignments.Select(a => a.SubjectId))
            .Distinct();
        var colors = await GetColorsAsync(subjectIds);

        var grid = new TimetableSlot?[6, 8];
        foreach (var s in slots)
            grid[s.Day, s.PeriodNumber] = s;

        return new TimetableGridVm
        {
            Grid = grid,
            AllSlots = slots,
            SelectedClass = cls,
            Term = term,
            AcademicYear = year,
            SubjectColors = colors,
            AvailableAssignments = assignments
        };
    }

    // ── Teacher week view ─────────────────────────────────────────────────────

    public async Task<TeacherDayVm> GetTeacherWeekAsync(int teacherId, string term, int year)
    {
        var teacher = await _db.Teachers.FindAsync(teacherId)
            ?? throw new KeyNotFoundException($"Teacher {teacherId} not found.");

        var slots = await _db.TimetableSlots
            .Include(s => s.Subject)
            .Include(s => s.Class).ThenInclude(c => c.Grade)
            .Where(s => s.TeacherId == teacherId && s.Term == term && s.AcademicYear == year)
            .OrderBy(s => s.Day).ThenBy(s => s.PeriodNumber)
            .ToListAsync();

        var colors = await GetColorsAsync(slots.Select(s => s.SubjectId).Distinct());

        return new TeacherDayVm
        {
            Teacher = teacher,
            Term = term,
            AcademicYear = year,
            Slots = slots,
            SubjectColors = colors
        };
    }

    // ── Learner timetable ─────────────────────────────────────────────────────

    public async Task<TimetableGridVm> GetLearnerTimetableAsync(int enrollmentId, string term, int year)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Class)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);
        if (enrollment == null) throw new KeyNotFoundException("Enrollment not found.");
        return await GetGridAsync(enrollment.ClassId, term, year);
    }

    // ── Save slot (AJAX) ──────────────────────────────────────────────────────

    public async Task<TimetableSlot> SaveSlotAsync(
        int classId, int day, int period,
        int subjectId, int teacherId,
        string term, int year, string adminUserId)
    {
        // Validate day / period
        if (day < 1 || day > 5) throw new ArgumentException("Day must be 1–5.");
        if (period < 1 || period > 7) throw new ArgumentException("Period must be 1–7.");
        if (!SchoolDay.IsPeriodAvailable(period, day))
            throw new InvalidOperationException("Period 7 is not available on Friday.");

        // Remove existing slot in same cell
        var existing = await _db.TimetableSlots
            .FirstOrDefaultAsync(s => s.ClassId == classId && s.Day == day
                && s.PeriodNumber == period && s.Term == term && s.AcademicYear == year);
        if (existing != null) _db.TimetableSlots.Remove(existing);

        // Check teacher double-booking
        var conflict = await _db.TimetableSlots
            .Include(s => s.Class).ThenInclude(c => c.Grade)
            .FirstOrDefaultAsync(s => s.TeacherId == teacherId && s.Day == day
                && s.PeriodNumber == period && s.Term == term && s.AcademicYear == year
                && s.ClassId != classId);
        if (conflict != null)
            throw new InvalidOperationException(
                $"This teacher is already scheduled for {conflict.Class?.DisplayName} at this time.");

        var slot = new TimetableSlot
        {
            ClassId = classId,
            SubjectId = subjectId,
            TeacherId = teacherId,
            Day = day,
            PeriodNumber = period,
            Term = term,
            AcademicYear = year
        };

        _db.TimetableSlots.Add(slot);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(adminUserId, null, "Timetable.SlotSaved",
            "TimetableSlot", slot.Id.ToString(),
            $"Class {classId} Day {day} Period {period} Subject {subjectId}");

        return await _db.TimetableSlots
            .Include(s => s.Subject)
            .Include(s => s.Teacher)
            .FirstAsync(s => s.Id == slot.Id);
    }

    // ── Remove slot (AJAX) ────────────────────────────────────────────────────

    public async Task RemoveSlotAsync(int slotId, string adminUserId)
    {
        var slot = await _db.TimetableSlots.FindAsync(slotId);
        if (slot == null) return;
        _db.TimetableSlots.Remove(slot);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, null, "Timetable.SlotRemoved",
            "TimetableSlot", slotId.ToString(), $"Removed slot {slotId}");
    }

    // ── Colors ────────────────────────────────────────────────────────────────

    public async Task<Dictionary<int, string>> GetColorsAsync(IEnumerable<int> subjectIds)
    {
        var ids = subjectIds.ToList();
        var rows = await _db.TimetableSubjectColors
            .Where(c => ids.Contains(c.SubjectId))
            .ToListAsync();
        var result = new Dictionary<int, string>();
        foreach (var id in ids)
        {
            var saved = rows.FirstOrDefault(r => r.SubjectId == id);
            result[id] = saved?.HexColor ?? SchoolDay.DefaultColor(id);
        }
        return result;
    }

    public async Task SetColorAsync(int subjectId, string hex)
    {
        var row = await _db.TimetableSubjectColors
            .FirstOrDefaultAsync(c => c.SubjectId == subjectId);
        if (row == null)
        {
            _db.TimetableSubjectColors.Add(new TimetableSubjectColor
            { SubjectId = subjectId, HexColor = hex });
        }
        else { row.HexColor = hex; }
        await _db.SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public async Task<List<Class>> GetClassListAsync() =>
        await _db.Classes
            .Include(c => c.Grade)
            .OrderBy(c => c.Grade.Level).ThenBy(c => c.Section)
            .ToListAsync();
}
