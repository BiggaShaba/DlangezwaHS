using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface IQuestionPaperService
{
    /// <summary>Teacher uploads a new question paper (status = Pending).</summary>
    Task<QuestionPaper> UploadAsync(UploadQuestionPaperData data, IFormFile file,
        string uploaderUserId, int uploaderTeacherId, string webRootPath);

    /// <summary>Admin approves a pending paper.</summary>
    Task<QuestionPaper> ApproveAsync(int id, string adminUserId, string adminName, string ip);

    /// <summary>Admin rejects a pending paper with a reason.</summary>
    Task<QuestionPaper> RejectAsync(int id, string reason, string adminUserId, string adminName, string ip);

    /// <summary>Admin releases an approved paper — makes it visible to learners.</summary>
    Task<QuestionPaper> ReleaseAsync(int id, string adminUserId, string adminName, string ip);

    /// <summary>Returns all papers visible to the browsing page (Admin sees all; Teacher/Learner see Released only).</summary>
    Task<IList<QuestionPaper>> BrowseAsync(int? subjectId, int? gradeId, string? term, bool adminMode = false);

    /// <summary>Returns papers pending Admin review.</summary>
    Task<IList<QuestionPaper>> GetPendingAsync();

    Task<QuestionPaper?> GetByIdAsync(int id);

    /// <summary>Returns the absolute file-system path for a released paper (authorised download).</summary>
    string GetFilePath(QuestionPaper paper, string webRootPath);

    /// <summary>Deletes all papers whose ExpiresAt &lt;= now (called by background job).</summary>
    Task PurgeExpiredAsync(string webRootPath);
}

// ─────────────────────────────────────────────────────────────────────────────
// DATA RECORD
// ─────────────────────────────────────────────────────────────────────────────

