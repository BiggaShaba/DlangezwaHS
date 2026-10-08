using DlangezwaHS.Web.Models.Domain;
using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// ADMIN — REGISTER TEACHER
// ─────────────────────────────────────────────────────────────────────────────

public class RegisterTeacherViewModel
{
    public int?   Id        { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName  { get; set; } = "";
    [Required, EmailAddress]      public string Email     { get; set; } = "";
    [Phone]                       public string? Phone    { get; set; }
    public List<int> ClassIds   { get; set; } = new();
    public List<int> SubjectIds { get; set; } = new();
    public IList<Class>   AllClasses  { get; set; } = new List<Class>();
    public IList<Subject> AllSubjects { get; set; } = new List<Subject>();
}

// ─────────────────────────────────────────────────────────────────────────────
// ADMIN — EMPLOY STAFF (unified Teacher / Housemaster / Kitchen Staff)
// ─────────────────────────────────────────────────────────────────────────────

public enum StaffRole { Teacher, Housemaster, KitchenStaff }

public class EmployStaffViewModel
{
    [Required] public StaffRole Role { get; set; } = StaffRole.Teacher;
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName  { get; set; } = "";
    [Required, EmailAddress]      public string Email     { get; set; } = "";
    [Phone]                       public string? Phone    { get; set; }

