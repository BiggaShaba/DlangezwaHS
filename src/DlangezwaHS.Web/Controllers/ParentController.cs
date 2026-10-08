using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize(Roles = "Parent")]
public class ParentController : Controller
{
    private readonly IApplicationService          _appService;
    private readonly IPaymentService              _paymentService;
    private readonly IDocumentService             _docService;
    private readonly ApplicationDbContext         _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ParentController>    _logger;

    private readonly ITeacherService _teacherSvc;
    private readonly IBoardingService _boardingSvc;
    private readonly IDietaryService _dietarySvc;
    private readonly IMealPlanService _mealPlanSvc;
    private readonly IMealOrderService _mealOrderSvc;
    private readonly IMealFeedbackService _feedbackSvc;
    private readonly ILearnerAccountService _learnerAccounts;

    public ParentController(IApplicationService appService, IPaymentService paymentService,
        IDocumentService docService, ApplicationDbContext db,
        UserManager<ApplicationUser> um, ITeacherService teacherSvc, IBoardingService boardingSvc,
        IDietaryService dietarySvc, IMealPlanService mealPlanSvc, IMealOrderService mealOrderSvc,
        IMealFeedbackService feedbackSvc, ILearnerAccountService learnerAccounts,
        ILogger<ParentController> logger)
    {
        _appService     = appService;
        _paymentService = paymentService;
        _docService     = docService;
        _db             = db;
        _userManager    = um;
        _teacherSvc     = teacherSvc;
        _boardingSvc    = boardingSvc;
        _dietarySvc     = dietarySvc;
        _mealPlanSvc    = mealPlanSvc;
        _mealOrderSvc   = mealOrderSvc;
        _feedbackSvc    = feedbackSvc;
        _learnerAccounts = learnerAccounts;
        _logger         = logger;
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var parent = await _userManager.FindByIdAsync(userId);

        var learners = await _db.Learners
            .Where(l => l.ParentId == userId)
            .ToListAsync();

        var cards = new List<LearnerCardViewModel>();

        foreach (var learner in learners)
        {
            // Latest application for this learner
            var app = await _db.Applications
                .Where(a => a.LearnerId == learner.Id && a.ParentId == userId)
                .OrderByDescending(a => a.SubmittedAt)
                .FirstOrDefaultAsync();

            // Active enrolment — must exist for fees to apply
            var enrollment = await _db.Enrollments
                .Include(e => e.Class).ThenInclude(c => c.Grade)
                .Where(e => e.LearnerId == learner.Id && e.IsActive)
                .FirstOrDefaultAsync();

            // ── Only calculate balance for ENROLLED learners ─────────────────
            decimal balance = 0;
            bool paidReg = false;
            bool paidAcc = false;
            string? bedRoom = null;

            if (enrollment is not null)
            {
                var feeTypes = await _db.FeeTypes.Where(f => f.IsActive).ToListAsync();
                var regFee = feeTypes.FirstOrDefault(f => f.Name.Contains("Registration"))?.Amount ?? 2567;
                var accFee = feeTypes.FirstOrDefault(f => f.Name.Contains("Accommodation"))?.Amount ?? 5439;

                paidReg = await _db.Payments.AnyAsync(p =>
                    p.LearnerId == learner.Id &&
                    p.Type == PaymentType.Registration &&
                    p.Status == PaymentStatus.Completed);

                // Accommodation fee only applies if a bed is allocated
                var hasBed = await _db.RoomAllocations
                    .AnyAsync(r => r.LearnerId == learner.Id && r.IsActive);
                bedRoom = hasBed ? await _db.RoomAllocations.Where(r => r.LearnerId == learner.Id && r.IsActive)
                    .Select(r => r.Room.Name + " · " + r.Bed.BedNumber).FirstOrDefaultAsync() : null;

                paidAcc = hasBed && await _db.Payments.AnyAsync(p =>
                    p.LearnerId == learner.Id &&
                    p.Type == PaymentType.Accommodation &&
                    p.Status == PaymentStatus.Completed);

                balance = (paidReg ? 0 : regFee) +
                          (hasBed && !paidAcc ? accFee : 0);
            }
            // ────────────────────────────────────────────────────────────────

            // Attendance % last 30 days — only meaningful if enrolled
            decimal attPct = 0;
            if (enrollment is not null)
            {
                var since = DateTime.Today.AddDays(-30);
                var attRecords = await _db.Attendances
                    .Where(a => a.LearnerId == learner.Id && a.Date >= since)
                    .ToListAsync();
                attPct = attRecords.Any()
                    ? Math.Round((decimal)attRecords.Count(a => a.Status == AttendanceStatus.Present)
                      / attRecords.Count * 100, 1)
                    : 0;
            }

            var hasProof = enrollment is not null && await _db.RegistrationProofs
                .AnyAsync(r => r.Enrollment != null && r.Enrollment.LearnerId == learner.Id);

            cards.Add(new LearnerCardViewModel
            {
                LearnerId = learner.Id,
                LearnerName = learner.FullName,
                ApplicationStatus = app?.Status.ToString() ?? "No Application",
                ApplicationId = app?.Id ?? 0,
                IsEnrolled = enrollment is not null,
                ClassName = enrollment?.Class?.DisplayName ?? "",
                BalanceDue = balance,
                HasProof = hasProof,
                AttendancePct = attPct,
                HasBed = bedRoom is not null,
                RoomName = bedRoom ?? "",
                PayType = paidReg ? "Accommodation" : "Registration",
                HasLogin = learner.HasLogin,
                LoginEnabled = await _learnerAccounts.IsAccessEnabledAsync(learner)
            });
        }

        return View(new ParentDashboardV2ViewModel
        {
            ParentName = parent?.FullName ?? "",
            Learners = cards,
            // Only notify about balance for ENROLLED learners
            Notifications = BuildNotifications(cards)
        });
    }

