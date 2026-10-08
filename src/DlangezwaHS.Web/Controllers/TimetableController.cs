using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize]
public class TimetableController : Controller
{
    private readonly ITimetableService _timetable;
    private readonly IPdfService _pdf;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public TimetableController(
        ITimetableService timetable,
        IPdfService pdf,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users)
    {
        _timetable = timetable;
        _pdf = pdf;
        _db = db;
        _users = users;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADMIN — Drag-and-drop timetable editor
    // GET /Timetable/Admin?classId=1&term=Term+1&year=2026
    // ─────────────────────────────────────────────────────────────────────────

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Admin(
        int? classId, string term = "Term 1", int? year = null)
    {
        int academicYear = year ?? DateTime.UtcNow.Year;
        var classes = await _timetable.GetClassListAsync();
        ViewBag.Classes = classes;
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };
        ViewBag.AcademicYear = academicYear;
        ViewBag.Term = term;

        if (classId == null || !classes.Any(c => c.Id == classId))
        {
            ViewBag.Grid = null;
            return View();
        }

        var vm = await _timetable.GetGridAsync(classId.Value, term, academicYear);
        ViewBag.ClassId = classId.Value;

        // All subjects for colour assignment panel
        var allSubjects = await _db.Subjects
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
        var allIds = allSubjects.Select(s => s.Id);
        var allColors = await _timetable.GetColorsAsync(allIds);
        ViewBag.AllSubjects = allSubjects;
        ViewBag.AllColors = allColors;

        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX — Save a dropped slot
    // POST /Timetable/SaveSlot
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveSlot(
        int classId, int day, int period,
        int subjectId, int teacherId,
        string term, int year)
    {
        try
        {
            var userId = _users.GetUserId(User)!;
            var slot = await _timetable.SaveSlotAsync(
                classId, day, period, subjectId, teacherId, term, year, userId);

            return Json(new
            {
                success = true,
                slotId = slot.Id,
                subjectName = slot.Subject?.Name,
                teacherName = slot.Teacher?.FullName,
                subjectId = slot.SubjectId
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX — Remove a slot
    // POST /Timetable/RemoveSlot
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveSlot(int slotId)
    {
        try
        {
            var userId = _users.GetUserId(User)!;
            await _timetable.RemoveSlotAsync(slotId, userId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX — Save subject colour
    // POST /Timetable/SaveColor
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveColor(int subjectId, string hex)
    {
        await _timetable.SetColorAsync(subjectId, hex);
        return Json(new { success = true });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEACHER — Weekly timetable view
    // GET /Timetable/TeacherView
    // ─────────────────────────────────────────────────────────────────────────

    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> TeacherView(string term = "Term 1", int? year = null)
    {
        int academicYear = year ?? DateTime.UtcNow.Year;
        var userId = _users.GetUserId(User)!;
        var teacher = await _db.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null) return Forbid();

        var vm = await _timetable.GetTeacherWeekAsync(teacher.Id, term, academicYear);
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };
        ViewBag.SelectedTerm = term;
        ViewBag.SelectedYear = academicYear;
        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LEARNER — Class timetable view
    // GET /Timetable/LearnerView
    // ─────────────────────────────────────────────────────────────────────────

    [Authorize(Roles = "Learner,Parent")]
    public async Task<IActionResult> LearnerView(int? enrollmentId,
        string term = "Term 1", int? year = null)
    {
        int academicYear = year ?? DateTime.UtcNow.Year;
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };
        ViewBag.SelectedTerm = term;
        ViewBag.SelectedYear = academicYear;

        // Only the learner's own enrolment (or, for a parent, one of their children's)
        var userId = _users.GetUserId(User)!;
        var ownEnrollments = User.IsInRole("Parent")
            ? _db.Enrollments.Where(e => e.IsActive && e.Learner.ParentId == userId)
            : _db.Enrollments.Where(e => e.IsActive && e.Learner.UserId == userId);

        var enroll = await ownEnrollments
            .Where(e => enrollmentId == null || e.Id == enrollmentId)
            .OrderByDescending(e => e.EnrolledAt)
            .FirstOrDefaultAsync();

        if (enroll == null)
        {
            TempData["Error"] = "No active enrolment found, so there is no timetable to show yet.";
            return User.IsInRole("Parent")
                ? RedirectToAction("Index", "Parent")
                : RedirectToAction("Index", "Learner");
        }
        enrollmentId = enroll.Id;

        var vm = await _timetable.GetLearnerTimetableAsync(enrollmentId.Value, term, academicYear);
        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PARENT — Timetable for a specific learner
    // GET /Timetable/ParentView?learnerId=3&term=Term+1&year=2026
    // ─────────────────────────────────────────────────────────────────────────

    [Authorize(Roles = "Parent")]
    public async Task<IActionResult> ParentView(int learnerId,
        string term = "Term 1", int? year = null)
    {
        int academicYear = year ?? DateTime.UtcNow.Year;
        var userId = _users.GetUserId(User)!;

        // Security: verify learner belongs to this parent
        var learner = await _db.Learners
            .FirstOrDefaultAsync(l => l.Id == learnerId && l.ParentId == userId);
        if (learner == null) return NotFound();

        var enrollment = await _db.Enrollments
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .FirstOrDefaultAsync(e => e.LearnerId == learnerId && e.IsActive);

        if (enrollment == null)
        {
            TempData["Error"] = $"{learner.FullName} does not have an active enrolment yet.";
            return RedirectToAction("Index", "Parent");
        }

        var vm = await _timetable.GetGridAsync(enrollment.ClassId, term, academicYear);
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };
        ViewBag.SelectedTerm = term;
        ViewBag.SelectedYear = academicYear;
        ViewBag.LearnerId = learnerId;
        ViewBag.LearnerName = learner.FullName;

        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DOWNLOAD — Class timetable PDF (Parent / Learner / Admin)
    // GET /Timetable/DownloadClassPdf?classId=1&term=Term+1&year=2026
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Parent,Learner,Admin")]
    public async Task<IActionResult> DownloadClassPdf(
        int classId, string term = "Term 1", int? year = null)
    {
        int academicYear = year ?? DateTime.UtcNow.Year;
        var vm = await _timetable.GetGridAsync(classId, term, academicYear);

        if (!vm.AllSlots.Any())
            return BadRequest("No timetable has been set up for this class and term yet.");

        var bytes = _pdf.GenerateTimetablePdf(vm);
        var fileName = $"Timetable_{vm.SelectedClass?.DisplayName}_{term}_{academicYear}.pdf"
                           .Replace(" ", "_");
        return File(bytes, "application/pdf", fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DOWNLOAD — Teacher timetable PDF (Teacher / Admin)
    // GET /Timetable/DownloadTeacherPdf?term=Term+1&year=2026
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DownloadTeacherPdf(
        string term = "Term 1", int? year = null)
    {
        int academicYear = year ?? DateTime.UtcNow.Year;
        var userId = _users.GetUserId(User)!;
        var teacher = await _db.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null) return Forbid();

        var vm = await _timetable.GetTeacherWeekAsync(teacher.Id, term, academicYear);

        if (!vm.Slots.Any())
            return BadRequest("No timetable has been set up for this term yet.");

        var bytes = _pdf.GenerateTeacherTimetablePdf(vm);
        var fileName = $"Timetable_{teacher.FullName}_{term}_{academicYear}.pdf"
                           .Replace(" ", "_");
        return File(bytes, "application/pdf", fileName);
    }


}
