using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DlangezwaHS.Web.Models.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// IDENTITY / USER
// ─────────────────────────────────────────────────────────────────────────────

public class ApplicationUser : IdentityUser
{
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [StringLength(20)] public string? Phone { get; set; }
    public string FullName => $"{FirstName} {LastName}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public ICollection<LearnerApplication> Applications { get; set; } = new List<LearnerApplication>();
    public ICollection<Learner> Learners { get; set; } = new List<Learner>();
}

// ─────────────────────────────────────────────────────────────────────────────
// LEARNER
// ─────────────────────────────────────────────────────────────────────────────

public class Learner
{
    public int Id { get; set; }
    // Learner's own login account (created by the parent). Empty string = no login yet.
    public string UserId { get; set; } = string.Empty;
    public bool HasLogin => !string.IsNullOrEmpty(UserId);
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required] public DateTime DateOfBirth { get; set; }
    [Required, StringLength(10)] public string Gender { get; set; } = ""; // Male/Female/Other
    [Required, StringLength(30)] public string LearnerIdNumber { get; set; } = "";
    // Persistent boarding QR badge hash (Increment 3) — generated lazily on first use
    [StringLength(64)] public string? BoardingQrCode { get; set; }
    public string? ParentId { get; set; }
    [ForeignKey(nameof(ParentId))] public ApplicationUser? Parent { get; set; }
    public string FullName => $"{FirstName} {LastName}";
    public ICollection<LearnerApplication> Applications { get; set; } = new List<LearnerApplication>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<RoomAllocation> Allocations { get; set; } = new List<RoomAllocation>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
    public ICollection<Mark> Marks { get; set; } = new List<Mark>();
}

// ─────────────────────────────────────────────────────────────────────────────
// APPLICATION
// ─────────────────────────────────────────────────────────────────────────────

public enum ApplicationStatus { Pending, Approved, Rejected, Enrolled }

public class LearnerApplication
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public string? ParentId { get; set; }
    [ForeignKey(nameof(ParentId))] public ApplicationUser? Parent { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }

    // Document uploads (paths or Azure Blob URLs)
    public string? LearnerIdDocPath { get; set; }
    public string? PreviousReportDocPath { get; set; }
    public string? GuardianIdDocPath { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

// ─────────────────────────────────────────────────────────────────────────────
// ACADEMIC STRUCTURE
// ─────────────────────────────────────────────────────────────────────────────

public class Grade
{
    public int Id { get; set; }
    [Required, StringLength(20)] public string Name { get; set; } = ""; // "Grade 8"
    public int Level { get; set; }   // 8-12
    public ICollection<Class> Classes { get; set; } = new List<Class>();
    public ICollection<SubjectGrade> SubjectGrades { get; set; } = new List<SubjectGrade>();
}

public class Class 
{
    public int Id { get; set; }
    public int GradeId { get; set; }
    [ForeignKey(nameof(GradeId))] public Grade Grade { get; set; } = null!;
    [Required, StringLength(10)] public string Section { get; set; } = ""; // "A","B","C","D"
    public string DisplayName => $"{Grade?.Name}{Section}";
    public int Capacity { get; set; } = 40;
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<ClassTeacher> ClassTeachers { get; set; } = new List<ClassTeacher>();
}

public class Subject
{ 
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(20)] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<SubjectGrade> SubjectGrades { get; set; } = new List<SubjectGrade>();
    public ICollection<EnrollmentSubject> EnrollmentSubjects { get; set; } = new List<EnrollmentSubject>();
    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
}

public class SubjectGrade
{
    public int SubjectId { get; set; }
    [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;
    public int GradeId { get; set; }
    [ForeignKey(nameof(GradeId))] public Grade Grade { get; set; } = null!;
}

public class Teacher
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(20)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public string FullName => $"{FirstName} {LastName}";
    // Extra staff responsibility (still logs in under the Teacher role)
    public bool IsCoach { get; set; } = false;
    // Linked Identity login account (created when Admin registers teacher)
    public string? UserId { get; set; }
    [ForeignKey(nameof(UserId))] public ApplicationUser? User { get; set; }
    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
    public ICollection<ClassTeacher> ClassTeachers { get; set; } = new List<ClassTeacher>();
    public ICollection<TeacherClassSubject> TeacherClassSubjects { get; set; } = new List<TeacherClassSubject>();
    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
    public ICollection<Assessment> Assessments { get; set; } = new List<Assessment>();
}

