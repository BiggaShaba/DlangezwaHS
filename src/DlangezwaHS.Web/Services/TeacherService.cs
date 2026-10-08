using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface ITeacherService
{
    // Admin operations
    Task<(Teacher teacher, string password)> RegisterTeacherAsync(RegisterTeacherViewModel vm);
    Task UpdateTeacherAssignmentsAsync(int teacherId, List<int> classIds, List<int> subjectIds);
    Task DeactivateTeacherAsync(int teacherId, string adminId);

    // Teacher lookups
    Task<Teacher?> GetByUserIdAsync(string userId);
    Task<IList<TeacherClassSubject>> GetAssignmentsAsync(int teacherId);

    // Attendance
    Task SaveAttendanceAsync(int teacherId, int classId, DateTime date, IList<AttendanceRow> rows);
    Task<bool> AttendanceTakenAsync(int classId, DateTime date);
    Task<IList<AttendanceSummaryRow>> GetAttendanceSummaryAsync(int classId, DateTime from, DateTime to);

    // Assessments
    Task<Assessment> CreateOrUpdateAssessmentAsync(int teacherId, CreateAssessmentViewModel vm);
    Task<Assessment?> GetAssessmentAsync(int assessmentId);

    // Marks
    Task SaveMarksAsync(int assessmentId, string userId, IList<MarkRow> rows);
    Task LockMarksAsync(int assessmentId);
    Task AdminEditMarkAsync(int markId, decimal newMarks, string? comments, string adminUserId, string reason);

    // Performance / Reports
    Task<ClassPerformanceViewModel> GetClassPerformanceAsync(int classId, int subjectId, string term);
    Task<ParentAcademicViewModel> GetParentAcademicViewAsync(int learnerId, int classId, string term);

}


// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class TeacherService : ITeacherService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly IAuditService _audit;
    private readonly ICalendarService _calendarService;

    public TeacherService(ApplicationDbContext db, UserManager<ApplicationUser> um,
        IEmailService email, IAuditService audit, ICalendarService calendarService)
    {
        _db = db;
        _userManager = um;
        _email = email;
        _audit = audit;
        _calendarService = calendarService;   // ← add this
    }

    private async Task<EmailTemplate?> GetTemplateAsync(string key)
    => await _db.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == key);

    // ── Grade calculation (SA CAPS) ──────────────────────────────────────────

    private static string CalcGrade(decimal pct) => pct switch
    {
        >= 80 => "A",
        >= 70 => "B",
        >= 60 => "C",
        >= 50 => "D",
        >= 40 => "E",
        >= 30 => "F",
        _ => "G"
    };

    private static bool CalcPassed(decimal pct) => pct >= 40;

    // ── Password generator ───────────────────────────────────────────────────

    private static string GeneratePassword(string firstName)
    {
        var rnd = new Random();
        var number = rnd.Next(1000, 9999);
        // Format: FirstName + number + ! — meets all Identity requirements
        var name = firstName.Length >= 4 ? firstName[..4] : firstName;
        return $"{char.ToUpper(name[0])}{name[1..].ToLower()}{number}!";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADMIN — REGISTER TEACHER
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(Teacher teacher, string password)> RegisterTeacherAsync(RegisterTeacherViewModel vm)
    {
        // 1. Create Identity user
        var password = GeneratePassword(vm.FirstName);
        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            Phone = vm.Phone,
            EmailConfirmed = true
        };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Could not create teacher account: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, "Teacher");

        // 2. Create Teacher record linked to the user
        var teacher = new Teacher
        {
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            Email = vm.Email,
            Phone = vm.Phone,
            UserId = user.Id,
            IsActive = true
        };
        _db.Teachers.Add(teacher);
        await _db.SaveChangesAsync();

        // 3. Send credentials email using template
        await _email.SendTeacherCredentialsAsync(vm.Email, teacher.FullName, password);

        // 4. Save class+subject assignments
        await SaveAssignmentsAsync(teacher.Id, vm.ClassIds, vm.SubjectIds);


        await _audit.LogAsync(null, null, "TeacherRegistered", "Teacher", teacher.Id.ToString(),
            $"{teacher.FullName} ({vm.Email})");

        return (teacher, password);
    }

    private async Task SaveAssignmentsAsync(int teacherId, List<int> classIds, List<int> subjectIds)
    {
        // Remove old assignments
        var old = await _db.ClassTeachers.Where(t => t.TeacherId == teacherId).ToListAsync();
        _db.ClassTeachers.RemoveRange(old);

        // ClassTeachers only has TeacherId + ClassId (no SubjectId)
        foreach (var classId in classIds.Distinct())
        {
            bool exists = await _db.ClassTeachers.AnyAsync(t =>
                t.TeacherId == teacherId && t.ClassId == classId);
            if (!exists)
                _db.ClassTeachers.Add(new ClassTeacher
                {
                    TeacherId = teacherId,
                    ClassId = classId
                });
        }
        await _db.SaveChangesAsync();
    }
    public async Task UpdateTeacherAssignmentsAsync(int teacherId, List<int> classIds, List<int> subjectIds)
        => await SaveAssignmentsAsync(teacherId, classIds, subjectIds);

    public async Task DeactivateTeacherAsync(int teacherId, string adminId)
    {
        var teacher = await _db.Teachers.FindAsync(teacherId);
        if (teacher is not null)
        {
            teacher.IsActive = false;
            if (teacher.UserId is not null)
            {
                var user = await _userManager.FindByIdAsync(teacher.UserId);
                if (user is not null)
                {
                    user.IsActive = false;
                    await _userManager.UpdateAsync(user);
                }
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync(adminId, null, "TeacherDeactivated", "Teacher", teacherId.ToString());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEACHER LOOKUPS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Teacher?> GetByUserIdAsync(string userId)
        => await _db.Teachers
            .Include(t => t.TeacherClassSubjects)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive);

    public async Task<IList<TeacherClassSubject>> GetAssignmentsAsync(int teacherId)
        => await _db.TeacherClassSubjects
            .Include(t => t.Class).ThenInclude(c => c.Grade)
            .Include(t => t.Subject)
            .Where(t => t.TeacherId == teacherId)
            .ToListAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // ATTENDANCE
    // ─────────────────────────────────────────────────────────────────────────

    public async Task SaveAttendanceAsync(int teacherId, int classId, DateTime date, IList<AttendanceRow> rows)
    {
        var day = date.Date;
        // Remove existing records for this class+day (allow re-submission same day)
        var existing = await _db.Attendances
            .Where(a => a.ClassId == classId && a.Date == day).ToListAsync();
        _db.Attendances.RemoveRange(existing);

        foreach (var row in rows)
        {
            _db.Attendances.Add(new Attendance
            {
                LearnerId = row.LearnerId,
                ClassId = classId,
                TeacherId = teacherId,
                Date = day,
                Status = row.Status,
                Notes = row.Notes
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<bool> AttendanceTakenAsync(int classId, DateTime date)
        => await _db.Attendances.AnyAsync(a => a.ClassId == classId && a.Date == date.Date);

    public async Task<IList<AttendanceSummaryRow>> GetAttendanceSummaryAsync(int classId, DateTime from, DateTime to)
    {
        var records = await _db.Attendances
            .Include(a => a.Learner)
            .Where(a => a.ClassId == classId && a.Date >= from.Date && a.Date <= to.Date)
            .ToListAsync();

        return records
            .GroupBy(a => a.Learner)
            .Select(g => new AttendanceSummaryRow
            {
                LearnerName = g.Key?.FullName ?? "",
                Present = g.Count(a => a.Status == AttendanceStatus.Present),
                Absent = g.Count(a => a.Status == AttendanceStatus.Absent),
                Late = g.Count(a => a.Status == AttendanceStatus.Late),
                Total = g.Count()
            })
            .OrderBy(r => r.LearnerName)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ASSESSMENTS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Assessment> CreateOrUpdateAssessmentAsync(int teacherId, CreateAssessmentViewModel vm)
    {
        Assessment assessment;
        if (vm.Id.HasValue)
        {
            assessment = await _db.Assessments.FindAsync(vm.Id.Value)
                ?? throw new InvalidOperationException("Assessment not found.");
            if (assessment.MarksLocked)
                throw new InvalidOperationException("Cannot edit a locked assessment.");
        }
        else
        {
            assessment = new Assessment { TeacherId = teacherId };
            _db.Assessments.Add(assessment);
        }

        assessment.Name = vm.Name;
        assessment.Type = vm.Type;
        assessment.SubjectId = vm.SubjectId;
        assessment.ClassId = vm.ClassId;
        assessment.Date = vm.Date;
        assessment.Term = vm.Term;
        assessment.TotalMarks = vm.TotalMarks;

        await _db.SaveChangesAsync();
        await _calendarService.SyncAssessmentEventAsync(assessment);

        return assessment;
    }

    public async Task<Assessment?> GetAssessmentAsync(int id)
        => await _db.Assessments
            .Include(a => a.Subject)
            .Include(a => a.Class).ThenInclude(c => c.Grade)
            .Include(a => a.Marks).ThenInclude(m => m.Learner)
            .FirstOrDefaultAsync(a => a.Id == id);

    // ─────────────────────────────────────────────────────────────────────────
    // MARKS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task SaveMarksAsync(int assessmentId, string userId, IList<MarkRow> rows)
    {
        var assessment = await _db.Assessments.FindAsync(assessmentId)
            ?? throw new InvalidOperationException("Assessment not found.");

        if (assessment.MarksLocked)
            throw new InvalidOperationException("Marks are locked. Contact Admin to make changes.");

        foreach (var row in rows)
        {
            if (row.MarksObtained is null) continue;

            var obtained = row.MarksObtained.Value;
            var pct = assessment.TotalMarks > 0
                ? Math.Round(obtained / assessment.TotalMarks * 100, 2)
                : 0;
            var grade = CalcGrade(pct);
            var passed = CalcPassed(pct);

            var existing = await _db.Marks.FirstOrDefaultAsync(m =>
                m.LearnerId == row.LearnerId && m.AssessmentId == assessmentId);

            if (existing is null)
            {
                _db.Marks.Add(new Mark
                {
                    LearnerId = row.LearnerId,
                    AssessmentId = assessmentId,
                    MarksObtained = obtained,
                    Percentage = pct,
                    Grade = grade,
                    IsPassed = passed,
                    Comments = row.Comments,
                    IsLocked = false,
                    RecordedAt = DateTime.UtcNow,
                    RecordedBy = userId
                });
            }
            else if (!existing.IsLocked)
            {
                existing.MarksObtained = obtained;
                existing.Percentage = pct;
                existing.Grade = grade;
                existing.IsPassed = passed;
                existing.Comments = row.Comments;
                existing.RecordedAt = DateTime.UtcNow;
                existing.RecordedBy = userId;
            }
        }
        await _db.SaveChangesAsync();
    }

    public async Task LockMarksAsync(int assessmentId)
    {
        var marks = await _db.Marks.Where(m => m.AssessmentId == assessmentId).ToListAsync();
        var assessment = await _db.Assessments.FindAsync(assessmentId);
        marks.ForEach(m => m.IsLocked = true);
        if (assessment is not null) assessment.MarksLocked = true;
        await _db.SaveChangesAsync();
    }

    public async Task AdminEditMarkAsync(int markId, decimal newMarks, string? comments,
        string adminUserId, string reason)
    {
        var mark = await _db.Marks.Include(m => m.Assessment).FirstOrDefaultAsync(m => m.Id == markId)
            ?? throw new InvalidOperationException("Mark not found.");

        var pct = mark.Assessment.TotalMarks > 0
            ? Math.Round(newMarks / mark.Assessment.TotalMarks * 100, 2)
            : 0;

        mark.MarksObtained = newMarks;
        mark.Percentage = pct;
        mark.Grade = CalcGrade(pct);
        mark.IsPassed = CalcPassed(pct);
        mark.Comments = comments;
        mark.IsLocked = true;
        mark.RecordedAt = DateTime.UtcNow;
        mark.RecordedBy = adminUserId;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, null, "AdminEditedMark", "Mark", markId.ToString(),
            $"New: {newMarks} — Reason: {reason}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PERFORMANCE REPORTS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ClassPerformanceViewModel> GetClassPerformanceAsync(int classId, int subjectId, string term)
    {
        var cls = await _db.Classes.Include(c => c.Grade).FirstOrDefaultAsync(c => c.Id == classId);
        var subject = await _db.Subjects.FindAsync(subjectId);

        var assessments = await _db.Assessments
            .Include(a => a.Marks).ThenInclude(m => m.Learner)
            .Where(a => a.ClassId == classId && a.SubjectId == subjectId && a.Term == term)
            .OrderBy(a => a.Date)
            .ToListAsync();

        var learners = await _db.Enrollments
            .Include(e => e.Learner)
            .Where(e => e.ClassId == classId && e.IsActive)
            .Select(e => e.Learner)
            .ToListAsync();

        var rows = new List<LearnerPerformanceRow>();
        foreach (var learner in learners.OrderBy(l => l.LastName))
        {
            var cells = new List<AssessmentMarkCell>();
            var pctList = new List<decimal>();

            foreach (var a in assessments)
            {
                var mark = a.Marks.FirstOrDefault(m => m.LearnerId == learner.Id);
                cells.Add(new AssessmentMarkCell
                {
                    AssessmentName = a.Name,
                    Percentage = mark?.Percentage,
                    Grade = mark?.Grade ?? "-"
                });
                if (mark is not null) pctList.Add(mark.Percentage);
            }

            var avg = pctList.Any() ? Math.Round(pctList.Average(), 1) : 0;
            var passed = CalcPassed(avg);
            rows.Add(new LearnerPerformanceRow
            {
                LearnerName = learner.FullName,
                LearnerID = learner.LearnerIdNumber,
                Marks = cells,
                Average = avg,
                FinalGrade = pctList.Any() ? CalcGrade(avg) : "-",
                IsPassed = passed
            });
        }

        var allAvg = rows.Where(r => r.Average > 0).Select(r => r.Average).ToList();
        var passCount = rows.Count(r => r.IsPassed && r.Average > 0);

        return new ClassPerformanceViewModel
        {
            ClassId = classId,
            ClassName = cls?.DisplayName ?? "",
            SubjectId = subjectId,
            SubjectName = subject?.Name ?? "",
            Term = term,
            ClassAverage = allAvg.Any() ? Math.Round(allAvg.Average(), 1) : 0,
            PassCount = passCount,
            FailCount = rows.Count(r => !r.IsPassed && r.Average > 0),
            TotalLearners = learners.Count,
            Learners = rows,
            Assessments = assessments
        };
    }

    public async Task<ParentAcademicViewModel> GetParentAcademicViewAsync(int learnerId, int classId, string term)
    {
        var learner = await _db.Learners.FindAsync(learnerId);
        var cls = await _db.Classes.Include(c => c.Grade).FirstOrDefaultAsync(c => c.Id == classId);

        // Attendance summary
        var attendance = await _db.Attendances
            .Where(a => a.LearnerId == learnerId && a.ClassId == classId)
            .ToListAsync();

        // Subject summaries — averages only, no raw marks
        var subjectIds = await _db.EnrollmentSubjects
            .Where(es => es.Enrollment.LearnerId == learnerId && es.Enrollment.IsActive)
            .Select(es => es.SubjectId)
            .ToListAsync();

        var summaries = new List<SubjectTermSummary>();
        foreach (var sid in subjectIds)
        {
            var subject = await _db.Subjects.FindAsync(sid);
            var marks = await _db.Marks
                .Include(m => m.Assessment)
                .Where(m => m.LearnerId == learnerId
                         && m.Assessment.SubjectId == sid
                         && m.Assessment.ClassId == classId
                         && m.Assessment.Term == term)
                .ToListAsync();

            if (!marks.Any()) continue;
            var avg = Math.Round(marks.Average(m => m.Percentage), 1);
            summaries.Add(new SubjectTermSummary
            {
                SubjectName = subject?.Name ?? "",
                Average = avg,
                FinalGrade = CalcGrade(avg),
                IsPassed = CalcPassed(avg)
            });
        }

        return new ParentAcademicViewModel
        {
            LearnerId = learnerId,
            LearnerName = learner?.FullName ?? "",
            ClassName = cls?.DisplayName ?? "",
            Term = term,
            PresentDays = attendance.Count(a => a.Status == AttendanceStatus.Present),
            AbsentDays = attendance.Count(a => a.Status == AttendanceStatus.Absent),
            LateDays = attendance.Count(a => a.Status == AttendanceStatus.Late),
            TotalDays = attendance.Count,
            SubjectSummaries = summaries,
            AvailableTerms = new List<string> { "Term 1", "Term 2", "Term 3", "Term 4" }
        };
    }

}
