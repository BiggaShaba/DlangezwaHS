using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// LEARNER PORTAL — the learner's own login, sharing data with the parent.
// Action names mirror ParentController so the Parent views are reused as-is.
// Every action works on the signed-in learner only; any learnerId posted by a
// shared view is ignored.
// ─────────────────────────────────────────────────────────────────────────────

[Authorize(Roles = LearnerAccountService.RoleName)]
public class LearnerController : Controller
{
    private const string ParentViews = "~/Views/Parent/";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILearnerAccountService _learnerAccounts;
    private readonly ITeacherService _teacherSvc;
    private readonly IDietaryService _dietarySvc;
    private readonly IMealPlanService _mealPlanSvc;
    private readonly IMealOrderService _mealOrderSvc;
    private readonly IMealFeedbackService _feedbackSvc;
    private readonly IBoardingService _boardingSvc;

    public LearnerController(ApplicationDbContext db, UserManager<ApplicationUser> um,
        ILearnerAccountService learnerAccounts, ITeacherService teacherSvc, IDietaryService dietarySvc,
        IMealPlanService mealPlanSvc, IMealOrderService mealOrderSvc, IMealFeedbackService feedbackSvc,
        IBoardingService boardingSvc)
    {
        _boardingSvc     = boardingSvc;
        _db              = db;
        _userManager     = um;
        _learnerAccounts = learnerAccounts;
        _teacherSvc      = teacherSvc;
        _dietarySvc      = dietarySvc;
        _mealPlanSvc     = mealPlanSvc;
        _mealOrderSvc    = mealOrderSvc;
        _feedbackSvc     = feedbackSvc;
    }

    private string UserId => _userManager.GetUserId(User)!;

