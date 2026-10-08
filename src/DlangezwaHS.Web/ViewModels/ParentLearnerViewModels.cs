using DlangezwaHS.Web.Models.Domain;
using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// PARENT — LEARNER OVERVIEW (the "hub" page per learner)
// ─────────────────────────────────────────────────────────────────────────────

public class LearnerOverviewViewModel
{
    // Learner core
    public int      LearnerId       { get; set; }
    public string   LearnerName     { get; set; } = "";
    public string   LearnerIdNumber { get; set; } = "";
    public string   Gender          { get; set; } = "";
    public DateTime DateOfBirth     { get; set; }

    // Enrolment
    public bool     IsEnrolled      { get; set; }
    public string   ClassName       { get; set; } = "";
    public string   GradeName       { get; set; } = "";
    public DateTime? EnrolledDate   { get; set; }
    public IList<string> Subjects   { get; set; } = new List<string>();

    // Boarding
    public bool     HasBed          { get; set; }
    public string   RoomName        { get; set; } = "";
    public string   BedNumber       { get; set; } = "";
    public DateTime? AllocatedDate  { get; set; }
    public IList<ParentLeaveRequestRow> LeaveRequests { get; set; } = new List<ParentLeaveRequestRow>();

    // Account / fees
    public decimal  RegistrationFeeAmount  { get; set; }
    public decimal  AccommodationFeeAmount { get; set; }
    public bool     RegistrationPaid       { get; set; }
    public bool     AccommodationPaid      { get; set; }
    public decimal  BalanceDue             { get; set; }
    public IList<PaymentSummaryRow> Payments { get; set; } = new List<PaymentSummaryRow>();

    // Proofs
    public IList<ProofRow> Proofs { get; set; } = new List<ProofRow>();

    // Attendance (latest 30 days summary)
    public int  PresentDays  { get; set; }
    public int  AbsentDays   { get; set; }
    public int  LateDays     { get; set; }
    public int  TotalDays    { get; set; }
    public decimal AttendancePct => TotalDays > 0
        ? Math.Round((decimal)PresentDays / TotalDays * 100, 1) : 0;

    // Available terms for academic tab
    public IList<string> AvailableTerms { get; set; } = new List<string>();
}

public class PaymentSummaryRow
{
    public int     PaymentId    { get; set; }
    public string  Type         { get; set; } = "";
    public decimal Amount       { get; set; }
    public string  Status       { get; set; } = "";
    public string  Reference    { get; set; } = "";
    public DateTime? PaidDate   { get; set; }
    public string  Method       { get; set; } = "";
}

