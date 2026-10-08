using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// AUDIT SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public interface IAuditService
{
    Task LogAsync(string? userId, string? userName, string action,
        string? entityType = null, string? entityId = null, string? details = null, string? ip = null);
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    public AuditService(ApplicationDbContext db) => _db = db;

    public async Task LogAsync(string? userId, string? userName, string action,
        string? entityType = null, string? entityId = null, string? details = null, string? ip = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId     = userId,
            UserName   = userName,
            Action     = action,
            EntityType = entityType,
            EntityId   = entityId,
            Details    = details,
            IpAddress  = ip
        });
        await _db.SaveChangesAsync();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// APPLICATION SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public interface IApplicationService
{
    Task<LearnerApplication> SubmitApplicationAsync(string parentId, NewApplicationData data, DocumentPaths docs);
    Task<LearnerApplication> ApproveApplicationAsync(int applicationId, string adminId, string adminName, string ip);
    Task<LearnerApplication> RejectApplicationAsync(int applicationId, string reason, string adminId, string adminName, string ip);
    Task<LearnerApplication?> GetByIdAsync(int id);
    Task<IList<LearnerApplication>> GetByParentAsync(string parentId);
    Task<IList<LearnerApplication>> GetAllAsync(ApplicationStatus? filter = null);
}

public record NewApplicationData(
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    string Gender,
    string LearnerIdNumber
);

public record DocumentPaths(
    string? LearnerIdDocPath,
    string? PreviousReportDocPath,
    string? GuardianIdDocPath
);