    // ── New Application ───────────────────────────────────────────────────────

    [HttpGet] public IActionResult Apply() => View(new NewApplicationViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(NewApplicationViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // Validate uploaded documents
        foreach (var (file, label) in new[] {
            (vm.LearnerIdDoc,      "Learner ID"),
            (vm.PreviousReportDoc, "Previous Report"),
            (vm.GuardianIdDoc,     "Guardian ID") })
        {
            if (!_docService.IsValidDocument(file, out var err))
            {
                ModelState.AddModelError("", $"{label}: {err}");
                return View(vm);
            }
        }

        var userId    = _userManager.GetUserId(User)!;
        var subfolder = $"applications/{userId}";

        var docs = new DocumentPaths(
            await _docService.SaveDocumentAsync(vm.LearnerIdDoc,      subfolder),
            await _docService.SaveDocumentAsync(vm.PreviousReportDoc, subfolder),
            await _docService.SaveDocumentAsync(vm.GuardianIdDoc,     subfolder)
        );

        var data = new NewApplicationData(vm.LearnerFirstName, vm.LearnerLastName,
            vm.DateOfBirth, vm.Gender, vm.LearnerIdNumber);

        try
        {
            var app = await _appService.SubmitApplicationAsync(userId, data, docs);
            TempData["Success"] = $"Application submitted successfully. Reference: #{app.Id}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting application");
            ModelState.AddModelError("", "An error occurred. Please try again.");
            return View(vm);
        }
    }

    // ── Application Detail ────────────────────────────────────────────────────

    public async Task<IActionResult> ApplicationDetail(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var app    = await _appService.GetByIdAsync(id);
        if (app is null || app.ParentId != userId) return NotFound();
        return View(new ApplicationDetailViewModel { Application = app });
    }

    // ── Download Proof ────────────────────────────────────────────────────────

