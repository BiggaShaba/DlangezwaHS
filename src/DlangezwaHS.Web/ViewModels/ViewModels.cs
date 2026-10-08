using DlangezwaHS.Web.Models.Domain;
using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// ACCOUNT
// ─────────────────────────────────────────────────────────────────────────────

public class RegisterViewModel
{
    [Required, StringLength(100)]     public string FirstName { get; set; } = "";
    [Required, StringLength(100)]     public string LastName  { get; set; } = "";
    [Required, EmailAddress]          public string Email     { get; set; } = "";
    [Required, Phone]                 public string Phone     { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
                                      public string Password  { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password)]
                                      public string ConfirmPassword { get; set; } = "";
}

public class LoginViewModel
{
    // Email for parents/staff, or Learner ID number for learners
    [Required, Display(Name = "Email or Learner ID")] public string Email    { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// APPLICATION
// ─────────────────────────────────────────────────────────────────────────────

public class NewApplicationViewModel
{
    // Learner details
    [Required, StringLength(100)] public string LearnerFirstName  { get; set; } = "";
    [Required, StringLength(100)] public string LearnerLastName   { get; set; } = "";
    [Required, DataType(DataType.Date)] public DateTime DateOfBirth { get; set; }
    [Required] public string Gender { get; set; } = "";
    [Required, StringLength(30)] public string LearnerIdNumber { get; set; } = "";

    // Documents
    [Required] public IFormFile? LearnerIdDoc      { get; set; }
    [Required] public IFormFile? PreviousReportDoc { get; set; }
    [Required] public IFormFile? GuardianIdDoc     { get; set; }
}

public class ApplicationListViewModel
{
    public IList<ApplicationSummaryViewModel> Applications { get; set; } = new List<ApplicationSummaryViewModel>();
    public string? StatusFilter { get; set; }
    public string? SearchTerm   { get; set; }
}

public class ApplicationSummaryViewModel
{
    public int    Id              { get; set; }
    public string LearnerName    { get; set; } = "";
    public string ParentName     { get; set; } = "";
    public string ParentEmail    { get; set; } = "";
    public ApplicationStatus Status { get; set; }
    public DateTime SubmittedAt  { get; set; }
}

public class ApplicationDetailViewModel
{
    public LearnerApplication Application { get; set; } = null!;
    public string? RejectionReason { get; set; }
}

public class RejectApplicationViewModel
{
    [Required] public int    ApplicationId   { get; set; }
    [Required, StringLength(500, MinimumLength = 10)]
               public string RejectionReason { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// ENROLMENT
// ─────────────────────────────────────────────────────────────────────────────

public class EnrollLearnerViewModel
{
    [Required] public int ApplicationId { get; set; }
    public string LearnerName { get; set; } = "";
    [Required] public int ClassId { get; set; }
    [Required, MinLength(1)] public List<int> SubjectIds { get; set; } = new();
    public IList<Class>   AvailableClasses  { get; set; } = new List<Class>();
    public IList<Subject> AvailableSubjects { get; set; } = new List<Subject>();
}

// ─────────────────────────────────────────────────────────────────────────────
// ROOM ALLOCATION
// ─────────────────────────────────────────────────────────────────────────────

public class AllocateBedViewModel
{
    [Required] public int LearnerId   { get; set; }
    public string  LearnerName { get; set; } = "";
    [Required] public int RoomId     { get; set; }
    [Required] public int BedId      { get; set; }
    public IList<Room> AvailableRooms { get; set; } = new List<Room>();
    public IList<Bed>  AvailableBeds  { get; set; } = new List<Bed>();
}

public class RoomManageViewModel
{
    public IList<Room> Rooms { get; set; } = new List<Room>();
}

public class AddRoomViewModel
{
    [Required, StringLength(50)] public string Name     { get; set; } = "";
    [Required, Range(1, 100)]    public int    Capacity { get; set; } = 4;
    public string? Notes { get; set; }
    public int BedCount { get; set; } = 4;
}

public class AddBedViewModel
{
    [Required]                   public int    RoomId    { get; set; }
    [Required, StringLength(20)] public string BedNumber { get; set; } = "";
    public IList<Room> Rooms { get; set; } = new List<Room>();
}

// ─────────────────────────────────────────────────────────────────────────────
// PAYMENT
// ─────────────────────────────────────────────────────────────────────────────

public class PaymentInitViewModel
{
    [Required] public int         LearnerId     { get; set; }
    public string  LearnerName  { get; set; } = "";
    [Required] public PaymentType Type         { get; set; }
    public decimal Amount       { get; set; }
    public IList<FeeType> FeeTypes { get; set; } = new List<FeeType>();
}

public class ManualPaymentViewModel
{
    [Required] public int         LearnerId { get; set; }
    public string  LearnerName  { get; set; } = "";
    [Required] public PaymentType Type      { get; set; }
    [Required, Range(1, 999999)] public decimal Amount { get; set; }
    [Required, StringLength(100)] public string BankRef { get; set; } = "";
    public string? Notes { get; set; }
}

public class PaymentResultViewModel
{
    public bool    Success    { get; set; }
    public string  Message    { get; set; } = "";
    public int?    PaymentId  { get; set; }
    public string? ProofUrl   { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// ADMIN SETTINGS
// ─────────────────────────────────────────────────────────────────────────────

public class SubjectFormViewModel
{
    public int?   Id       { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(20)]  public string Code { get; set; } = "";
    public List<int> GradeIds { get; set; } = new();
    public IList<Grade> AllGrades { get; set; } = new List<Grade>();
}

public class TeacherFormViewModel
{
    public int?   Id        { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName  { get; set; } = "";
    [EmailAddress]                public string? Email    { get; set; }
    [Phone]                       public string? Phone    { get; set; }
    public List<int> SubjectIds { get; set; } = new();
    public IList<Subject> AllSubjects { get; set; } = new List<Subject>();
}

public class GradeFormViewModel
{
    public int?   Id    { get; set; }
    [Required, StringLength(20)]  public string Name  { get; set; } = "";
    [Required, Range(8, 12)]      public int    Level { get; set; } = 8;
}

public class ClassFormViewModel
{
    public int?   Id      { get; set; }
    [Required]            public int    GradeId  { get; set; }
    [Required, StringLength(10)] public string Section  { get; set; } = "";
    [Range(1, 200)]       public int    Capacity { get; set; } = 40;
    public IList<Grade> AllGrades { get; set; } = new List<Grade>();
}

public class FeeTypeFormViewModel
{
    public int?    Id     { get; set; }
    [Required, StringLength(100)]     public string  Name        { get; set; } = "";
    [Required, Range(0, 9999999)]     public decimal Amount      { get; set; }
    public string? Description { get; set; }
}

public class SmtpSettingsViewModel
{
    [Required] public string Host     { get; set; } = "";
    [Required, Range(1, 65535)] public int Port { get; set; } = 587;
    [Required] public string UserName { get; set; } = "";
               public string Password { get; set; } = "";
    public bool   UseSsl   { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string FromName    { get; set; } = "Dlangezwa High School";
}

// ─────────────────────────────────────────────────────────────────────────────
// DASHBOARD
// ─────────────────────────────────────────────────────────────────────────────

public class AdminDashboardViewModel
{
    public int PendingApplications { get; set; }
    public int TotalEnrolments     { get; set; }
    public int TotalRooms          { get; set; }
    public int AvailableBeds       { get; set; }
    public int TotalRevenue        { get; set; }
    public IList<ApplicationSummaryViewModel> RecentApplications { get; set; } = new List<ApplicationSummaryViewModel>();
    public IList<Payment> RecentPayments { get; set; } = new List<Payment>();
}

public class ParentDashboardViewModel
{
    public IList<LearnerApplication> Applications { get; set; } = new List<LearnerApplication>();
    public IList<Payment>            Payments     { get; set; } = new List<Payment>();
    public IList<RegistrationProof>  Proofs       { get; set; } = new List<RegistrationProof>();
}

public class EnrolmentsExportRow
{
    public string LearnerName  { get; set; } = "";
    public string LearnerID    { get; set; } = "";
    public string ClassName    { get; set; } = "";
    public string Subjects     { get; set; } = "";
    public string Room         { get; set; } = "";
    public string Bed          { get; set; } = "";
    public string EnrolledDate { get; set; } = "";
}