    // Resolved once per request in OnActionExecutionAsync
    private Learner Me { get; set; } = null!;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var learner = await _learnerAccounts.GetLearnerForUserAsync(UserId);
        if (learner is null)
        {
            // Account exists but is no longer linked to a learner record
            context.Result = Forbid();
            return;
        }
        Me = learner;
        await next();
    }

    private Task<Enrollment?> ActiveEnrollmentAsync() => _db.Enrollments
        .Include(e => e.Class).ThenInclude(c => c.Grade)
        .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
        .FirstOrDefaultAsync(e => e.LearnerId == Me.Id && e.IsActive);

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var enrollment = await ActiveEnrollmentAsync();
        var parent = Me.ParentId is null ? null : await _userManager.FindByIdAsync(Me.ParentId);

        var since = DateTime.Today.AddDays(-30);
        var att = await _db.Attendances.Where(a => a.LearnerId == Me.Id && a.Date >= since).ToListAsync();

        var preOrders = await _mealOrderSvc.GetPreOrderScreenAsync(Me.Id);
        var dietary = await _dietarySvc.GetParentViewAsync(Me.Id);

        return View(new LearnerDashboardViewModel
        {
            LearnerId      = Me.Id,
            LearnerName    = Me.FullName,
            ParentName     = parent?.FullName ?? "",
            IsEnrolled     = enrollment is not null,
            ClassName      = enrollment?.Class?.DisplayName ?? "",
            Subjects       = enrollment?.EnrollmentSubjects.Select(es => es.Subject?.Name ?? "").OrderBy(n => n).ToList() ?? new(),
            AttendanceDays = att.Count,
            AttendancePct  = att.Any()
                ? Math.Round((decimal)att.Count(a => a.Status == AttendanceStatus.Present) / att.Count * 100, 1)
                : 0,
            UpcomingMealOrders      = preOrders?.Meals.Count(m => m.CurrentOrderStatus is not null) ?? 0,
            HasActiveDietaryProfile = dietary.ActiveProfile is not null,
            IsBoarder               = await _db.RoomAllocations.AnyAsync(r => r.LearnerId == Me.Id && r.IsActive),
            UpcomingAssessments     = (await MyAssessmentsAsync(enrollment)).Where(a => a.IsUpcoming).OrderBy(a => a.Date).Take(6).ToList(),
            RecentResults           = (await MyAssessmentsAsync(enrollment)).Where(a => a.MarksObtained is not null).OrderByDescending(a => a.Date).Take(6).ToList(),
            RecentAttendance        = att.OrderByDescending(a => a.Date).Take(10)
                .Select(a => new AttendanceDayRow { Date = a.Date, Status = a.Status.ToString(), Notes = a.Notes }).ToList()
        });
    }

    // Assessments for the learner's class in the subjects they take, with their own marks
    private async Task<IList<LearnerAssessmentRow>> MyAssessmentsAsync(Enrollment? enrollment)
    {
        if (enrollment is null) return new List<LearnerAssessmentRow>();
        var subjectIds = enrollment.EnrollmentSubjects.Select(es => es.SubjectId).ToList();
        var assessments = await _db.Assessments.Include(a => a.Subject)
            .Where(a => a.ClassId == enrollment.ClassId && (subjectIds.Count == 0 || subjectIds.Contains(a.SubjectId)))
            .ToListAsync();
        var ids = assessments.Select(a => a.Id).ToList();
        var marks = await _db.Marks.Where(m => m.LearnerId == Me.Id && ids.Contains(m.AssessmentId))
            .ToDictionaryAsync(m => m.AssessmentId);
        return assessments.Select(a =>
        {
            marks.TryGetValue(a.Id, out var m);
            return new LearnerAssessmentRow
            {
                Id = a.Id, Name = a.Name, Type = a.Type.ToString(), Subject = a.Subject?.Name ?? "", Date = a.Date, Term = a.Term,
                TotalMarks = a.TotalMarks, MarksObtained = m?.MarksObtained, Percentage = m?.Percentage, Grade = m?.Grade, IsPassed = m?.IsPassed
            };
        }).ToList();
    }

    public async Task<IActionResult> Assessments()
    {
        var enrollment = await ActiveEnrollmentAsync();
        return View(new LearnerAssessmentsViewModel
        {
            LearnerName = Me.FullName,
            ClassName = enrollment?.Class?.DisplayName ?? "",
            Assessments = (await MyAssessmentsAsync(enrollment)).OrderByDescending(a => a.Date).ToList()
        });
    }

    // The learner's own boarding badge (same page the parent sees)
    public async Task<IActionResult> LearnerQr()
    {
        if (!await _db.RoomAllocations.AnyAsync(r => r.LearnerId == Me.Id && r.IsActive))
        {
            TempData["Error"] = "QR badges are only available for boarding learners.";
            return RedirectToAction(nameof(Index));
        }
        ViewBag.QrCode = await _boardingSvc.GetOrCreateLearnerQrCodeAsync(Me.Id);
        return View(ParentViews + "LearnerQr.cshtml", Me);
    }

    // Shared views link back to the parent's "learner overview" hub — for a learner that is the dashboard
    public IActionResult LearnerOverview() => RedirectToAction(nameof(Index));

    // ── Academics & Attendance ────────────────────────────────────────────────

    public async Task<IActionResult> AcademicReport(string term = "Term 1")
    {
        var enrollment = await ActiveEnrollmentAsync();
        if (enrollment is null)
        {
            TempData["Error"] = "You do not have an active enrolment yet.";
            return RedirectToAction(nameof(Index));
        }

        var report = await _teacherSvc.GetParentAcademicViewAsync(Me.Id, enrollment.ClassId, term);
        return View(ParentViews + "AcademicReport.cshtml", report);
    }

    public async Task<IActionResult> LearnerAttendance(string? from = null, string? to = null)
    {
        var fromDate = DateTime.TryParse(from, out var f) ? f : DateTime.Today.AddDays(-30);
        var toDate   = DateTime.TryParse(to, out var t) ? t : DateTime.Today;

        var enrollment = await ActiveEnrollmentAsync();
        var records = await _db.Attendances
            .Where(a => a.LearnerId == Me.Id && a.Date >= fromDate.Date && a.Date <= toDate.Date)
            .OrderByDescending(a => a.Date)
            .ToListAsync();

        return View(ParentViews + "LearnerAttendance.cshtml", new LearnerAttendanceViewModel
        {
            LearnerId   = Me.Id,
            LearnerName = Me.FullName,
            ClassName   = enrollment?.Class?.DisplayName ?? "",
            FromDate    = fromDate,
            ToDate      = toDate,
            PresentDays = records.Count(a => a.Status == AttendanceStatus.Present),
            AbsentDays  = records.Count(a => a.Status == AttendanceStatus.Absent),
            LateDays    = records.Count(a => a.Status == AttendanceStatus.Late),
            TotalDays   = records.Count,
            Records     = records.Select(a => new AttendanceDayRow
            {
                Date = a.Date, Status = a.Status.ToString(), Notes = a.Notes
            }).ToList()
        });
    }

    // ── Dietary Profile — active as soon as it is saved (parent is informed) ──

    public async Task<IActionResult> DietaryProfile()
        => View(ParentViews + "DietaryProfile.cshtml", await _dietarySvc.GetParentViewAsync(Me.Id));

    [HttpGet]
    public IActionResult DietaryProfileCreate()
        => View(ParentViews + "DietaryProfileCreate.cshtml",
            new DietaryProfileSubmitViewModel { LearnerId = Me.Id, LearnerName = Me.FullName });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DietaryProfileCreate(DietaryProfileSubmitViewModel vm)
    {
        vm.LearnerId = Me.Id;
        if (!vm.Items.Any(i => !string.IsNullOrWhiteSpace(i.Name)))
            ModelState.AddModelError("", "Add at least one dietary item, allergy, or requirement.");
        if (_dietarySvc.ValidateDocuments(vm.Documents) is { } docError)
            ModelState.AddModelError("", docError);

        if (!ModelState.IsValid)
        {
            vm.LearnerName = Me.FullName;
            return View(ParentViews + "DietaryProfileCreate.cshtml", vm);
        }

        await _dietarySvc.AddProfileAsync(Me.Id, UserId, vm.Items, vm.Documents);
        TempData["Success"] = "Dietary profile saved. It is now active and your parent has been informed.";
        return RedirectToAction(nameof(DietaryProfile));
    }

    public async Task<IActionResult> DietaryDocument(int id)
    {
        var doc = await _dietarySvc.GetDocumentAsync(id);
        if (doc is null || doc.Value.learnerId != Me.Id) return NotFound();
        return PhysicalFile(doc.Value.path, doc.Value.contentType);
    }

    // ── Meals ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> MealPlan(int week = 0)
        => View(ParentViews + "MealPlan.cshtml", await _mealPlanSvc.GetWeekForParentAsync(Math.Clamp(week, 0, 1)));

    public async Task<IActionResult> PreOrder()
        => View(ParentViews + "PreOrder.cshtml", await _mealOrderSvc.GetPreOrderScreenAsync(Me.Id));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceMealOrder(int mealPlanItemId, PortionSize portionSize)
    {
        // Recorded with the learner's own user id so the parent can see who placed it
        var (success, message) = await _mealOrderSvc.PlaceOrderAsync(mealPlanItemId, Me.Id, UserId, portionSize);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(PreOrder));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelMealOrder(int mealPlanItemId)
    {
        await _mealOrderSvc.CancelOrderAsync(mealPlanItemId, Me.Id);
        TempData["Success"] = "Pre-order cancelled.";
        return RedirectToAction(nameof(PreOrder));
    }

    // Anonymous feedback — identical to the parent flow
    [HttpGet]
    public async Task<IActionResult> MealFeedback(int mealPlanItemId)
    {
        var item = await _feedbackSvc.GetMealForFeedbackAsync(mealPlanItemId);
        if (item is null) return NotFound();

        if (!_feedbackSvc.IsWithinFeedbackWindow(item.MealPlan, item))
        {
            TempData["Error"] = "Feedback can only be submitted within 24 hours of the meal's serving time.";
            return RedirectToAction(nameof(MealPlan));
        }

        return View(ParentViews + "MealFeedback.cshtml", new MealFeedbackSubmitViewModel
        {
            MealPlanItemId  = item.Id,
            MenuDescription = item.MenuDescription,
            MealType        = item.MealType.ToString()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MealFeedback(MealFeedbackSubmitViewModel vm)
    {
        if (!ModelState.IsValid) return View(ParentViews + "MealFeedback.cshtml", vm);

        var (success, message) = await _feedbackSvc.SubmitFeedbackAsync(vm.MealPlanItemId, vm.Rating, vm.Comment, vm.Portion);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(MealPlan));
    }
}
