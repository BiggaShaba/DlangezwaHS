using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize]
public class QuestionPapersController : Controller
{
    private readonly IQuestionPaperService _qpService;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IWebHostEnvironment _env;

    public QuestionPapersController(
        IQuestionPaperService qpService,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IWebHostEnvironment env)
    {
        _qpService = qpService;
        _db = db;
        _users = users;
        _env = env;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BROWSE  — Teacher / Learner / Admin
    // GET /QuestionPapers
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(int? subjectId, int? gradeId, string? term)
    {
        bool isAdmin = User.IsInRole("Admin");

        var papers = await _qpService.BrowseAsync(subjectId, gradeId, term, adminMode: isAdmin);

        // Dropdowns for filter
        ViewBag.Subjects = await _db.Subjects.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        ViewBag.Grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync();
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };

        // Current filter values
        ViewBag.SelSubject = subjectId;
        ViewBag.SelGrade = gradeId;
        ViewBag.SelTerm = term;

        return View(papers);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UPLOAD  — Teacher only
    // GET /QuestionPapers/Upload
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Upload()
    {
        await PopulateUploadDropdownsAsync();
        return View();
    }

    // POST /QuestionPapers/Upload
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Upload(
        string title, int subjectId, int gradeId,
        string term, int academicYear, AssessmentType paperType,
        IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("", "Please select a file to upload.");
            await PopulateUploadDropdownsAsync();
            return View();
        }

        var userId = _users.GetUserId(User)!;
        var teacher = await _db.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null) return Forbid();

        try
        {
            var data = new UploadQuestionPaperData(
                title, subjectId, gradeId, term, academicYear, paperType);

            await _qpService.UploadAsync(data, file, userId, teacher.Id, _env.WebRootPath);

            TempData["Success"] = "Question paper uploaded successfully. It is now awaiting Admin approval.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateUploadDropdownsAsync();
            return View();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DOWNLOAD  — Teacher / Learner / Admin  (released papers only; Admin sees all)
    // GET /QuestionPapers/Download/5
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var paper = await _qpService.GetByIdAsync(id);
        if (paper == null) return NotFound();

        bool isAdmin = User.IsInRole("Admin");

        // Non-admin users can only download released papers
        if (!isAdmin && !paper.IsReleased)
            return Forbid();

        var filePath = _qpService.GetFilePath(paper, _env.WebRootPath);
        if (!System.IO.File.Exists(filePath))
            return NotFound("The file could not be found on the server.");

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var contentType = string.IsNullOrEmpty(paper.ContentType) ? "application/octet-stream" : paper.ContentType;
        var fileName = string.IsNullOrEmpty(paper.OriginalFileName) ? $"paper_{id}{Path.GetExtension(filePath)}" : paper.OriginalFileName;

        return File(bytes, contentType, fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PopulateUploadDropdownsAsync()
    {
        ViewBag.Subjects = await _db.Subjects.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        ViewBag.Grades = await _db.Grades.OrderBy(g => g.Level).ToListAsync();
        ViewBag.Terms = new[] { "Term 1", "Term 2", "Term 3", "Term 4" };
        ViewBag.AcademicYears = Enumerable.Range(DateTime.UtcNow.Year - 1, 3).ToArray();
        ViewBag.PaperTypes = Enum.GetValues<AssessmentType>();
    }
}

