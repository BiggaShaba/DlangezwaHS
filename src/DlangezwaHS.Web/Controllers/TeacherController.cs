using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize(Roles = "Teacher")]
public class TeacherController : Controller
{
    private readonly ApplicationDbContext          _db;
    private readonly ITeacherService               _teacherSvc;
    private readonly UserManager<ApplicationUser>  _userManager;
    private readonly ILogger<TeacherController>    _logger;

    public TeacherController(ApplicationDbContext db, ITeacherService teacherSvc,
        UserManager<ApplicationUser> um, ILogger<TeacherController> logger)
    {
        _db         = db;
        _teacherSvc = teacherSvc;
        _userManager = um;
        _logger     = logger;
    }

    private string UserId => _userManager.GetUserId(User)!;

    private async Task<Teacher?> GetTeacherAsync()
        => await _teacherSvc.GetByUserIdAsync(UserId);

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Dashboard()
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return RedirectToAction("Login", "Account");

        var assignments = await _teacherSvc.GetAssignmentsAsync(teacher.Id);
        var classIds    = assignments.Select(a => a.ClassId).Distinct().ToList();

        // Count classes where today's attendance not yet recorded
        int pending = 0;
        foreach (var cid in classIds)
            if (!await _teacherSvc.AttendanceTakenAsync(cid, DateTime.Today)) pending++;

        var totalAssessments = await _db.Assessments
            .CountAsync(a => a.TeacherId == teacher.Id);