public class TeacherSubject
{
    public int TeacherId { get; set; }
    [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;
    public int SubjectId { get; set; }
    [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;
}

public class ClassTeacher
{
    public int TeacherId { get; set; }
    [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;
    public int ClassId { get; set; }
    [ForeignKey(nameof(ClassId))] public Class Class { get; set; } = null!;
}

// ─────────────────────────────────────────────────────────────────────────────
// ENROLLMENT
// ─────────────────────────────────────────────────────────────────────────────

public class Enrollment
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public int ClassId { get; set; }
    [ForeignKey(nameof(ClassId))] public Class Class { get; set; } = null!;
    public int? ApplicationId { get; set; }
    [ForeignKey(nameof(ApplicationId))] public LearnerApplication? Application { get; set; }
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public ICollection<EnrollmentSubject> EnrollmentSubjects { get; set; } = new List<EnrollmentSubject>();
    public ICollection<RegistrationProof> Proofs { get; set; } = new List<RegistrationProof>();
}

public class EnrollmentSubject
{
    public int EnrollmentId { get; set; }
    [ForeignKey(nameof(EnrollmentId))] public Enrollment Enrollment { get; set; } = null!;
    public int SubjectId { get; set; }
    [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;
}

// ─────────────────────────────────────────────────────────────────────────────
// BOARDING
// ─────────────────────────────────────────────────────────────────────────────

public class Room
{
    public int Id { get; set; }
    [Required, StringLength(50)] public string Name { get; set; } = "";
    public int Capacity { get; set; } = 4;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public ICollection<Bed> Beds { get; set; } = new List<Bed>();
    public ICollection<RoomAllocation> Allocations { get; set; } = new List<RoomAllocation>();
}

public enum BedStatus { Available, Occupied, Maintenance }

public class Bed
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    [ForeignKey(nameof(RoomId))] public Room Room { get; set; } = null!;
    [Required, StringLength(20)] public string BedNumber { get; set; } = "";
    public BedStatus Status { get; set; } = BedStatus.Available;
    public ICollection<RoomAllocation> Allocations { get; set; } = new List<RoomAllocation>();
}

public class RoomAllocation
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public int RoomId { get; set; }
    [ForeignKey(nameof(RoomId))] public Room Room { get; set; } = null!;
    public int BedId { get; set; }
    [ForeignKey(nameof(BedId))] public Bed Bed { get; set; } = null!;
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CheckedOutAt { get; set; }
    public bool IsActive { get; set; } = true;
}

// ─────────────────────────────────────────────────────────────────────────────
// FEES & PAYMENTS
// ─────────────────────────────────────────────────────────────────────────────

public class FeeType
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum PaymentType { Registration, Accommodation, Other }
public enum PaymentStatus { Pending, Completed, Failed, Refunded }
public enum PaymentMethod { Online, ManualBankTransfer, Cash }

public class Payment
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public string? ParentId { get; set; }
    [ForeignKey(nameof(ParentId))] public ApplicationUser? Parent { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public PaymentType Type { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentMethod Method { get; set; }
    public string? ProviderRef { get; set; }   // Gateway transaction ID
    public string? BankRef { get; set; }   // Manual bank transfer ref
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public string? RecordedByUserId { get; set; }   // Admin who recorded manual pay
    public ICollection<RegistrationProof> Proofs { get; set; } = new List<RegistrationProof>();
}

// ─────────────────────────────────────────────────────────────────────────────
// REGISTRATION PROOF / PDF
// ─────────────────────────────────────────────────────────────────────────────

public class RegistrationProof
{
    public int Id { get; set; }
    public int? EnrollmentId { get; set; }
    [ForeignKey(nameof(EnrollmentId))] public Enrollment? Enrollment { get; set; }
    public int? PaymentId { get; set; }
    [ForeignKey(nameof(PaymentId))] public Payment? Payment { get; set; }
    public string PdfPath { get; set; } = "";   // File path or Blob URL
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public bool EmailSent { get; set; } = false;
}

// ─────────────────────────────────────────────────────────────────────────────
// EMAIL TEMPLATES
// ─────────────────────────────────────────────────────────────────────────────

public class EmailTemplate
{
    public int Id { get; set; }
    [Required, StringLength(50)] public string TemplateKey { get; set; } = ""; // e.g. "ApplicationReceived"
    [Required, StringLength(200)] public string Subject { get; set; } = "";
    [Required] public string Body { get; set; } = "";  // HTML, supports {{tokens}}
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────────────────────────────────────────────────────────
// AUDIT LOG
// ─────────────────────────────────────────────────────────────────────────────

public class AuditLog
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────────────────────────────────────────────────────────
// TEACHER FEATURE — CLASS/SUBJECT ASSIGNMENT
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Links a teacher to a specific class AND subject (what they teach, to whom)</summary>
public class TeacherClassSubject
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;
    public int ClassId { get; set; }
    [ForeignKey(nameof(ClassId))] public Class Class { get; set; } = null!;
    public int SubjectId { get; set; }
    [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;
}

// ─────────────────────────────────────────────────────────────────────────────
// ATTENDANCE
// ─────────────────────────────────────────────────────────────────────────────

public enum AttendanceStatus { Present, Absent, Late }

public class Attendance
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public int ClassId { get; set; }
    [ForeignKey(nameof(ClassId))] public Class Class { get; set; } = null!;
    public int TeacherId { get; set; }
    [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Today;
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public string? Notes { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// ASSESSMENT
// ─────────────────────────────────────────────────────────────────────────────

public enum AssessmentType { Test, Assignment, Exam }

public class Assessment
{
    public int Id { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = "";
    public AssessmentType Type { get; set; } = AssessmentType.Test;
    public int SubjectId { get; set; }
    [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;
    public int ClassId { get; set; }
    [ForeignKey(nameof(ClassId))] public Class Class { get; set; } = null!;
    public int TeacherId { get; set; }
    [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;
    public DateTime Date { get; set; }
    [Required, StringLength(20)] public string Term { get; set; } = "Term 1"; // Term 1-4
    [Column(TypeName = "decimal(6,2)")] public decimal TotalMarks { get; set; } = 100;
    public bool MarksLocked { get; set; } = false; // true after teacher submits
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Mark> Marks { get; set; } = new List<Mark>();
}

// ─────────────────────────────────────────────────────────────────────────────
// MARK
// ─────────────────────────────────────────────────────────────────────────────

public class Mark
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public int AssessmentId { get; set; }
    [ForeignKey(nameof(AssessmentId))] public Assessment Assessment { get; set; } = null!;
    [Column(TypeName = "decimal(6,2)")] public decimal MarksObtained { get; set; }
    // Calculated and stored for quick reporting
    [Column(TypeName = "decimal(5,2)")] public decimal Percentage { get; set; }
    [StringLength(5)] public string Grade { get; set; } = ""; // A, B, C, D, E, F
    public bool IsPassed { get; set; }
    public string? Comments { get; set; }
    // Locked — only Admin can change after teacher submission
    public bool IsLocked { get; set; } = false;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string? RecordedBy { get; set; } // UserId
}
// ─────────────────────────────────────────────────────────────────────────────
// QUESTION PAPERS  (UC14)
// ─────────────────────────────────────────────────────────────────────────────

public enum QuestionPaperStatus { Pending, Approved, Rejected, Released }

public class QuestionPaper
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    /// <summary>e.g. "Mathematics Term 2 Test – Grade 10"</summary>
    public int SubjectId { get; set; }
    [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;

    public int GradeId { get; set; }
    [ForeignKey(nameof(GradeId))] public Grade Grade { get; set; } = null!;

    [Required, StringLength(20)]
    public string Term { get; set; } = "Term 1";           // Term 1–4

    public int AcademicYear { get; set; } = DateTime.UtcNow.Year;

    public AssessmentType PaperType { get; set; } = AssessmentType.Test;

    // File storage
    public string FilePath { get; set; } = "";     // relative path under uploads/questionpapers/
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";     // application/pdf | application/vnd.openxmlformats…
    public long FileSizeBytes { get; set; }

    // Workflow
    public QuestionPaperStatus Status { get; set; } = QuestionPaperStatus.Pending;
    public string? RejectionReason { get; set; }

    // Upload
    public string? UploadedByUserId { get; set; }
    [ForeignKey(nameof(UploadedByUserId))] public ApplicationUser? UploadedBy { get; set; }

    public int? UploadedByTeacherId { get; set; }
    [ForeignKey(nameof(UploadedByTeacherId))] public Teacher? UploadedByTeacher { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Approval / Release
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public bool IsReleased { get; set; } = false;
    public DateTime? ReleasedAt { get; set; }

    // Auto-delete after 3 years
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddYears(3);

    // ─────────────────────────────────────────────────────────────────────────────
    // TIMETABLE  (UC16)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One assigned period in the weekly timetable.
    /// Day: 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri
    /// PeriodNumber: 1–7 (maps to fixed school-day time slots; breaks are not stored)
    /// </summary>
    public class TimetableSlot
    {
        public int Id { get; set; }

        public int ClassId { get; set; }
        [ForeignKey(nameof(ClassId))] public Class Class { get; set; } = null!;

        public int SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;

        public int TeacherId { get; set; }
        [ForeignKey(nameof(TeacherId))] public Teacher Teacher { get; set; } = null!;

        /// <summary>1=Mon 2=Tue 3=Wed 4=Thu 5=Fri</summary>
        public int Day { get; set; }

        /// <summary>1–7 (see SchoolDay constants). Period 7 is Mon–Thu only.</summary>
        public int PeriodNumber { get; set; }

        [StringLength(20)] public string Term { get; set; } = "Term 1";
        public int AcademicYear { get; set; } = DateTime.UtcNow.Year;
    }

    /// <summary>
    /// Persists the hex colour assigned to a subject for timetable display.
    /// Admin can reassign; defaults auto-generated from a palette.
    /// </summary>
    public class TimetableSubjectColor
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))] public Subject Subject { get; set; } = null!;
        [Required, StringLength(7)] public string HexColor { get; set; } = "#5ea8d3";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ENUM
    // ─────────────────────────────────────────────────────────────────────────────

    public enum CalendarEventType
    {
        Holiday = 0,
        Announcement = 1,
        Notice = 2,
        SchoolEvent = 3,
        Assessment = 4   // auto-created when teacher saves an assessment
    }
    
}