    public async Task<IActionResult> DownloadProof(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var proof  = await _db.RegistrationProofs
            .Include(r => r.Enrollment).ThenInclude(e => e!.Learner)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (proof is null || proof.Enrollment?.Learner?.ParentId != userId)
            return NotFound();

        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", proof.PdfPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path))
            return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes, "application/pdf", Path.GetFileName(path));
    }

    // ── Pay Registration / Accommodation ─────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Pay(int learnerId, PaymentType type = PaymentType.Registration)
    {
        var userId  = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FindAsync(learnerId);
        if (learner is null || learner.ParentId != userId) return NotFound();

        var feeTypes = await _db.FeeTypes.Where(f => f.IsActive).ToListAsync();
        var fee      = type == PaymentType.Registration
            ? feeTypes.FirstOrDefault(f => f.Name.Contains("Registration"))
            : feeTypes.FirstOrDefault(f => f.Name.Contains("Accommodation"));

        return View(new PaymentInitViewModel
        {
            LearnerId   = learnerId,
            LearnerName = learner.FullName,
            Type        = type,
            Amount      = fee?.Amount ?? 0,
            FeeTypes    = feeTypes
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(PaymentInitViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId   = _userManager.GetUserId(User)!;
        var returnUrl = Url.Action("PaymentCallback", "Parent", null, Request.Scheme)!;
        var cancelUrl = Url.Action("Index", "Parent", null, Request.Scheme)!;

        try
        {
            var (payment, redirect) = await _paymentService.InitiateOnlinePaymentAsync(
                vm.LearnerId, userId, vm.Amount, vm.Type, returnUrl, cancelUrl);
            return Redirect(redirect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment initiation failed");
            TempData["Error"] = "Payment could not be initiated. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> PaymentCallback(int paymentId, string? @ref, string? status)
    {
        if (paymentId == 0) return BadRequest();
        try
        {
            var payment = await _paymentService.CompleteOnlinePaymentAsync(paymentId, @ref ?? "UNKNOWN");
            if (payment.Status == PaymentStatus.Completed)
            {
                TempData["Success"] = "Payment successful! Your receipt has been emailed.";
            }
            else
            {
                TempData["Error"] = "Payment failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment callback failed");
            TempData["Error"] = "Unable to confirm payment status.";
        }
        return RedirectToAction(nameof(Index));
    }
    // ── Academic Report (attendance + term summary — no raw marks) ────────────

    public async Task<IActionResult> AcademicReport(int learnerId, string term = "Term 1")
    {
        var userId  = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var enrollment = await _db.Enrollments
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .FirstOrDefaultAsync(e => e.LearnerId == learnerId && e.IsActive);

        if (enrollment is null)
        {
            TempData["Error"] = "No active enrollment found for this learner.";
            return RedirectToAction(nameof(Index));
        }

        var report = await _teacherSvc.GetParentAcademicViewAsync(learnerId, enrollment.ClassId, term);
        return View(report);
    }



    private static List<string> BuildNotifications(IList<LearnerCardViewModel> cards)
    {
        var notes = new List<string>();
        foreach (var c in cards)
        {
            // Only show balance notification for enrolled learners
            if (c.IsEnrolled && c.BalanceDue > 0)
                notes.Add($"{c.LearnerName}: R{c.BalanceDue:N0} balance outstanding.");
            if (c.IsEnrolled && c.AttendanceCrit)
                notes.Add($"{c.LearnerName}: Attendance critically low ({c.AttendancePct}%).");
            if (c.IsEnrolled && c.AttendanceRisk)
                notes.Add($"{c.LearnerName}: Attendance at risk ({c.AttendancePct}%).");
        }
        return notes;
    }

    // ── Learner Overview (hub page) ───────────────────────────────────────────

    public async Task<IActionResult> LearnerOverview(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners
            .FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        // Enrolment
        var enrollment = await _db.Enrollments
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .FirstOrDefaultAsync(e => e.LearnerId == learnerId && e.IsActive);

        // Boarding
        var allocation = await _db.RoomAllocations
            .Include(r => r.Room).Include(r => r.Bed)
            .FirstOrDefaultAsync(r => r.LearnerId == learnerId && r.IsActive);

        // Fees & payments
        var feeTypes = await _db.FeeTypes.Where(f => f.IsActive).ToListAsync();
        var regFeeAmt = feeTypes.FirstOrDefault(f => f.Name.Contains("Registration"))?.Amount ?? 2567;
        var accFeeAmt = feeTypes.FirstOrDefault(f => f.Name.Contains("Accommodation"))?.Amount ?? 5439;

        var payments = await _db.Payments
            .Where(p => p.LearnerId == learnerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var paidReg = payments.Any(p => p.Type == PaymentType.Registration && p.Status == PaymentStatus.Completed);
        var paidAcc = payments.Any(p => p.Type == PaymentType.Accommodation && p.Status == PaymentStatus.Completed);
        var balance = (paidReg ? 0 : regFeeAmt) + (allocation is not null && paidAcc ? 0 : allocation is not null ? accFeeAmt : 0);

        var payRows = payments.Select(p => new PaymentSummaryRow
        {
            PaymentId = p.Id,
            Type = p.Type.ToString(),
            Amount = p.Amount,
            Status = p.Status.ToString(),
            Reference = p.ProviderRef ?? p.BankRef ?? $"#{p.Id}",
            PaidDate = p.PaidAt,
            Method = p.Method.ToString()
        }).ToList();

        // Proofs
        var proofs = await _db.RegistrationProofs
            .Include(r => r.Enrollment)
            .Where(r => r.Enrollment != null && r.Enrollment.LearnerId == learnerId)
            .OrderByDescending(r => r.GeneratedAt)
            .ToListAsync();

        var proofRows = proofs.Select(pr => new ProofRow
        {
            ProofId = pr.Id,
            LearnerName = learner.FullName,
            GeneratedAt = pr.GeneratedAt,
            DownloadUrl = pr.PdfPath
        }).ToList();

        // Attendance last 30 days
        var since = DateTime.Today.AddDays(-30);
        var attRecords = await _db.Attendances
            .Where(a => a.LearnerId == learnerId && a.Date >= since)
            .ToListAsync();

        return View(new LearnerOverviewViewModel
        {
            LearnerId = learner.Id,
            LearnerName = learner.FullName,
            LearnerIdNumber = learner.LearnerIdNumber,
            Gender = learner.Gender,
            DateOfBirth = learner.DateOfBirth,
            IsEnrolled = enrollment is not null,
            ClassName = enrollment?.Class?.DisplayName ?? "",
            GradeName = enrollment?.Class?.Grade?.Name ?? "",
            EnrolledDate = enrollment?.EnrolledAt,
            Subjects = enrollment?.EnrollmentSubjects.Select(es => es.Subject?.Name ?? "").ToList() ?? new(),
            HasBed = allocation is not null,
            RoomName = allocation?.Room?.Name ?? "",
            BedNumber = allocation?.Bed?.BedNumber ?? "",
            AllocatedDate = allocation?.AllocatedAt,
            LeaveRequests = await _boardingSvc.GetLeaveRequestsForLearnerAsync(learnerId),
            RegistrationFeeAmount = regFeeAmt,
            AccommodationFeeAmount = accFeeAmt,
            RegistrationPaid = paidReg,
            AccommodationPaid = paidAcc,
            BalanceDue = balance,
            Payments = payRows,
            Proofs = proofRows,
            PresentDays = attRecords.Count(a => a.Status == AttendanceStatus.Present),
            AbsentDays = attRecords.Count(a => a.Status == AttendanceStatus.Absent),
            LateDays = attRecords.Count(a => a.Status == AttendanceStatus.Late),
            TotalDays = attRecords.Count,
            AvailableTerms = new List<string> { "Term 1", "Term 2", "Term 3", "Term 4" }
        });
    }

    // ── Learner Attendance History ────────────────────────────────────────────

    public async Task<IActionResult> LearnerAttendance(int learnerId,
        string? from = null, string? to = null)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners
            .FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var fromDate = from is not null ? DateTime.Parse(from) : DateTime.Today.AddDays(-30);
        var toDate = to is not null ? DateTime.Parse(to) : DateTime.Today;

        var enrollment = await _db.Enrollments
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .FirstOrDefaultAsync(e => e.LearnerId == learnerId && e.IsActive);

        var records = await _db.Attendances
            .Where(a => a.LearnerId == learnerId
                     && a.Date >= fromDate.Date
                     && a.Date <= toDate.Date)
            .OrderByDescending(a => a.Date)
            .ToListAsync();

        var dayRows = records.Select(a => new AttendanceDayRow
        {
            Date = a.Date,
            Status = a.Status.ToString(),
            Notes = a.Notes
        }).ToList();

        return View(new LearnerAttendanceViewModel
        {
            LearnerId = learner.Id,
            LearnerName = learner.FullName,
            ClassName = enrollment?.Class?.DisplayName ?? "",
            FromDate = fromDate,
            ToDate = toDate,
            PresentDays = records.Count(a => a.Status == AttendanceStatus.Present),
            AbsentDays = records.Count(a => a.Status == AttendanceStatus.Absent),
            LateDays = records.Count(a => a.Status == AttendanceStatus.Late),
            TotalDays = records.Count,
            Records = dayRows
        });
    }

    // ── Leave Request (UC2) ───────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> LeaveRequestCreate(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var hasBed = await _db.RoomAllocations.AnyAsync(r => r.LearnerId == learnerId && r.IsActive);
        if (!hasBed)
        {
            TempData["Error"] = "Leave requests are only available for boarding learners.";
            return RedirectToAction(nameof(LearnerOverview), new { learnerId });
        }

        return View(new LeaveRequestCreateViewModel { LearnerId = learnerId, LearnerName = learner.FullName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LeaveRequestCreate(LeaveRequestCreateViewModel vm)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == vm.LearnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        if (vm.ExpectedReturnDate < vm.DepartureDate)
            ModelState.AddModelError(nameof(vm.ExpectedReturnDate), "Return date must be on or after the departure date.");

        if (!ModelState.IsValid)
        {
            vm.LearnerName = learner.FullName;
            return View(vm);
        }

        await _boardingSvc.CreateLeaveRequestAsync(vm.LearnerId, userId, vm.Destination, vm.Purpose, vm.DepartureDate, vm.ExpectedReturnDate);
        TempData["Success"] = "Leave request submitted. The housemaster will review it shortly.";
        return RedirectToAction(nameof(LearnerOverview), new { learnerId = vm.LearnerId });
    }

    // ── Boarding QR Badge (UC1) ───────────────────────────────────────────────

    public async Task<IActionResult> LearnerQr(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var hasBed = await _db.RoomAllocations.AnyAsync(r => r.LearnerId == learnerId && r.IsActive);
        if (!hasBed)
        {
            TempData["Error"] = "QR badges are only available for boarding learners.";
            return RedirectToAction(nameof(LearnerOverview), new { learnerId });
        }

        ViewBag.QrCode = await _boardingSvc.GetOrCreateLearnerQrCodeAsync(learnerId);
        return View(learner);
    }

    // ── Learner Login (parent-managed) ────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> LearnerAccount(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        return View(await BuildLearnerAccountVmAsync(learner));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LearnerAccount(LearnerAccountViewModel vm)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == vm.LearnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.LearnerId == learner.Id && e.IsActive);
        if (!isEnrolled)
        {
            TempData["Error"] = "A learner login can only be created once the learner is enrolled.";
            return RedirectToAction(nameof(Index));
        }

        if (ModelState.IsValid)
        {
            var creating = !learner.HasLogin;
            var result = creating
                ? await _learnerAccounts.CreateLoginAsync(learner, vm.Password, vm.Email)
                : await _learnerAccounts.ResetPasswordAsync(learner, vm.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("Parent {ParentId} {Action} login for learner {LearnerId}",
                    userId, creating ? "created" : "reset password of", learner.Id);
                TempData["Success"] = creating
                    ? $"Login created. {learner.FirstName} can now sign in with Learner ID {learner.LearnerIdNumber}."
                    : $"Password for {learner.FirstName} has been reset.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
        }

        var fresh = await BuildLearnerAccountVmAsync(learner);
        fresh.Email = vm.Email;
        return View(fresh);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLearnerAccess(int learnerId, bool enabled)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null || !learner.HasLogin) return NotFound();

        await _learnerAccounts.SetAccessAsync(learner, enabled);
        TempData["Success"] = enabled
            ? $"{learner.FirstName}'s login has been enabled."
            : $"{learner.FirstName}'s login has been disabled.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<LearnerAccountViewModel> BuildLearnerAccountVmAsync(Learner learner) => new()
    {
        LearnerId       = learner.Id,
        LearnerName     = learner.FullName,
        LearnerIdNumber = learner.LearnerIdNumber,
        HasLogin        = learner.HasLogin,
        IsEnabled       = await _learnerAccounts.IsAccessEnabledAsync(learner)
    };

    // ── Dietary Profile (UC3) ─────────────────────────────────────────────────

    public async Task<IActionResult> DietaryProfile(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        return View(await _dietarySvc.GetParentViewAsync(learnerId));
    }

    [HttpGet]
    public async Task<IActionResult> DietaryProfileCreate(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        return View(new DietaryProfileSubmitViewModel { LearnerId = learnerId, LearnerName = learner.FullName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DietaryProfileCreate(DietaryProfileSubmitViewModel vm)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == vm.LearnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        if (!vm.Items.Any(i => !string.IsNullOrWhiteSpace(i.Name)))
            ModelState.AddModelError("", "Add at least one dietary item, allergy, or requirement.");
        if (_dietarySvc.ValidateDocuments(vm.Documents) is { } docError)
            ModelState.AddModelError("", docError);

        if (!ModelState.IsValid)
        {
            vm.LearnerName = learner.FullName;
            return View(vm);
        }

        await _dietarySvc.AddProfileAsync(vm.LearnerId, userId, vm.Items, vm.Documents);
        TempData["Success"] = "Dietary profile saved. It is now active and shared with the kitchen and housemaster.";
        return RedirectToAction(nameof(DietaryProfile), new { learnerId = vm.LearnerId });
    }

    // Supporting documents open inline so PDFs/images open in the browser — only for this parent's own children
    public async Task<IActionResult> DietaryDocument(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var doc = await _dietarySvc.GetDocumentAsync(id);
        if (doc is null || !await _db.Learners.AnyAsync(l => l.Id == doc.Value.learnerId && l.ParentId == userId)) return NotFound();
        return PhysicalFile(doc.Value.path, doc.Value.contentType);
    }

    // ── Meal Plan (UC4, read-only) ────────────────────────────────────────────

    public async Task<IActionResult> MealPlan(int week = 0)
        => View(await _mealPlanSvc.GetWeekForParentAsync(Math.Clamp(week, 0, 1)));

    // ── Meal Pre-Order (UC5) ─────────────────────────────────────────────────

    public async Task<IActionResult> PreOrder(int learnerId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var vm = await _mealOrderSvc.GetPreOrderScreenAsync(learnerId);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceMealOrder(int learnerId, int mealPlanItemId, PortionSize portionSize)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        var (success, message) = await _mealOrderSvc.PlaceOrderAsync(mealPlanItemId, learnerId, userId, portionSize);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(PreOrder), new { learnerId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelMealOrder(int learnerId, int mealPlanItemId)
    {
        var userId = _userManager.GetUserId(User)!;
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner is null) return NotFound();

        await _mealOrderSvc.CancelOrderAsync(mealPlanItemId, learnerId);
        TempData["Success"] = "Pre-order cancelled.";
        return RedirectToAction(nameof(PreOrder), new { learnerId });
    }

    // ── Meal Feedback (UC9, anonymous) ────────────────────────────────────────

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

        return View(new MealFeedbackSubmitViewModel
        {
            MealPlanItemId = item.Id,
            MenuDescription = item.MenuDescription,
            MealType = item.MealType.ToString()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MealFeedback(MealFeedbackSubmitViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var (success, message) = await _feedbackSvc.SubmitFeedbackAsync(vm.MealPlanItemId, vm.Rating, vm.Comment, vm.Portion);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(MealPlan));
    }
}