public class ProofRow
{
    public int      ProofId      { get; set; }
    public string   LearnerName  { get; set; } = "";
    public DateTime GeneratedAt  { get; set; }
    public string   DownloadUrl  { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// PARENT — LEARNER ATTENDANCE (full history page)
// ─────────────────────────────────────────────────────────────────────────────

public class LearnerAttendanceViewModel
{
    public int      LearnerId    { get; set; }
    public string   LearnerName  { get; set; } = "";
    public string   ClassName    { get; set; } = "";
    public DateTime FromDate     { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime ToDate       { get; set; } = DateTime.Today;

    // Summary
    public int      PresentDays  { get; set; }
    public int      AbsentDays   { get; set; }
    public int      LateDays     { get; set; }
    public int      TotalDays    { get; set; }
    public decimal  AttendancePct => TotalDays > 0
        ? Math.Round((decimal)PresentDays / TotalDays * 100, 1) : 0;

    // Daily records
    public IList<AttendanceDayRow> Records { get; set; } = new List<AttendanceDayRow>();
}

public class AttendanceDayRow
{
    public DateTime Date    { get; set; }
    public string   Status  { get; set; } = "";  // Present / Absent / Late
    public string?  Notes   { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// PARENT — UPDATED DASHBOARD (lists all children with quick stats)
// ─────────────────────────────────────────────────────────────────────────────

public class ParentDashboardV2ViewModel
{
    public string              ParentName   { get; set; } = "";
    public IList<LearnerCardViewModel> Learners { get; set; } = new List<LearnerCardViewModel>();
    // Notifications / recent activity
    public IList<string> Notifications { get; set; } = new List<string>();
}

public class LearnerCardViewModel
{
    public int     LearnerId      { get; set; }
    public string  LearnerName    { get; set; } = "";
    public string  ApplicationStatus { get; set; } = "";
    public int     ApplicationId  { get; set; }
    public bool    IsEnrolled     { get; set; }
    public string  ClassName      { get; set; } = "";
    public decimal BalanceDue     { get; set; }
    public bool    HasProof       { get; set; }
    public decimal AttendancePct  { get; set; }
    // Boarding
    public bool    HasBed         { get; set; }
    public string  RoomName       { get; set; } = "";
    // Which fee the "Pay" button should collect: registration first, then accommodation
    public string  PayType        { get; set; } = "Registration";
    // Learner login (created by the parent)
    public bool    HasLogin       { get; set; }
    public bool    LoginEnabled   { get; set; }
    // For badge colour
    public bool    AttendanceGood => AttendancePct >= 80;
    public bool    AttendanceRisk => AttendancePct >= 60 && AttendancePct < 80;
    public bool    AttendanceCrit => AttendancePct < 60 && AttendancePct > 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// LEARNER LOGIN — parent creates / resets the child's account
// ─────────────────────────────────────────────────────────────────────────────

public class LearnerAccountViewModel
{
    public int    LearnerId       { get; set; }
    public string LearnerName     { get; set; } = "";
    public string LearnerIdNumber { get; set; } = "";
    public bool   HasLogin        { get; set; }
    public bool   IsEnabled       { get; set; }

    [EmailAddress, StringLength(200), Display(Name = "Learner email (optional)")]
    public string? Email { get; set; }

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords do not match."),
     Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// LEARNER DASHBOARD — the learner's own portal home page
// ─────────────────────────────────────────────────────────────────────────────

public class LearnerDashboardViewModel
{
    public int     LearnerId      { get; set; }
    public string  LearnerName    { get; set; } = "";
    public string  ParentName     { get; set; } = "";
    public bool    IsEnrolled     { get; set; }
    public string  ClassName      { get; set; } = "";
    public IList<string> Subjects { get; set; } = new List<string>();
    public decimal AttendancePct  { get; set; }
    public int     AttendanceDays { get; set; }
    public int     UpcomingMealOrders { get; set; }
    public bool    HasActiveDietaryProfile { get; set; }
    public bool    IsBoarder      { get; set; }
    public IList<LearnerAssessmentRow> UpcomingAssessments { get; set; } = new List<LearnerAssessmentRow>();
    public IList<LearnerAssessmentRow> RecentResults       { get; set; } = new List<LearnerAssessmentRow>();
    public IList<AttendanceDayRow>     RecentAttendance    { get; set; } = new List<AttendanceDayRow>();
}

// A class assessment for one learner, with their mark once it has been captured
public class LearnerAssessmentRow
{
    public int      Id          { get; set; }
    public string   Name        { get; set; } = "";
    public string   Type        { get; set; } = "";
    public string   Subject     { get; set; } = "";
    public DateTime Date        { get; set; }
    public string   Term        { get; set; } = "";
    public decimal  TotalMarks  { get; set; }
    public decimal? MarksObtained { get; set; }
    public decimal? Percentage  { get; set; }
    public string?  Grade       { get; set; }
    public bool?    IsPassed    { get; set; }
    public bool     IsUpcoming  => Date.Date >= DateTime.Today && MarksObtained is null;
}

public class LearnerAssessmentsViewModel
{
    public string LearnerName { get; set; } = "";
    public string ClassName   { get; set; } = "";
    public IList<LearnerAssessmentRow> Assessments { get; set; } = new List<LearnerAssessmentRow>();
}