public record UploadQuestionPaperData(
    string Title,
    int SubjectId,
    int GradeId,
    string Term,
    int AcademicYear,
    AssessmentType PaperType
);

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class QuestionPaperService : IQuestionPaperService
{
    private const long MaxFileSizeBytes = 3 * 1024 * 1024;   // 3 MB
    private static readonly string[] AllowedMimes =
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"  // .docx
    };
    private static readonly string[] AllowedExtensions = { ".pdf", ".docx" };

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public QuestionPaperService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<QuestionPaper> UploadAsync(
        UploadQuestionPaperData data,
        IFormFile file,
        string uploaderUserId,
        int uploaderTeacherId,
        string webRootPath)
    {
        // Validate file size
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds the 3 MB limit ({file.Length / 1024.0 / 1024.0:F2} MB uploaded).");

        // Validate extension + MIME
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("Only PDF and DOCX files are allowed.");

        var contentType = file.ContentType.ToLowerInvariant();
        if (!AllowedMimes.Any(m => contentType.Contains(m.Split('/')[1])))
            throw new InvalidOperationException("Invalid file type. Upload a PDF or DOCX.");

        // Build storage path: wwwroot/uploads/questionpapers/{year}/{guid}{ext}
        var relativeDir = Path.Combine("uploads", "questionpapers", data.AcademicYear.ToString());
        var absoluteDir = Path.Combine(webRootPath, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var storedName = $"{Guid.NewGuid():N}{ext}";
        var absolutePath = Path.Combine(absoluteDir, storedName);

        await using (var stream = File.Create(absolutePath))
            await file.CopyToAsync(stream);

        var paper = new QuestionPaper
        {
            Title = data.Title.Trim(),
            SubjectId = data.SubjectId,
            GradeId = data.GradeId,
            Term = data.Term,
            AcademicYear = data.AcademicYear,
            PaperType = data.PaperType,
            FilePath = Path.Combine(relativeDir, storedName),
            OriginalFileName = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Status = QuestionPaperStatus.Pending,
            UploadedByUserId = uploaderUserId,
            UploadedByTeacherId = uploaderTeacherId,
            UploadedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddYears(3)
        };

        _db.QuestionPapers.Add(paper);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(uploaderUserId, null, "QuestionPaper.Uploaded",
            "QuestionPaper", paper.Id.ToString(), $"Uploaded: {paper.Title}");

        return paper;
    }

    // ── Approve ───────────────────────────────────────────────────────────────

    public async Task<QuestionPaper> ApproveAsync(int id, string adminUserId, string adminName, string ip)
    {
        var paper = await GetOrThrowAsync(id);
        if (paper.Status != QuestionPaperStatus.Pending)
            throw new InvalidOperationException("Only Pending papers can be approved.");

        paper.Status = QuestionPaperStatus.Approved;
        paper.ApprovedAt = DateTime.UtcNow;
        paper.ApprovedByUserId = adminUserId;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, adminName, "QuestionPaper.Approved",
            "QuestionPaper", id.ToString(), $"Approved: {paper.Title}", ip);

        return paper;
    }

    // ── Reject ────────────────────────────────────────────────────────────────

    public async Task<QuestionPaper> RejectAsync(int id, string reason, string adminUserId, string adminName, string ip)
    {
        var paper = await GetOrThrowAsync(id);
        if (paper.Status == QuestionPaperStatus.Released)
            throw new InvalidOperationException("A released paper cannot be rejected.");

        paper.Status = QuestionPaperStatus.Rejected;
        paper.RejectionReason = reason;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, adminName, "QuestionPaper.Rejected",
            "QuestionPaper", id.ToString(), $"Rejected: {paper.Title} – {reason}", ip);

        return paper;
    }

    // ── Release ───────────────────────────────────────────────────────────────

    public async Task<QuestionPaper> ReleaseAsync(int id, string adminUserId, string adminName, string ip)
    {
        var paper = await GetOrThrowAsync(id);
        if (paper.Status != QuestionPaperStatus.Approved)
            throw new InvalidOperationException("Only Approved papers can be released.");

        paper.Status = QuestionPaperStatus.Released;
        paper.IsReleased = true;
        paper.ReleasedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, adminName, "QuestionPaper.Released",
            "QuestionPaper", id.ToString(), $"Released: {paper.Title}", ip);

        return paper;
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    public async Task<IList<QuestionPaper>> BrowseAsync(
        int? subjectId, int? gradeId, string? term, bool adminMode = false)
    {
        var q = _db.QuestionPapers
            .Include(p => p.Subject)
            .Include(p => p.Grade)
            .Include(p => p.UploadedByTeacher)
            .AsQueryable();

        if (!adminMode)
            q = q.Where(p => p.IsReleased);

        if (subjectId.HasValue) q = q.Where(p => p.SubjectId == subjectId.Value);
        if (gradeId.HasValue) q = q.Where(p => p.GradeId == gradeId.Value);
        if (!string.IsNullOrEmpty(term)) q = q.Where(p => p.Term == term);

        return await q
            .OrderByDescending(p => p.AcademicYear)
            .ThenBy(p => p.Term)
            .ThenBy(p => p.Subject.Name)
            .ToListAsync();
    }

    // ── Pending list (Admin) ──────────────────────────────────────────────────

    public async Task<IList<QuestionPaper>> GetPendingAsync() =>
        await _db.QuestionPapers
            .Include(p => p.Subject)
            .Include(p => p.Grade)
            .Include(p => p.UploadedByTeacher)
            .Where(p => p.Status == QuestionPaperStatus.Pending)
            .OrderBy(p => p.UploadedAt)
            .ToListAsync();

    // ── Get by ID ─────────────────────────────────────────────────────────────

    public async Task<QuestionPaper?> GetByIdAsync(int id) =>
        await _db.QuestionPapers
            .Include(p => p.Subject)
            .Include(p => p.Grade)
            .Include(p => p.UploadedBy)
            .Include(p => p.UploadedByTeacher)
            .FirstOrDefaultAsync(p => p.Id == id);

    // ── File path ─────────────────────────────────────────────────────────────

    public string GetFilePath(QuestionPaper paper, string webRootPath) =>
        Path.Combine(webRootPath, paper.FilePath);

    // ── Purge expired (background job) ───────────────────────────────────────

    public async Task PurgeExpiredAsync(string webRootPath)
    {
        var expired = await _db.QuestionPapers
            .Where(p => p.ExpiresAt <= DateTime.UtcNow.AddYears(3))
            .ToListAsync();

        foreach (var paper in expired)
        {
            var abs = GetFilePath(paper, webRootPath);
            if (File.Exists(abs)) File.Delete(abs);
            _db.QuestionPapers.Remove(paper);
        }

        if (expired.Count > 0)
            await _db.SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<QuestionPaper> GetOrThrowAsync(int id)
    {
        var p = await _db.QuestionPapers.FindAsync(id);
        if (p == null) throw new KeyNotFoundException($"QuestionPaper {id} not found.");
        return p;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BACKGROUND SERVICE — auto-delete expired papers (runs daily)
// ─────────────────────────────────────────────────────────────────────────────

public class QuestionPaperExpiryJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<QuestionPaperExpiryJob> _log;

    public QuestionPaperExpiryJob(
        IServiceProvider services,
        IWebHostEnvironment env,
        ILogger<QuestionPaperExpiryJob> log)
    {
        _services = services;
        _env = env;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IQuestionPaperService>();
                await svc.PurgeExpiredAsync(_env.WebRootPath);
                _log.LogInformation("QuestionPaperExpiryJob: purge run completed.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "QuestionPaperExpiryJob: error during purge.");
            }

            //Run once a day
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}