        return View(new TeacherDashboardViewModel
        {
            TeacherName       = teacher.FullName,
            TotalClasses      = classIds.Count,
            TotalSubjects     = assignments.Select(a => a.SubjectId).Distinct().Count(),
            PendingAttendance = pending,
            TotalAssessments  = totalAssessments,
            Assignments       = assignments
        });
    }

    // ── My Classes & Learner List ─────────────────────────────────────────────

    public async Task<IActionResult> MyClasses()
    {
        var teacher     = await GetTeacherAsync();
        if (teacher is null) return NotFound();
        var assignments = await _teacherSvc.GetAssignmentsAsync(teacher.Id);
        return View(assignments);
    }

    public async Task<IActionResult> ClassLearners(int classId)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        // Security: teacher must be assigned to this class
        var assigned = await _db.TeacherClassSubjects
            .AnyAsync(t => t.TeacherId == teacher.Id && t.ClassId == classId);
        if (!assigned) return Forbid();

        var cls = await _db.Classes.Include(c => c.Grade).FirstOrDefaultAsync(c => c.Id == classId);
        var learners = await _db.Enrollments
            .Include(e => e.Learner)
            .Where(e => e.ClassId == classId && e.IsActive)
            .OrderBy(e => e.Learner.LastName)
            .Select(e => e.Learner)
            .ToListAsync();

        ViewBag.Class    = cls;
        ViewBag.ClassId  = classId;
        ViewBag.Teacher  = teacher;
        return View(learners);
    }

    // ── Attendance ────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> RecordAttendance(int classId, string? date = null)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        var assigned = await _db.TeacherClassSubjects
            .AnyAsync(t => t.TeacherId == teacher.Id && t.ClassId == classId);
        if (!assigned) return Forbid();

        var attendanceDate = date is not null ? DateTime.Parse(date).Date : DateTime.Today;
        var cls            = await _db.Classes.Include(c => c.Grade).FirstOrDefaultAsync(c => c.Id == classId);
        var alreadyTaken   = await _teacherSvc.AttendanceTakenAsync(classId, attendanceDate);

        var learners = await _db.Enrollments
            .Include(e => e.Learner)
            .Where(e => e.ClassId == classId && e.IsActive)
            .OrderBy(e => e.Learner.LastName)
            .ToListAsync();

        // Load existing records if re-viewing
        var existing = await _db.Attendances
            .Where(a => a.ClassId == classId && a.Date == attendanceDate)
            .ToListAsync();

        var rows = learners.Select(e =>
        {
            var rec = existing.FirstOrDefault(a => a.LearnerId == e.LearnerId);
            return new AttendanceRow
            {
                LearnerId = e.LearnerId,
                FullName  = e.Learner.FullName,
                Status    = rec?.Status ?? AttendanceStatus.Present,
                Notes     = rec?.Notes
            };
        }).ToList();

        return View(new RecordAttendanceViewModel
        {
            TeacherId    = teacher.Id,
            ClassId      = classId,
            ClassName    = cls?.DisplayName ?? "",
            Date         = attendanceDate,
            AlreadyTaken = alreadyTaken,
            Learners     = rows
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordAttendance(RecordAttendanceViewModel vm)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        try
        {
            await _teacherSvc.SaveAttendanceAsync(teacher.Id, vm.ClassId, vm.Date, vm.Learners);
            TempData["Success"] = $"Attendance saved for {vm.ClassName} on {vm.Date:dd MMM yyyy}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving attendance");
            TempData["Error"] = "Could not save attendance. Please try again.";
        }
        return RedirectToAction(nameof(RecordAttendance), new { classId = vm.ClassId });
    }

    public async Task<IActionResult> AttendanceHistory(int classId,
        string? from = null, string? to = null)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        var fromDate = from is not null ? DateTime.Parse(from) : DateTime.Today.AddDays(-30);
        var toDate   = to   is not null ? DateTime.Parse(to)   : DateTime.Today;
        var cls      = await _db.Classes.Include(c => c.Grade).FirstOrDefaultAsync(c => c.Id == classId);

        var summary = await _teacherSvc.GetAttendanceSummaryAsync(classId, fromDate, toDate);

        ViewBag.Class    = cls;
        ViewBag.ClassId  = classId;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate   = toDate;
        return View(summary);
    }

    // ── Assessments ───────────────────────────────────────────────────────────

    public async Task<IActionResult> Assessments(string? term = null, string? type = null)
    {
        var teacher     = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        var q = _db.Assessments
            .Include(a => a.Class).ThenInclude(c => c.Grade)
            .Include(a => a.Subject)
            .Where(a => a.TeacherId == teacher.Id)
            .AsQueryable();

        if (!string.IsNullOrEmpty(term)) q = q.Where(a => a.Term == term);
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<AssessmentType>(type, out var t))
            q = q.Where(a => a.Type == t);

        return View(new AssessmentListViewModel
        {
            TeacherId   = teacher.Id,
            TermFilter  = term,
            TypeFilter  = type,
            Assessments = await q.OrderByDescending(a => a.Date).ToListAsync()
        });
    }

    [HttpGet]
    public async Task<IActionResult> CreateAssessment(int? id = null)
    {
        var teacher     = await GetTeacherAsync();
        if (teacher is null) return NotFound();
        var assignments = await _teacherSvc.GetAssignmentsAsync(teacher.Id);

        CreateAssessmentViewModel vm;
        if (id.HasValue)
        {
            var a = await _teacherSvc.GetAssessmentAsync(id.Value);
            if (a is null || a.TeacherId != teacher.Id) return NotFound();
            vm = new CreateAssessmentViewModel
            {
                Id         = a.Id,
                Name       = a.Name,
                Type       = a.Type,
                SubjectId  = a.SubjectId,
                ClassId    = a.ClassId,
                Date       = a.Date,
                Term       = a.Term,
                TotalMarks = a.TotalMarks
            };
        }
        else
        {
            vm = new CreateAssessmentViewModel { Date = DateTime.Today };
        }

        vm.Assignments = assignments;
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAssessment(CreateAssessmentViewModel vm)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        if (!ModelState.IsValid)
        {
            vm.Assignments = await _teacherSvc.GetAssignmentsAsync(teacher.Id);
            return View(vm);
        }

        try
        {
            var assessment = await _teacherSvc.CreateOrUpdateAssessmentAsync(teacher.Id, vm);
            TempData["Success"] = $"Assessment '{assessment.Name}' saved.";
            return RedirectToAction(nameof(CaptureMarks), new { assessmentId = assessment.Id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            vm.Assignments = await _teacherSvc.GetAssignmentsAsync(teacher.Id);
            return View(vm);
        }
    }

    // ── Marks ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> CaptureMarks(int assessmentId)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        var assessment = await _teacherSvc.GetAssessmentAsync(assessmentId);
        if (assessment is null || assessment.TeacherId != teacher.Id) return NotFound();

        var learners = await _db.Enrollments
            .Include(e => e.Learner)
            .Where(e => e.ClassId == assessment.ClassId && e.IsActive)
            .OrderBy(e => e.Learner.LastName)
            .ToListAsync();

        var rows = learners.Select(e =>
        {
            var mark = assessment.Marks.FirstOrDefault(m => m.LearnerId == e.LearnerId);
            return new MarkRow
            {
                LearnerId     = e.LearnerId,
                FullName      = e.Learner.FullName,
                MarksObtained = mark?.MarksObtained,
                Comments      = mark?.Comments,
                Percentage    = mark?.Percentage ?? 0,
                Grade         = mark?.Grade ?? "",
                IsPassed      = mark?.IsPassed ?? false,
                IsLocked      = mark?.IsLocked ?? false
            };
        }).ToList();

        return View(new CaptureMarksViewModel
        {
            AssessmentId   = assessmentId,
            AssessmentName = assessment.Name,
            ClassName      = assessment.Class?.DisplayName ?? "",
            SubjectName    = assessment.Subject?.Name ?? "",
            TotalMarks     = assessment.TotalMarks,
            Term           = assessment.Term,
            IsLocked       = assessment.MarksLocked,
            Marks          = rows
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CaptureMarks(CaptureMarksViewModel vm)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        try
        {
            await _teacherSvc.SaveMarksAsync(vm.AssessmentId, UserId, vm.Marks);
            TempData["Success"] = "Marks saved successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(CaptureMarks), new { assessmentId = vm.AssessmentId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitMarks(int assessmentId)
    {
        var teacher = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        var assessment = await _db.Assessments.FindAsync(assessmentId);
        if (assessment is null || assessment.TeacherId != teacher.Id) return NotFound();

        await _teacherSvc.LockMarksAsync(assessmentId);
        TempData["Success"] = "Marks submitted and locked. Admin can make further changes if needed.";
        return RedirectToAction(nameof(CaptureMarks), new { assessmentId });
    }

    // ── Class Performance ─────────────────────────────────────────────────────

    public async Task<IActionResult> ClassPerformance(int classId, int subjectId, string term = "Term 1")
    {
        var teacher  = await GetTeacherAsync();
        if (teacher is null) return NotFound();

        var assigned = await _db.TeacherClassSubjects
            .AnyAsync(t => t.TeacherId == teacher.Id && t.ClassId == classId && t.SubjectId == subjectId);
        if (!assigned) return Forbid();

        var report = await _teacherSvc.GetClassPerformanceAsync(classId, subjectId, term);
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };
        return View(report);
    }
}
