using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DlangezwaHS.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IApplicationService _appService;
    private readonly IEnrollmentService _enrollService;
    private readonly IAllocationService _allocService;
    private readonly IPaymentService _paymentService;
    private readonly ITeacherService _teacherSvc;
    private readonly IQuestionPaperService _qpService;
    private readonly IStaffOnboardingService _staffSvc;
    private readonly IKitchenScheduleService _kitchenScheduleSvc;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AdminController> _logger;

    public AdminController(ApplicationDbContext db, IApplicationService appService,
        IEnrollmentService enrollService, IAllocationService allocService,
        IPaymentService paymentService, ITeacherService teacherSvc,
        IQuestionPaperService qpService, IStaffOnboardingService staffSvc,
        IKitchenScheduleService kitchenScheduleSvc,
        UserManager<ApplicationUser> um, ILogger<AdminController> logger)
    {
        _db = db;
        _appService = appService;
        _enrollService = enrollService;
        _allocService = allocService;
        _paymentService = paymentService;
        _teacherSvc = teacherSvc;
        _qpService = qpService;
        _staffSvc = staffSvc;
        _kitchenScheduleSvc = kitchenScheduleSvc;
        _userManager = um;
        _logger = logger;
    }

    private string UserId => _userManager.GetUserId(User)!;
    private string UserName => User.Identity?.Name ?? "Admin";
    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Dashboard()
    {
        var vm = new AdminDashboardViewModel
        {
            PendingApplications = await _db.Applications.CountAsync(a => a.Status == ApplicationStatus.Pending),
            TotalEnrolments = await _db.Enrollments.CountAsync(e => e.IsActive),
            TotalRooms = await _db.Rooms.CountAsync(r => r.IsActive),
            AvailableBeds = await _db.Beds.CountAsync(b => b.Status == BedStatus.Available),
            TotalRevenue = (int)await _db.Payments.Where(p => p.Status == PaymentStatus.Completed).SumAsync(p => p.Amount),
            RecentApplications = (await _appService.GetAllAsync(ApplicationStatus.Pending)).Take(5)
                .Select(a => new ApplicationSummaryViewModel
                {
                    Id = a.Id,
                    LearnerName = a.Learner?.FullName ?? "",
                    ParentName = a.Parent?.FullName ?? "",
                    ParentEmail = a.Parent?.Email ?? "",
                    Status = a.Status,
                    SubmittedAt = a.SubmittedAt
                }).ToList(),
            RecentPayments = await _db.Payments.Include(p => p.Learner)
                .OrderByDescending(p => p.CreatedAt).Take(5).ToListAsync()
        };
        return View(vm);
    }

    // ── Applications ──────────────────────────────────────────────────────────

    public async Task<IActionResult> Applications(string? status, string? search)
    {
        ApplicationStatus? filter = status switch
        {
            "pending" => ApplicationStatus.Pending,
            "approved" => ApplicationStatus.Approved,
            "rejected" => ApplicationStatus.Rejected,
            "enrolled" => ApplicationStatus.Enrolled,
            _ => null
        };
        var all = await _appService.GetAllAsync(filter);

        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(a => (a.Learner?.FullName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                                || (a.Parent?.Email ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        return View(new ApplicationListViewModel
        {
            Applications = all.Select(a => new ApplicationSummaryViewModel
            {
                Id = a.Id,
                LearnerName = a.Learner?.FullName ?? "",
                ParentName = a.Parent?.FullName ?? "",
                ParentEmail = a.Parent?.Email ?? "",
                Status = a.Status,
                SubmittedAt = a.SubmittedAt
            }).ToList(),
            StatusFilter = status,
            SearchTerm = search
        });
    }

    public async Task<IActionResult> ApplicationDetail(int id)
    {
        var app = await _appService.GetByIdAsync(id);
        if (app is null) return NotFound();

        // Audit history for this application
        ViewBag.AuditLogs = await _db.AuditLogs
            .Where(l => l.EntityType == "Application" && l.EntityId == id.ToString())
            .OrderByDescending(l => l.Timestamp).ToListAsync();

        return View(new ApplicationDetailViewModel { Application = app });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            await _appService.ApproveApplicationAsync(id, UserId, UserName, ClientIp);
            TempData["Success"] = "Application approved. Notification sent to parent.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving application {Id}", id);
            TempData["Error"] = "Could not approve application.";
        }
        return RedirectToAction(nameof(ApplicationDetail), new { id });
    }

    [HttpGet] public IActionResult Reject(int id) => View(new RejectApplicationViewModel { ApplicationId = id });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(RejectApplicationViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _appService.RejectApplicationAsync(vm.ApplicationId, vm.RejectionReason, UserId, UserName, ClientIp);
            TempData["Success"] = "Application rejected. Notification sent to parent.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting application {Id}", vm.ApplicationId);
            TempData["Error"] = "Could not reject application.";
        }
        return RedirectToAction(nameof(ApplicationDetail), new { id = vm.ApplicationId });
    }

    // ── Enrolment ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Enroll(int applicationId)
    {
        var app = await _appService.GetByIdAsync(applicationId);
        if (app is null || app.Status != ApplicationStatus.Approved) return NotFound();

        var classes = await _db.Classes.Include(c => c.Grade).OrderBy(c => c.Grade.Level).ThenBy(c => c.Section).ToListAsync();
        var subjects = await _db.Subjects.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();

        return View(new EnrollLearnerViewModel
        {
            ApplicationId = applicationId,
            LearnerName = app.Learner?.FullName ?? "",
            AvailableClasses = classes,
            AvailableSubjects = subjects
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Enroll(EnrollLearnerViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.AvailableClasses = await _db.Classes.Include(c => c.Grade).OrderBy(c => c.Grade.Level).ThenBy(c => c.Section).ToListAsync();
            vm.AvailableSubjects = await _db.Subjects.Where(s => s.IsActive).ToListAsync();
            return View(vm);
        }
        try
        {
            var enrollment = await _enrollService.EnrollAsync(vm.ApplicationId, vm.ClassId, vm.SubjectIds, UserId, UserName, ClientIp);
            TempData["Success"] = "Learner enrolled successfully.";
            return RedirectToAction(nameof(EnrollmentDetail), new { id = enrollment.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enrolling learner");
            TempData["Error"] = ex.Message;
            vm.AvailableClasses = await _db.Classes.Include(c => c.Grade).ToListAsync();
            vm.AvailableSubjects = await _db.Subjects.Where(s => s.IsActive).ToListAsync();
            return View(vm);
        }
    }

    public async Task<IActionResult> Enrollments(string? search)
    {
        var q = _db.Enrollments
            .Include(e => e.Learner)
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .Where(e => e.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(e => e.Learner.FirstName.Contains(search) || e.Learner.LastName.Contains(search));

        return View(await q.OrderByDescending(e => e.EnrolledAt).ToListAsync());
    }

    public async Task<IActionResult> EnrollmentDetail(int id)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Learner).ThenInclude(l => l.Parent)
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (enrollment is null) return NotFound();

        var allocation = await _allocService.GetActiveAllocationAsync(enrollment.LearnerId);
        var payments = await _db.Payments.Where(p => p.LearnerId == enrollment.LearnerId && p.Status == PaymentStatus.Completed).ToListAsync();
        var proofs = await _db.RegistrationProofs.Where(r => r.EnrollmentId == id).ToListAsync();

        ViewBag.Allocation = allocation;
        ViewBag.Payments = payments;
        ViewBag.Proofs = proofs;
        return View(enrollment);
    }

    // ── Room / Bed Allocation ─────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> AllocateBed(int learnerId)
    {
        var learner = await _db.Learners.FindAsync(learnerId);
        if (learner is null) return NotFound();
        var rooms = await _db.Rooms.Include(r => r.Beds).Where(r => r.IsActive).ToListAsync();
        return View(new AllocateBedViewModel
        {
            LearnerId = learnerId,
            LearnerName = learner.FullName,
            AvailableRooms = rooms
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AllocateBed(AllocateBedViewModel vm)
    {
        if (!ModelState.IsValid) return await AllocateBed(vm.LearnerId);
        try
        {
            await _allocService.AllocateAsync(vm.LearnerId, vm.RoomId, vm.BedId, UserId, UserName, ClientIp);
            TempData["Success"] = "Bed allocated successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("EnrollmentDetail", new { id = (await _enrollService.GetEnrollmentByLearnerAsync(vm.LearnerId))?.Id ?? 0 });
    }

    [HttpGet("/api/rooms/{roomId}/beds")]
    public async Task<IActionResult> GetAvailableBeds(int roomId)
    {
        var beds = await _allocService.GetAvailableBedsAsync(roomId);
        return Json(beds.Select(b => new { b.Id, b.BedNumber }));
    }

    // ── Rooms management ──────────────────────────────────────────────────────

    public async Task<IActionResult> Rooms()
    {
        var rooms = await _db.Rooms
            .Include(r => r.Beds)
            .Include(r => r.Allocations.Where(a => a.IsActive))
            .Where(r => r.IsActive)
            .ToListAsync();
        return View(rooms);
    }

    [HttpGet] public IActionResult AddRoom() => View(new AddRoomViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRoom(AddRoomViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var room = new Room { Name = vm.Name, Capacity = vm.Capacity, Notes = vm.Notes };
        _db.Rooms.Add(room);
        await _db.SaveChangesAsync();
        // Auto-create beds
        for (int i = 1; i <= vm.BedCount; i++)
            _db.Beds.Add(new Bed { RoomId = room.Id, BedNumber = $"B{i}", Status = BedStatus.Available });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Room '{vm.Name}' added with {vm.BedCount} beds.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpGet]
    public async Task<IActionResult> AddBed(int roomId)
    {
        var rooms = await _db.Rooms.Where(r => r.IsActive).ToListAsync();
        return View(new AddBedViewModel { RoomId = roomId, Rooms = rooms });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBed(AddBedViewModel vm)
    {
        if (!ModelState.IsValid) { vm.Rooms = await _db.Rooms.ToListAsync(); return View(vm); }
        _db.Beds.Add(new Bed { RoomId = vm.RoomId, BedNumber = vm.BedNumber, Status = BedStatus.Available });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Bed added.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room is not null) { room.IsActive = false; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Rooms));
    }

    // ── Manual Payment ────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> RecordPayment(int learnerId)
    {
        var learner = await _db.Learners.Include(l => l.Parent).FirstOrDefaultAsync(l => l.Id == learnerId);
        if (learner is null) return NotFound();
        return View(new ManualPaymentViewModel
        {
            LearnerId = learnerId,
            LearnerName = learner.FullName
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(ManualPaymentViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var learner = await _db.Learners.Include(l => l.Parent).FirstOrDefaultAsync(l => l.Id == vm.LearnerId);
        if (learner is null) return NotFound();
        try
        {
            await _paymentService.RecordManualPaymentAsync(vm.LearnerId, learner.ParentId!, vm.Amount, vm.Type, vm.BankRef, vm.Notes, UserId);
            TempData["Success"] = "Payment recorded successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording payment");
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("EnrollmentDetail", new { id = (await _enrollService.GetEnrollmentByLearnerAsync(vm.LearnerId))?.Id ?? 0 });
    }

    // ── Generate Proof ────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateProof(int enrollmentId)
    {
        try
        {
            var pdfBytes = await _paymentService.GenerateAndStoreProofAsync(enrollmentId, UserId);
            TempData["Success"] = "Proof of Registration generated and emailed to parent.";
            return File(pdfBytes, "application/pdf", $"proof_{enrollmentId}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating proof");
            TempData["Error"] = ex.Message;
            return RedirectToAction("EnrollmentDetail", new { id = enrollmentId });
        }
    }

    // ── Payments List ─────────────────────────────────────────────────────────

    public async Task<IActionResult> Payments(string? status)
    {
        var q = _db.Payments.Include(p => p.Learner).Include(p => p.Parent).AsQueryable();
        if (status == "completed") q = q.Where(p => p.Status == PaymentStatus.Completed);
        if (status == "pending") q = q.Where(p => p.Status == PaymentStatus.Pending);
        return View(await q.OrderByDescending(p => p.CreatedAt).ToListAsync());
    }

    // ── Export CSV ────────────────────────────────────────────────────────────

    public async Task<IActionResult> ExportEnrollments()
    {
        var enrollments = await _db.Enrollments
            .Include(e => e.Learner)
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .Where(e => e.IsActive)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("LearnerName,LearnerID,Class,Subjects,EnrolledDate");
        foreach (var e in enrollments)
        {
            var subjects = string.Join("|", e.EnrollmentSubjects.Select(es => es.Subject?.Name ?? ""));
            sb.AppendLine($"\"{e.Learner?.FullName}\",\"{e.Learner?.LearnerIdNumber}\",\"{e.Class?.DisplayName}\",\"{subjects}\",\"{e.EnrolledAt:yyyy-MM-dd}\"");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "enrollments.csv");
    }

    // ── Audit Logs ────────────────────────────────────────────────────────────

    public async Task<IActionResult> AuditLogs(string? userId, string? action, int page = 1)
    {
        const int pageSize = 50;
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(userId)) q = q.Where(l => l.UserId == userId);
        if (!string.IsNullOrEmpty(action)) q = q.Where(l => l.Action.Contains(action));
        var total = await q.CountAsync();
        var logs = await q.OrderByDescending(l => l.Timestamp).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.Pages = (int)Math.Ceiling((double)total / pageSize);
        return View(logs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SETTINGS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Settings()
    {
        ViewBag.Grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync();
        ViewBag.Classes = await _db.Classes.Include(c => c.Grade).OrderBy(c => c.Grade.Level).ThenBy(c => c.Section).ToListAsync();
        ViewBag.Subjects = await _db.Subjects.OrderBy(s => s.Name).ToListAsync();
        ViewBag.Teachers = await _db.Teachers.Include(t => t.TeacherSubjects).ThenInclude(ts => ts.Subject).OrderBy(t => t.LastName).ToListAsync();
        ViewBag.FeeTypes = await _db.FeeTypes.Where(f => f.IsActive).ToListAsync();
        ViewBag.Templates = await _db.EmailTemplates.OrderBy(t => t.TemplateKey).ToListAsync();
        ViewBag.BoardingSettings = await _db.BoardingSettings.FirstOrDefaultAsync();
        return View();
    }

    // ── Employ Staff (unified Teacher / Housemaster / Kitchen Staff) ───────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EmployStaff(EmployStaffViewModel vm)
    {
        // ClassIds/SubjectIds arrive as strings because the dropdowns are hidden
        // (not removed) for non-Teacher roles and may submit their blank default option.
        var classIds = vm.ClassIds.Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
        var subjectIds = vm.SubjectIds.Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();

        if (vm.Role == StaffRole.Teacher && (!classIds.Any() || !subjectIds.Any()))
            ModelState.AddModelError("", "Assign at least one class and subject for a teacher.");

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Settings));
        }

        try
        {
            switch (vm.Role)
            {
                case StaffRole.Teacher:
                    var teacherVm = new RegisterTeacherViewModel
                    {
                        FirstName = vm.FirstName, LastName = vm.LastName, Email = vm.Email, Phone = vm.Phone,
                        ClassIds = classIds,
                        SubjectIds = subjectIds
                    };
                    var (teacher, tPwd) = await _teacherSvc.RegisterTeacherAsync(teacherVm);
                    TempData["Success"] = $"Teacher {teacher.FullName} employed. Credentials emailed to {vm.Email}. Temporary password: {tPwd}";
                    break;

                case StaffRole.Housemaster:
                    var hmVm = new RegisterHousemasterViewModel { FirstName = vm.FirstName, LastName = vm.LastName, Email = vm.Email, Phone = vm.Phone };
                    var (housemaster, hPwd) = await _staffSvc.RegisterHousemasterAsync(hmVm);
                    TempData["Success"] = $"Housemaster {housemaster.FullName} employed. Credentials emailed to {vm.Email}. Temporary password: {hPwd}";
                    break;

                case StaffRole.KitchenStaff:
                    var ksVm = new RegisterKitchenStaffViewModel { FirstName = vm.FirstName, LastName = vm.LastName, Email = vm.Email, Phone = vm.Phone };
                    var (staff, kPwd) = await _staffSvc.RegisterKitchenStaffAsync(ksVm);
                    TempData["Success"] = $"Kitchen staff {staff.FullName} employed. Credentials emailed to {vm.Email}. Temporary password: {kPwd}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error employing staff member");
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBoardingSettings(BoardingSettingsViewModel vm)
    {
        var times = new[] { vm.BreakfastStart, vm.BreakfastServe, vm.LunchStart, vm.LunchServe, vm.DinnerStart, vm.DinnerServe };
        if (times.Any(t => !TimeSpan.TryParse(t, out _)))
        {
            TempData["Error"] = "Enter every kitchen shift time as HH:mm.";
            return RedirectToAction(nameof(Settings));
        }
        if (TimeSpan.Parse(vm.BreakfastStart) >= TimeSpan.Parse(vm.BreakfastServe)
            || TimeSpan.Parse(vm.LunchStart) >= TimeSpan.Parse(vm.LunchServe)
            || TimeSpan.Parse(vm.DinnerStart) >= TimeSpan.Parse(vm.DinnerServe))
        {
            TempData["Error"] = "Each kitchen shift must start before the meal is served.";
            return RedirectToAction(nameof(Settings));
        }

        var settings = await _db.BoardingSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new BoardingSettings();
            _db.BoardingSettings.Add(settings);
        }
        settings.WeeklyMealBudget = vm.WeeklyMealBudget;
        settings.MaxPreOrdersPerMeal = vm.MaxPreOrdersPerMeal;
        settings.ExpectedDinersPerMeal = Math.Clamp(vm.ExpectedDinersPerMeal, 1, 5000);
        settings.BreakfastStart = vm.BreakfastStart;
        settings.BreakfastServe = vm.BreakfastServe;
        settings.LunchStart = vm.LunchStart;
        settings.LunchServe = vm.LunchServe;
        settings.DinnerStart = vm.DinnerStart;
        settings.DinnerServe = vm.DinnerServe;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Boarding settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    // Grades
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGrade(GradeFormViewModel vm)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Invalid data."; return RedirectToAction(nameof(Settings)); }
        if (vm.Id.HasValue)
        {
            var g = await _db.Grades.FindAsync(vm.Id.Value);
            if (g is not null) { g.Name = vm.Name; g.Level = vm.Level; }
        }
        else _db.Grades.Add(new Grade { Name = vm.Name, Level = vm.Level });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Grade saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGrade(int id)
    {
        var g = await _db.Grades.FindAsync(id);
        if (g is not null) { _db.Grades.Remove(g); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Settings));
    }

    // Classes
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveClass(ClassFormViewModel vm)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Invalid data."; return RedirectToAction(nameof(Settings)); }
        if (vm.Id.HasValue)
        {
            var c = await _db.Classes.FindAsync(vm.Id.Value);
            if (c is not null) { c.GradeId = vm.GradeId; c.Section = vm.Section; c.Capacity = vm.Capacity; }
        }
        else _db.Classes.Add(new Class { GradeId = vm.GradeId, Section = vm.Section, Capacity = vm.Capacity });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Class saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteClass(int id)
    {
        var c = await _db.Classes.FindAsync(id);
        if (c is not null) { _db.Classes.Remove(c); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Settings));
    }

    // Subjects
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSubject(SubjectFormViewModel vm)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Invalid data."; return RedirectToAction(nameof(Settings)); }
        Subject subject;
        if (vm.Id.HasValue)
        {
            subject = (await _db.Subjects.FindAsync(vm.Id.Value))!;
            subject.Name = vm.Name; subject.Code = vm.Code;
        }
        else
        {
            subject = new Subject { Name = vm.Name, Code = vm.Code };
            _db.Subjects.Add(subject);
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Subject saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubject(int id)
    {
        var s = await _db.Subjects.FindAsync(id);
        if (s is not null) { s.IsActive = false; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Settings));
    }

    // Teachers
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTeacher(TeacherFormViewModel vm)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Invalid data."; return RedirectToAction(nameof(Settings)); }
        Teacher teacher;
        if (vm.Id.HasValue)
        {
            teacher = (await _db.Teachers.Include(t => t.TeacherSubjects).FirstOrDefaultAsync(t => t.Id == vm.Id))!;
            teacher.FirstName = vm.FirstName; teacher.LastName = vm.LastName;
            teacher.Email = vm.Email; teacher.Phone = vm.Phone;
            teacher.TeacherSubjects.Clear();
        }
        else
        {
            teacher = new Teacher { FirstName = vm.FirstName, LastName = vm.LastName, Email = vm.Email, Phone = vm.Phone };
            _db.Teachers.Add(teacher);
        }
        await _db.SaveChangesAsync();
        foreach (var sid in vm.SubjectIds)
            _db.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = sid });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Teacher saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTeacher(int id)
    {
        var t = await _db.Teachers.FindAsync(id);
        if (t is not null) { t.IsActive = false; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Settings));
    }

    // Fee Types
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFeeType(FeeTypeFormViewModel vm)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Invalid data."; return RedirectToAction(nameof(Settings)); }
        if (vm.Id.HasValue)
        {
            var f = await _db.FeeTypes.FindAsync(vm.Id.Value);
            if (f is not null) { f.Name = vm.Name; f.Amount = vm.Amount; f.Description = vm.Description; }
        }
        else _db.FeeTypes.Add(new FeeType { Name = vm.Name, Amount = vm.Amount, Description = vm.Description });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Fee type saved.";
        return RedirectToAction(nameof(Settings));
    }

    // Email Templates
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEmailTemplate(int id, string subject, string body)
    {
        var t = await _db.EmailTemplates.FindAsync(id);
        if (t is not null) { t.Subject = subject; t.Body = body; t.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        TempData["Success"] = "Template saved.";
        return RedirectToAction(nameof(Settings));
    }
    // ─────────────────────────────────────────────────────────────────────────
    // TEACHER MANAGEMENT
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Teachers()
    {
        var teachers = await _db.Teachers
            .Include(t => t.TeacherClassSubjects)
                .ThenInclude(tc => tc.Class).ThenInclude(c => c.Grade)
            .Include(t => t.TeacherClassSubjects)
                .ThenInclude(tc => tc.Subject)
            .OrderBy(t => t.LastName)
            .ToListAsync();

        var rows = teachers.Select(t => new TeacherSummaryRow
        {
            Id = t.Id,
            FullName = t.FullName,
            Email = t.Email ?? "",
            Phone = t.Phone ?? "",
            HasLogin = t.UserId is not null,
            IsActive = t.IsActive,
            Assignments = string.Join(", ", t.TeacherClassSubjects
                .Select(tc => $"{tc.Class?.DisplayName} – {tc.Subject?.Name}"))
        }).ToList();

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> RegisterTeacher(int? id = null)
    {
        var classes = await _db.Classes.Include(c => c.Grade)
            .OrderBy(c => c.Grade.Level).ThenBy(c => c.Section).ToListAsync();
        var subjects = await _db.Subjects.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();

        var vm = new RegisterTeacherViewModel
        {
            AllClasses = classes,
            AllSubjects = subjects
        };

        if (id.HasValue)
        {
            var t = await _db.Teachers
                .Include(t => t.TeacherClassSubjects)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (t is not null)
            {
                vm.Id = t.Id;
                vm.FirstName = t.FirstName;
                vm.LastName = t.LastName;
                vm.Email = t.Email ?? "";
                vm.Phone = t.Phone;
                vm.ClassIds = t.TeacherClassSubjects.Select(tc => tc.ClassId).ToList();
                vm.SubjectIds = t.TeacherClassSubjects.Select(tc => tc.SubjectId).ToList();
            }
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterTeacher(RegisterTeacherViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.AllClasses = await _db.Classes.Include(c => c.Grade).OrderBy(c => c.Grade.Level).ToListAsync();
            vm.AllSubjects = await _db.Subjects.Where(s => s.IsActive).ToListAsync();
            return View(vm);
        }

        try
        {
            if (vm.Id.HasValue)
            {
                // Update existing teacher assignments only
                await _teacherSvc.UpdateTeacherAssignmentsAsync(vm.Id.Value, vm.ClassIds, vm.SubjectIds);
                TempData["Success"] = "Teacher assignments updated.";
            }
            else
            {
                var (teacher, password) = await _teacherSvc.RegisterTeacherAsync(vm);
                TempData["Success"] = $"Teacher {teacher.FullName} registered. Credentials emailed to {vm.Email}. Temporary password: {password}";
            }
            return RedirectToAction(nameof(Teachers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering teacher");
            ModelState.AddModelError("", ex.Message);
            vm.AllClasses = await _db.Classes.Include(c => c.Grade).OrderBy(c => c.Grade.Level).ToListAsync();
            vm.AllSubjects = await _db.Subjects.Where(s => s.IsActive).ToListAsync();
            return View(vm);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateTeacher(int id)
    {
        await _teacherSvc.DeactivateTeacherAsync(id, UserId);
        TempData["Success"] = "Teacher account deactivated.";
        return RedirectToAction(nameof(Teachers));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HOUSEMASTERS (Increment 3)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Housemasters()
        => View(await _staffSvc.GetHousemastersAsync());

    [HttpGet]
    public async Task<IActionResult> RegisterHousemaster(int? id = null)
    {
        var vm = new RegisterHousemasterViewModel();
        if (id.HasValue)
        {
            var h = await _db.Housemasters.FirstOrDefaultAsync(h => h.Id == id);
            if (h is not null)
            {
                vm.Id = h.Id;
                vm.FirstName = h.FirstName;
                vm.LastName = h.LastName;
                vm.Email = h.Email ?? "";
                vm.Phone = h.Phone;
            }
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterHousemaster(RegisterHousemasterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            if (vm.Id.HasValue)
            {
                await _staffSvc.UpdateHousemasterAsync(vm.Id.Value, vm);
                TempData["Success"] = "Housemaster details updated.";
            }
            else
            {
                var (housemaster, password) = await _staffSvc.RegisterHousemasterAsync(vm);
                TempData["Success"] = $"Housemaster {housemaster.FullName} registered. Credentials emailed to {vm.Email}. Temporary password: {password}";
            }
            return RedirectToAction(nameof(Housemasters));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering housemaster");
            ModelState.AddModelError("", ex.Message);
            return View(vm);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateHousemaster(int id)
    {
        await _staffSvc.DeactivateHousemasterAsync(id);
        TempData["Success"] = "Housemaster account deactivated.";
        return RedirectToAction(nameof(Housemasters));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KITCHEN STAFF (Increment 3)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> KitchenStaff()
        => View(await _staffSvc.GetKitchenStaffAsync());

    [HttpGet]
    public async Task<IActionResult> RegisterKitchenStaff(int? id = null)
    {
        var vm = new RegisterKitchenStaffViewModel();
        if (id.HasValue)
        {
            var k = await _db.KitchenStaffMembers.FirstOrDefaultAsync(k => k.Id == id);
            if (k is not null)
            {
                vm.Id = k.Id;
                vm.FirstName = k.FirstName;
                vm.LastName = k.LastName;
                vm.Email = k.Email ?? "";
                vm.Phone = k.Phone;
            }
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterKitchenStaff(RegisterKitchenStaffViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            if (vm.Id.HasValue)
            {
                await _staffSvc.UpdateKitchenStaffAsync(vm.Id.Value, vm);
                TempData["Success"] = "Kitchen staff details updated.";
            }
            else
            {
                var (staff, password) = await _staffSvc.RegisterKitchenStaffAsync(vm);
                TempData["Success"] = $"Kitchen staff {staff.FullName} registered. Credentials emailed to {vm.Email}. Temporary password: {password}";
            }
            return RedirectToAction(nameof(KitchenStaff));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering kitchen staff");
            ModelState.AddModelError("", ex.Message);
            return View(vm);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateKitchenStaff(int id)
    {
        await _staffSvc.DeactivateKitchenStaffAsync(id);
        TempData["Success"] = "Kitchen staff account deactivated.";
        return RedirectToAction(nameof(KitchenStaff));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KITCHEN SCHEDULE (UC7, Increment 3)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> KitchenSchedule()
        => View(await _kitchenScheduleSvc.GetSchedulesAsync());

    // Planner: upcoming meals by day and serving time; picking a team schedules it straight away
    public async Task<IActionResult> KitchenScheduleCreate()
    {
        ViewBag.BackAction = nameof(KitchenSchedule);
        return View("~/Views/Shared/KitchenPlanner.cshtml", await _kitchenScheduleSvc.GetPlannerAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickSchedule(int mealPlanItemId, int teamId, bool acknowledgeUnderstaff)
        => Json(await _kitchenScheduleSvc.QuickScheduleAsync(mealPlanItemId, teamId, acknowledgeUnderstaff));

    // ── Kitchen Teams ────────────────────────────────────────────────────────

    public async Task<IActionResult> KitchenTeams()
    {
        ViewBag.BackAction = nameof(KitchenSchedule);
        return View("~/Views/Shared/KitchenTeams.cshtml", await _kitchenScheduleSvc.GetTeamsAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveKitchenTeam(int? id, string name, List<int> memberIds, int? headChefId)
    {
        try
        {
            var team = await _kitchenScheduleSvc.SaveTeamAsync(id, name, memberIds, headChefId);
            TempData["Success"] = $"Team \"{team.Name}\" saved.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(KitchenTeams));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleKitchenTeam(int id)
    {
        await _kitchenScheduleSvc.ToggleTeamAsync(id);
        return RedirectToAction(nameof(KitchenTeams));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishKitchenSchedule(int id)
    {
        await _kitchenScheduleSvc.PublishScheduleAsync(id, UserId);
        if (Request.Headers.XRequestedWith == "XMLHttpRequest") return Json(new { success = true });
        TempData["Success"] = "Kitchen schedule published. Staff have been notified.";
        return RedirectToAction(nameof(KitchenSchedule));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ACADEMIC REPORTS & MARK EDITING
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> AcademicReports(int? classId, int? subjectId, string term = "Term 1")
    {
        var classes = await _db.Classes.Include(c => c.Grade)
            .OrderBy(c => c.Grade.Level).ThenBy(c => c.Section).ToListAsync();
        var subjects = await _db.Subjects.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();

        ClassPerformanceViewModel? report = null;
        if (classId.HasValue && subjectId.HasValue)
        {
            report = await _teacherSvc.GetClassPerformanceAsync(classId.Value, subjectId.Value, term);
        }

        return View(new AcademicReportFilterViewModel
        {
            ClassId = classId,
            SubjectId = subjectId,
            Term = term,
            AllClasses = classes,
            AllSubjects = subjects,
            Report = report
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditMark(int markId)
    {
        var mark = await _db.Marks
            .Include(m => m.Learner)
            .Include(m => m.Assessment)
            .FirstOrDefaultAsync(m => m.Id == markId);

        if (mark is null) return NotFound();

        return View(new AdminEditMarkViewModel
        {
            MarkId = mark.Id,
            LearnerName = mark.Learner?.FullName ?? "",
            AssessmentName = mark.Assessment?.Name ?? "",
            TotalMarks = mark.Assessment?.TotalMarks ?? 0,
            MarksObtained = mark.MarksObtained,
            Comments = mark.Comments
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMark(AdminEditMarkViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await _teacherSvc.AdminEditMarkAsync(vm.MarkId, vm.MarksObtained,
                vm.Comments, UserId, vm.AdminReason);
            TempData["Success"] = "Mark updated successfully. Change recorded in audit log.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing mark");
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(AcademicReports));
    }

    // Attendance report for admin
    public async Task<IActionResult> AttendanceReport(int? classId, string? from, string? to)
    {
        var classes = await _db.Classes.Include(c => c.Grade)
            .OrderBy(c => c.Grade.Level).ThenBy(c => c.Section).ToListAsync();
        ViewBag.Classes = classes;
        ViewBag.ClassId = classId;

        if (!classId.HasValue) return View(new List<AttendanceSummaryRow>());

        var fromDate = from is not null ? DateTime.Parse(from) : DateTime.Today.AddDays(-30);
        var toDate = to is not null ? DateTime.Parse(to) : DateTime.Today;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        var summary = await _teacherSvc.GetAttendanceSummaryAsync(classId.Value, fromDate, toDate);
        var cls = classes.FirstOrDefault(c => c.Id == classId);
        ViewBag.ClassName = cls?.DisplayName ?? "";
        return View(summary);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UC14 — QUESTION PAPERS (Admin management)
    // ─────────────────────────────────────────────────────────────────────────

    // GET /Admin/QuestionPapers
    public async Task<IActionResult> QuestionPapers(string? statusFilter)
    {
        var all = await _db.QuestionPapers
            .Include(p => p.Subject)
            .Include(p => p.Grade)
            .Include(p => p.UploadedByTeacher)
            .OrderByDescending(p => p.UploadedAt)
            .ToListAsync();

        if (!string.IsNullOrEmpty(statusFilter) &&
            Enum.TryParse<QuestionPaperStatus>(statusFilter, out var sf))
            all = all.Where(p => p.Status == sf).ToList();

        ViewBag.StatusFilter = statusFilter;
        ViewBag.PendingCount = all.Count(p => p.Status == QuestionPaperStatus.Pending);
        return View(all);
    }

    // GET /Admin/QuestionPaperDetail/5
    public async Task<IActionResult> QuestionPaperDetail(int id)
    {
        var paper = await _qpService.GetByIdAsync(id);
        if (paper == null) return NotFound();
        return View(paper);
    }

    // POST /Admin/ApproveQuestionPaper/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveQuestionPaper(int id)
    {
        try
        {
            await _qpService.ApproveAsync(id, UserId, UserName, ClientIp);
            TempData["Success"] = "Paper approved. You can now release it to learners.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(QuestionPaperDetail), new { id });
    }

    // POST /Admin/RejectQuestionPaper/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectQuestionPaper(int id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A rejection reason is required.";
            return RedirectToAction(nameof(QuestionPaperDetail), new { id });
        }
        try
        {
            await _qpService.RejectAsync(id, reason, UserId, UserName, ClientIp);
            TempData["Success"] = "Paper rejected.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(QuestionPapers));
    }

    // POST /Admin/ReleaseQuestionPaper/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseQuestionPaper(int id)
    {
        try
        {
            await _qpService.ReleaseAsync(id, UserId, UserName, ClientIp);
            TempData["Success"] = "Paper released. Teachers and learners can now download it.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(QuestionPaperDetail), new { id });
    }


}