public class ApplicationService : IApplicationService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService        _email;
    private readonly IAuditService        _audit;

    public ApplicationService(ApplicationDbContext db, IEmailService email, IAuditService audit)
    {
        _db    = db;
        _email = email;
        _audit = audit;
    }

    public async Task<LearnerApplication> SubmitApplicationAsync(string parentId, NewApplicationData data, DocumentPaths docs)
    {
        // Check if learner already exists for this parent
        var existing = await _db.Learners
            .FirstOrDefaultAsync(l => l.LearnerIdNumber == data.LearnerIdNumber);

        Learner learner;
        if (existing is null)
        {
            learner = new Learner
            {
                FirstName      = data.FirstName,
                LastName       = data.LastName,
                DateOfBirth    = data.DateOfBirth,
                Gender         = data.Gender,
                LearnerIdNumber = data.LearnerIdNumber,
                ParentId        = parentId
            };
            _db.Learners.Add(learner);
            await _db.SaveChangesAsync();
        }
        else
        {
            learner = existing;
        }

        var application = new LearnerApplication
        {
            LearnerId             = learner.Id,
            ParentId              = parentId,
            Status                = ApplicationStatus.Pending,
            LearnerIdDocPath      = docs.LearnerIdDocPath,
            PreviousReportDocPath = docs.PreviousReportDocPath,
            GuardianIdDocPath     = docs.GuardianIdDocPath
        };
        _db.Applications.Add(application);
        await _db.SaveChangesAsync();

        // Load relations for email
        await _db.Entry(application).Reference(a => a.Learner).LoadAsync();
        await _db.Entry(application).Reference(a => a.Parent).LoadAsync();

        await _email.SendApplicationReceivedAsync(application);
        await _audit.LogAsync(parentId, null, "ApplicationSubmitted", "Application", application.Id.ToString());

        return application;
    }

    public async Task<LearnerApplication> ApproveApplicationAsync(int applicationId, string adminId, string adminName, string ip)
    {
        var application = await GetFullApplicationAsync(applicationId);
        application.Status         = ApplicationStatus.Approved;
        application.ReviewedAt     = DateTime.UtcNow;
        application.ReviewedByUserId = adminId;
        await _db.SaveChangesAsync();

        await _email.SendApplicationApprovedAsync(application);
        await _audit.LogAsync(adminId, adminName, "ApplicationApproved", "Application", applicationId.ToString(), ip: ip);
        return application;
    }

    public async Task<LearnerApplication> RejectApplicationAsync(int applicationId, string reason, string adminId, string adminName, string ip)
    {
        var application = await GetFullApplicationAsync(applicationId);
        application.Status          = ApplicationStatus.Rejected;
        application.ReviewedAt      = DateTime.UtcNow;
        application.ReviewedByUserId = adminId;
        application.RejectionReason  = reason;
        await _db.SaveChangesAsync();

        await _email.SendApplicationRejectedAsync(application);
        await _audit.LogAsync(adminId, adminName, "ApplicationRejected", "Application", applicationId.ToString(), reason, ip);
        return application;
    }

    public async Task<LearnerApplication?> GetByIdAsync(int id)
        => await _db.Applications
            .Include(a => a.Learner)
            .Include(a => a.Parent)
            .FirstOrDefaultAsync(a => a.Id == id);

    public async Task<IList<LearnerApplication>> GetByParentAsync(string parentId)
        => await _db.Applications
            .Include(a => a.Learner)
            .Where(a => a.ParentId == parentId)
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync();

    public async Task<IList<LearnerApplication>> GetAllAsync(ApplicationStatus? filter = null)
    {
        var q = _db.Applications
            .Include(a => a.Learner)
            .Include(a => a.Parent)
            .AsQueryable();
        if (filter.HasValue)
            q = q.Where(a => a.Status == filter.Value);
        return await q.OrderByDescending(a => a.SubmittedAt).ToListAsync();
    }

    private async Task<LearnerApplication> GetFullApplicationAsync(int id)
    {
        var app = await _db.Applications
            .Include(a => a.Learner)
            .Include(a => a.Parent)
            .FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException($"Application {id} not found.");
        return app;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ENROLLMENT SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public interface IEnrollmentService
{
    Task<Enrollment> EnrollAsync(int applicationId, int classId, IList<int> subjectIds, string adminId, string adminName, string ip);
    Task<Enrollment?> GetEnrollmentByLearnerAsync(int learnerId);
}

public class EnrollmentService : IEnrollmentService
{
    private readonly ApplicationDbContext _db;
    private readonly IPdfService          _pdf;
    private readonly IEmailService        _email;
    private readonly IAuditService        _audit;

    public EnrollmentService(ApplicationDbContext db, IPdfService pdf, IEmailService email, IAuditService audit)
    {
        _db    = db;
        _pdf   = pdf;
        _email = email;
        _audit = audit;
    }

    public async Task<Enrollment> EnrollAsync(int applicationId, int classId, IList<int> subjectIds, string adminId, string adminName, string ip)
    {
        var application = await _db.Applications
            .Include(a => a.Learner)
            .Include(a => a.Parent)
            .FirstOrDefaultAsync(a => a.Id == applicationId)
            ?? throw new InvalidOperationException("Application not found.");

        if (application.Status != ApplicationStatus.Approved)
            throw new InvalidOperationException("Application must be Approved before enrolment.");

        // Deactivate previous enrolment if any
        var prev = await _db.Enrollments.Where(e => e.LearnerId == application.LearnerId && e.IsActive).ToListAsync();
        prev.ForEach(e => e.IsActive = false);

        var enrollment = new Enrollment
        {
            LearnerId     = application.LearnerId,
            ClassId       = classId,
            ApplicationId = applicationId,
            IsActive      = true
        };
        _db.Enrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        // Assign subjects
        foreach (var sid in subjectIds)
            _db.EnrollmentSubjects.Add(new EnrollmentSubject { EnrollmentId = enrollment.Id, SubjectId = sid });

        application.Status = ApplicationStatus.Enrolled;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(adminId, adminName, "LearnerEnrolled",
            "Enrollment", enrollment.Id.ToString(), $"Class {classId}", ip);

        return enrollment;
    }

    public async Task<Enrollment?> GetEnrollmentByLearnerAsync(int learnerId)
        => await _db.Enrollments
            .Include(e => e.Learner).ThenInclude(l => l.Parent)
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .Where(e => e.LearnerId == learnerId && e.IsActive)
            .FirstOrDefaultAsync();
}

// ─────────────────────────────────────────────────────────────────────────────
// ROOM ALLOCATION SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public interface IAllocationService
{
    Task<RoomAllocation> AllocateAsync(int learnerId, int roomId, int bedId, string adminId, string adminName, string ip);
    Task<IList<Bed>> GetAvailableBedsAsync(int roomId);
    Task<RoomAllocation?> GetActiveAllocationAsync(int learnerId);
    Task DeallocateAsync(int allocationId, string adminId, string adminName, string ip);
}

public class AllocationService : IAllocationService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService        _audit;

    public AllocationService(ApplicationDbContext db, IAuditService audit)
    {
        _db    = db;
        _audit = audit;
    }

    public async Task<RoomAllocation> AllocateAsync(int learnerId, int roomId, int bedId, string adminId, string adminName, string ip)
    {
        var bed = await _db.Beds.FindAsync(bedId)
            ?? throw new InvalidOperationException("Bed not found.");

        if (bed.Status != BedStatus.Available)
            throw new InvalidOperationException($"Bed {bed.BedNumber} is not available.");

        // Deactivate previous allocation
        var prev = await _db.RoomAllocations.Where(r => r.LearnerId == learnerId && r.IsActive).ToListAsync();
        foreach (var p in prev)
        {
            p.IsActive      = false;
            p.CheckedOutAt  = DateTime.UtcNow;
            var oldBed = await _db.Beds.FindAsync(p.BedId);
            if (oldBed is not null) oldBed.Status = BedStatus.Available;
        }

        var allocation = new RoomAllocation
        {
            LearnerId   = learnerId,
            RoomId      = roomId,
            BedId       = bedId,
            AllocatedAt = DateTime.UtcNow,
            IsActive    = true
        };
        _db.RoomAllocations.Add(allocation);
        bed.Status = BedStatus.Occupied;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(adminId, adminName, "BedAllocated", "RoomAllocation",
            allocation.Id.ToString(), $"Learner {learnerId} → Room {roomId} Bed {bedId}", ip);

        return allocation;
    }

    public async Task<IList<Bed>> GetAvailableBedsAsync(int roomId)
        => await _db.Beds
            .Where(b => b.RoomId == roomId && b.Status == BedStatus.Available)
            .ToListAsync();

    public async Task<RoomAllocation?> GetActiveAllocationAsync(int learnerId)
        => await _db.RoomAllocations
            .Include(r => r.Room)
            .Include(r => r.Bed)
            .Where(r => r.LearnerId == learnerId && r.IsActive)
            .FirstOrDefaultAsync();

    public async Task DeallocateAsync(int allocationId, string adminId, string adminName, string ip)
    {
        var allocation = await _db.RoomAllocations.Include(r => r.Bed).FirstOrDefaultAsync(r => r.Id == allocationId)
            ?? throw new InvalidOperationException("Allocation not found.");
        allocation.IsActive     = false;
        allocation.CheckedOutAt = DateTime.UtcNow;
        allocation.Bed.Status   = BedStatus.Available;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminId, adminName, "BedDeallocated", "RoomAllocation", allocationId.ToString(), ip: ip);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PAYMENT SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public interface IPaymentService
{
    Task<Payment> RecordManualPaymentAsync(int learnerId, string parentId, decimal amount, PaymentType type, string bankRef, string? notes, string adminId);
    Task<(Payment payment, string redirectUrl)> InitiateOnlinePaymentAsync(int learnerId, string parentId, decimal amount, PaymentType type, string returnUrl, string cancelUrl);
    Task<Payment> CompleteOnlinePaymentAsync(int paymentId, string providerRef);
    Task<byte[]> GenerateAndStoreProofAsync(int enrollmentId, string adminId);
}

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentGateway      _gateway;
    private readonly IPdfService          _pdf;
    private readonly IEmailService        _email;
    private readonly IAuditService        _audit;
    private readonly IWebHostEnvironment  _env;

    public PaymentService(ApplicationDbContext db, IPaymentGateway gateway, IPdfService pdf,
        IEmailService email, IAuditService audit, IWebHostEnvironment env)
    {
        _db      = db;
        _gateway = gateway;
        _pdf     = pdf;
        _email   = email;
        _audit   = audit;
        _env     = env;
    }

    public async Task<Payment> RecordManualPaymentAsync(int learnerId, string parentId, decimal amount,
        PaymentType type, string bankRef, string? notes, string adminId)
    {
        var payment = new Payment
        {
            LearnerId        = learnerId,
            ParentId         = parentId,
            Amount           = amount,
            Type             = type,
            Status           = PaymentStatus.Completed,
            Method           = PaymentMethod.ManualBankTransfer,
            BankRef          = bankRef,
            Notes            = notes,
            PaidAt           = DateTime.UtcNow,
            RecordedByUserId = adminId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(adminId, null, "ManualPaymentRecorded", "Payment", payment.Id.ToString(),
            $"R{amount} {type} – {bankRef}");

        // Load learner & parent for email
        await _db.Entry(payment).Reference(p => p.Learner).LoadAsync();
        await _db.Entry(payment).Reference(p => p.Parent).LoadAsync();

        await _email.SendPaymentConfirmationAsync(payment);
        return payment;
    }

    public async Task<(Payment payment, string redirectUrl)> InitiateOnlinePaymentAsync(int learnerId,
        string parentId, decimal amount, PaymentType type, string returnUrl, string cancelUrl)
    {
        var payment = new Payment
        {
            LearnerId = learnerId,
            ParentId  = parentId,
            Amount    = amount,
            Type      = type,
            Status    = PaymentStatus.Pending,
            Method    = PaymentMethod.Online
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var learner = await _db.Learners.FindAsync(learnerId);
        var parent  = await _db.Users.FindAsync(parentId);

        var result = await _gateway.InitiatePaymentAsync(new PaymentRequest(
            MerchantRef:   $"DHS-{payment.Id}",
            Amount:        amount,
            Currency:      "ZAR",
            Description:   $"{type} fee for {learner?.FullName}",
            CustomerEmail: parent?.Email ?? "",
            CustomerName:  parent?.FullName ?? "",
            ReturnUrl:     $"{returnUrl}?paymentId={payment.Id}",
            CancelUrl:     cancelUrl
        ));

        if (!result.Success)
            throw new InvalidOperationException($"Payment gateway error: {result.ErrorMessage}");

        payment.ProviderRef = result.ProviderRef;
        await _db.SaveChangesAsync();

        return (payment, result.RedirectUrl!);
    }

    public async Task<Payment> CompleteOnlinePaymentAsync(int paymentId, string providerRef)
    {
        var payment = await _db.Payments.FindAsync(paymentId)
            ?? throw new InvalidOperationException("Payment not found.");

        var verify = await _gateway.VerifyPaymentAsync(providerRef);
        payment.Status      = verify.Success ? PaymentStatus.Completed : PaymentStatus.Failed;
        payment.ProviderRef = verify.ProviderRef ?? providerRef;
        payment.PaidAt      = verify.Success ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();

        if (verify.Success)
        {
            await _db.Entry(payment).Reference(p => p.Learner).LoadAsync();
            await _db.Entry(payment).Reference(p => p.Parent).LoadAsync();
            await _email.SendPaymentConfirmationAsync(payment);
        }

        return payment;
    }

    public async Task<byte[]> GenerateAndStoreProofAsync(int enrollmentId, string adminId)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Learner).ThenInclude(l => l.Parent)
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new InvalidOperationException("Enrollment not found.");

        var learnerId = enrollment.LearnerId;
        var regPay = await _db.Payments.Where(p => p.LearnerId == learnerId && p.Type == PaymentType.Registration && p.Status == PaymentStatus.Completed).FirstOrDefaultAsync();
        var accPay = await _db.Payments.Where(p => p.LearnerId == learnerId && p.Type == PaymentType.Accommodation && p.Status == PaymentStatus.Completed).FirstOrDefaultAsync();
        var alloc  = await _db.RoomAllocations.Include(r => r.Room).Include(r => r.Bed).Where(r => r.LearnerId == learnerId && r.IsActive).FirstOrDefaultAsync();

        var pdfBytes = _pdf.GenerateRegistrationProof(enrollment, regPay, accPay, alloc);

        // Persist path reference
        var proofDir  = Path.Combine(_env.WebRootPath, "proofs");
        Directory.CreateDirectory(proofDir);
        var fileName  = $"proof_{enrollmentId}_{DateTime.UtcNow:yyyyMMddHHmm}.pdf";
        var filePath  = Path.Combine(proofDir, fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        _db.RegistrationProofs.Add(new RegistrationProof
        {
            EnrollmentId = enrollmentId,
            PdfPath      = $"/proofs/{fileName}",
            EmailSent    = false
        });
        await _db.SaveChangesAsync();

        // Email proof
        await _email.SendRegistrationProofAsync(enrollment, pdfBytes, fileName);

        await _audit.LogAsync(adminId, null, "ProofGenerated", "RegistrationProof", null,
            $"Enrollment {enrollmentId}");

        return pdfBytes;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DOCUMENT UPLOAD SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public interface IDocumentService
{
    Task<string?> SaveDocumentAsync(IFormFile? file, string subfolder);
    bool IsValidDocument(IFormFile? file, out string errorMessage);
}

public class DocumentService : IDocumentService
{
    private readonly IWebHostEnvironment _env;
    private static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public DocumentService(IWebHostEnvironment env) => _env = env;

    public bool IsValidDocument(IFormFile? file, out string errorMessage)
    {
        errorMessage = "";
        if (file is null || file.Length == 0) { errorMessage = "File is required."; return false; }
        if (file.Length > MaxFileSizeBytes) { errorMessage = "File must be under 5 MB."; return false; }
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!AllowedExtensions.Contains(ext)) { errorMessage = "Only PDF, JPG, and PNG files are allowed."; return false; }
        return true;
    }

    public async Task<string?> SaveDocumentAsync(IFormFile? file, string subfolder)
    {
        if (file is null || file.Length == 0) return null;
        var dir = Path.Combine(_env.WebRootPath, "uploads", subfolder);
        Directory.CreateDirectory(dir);
        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var path     = Path.Combine(dir, fileName);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        return $"/uploads/{subfolder}/{fileName}";
    }
}