    // Teacher-only — bound as strings so a blank/unselected dropdown (e.g. when
    // the section is hidden for Housemaster/Kitchen Staff) never fails int binding.
    public List<string> ClassIds   { get; set; } = new();
    public List<string> SubjectIds { get; set; } = new();
}

public class TeacherSummaryRow
{
    public int    Id          { get; set; }
    public string FullName    { get; set; } = "";
    public string Email       { get; set; } = "";
    public string Phone       { get; set; } = "";
    public string Assignments { get; set; } = "";
    public bool   HasLogin    { get; set; }
    public bool   IsActive    { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// TEACHER — DASHBOARD
// ─────────────────────────────────────────────────────────────────────────────

public class TeacherDashboardViewModel
{
    public string TeacherName       { get; set; } = "";
    public int    TotalClasses      { get; set; }
    public int    TotalSubjects     { get; set; }
    public int    PendingAttendance { get; set; }
    public int    TotalAssessments  { get; set; }
    public IList<TeacherClassSubject> Assignments { get; set; } = new List<TeacherClassSubject>();
}

// ─────────────────────────────────────────────────────────────────────────────
// TEACHER — ATTENDANCE
// ─────────────────────────────────────────────────────────────────────────────

public class RecordAttendanceViewModel
{
    public int      TeacherId    { get; set; }
    public int      ClassId      { get; set; }
    public string   ClassName    { get; set; } = "";
    public DateTime Date         { get; set; } = DateTime.Today;
    public bool     AlreadyTaken { get; set; }
    public IList<AttendanceRow> Learners { get; set; } = new List<AttendanceRow>();
}

public class AttendanceRow
{
    public int              LearnerId { get; set; }
    public string           FullName  { get; set; } = "";
    public AttendanceStatus Status    { get; set; } = AttendanceStatus.Present;
    public string?          Notes     { get; set; }
}

public class AttendanceSummaryRow
{
    public string  LearnerName   { get; set; } = "";
    public int     Present       { get; set; }
    public int     Absent        { get; set; }
    public int     Late          { get; set; }
    public int     Total         { get; set; }
    public decimal AttendancePct => Total > 0 ? Math.Round((decimal)Present / Total * 100, 1) : 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// TEACHER — ASSESSMENTS
// ─────────────────────────────────────────────────────────────────────────────

public class AssessmentListViewModel
{
    public int    TeacherId  { get; set; }
    public string? TermFilter { get; set; }
    public string? TypeFilter { get; set; }
    public IList<Assessment> Assessments { get; set; } = new List<Assessment>();
}

public class CreateAssessmentViewModel
{
    public int?   Id       { get; set; }
    [Required, StringLength(150)] public string Name     { get; set; } = "";
    [Required] public AssessmentType Type { get; set; } = AssessmentType.Test;
    [Required] public int    SubjectId   { get; set; }
    [Required] public int    ClassId     { get; set; }
    [Required, DataType(DataType.Date)] public DateTime Date { get; set; } = DateTime.Today;
    [Required] public string Term        { get; set; } = "Term 1";
    [Required, Range(1, 1000)] public decimal TotalMarks { get; set; } = 100;
    public IList<TeacherClassSubject> Assignments { get; set; } = new List<TeacherClassSubject>();
}

// ─────────────────────────────────────────────────────────────────────────────
// TEACHER — MARKS
// ─────────────────────────────────────────────────────────────────────────────

public class CaptureMarksViewModel
{
    public int     AssessmentId   { get; set; }
    public string  AssessmentName { get; set; } = "";
    public string  ClassName      { get; set; } = "";
    public string  SubjectName    { get; set; } = "";
    public decimal TotalMarks     { get; set; }
    public string  Term           { get; set; } = "";
    public bool    IsLocked       { get; set; }
    public IList<MarkRow> Marks   { get; set; } = new List<MarkRow>();
}

public class MarkRow
{
    public int     LearnerId     { get; set; }
    public string  FullName      { get; set; } = "";
    [Range(0, 10000)] public decimal? MarksObtained { get; set; }
    public string? Comments      { get; set; }
    public decimal Percentage    { get; set; }
    public string  Grade         { get; set; } = "";
    public bool    IsPassed      { get; set; }
    public bool    IsLocked      { get; set; }
}

public class AdminEditMarkViewModel
{
    [Required] public int    MarkId        { get; set; }
    public string  LearnerName    { get; set; } = "";
    public string  AssessmentName { get; set; } = "";
    public decimal TotalMarks     { get; set; }
    [Required, Range(0, 10000)] public decimal MarksObtained { get; set; }
    public string? Comments       { get; set; }
    [Required] public string AdminReason { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// CLASS PERFORMANCE
// ─────────────────────────────────────────────────────────────────────────────

public class ClassPerformanceViewModel
{
    public int     ClassId       { get; set; }
    public string  ClassName     { get; set; } = "";
    public int     SubjectId     { get; set; }
    public string  SubjectName   { get; set; } = "";
    public string  Term          { get; set; } = "Term 1";
    public decimal ClassAverage  { get; set; }
    public int     PassCount     { get; set; }
    public int     FailCount     { get; set; }
    public int     TotalLearners { get; set; }
    public decimal PassRate      => TotalLearners > 0 ? Math.Round((decimal)PassCount / TotalLearners * 100, 1) : 0;
    public IList<LearnerPerformanceRow> Learners { get; set; } = new List<LearnerPerformanceRow>();
    public IList<Assessment> Assessments         { get; set; } = new List<Assessment>();
}

public class LearnerPerformanceRow
{
    public string  LearnerName { get; set; } = "";
    public string  LearnerID   { get; set; } = "";
    public IList<AssessmentMarkCell> Marks { get; set; } = new List<AssessmentMarkCell>();
    public decimal Average     { get; set; }
    public string  FinalGrade  { get; set; } = "";
    public bool    IsPassed    { get; set; }
}

public class AssessmentMarkCell
{
    public string   AssessmentName { get; set; } = "";
    public decimal? Percentage     { get; set; }
    public string   Grade          { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// ADMIN — ACADEMIC REPORTS
// ─────────────────────────────────────────────────────────────────────────────

public class AcademicReportFilterViewModel
{
    public int?    ClassId    { get; set; }
    public int?    SubjectId  { get; set; }
    public string  Term       { get; set; } = "Term 1";
    public IList<Class>   AllClasses  { get; set; } = new List<Class>();
    public IList<Subject> AllSubjects { get; set; } = new List<Subject>();
    public ClassPerformanceViewModel? Report { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// PARENT — ACADEMIC VIEW (no raw marks exposed)
// ─────────────────────────────────────────────────────────────────────────────

public class ParentAcademicViewModel
{
    public int     LearnerId    { get; set; }
    public string  LearnerName  { get; set; } = "";
    public string  ClassName    { get; set; } = "";
    public string  Term         { get; set; } = "";
    public int     PresentDays  { get; set; }
    public int     AbsentDays   { get; set; }
    public int     LateDays     { get; set; }
    public int     TotalDays    { get; set; }
    public decimal AttendancePct => TotalDays > 0 ? Math.Round((decimal)PresentDays / TotalDays * 100, 1) : 0;
    public IList<SubjectTermSummary> SubjectSummaries { get; set; } = new List<SubjectTermSummary>();
    public IList<string> AvailableTerms               { get; set; } = new List<string>();
}

public class SubjectTermSummary
{
    public string  SubjectName { get; set; } = "";
    public decimal Average     { get; set; }
    public string  FinalGrade  { get; set; } = "";
    public bool    IsPassed    { get; set; }
}
