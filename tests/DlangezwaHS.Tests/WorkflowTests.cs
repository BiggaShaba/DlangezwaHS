using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DlangezwaHS.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// TEST FIXTURE – shared in-memory DB context
// ─────────────────────────────────────────────────────────────────────────────

public class DbFixture : IDisposable
{
    public ApplicationDbContext Db { get; }

    public DbFixture()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new ApplicationDbContext(opts);
        Db.Database.EnsureCreated();
    }

    public void Dispose() => Db.Dispose();
}

// ─────────────────────────────────────────────────────────────────────────────
// HELPERS
// ─────────────────────────────────────────────────────────────────────────────

internal static class TestHelpers
{
    public static ApplicationUser MakeParent(string id = "parent-1") => new()
    {
        Id             = id,
        UserName       = $"{id}@test.com",
        Email          = $"{id}@test.com",
        FirstName      = "Test",
        LastName       = "Parent",
        EmailConfirmed = true
    };

    public static Learner MakeLearner(string parentId, int id = 1) => new()
    {
        Id              = id,
        FirstName       = "Sipho",
        LastName        = "Dlamini",
        DateOfBirth     = new DateTime(2008, 3, 15),
        Gender          = "Male",
        LearnerIdNumber = $"ID{id:000}",
        ParentId        = parentId
    };

    public static Grade MakeGrade(int id = 1, int level = 8) => new()
        { Id = id, Name = $"Grade {level}", Level = level };

    public static Class MakeClass(int id = 1, int gradeId = 1) => new()
        { Id = id, GradeId = gradeId, Section = "A", Capacity = 40 };

    public static Subject MakeSubject(int id = 1, string code = "MATH") => new()
        { Id = id, Name = "Mathematics", Code = code, IsActive = true };

    public static IAuditService MockAudit()
    {
        var m = new Mock<IAuditService>();
        m.Setup(a => a.LogAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
         .Returns(Task.CompletedTask);
        return m.Object;
    }

    public static IEmailService MockEmail()
    {
        var m = new Mock<IEmailService>();
        m.Setup(e => e.SendApplicationReceivedAsync(It.IsAny<LearnerApplication>())).Returns(Task.CompletedTask);
        m.Setup(e => e.SendApplicationApprovedAsync(It.IsAny<LearnerApplication>())).Returns(Task.CompletedTask);
        m.Setup(e => e.SendApplicationRejectedAsync(It.IsAny<LearnerApplication>())).Returns(Task.CompletedTask);
        m.Setup(e => e.SendPaymentConfirmationAsync(It.IsAny<Payment>(), It.IsAny<byte[]?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        m.Setup(e => e.SendRegistrationProofAsync(It.IsAny<Enrollment>(), It.IsAny<byte[]>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        return m.Object;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// APPLICATION SERVICE TESTS
// ─────────────────────────────────────────────────────────────────────────────

public class ApplicationServiceTests : IDisposable
{
    private readonly DbFixture        _fix = new();
    private readonly ApplicationService _svc;

    public ApplicationServiceTests()
    {
        _svc = new ApplicationService(_fix.Db, TestHelpers.MockEmail(), TestHelpers.MockAudit());
    }

    private async Task SeedParentAsync()
    {
        var parent = TestHelpers.MakeParent();
        _fix.Db.Users.Add(parent);
        await _fix.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitApplication_CreatesLearnerAndApplication()
    {
        await SeedParentAsync();
        var data = new NewApplicationData("Sipho", "Dlamini", new DateTime(2008, 3, 15), "Male", "ID001A");
        var docs = new DocumentPaths(null, null, null);

        var result = await _svc.SubmitApplicationAsync("parent-1", data, docs);

        Assert.Equal(ApplicationStatus.Pending, result.Status);
        Assert.Equal("parent-1", result.ParentId);
        Assert.True(result.LearnerId > 0);
        Assert.Equal(1, await _fix.Db.Learners.CountAsync());
    }

    [Fact]
    public async Task SubmitApplication_DuplicateIdNumber_ReusesLearner()
    {
        await SeedParentAsync();
        var data = new NewApplicationData("Sipho", "Dlamini", new DateTime(2008, 3, 15), "Male", "ID001B");
        var docs = new DocumentPaths(null, null, null);

        await _svc.SubmitApplicationAsync("parent-1", data, docs);
        await _svc.SubmitApplicationAsync("parent-1", data, docs);

        // Only one learner, two applications
        Assert.Equal(1, await _fix.Db.Learners.CountAsync());
        Assert.Equal(2, await _fix.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task ApproveApplication_ChangesStatusAndSendsEmail()
    {
        await SeedParentAsync();
        var data = new NewApplicationData("Sipho", "Dlamini", new DateTime(2008, 3, 15), "Male", "ID002");
        var docs = new DocumentPaths(null, null, null);
        var app  = await _svc.SubmitApplicationAsync("parent-1", data, docs);

        var updated = await _svc.ApproveApplicationAsync(app.Id, "admin-1", "Admin", "127.0.0.1");

        Assert.Equal(ApplicationStatus.Approved, updated.Status);
        Assert.NotNull(updated.ReviewedAt);
        Assert.Equal("admin-1", updated.ReviewedByUserId);
    }

    [Fact]
    public async Task RejectApplication_SetsReasonAndStatus()
    {
        await SeedParentAsync();
        var data = new NewApplicationData("Thabo", "Nkosi", new DateTime(2009, 5, 10), "Male", "ID003");
        var app  = await _svc.SubmitApplicationAsync("parent-1", data, new DocumentPaths(null, null, null));

        var updated = await _svc.RejectApplicationAsync(app.Id, "Incomplete documents", "admin-1", "Admin", "127.0.0.1");

        Assert.Equal(ApplicationStatus.Rejected, updated.Status);
        Assert.Equal("Incomplete documents", updated.RejectionReason);
    }

    [Fact]
    public async Task GetByParent_ReturnsOnlyParentsApplications()
    {
        _fix.Db.Users.Add(TestHelpers.MakeParent("parent-1"));
        _fix.Db.Users.Add(TestHelpers.MakeParent("parent-2"));
        await _fix.Db.SaveChangesAsync();

        await _svc.SubmitApplicationAsync("parent-1", new NewApplicationData("A","B", DateTime.Now.AddYears(-14),"M","P1A"), new DocumentPaths(null,null,null));
        await _svc.SubmitApplicationAsync("parent-2", new NewApplicationData("C","D", DateTime.Now.AddYears(-14),"F","P2A"), new DocumentPaths(null,null,null));

        var p1Apps = await _svc.GetByParentAsync("parent-1");
        Assert.Single(p1Apps);
    }

    public void Dispose() => _fix.Dispose();
}

// ─────────────────────────────────────────────────────────────────────────────
// ENROLLMENT SERVICE TESTS
// ─────────────────────────────────────────────────────────────────────────────

public class EnrollmentServiceTests : IDisposable
{
    private readonly DbFixture         _fix = new();
    private readonly EnrollmentService _svc;

    public EnrollmentServiceTests()
    {
        var pdfMock = new Mock<IPdfService>();
        _svc = new EnrollmentService(_fix.Db, pdfMock.Object, TestHelpers.MockEmail(), TestHelpers.MockAudit());
    }

    private async Task<(LearnerApplication app, int classId, int subjectId)> SeedAsync()
    {
        var parent = TestHelpers.MakeParent();
        _fix.Db.Users.Add(parent);
        var learner = TestHelpers.MakeLearner(parent.Id);
        _fix.Db.Learners.Add(learner);
        var grade   = TestHelpers.MakeGrade();
        _fix.Db.Grades.Add(grade);
        await _fix.Db.SaveChangesAsync();
        var cls = TestHelpers.MakeClass(gradeId: grade.Id);
        _fix.Db.Classes.Add(cls);
        var subject = TestHelpers.MakeSubject();
        _fix.Db.Subjects.Add(subject);
        var app = new LearnerApplication
        {
            LearnerId = learner.Id, ParentId = parent.Id,
            Status    = ApplicationStatus.Approved
        };
        _fix.Db.Applications.Add(app);
        await _fix.Db.SaveChangesAsync();
        return (app, cls.Id, subject.Id);
    }

    [Fact]
    public async Task Enroll_CreatesEnrollmentAndSubjects()
    {
        var (app, classId, subjectId) = await SeedAsync();

        var enrollment = await _svc.EnrollAsync(app.Id, classId, new[] { subjectId }, "admin-1", "Admin", "127.0.0.1");

        Assert.True(enrollment.Id > 0);
        Assert.Equal(classId, enrollment.ClassId);
        Assert.Equal(ApplicationStatus.Enrolled, (await _fix.Db.Applications.FindAsync(app.Id))!.Status);
        Assert.Equal(1, await _fix.Db.EnrollmentSubjects.CountAsync());
    }

    [Fact]
    public async Task Enroll_PendingApplication_Throws()
    {
        var (app, classId, subjectId) = await SeedAsync();
        app.Status = ApplicationStatus.Pending;
        await _fix.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.EnrollAsync(app.Id, classId, new[] { subjectId }, "admin-1", "Admin", "127.0.0.1"));
    }

    [Fact]
    public async Task GetEnrollmentByLearner_ReturnsActiveEnrollment()
    {
        var (app, classId, subjectId) = await SeedAsync();
        var enrollment = await _svc.EnrollAsync(app.Id, classId, new[] { subjectId }, "admin-1", "Admin", "127.0.0.1");

        var found = await _svc.GetEnrollmentByLearnerAsync(app.LearnerId);

        Assert.NotNull(found);
        Assert.Equal(enrollment.Id, found!.Id);
    }

    public void Dispose() => _fix.Dispose();
}

// ─────────────────────────────────────────────────────────────────────────────
// ALLOCATION SERVICE TESTS
// ─────────────────────────────────────────────────────────────────────────────

public class AllocationServiceTests : IDisposable
{
    private readonly DbFixture         _fix = new();
    private readonly AllocationService _svc;

    public AllocationServiceTests()
    {
        _svc = new AllocationService(_fix.Db, TestHelpers.MockAudit());
    }

    private async Task<(int learnerId, int roomId, int bedId)> SeedAsync()
    {
        var parent  = TestHelpers.MakeParent();
        _fix.Db.Users.Add(parent);
        var learner = TestHelpers.MakeLearner(parent.Id);
        _fix.Db.Learners.Add(learner);
        var room = new Room { Name = "R1", Capacity = 4, IsActive = true };
        _fix.Db.Rooms.Add(room);
        await _fix.Db.SaveChangesAsync();
        var bed = new Bed { RoomId = room.Id, BedNumber = "B1", Status = BedStatus.Available };
        _fix.Db.Beds.Add(bed);
        await _fix.Db.SaveChangesAsync();
        return (learner.Id, room.Id, bed.Id);
    }

    [Fact]
    public async Task Allocate_SetsBedOccupiedAndCreatesAllocation()
    {
        var (learnerId, roomId, bedId) = await SeedAsync();

        var allocation = await _svc.AllocateAsync(learnerId, roomId, bedId, "admin-1", "Admin", "127.0.0.1");

        Assert.True(allocation.Id > 0);
        Assert.True(allocation.IsActive);
        var bed = await _fix.Db.Beds.FindAsync(bedId);
        Assert.Equal(BedStatus.Occupied, bed!.Status);
    }

    [Fact]
    public async Task Allocate_OccupiedBed_Throws()
    {
        var (learnerId, roomId, bedId) = await SeedAsync();
        var bed = await _fix.Db.Beds.FindAsync(bedId);
        bed!.Status = BedStatus.Occupied;
        await _fix.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.AllocateAsync(learnerId, roomId, bedId, "admin-1", "Admin", "127.0.0.1"));
    }

    [Fact]
    public async Task GetAvailableBeds_ReturnsOnlyAvailable()
    {
        var (_, roomId, _) = await SeedAsync();
        // Add a second bed, mark it occupied
        var room    = await _fix.Db.Rooms.FindAsync(roomId);
        var occBed  = new Bed { RoomId = roomId, BedNumber = "B2", Status = BedStatus.Occupied };
        _fix.Db.Beds.Add(occBed);
        await _fix.Db.SaveChangesAsync();

        var avail = await _svc.GetAvailableBedsAsync(roomId);
        Assert.Single(avail);
        Assert.Equal("B1", avail[0].BedNumber);
    }

    [Fact]
    public async Task Deallocate_SetsBedAvailableAndClosesAllocation()
    {
        var (learnerId, roomId, bedId) = await SeedAsync();
        var allocation = await _svc.AllocateAsync(learnerId, roomId, bedId, "admin-1", "Admin", "127.0.0.1");

        await _svc.DeallocateAsync(allocation.Id, "admin-1", "Admin", "127.0.0.1");

        var bed = await _fix.Db.Beds.FindAsync(bedId);
        Assert.Equal(BedStatus.Available, bed!.Status);
        var alloc = await _fix.Db.RoomAllocations.FindAsync(allocation.Id);
        Assert.False(alloc!.IsActive);
        Assert.NotNull(alloc.CheckedOutAt);
    }

    public void Dispose() => _fix.Dispose();
}

// ─────────────────────────────────────────────────────────────────────────────
// PAYMENT SERVICE TESTS
// ─────────────────────────────────────────────────────────────────────────────

public class PaymentServiceTests : IDisposable
{
    private readonly DbFixture      _fix = new();
    private readonly PaymentService _svc;

    public PaymentServiceTests()
    {
        var pdfMock = new Mock<IPdfService>();
        pdfMock.Setup(p => p.GenerateRegistrationProof(
            It.IsAny<Enrollment>(), It.IsAny<Payment?>(), It.IsAny<Payment?>(), It.IsAny<RoomAllocation?>()))
            .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());

        _svc = new PaymentService(_fix.Db, new MockPaymentGateway(new MockPaymentOptions(), new Mock<Microsoft.Extensions.Logging.ILogger<MockPaymentGateway>>().Object),
            pdfMock.Object, TestHelpers.MockEmail(), TestHelpers.MockAudit(), envMock.Object);
    }

    private async Task<(int learnerId, string parentId)> SeedAsync()
    {
        var parent  = TestHelpers.MakeParent();
        _fix.Db.Users.Add(parent);
        var learner = TestHelpers.MakeLearner(parent.Id);
        _fix.Db.Learners.Add(learner);
        await _fix.Db.SaveChangesAsync();
        return (learner.Id, parent.Id);
    }

    [Fact]
    public async Task RecordManualPayment_CreatesCompletedPayment()
    {
        var (learnerId, parentId) = await SeedAsync();

        var payment = await _svc.RecordManualPaymentAsync(learnerId, parentId, 2567m,
            PaymentType.Registration, "FNB123", null, "admin-1");

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(PaymentMethod.ManualBankTransfer, payment.Method);
        Assert.Equal("FNB123", payment.BankRef);
        Assert.Equal(2567m, payment.Amount);
        Assert.NotNull(payment.PaidAt);
    }

    [Fact]
    public async Task InitiateOnlinePayment_CreatesPendingPayment()
    {
        var (learnerId, parentId) = await SeedAsync();

        var (payment, redirect) = await _svc.InitiateOnlinePaymentAsync(
            learnerId, parentId, 2567m, PaymentType.Registration,
            "https://test.com/callback", "https://test.com/cancel");

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.NotNull(payment.ProviderRef);
        Assert.Contains("MOCK-", payment.ProviderRef);
        Assert.NotNull(redirect);
    }

    [Fact]
    public async Task CompleteOnlinePayment_MarksCompleted()
    {
        var (learnerId, parentId) = await SeedAsync();
        var (payment, _) = await _svc.InitiateOnlinePaymentAsync(
            learnerId, parentId, 5439m, PaymentType.Accommodation,
            "https://test.com/callback", "https://test.com/cancel");

        var updated = await _svc.CompleteOnlinePaymentAsync(payment.Id, payment.ProviderRef!);

        Assert.Equal(PaymentStatus.Completed, updated.Status);
        Assert.NotNull(updated.PaidAt);
    }

    public void Dispose() => _fix.Dispose();
}

// ─────────────────────────────────────────────────────────────────────────────
// MOCK PAYMENT GATEWAY TESTS
// ─────────────────────────────────────────────────────────────────────────────

public class MockPaymentGatewayTests
{
    private readonly MockPaymentGateway _gw = new(
        new MockPaymentOptions(),
        new Mock<Microsoft.Extensions.Logging.ILogger<MockPaymentGateway>>().Object);

    [Fact]
    public async Task Initiate_ReturnsProviderRefAndRedirectUrl()
    {
        var req    = new PaymentRequest("REF001", 2567m, "ZAR", "Test", "t@test.com", "Test User",
            "https://return.test", "https://cancel.test");
        var result = await _gw.InitiatePaymentAsync(req);

        Assert.True(result.Success);
        Assert.NotNull(result.ProviderRef);
        Assert.NotNull(result.RedirectUrl);
    }

    [Fact]
    public async Task Verify_SuccessRef_ReturnsComplete()
    {
        var result = await _gw.VerifyPaymentAsync("MOCK-ABC123");
        Assert.True(result.Success);
        Assert.Equal("COMPLETE", result.Status);
    }

    [Fact]
    public async Task Verify_FailRef_ReturnsFailed()
    {
        var result = await _gw.VerifyPaymentAsync("FAIL-PAYMENT");
        Assert.False(result.Success);
        Assert.Equal("FAILED", result.Status);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DOCUMENT SERVICE TESTS
// ─────────────────────────────────────────────────────────────────────────────

public class DocumentServiceTests
{
    private readonly DocumentService _svc;

    public DocumentServiceTests()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());
        _svc = new DocumentService(envMock.Object);
    }

    [Fact]
    public void IsValidDocument_NullFile_ReturnsFalse()
    {
        var valid = _svc.IsValidDocument(null, out var err);
        Assert.False(valid);
        Assert.NotEmpty(err);
    }

    [Fact]
    public void IsValidDocument_NullFile_ErrorMessageSet()
    {
        _svc.IsValidDocument(null, out var err);
        Assert.Equal("File is required.", err);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FULL WORKFLOW INTEGRATION TEST
// ─────────────────────────────────────────────────────────────────────────────

public class FullWorkflowIntegrationTests : IDisposable
{
    private readonly DbFixture _fix = new();

    [Fact]
    public async Task CompleteWorkflow_ApplyApproveEnrollAllocatePay()
    {
        // ── SETUP ─────────────────────────────────────────────────────────────
        var parent  = TestHelpers.MakeParent();
        _fix.Db.Users.Add(parent);
        var grade   = TestHelpers.MakeGrade();
        _fix.Db.Grades.Add(grade);
        await _fix.Db.SaveChangesAsync();
        var cls     = TestHelpers.MakeClass(gradeId: grade.Id);
        _fix.Db.Classes.Add(cls);
        var subject = TestHelpers.MakeSubject();
        _fix.Db.Subjects.Add(subject);
        var room    = new Room { Name = "R1", Capacity = 4, IsActive = true };
        _fix.Db.Rooms.Add(room);
        await _fix.Db.SaveChangesAsync();
        var bed = new Bed { RoomId = room.Id, BedNumber = "B1", Status = BedStatus.Available };
        _fix.Db.Beds.Add(bed);
        await _fix.Db.SaveChangesAsync();

        var auditSvc  = TestHelpers.MockAudit();
        var emailSvc  = TestHelpers.MockEmail();
        var pdfMock   = new Mock<IPdfService>();
        pdfMock.Setup(p => p.GenerateRegistrationProof(It.IsAny<Enrollment>(),
            It.IsAny<Payment?>(), It.IsAny<Payment?>(), It.IsAny<RoomAllocation?>()))
            .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var appSvc    = new ApplicationService(_fix.Db, emailSvc, auditSvc);
        var enrollSvc = new EnrollmentService(_fix.Db, pdfMock.Object, emailSvc, auditSvc);
        var allocSvc  = new AllocationService(_fix.Db, auditSvc);

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());
        var paySvc = new PaymentService(_fix.Db,
            new MockPaymentGateway(new MockPaymentOptions(),
                new Mock<Microsoft.Extensions.Logging.ILogger<MockPaymentGateway>>().Object),
            pdfMock.Object, emailSvc, auditSvc, envMock.Object);

        // ── STEP 1: Parent applies ─────────────────────────────────────────────
        var data = new NewApplicationData("Sipho", "Dlamini", new DateTime(2008, 3, 15), "Male", "SA0001");
        var app  = await appSvc.SubmitApplicationAsync(parent.Id, data, new DocumentPaths(null, null, null));
        Assert.Equal(ApplicationStatus.Pending, app.Status);

        // ── STEP 2: Admin approves ────────────────────────────────────────────
        app = await appSvc.ApproveApplicationAsync(app.Id, "admin-1", "Admin", "127.0.0.1");
        Assert.Equal(ApplicationStatus.Approved, app.Status);

        // ── STEP 3: Admin enrolls ─────────────────────────────────────────────
        var enrollment = await enrollSvc.EnrollAsync(app.Id, cls.Id, new[] { subject.Id }, "admin-1", "Admin", "127.0.0.1");
        Assert.True(enrollment.IsActive);
        var updatedApp = await appSvc.GetByIdAsync(app.Id);
        Assert.Equal(ApplicationStatus.Enrolled, updatedApp!.Status);

        // ── STEP 4: Admin allocates bed ───────────────────────────────────────
        var alloc = await allocSvc.AllocateAsync(enrollment.LearnerId, room.Id, bed.Id, "admin-1", "Admin", "127.0.0.1");
        Assert.True(alloc.IsActive);
        Assert.Equal(BedStatus.Occupied, (await _fix.Db.Beds.FindAsync(bed.Id))!.Status);

        // ── STEP 5: Record registration payment ───────────────────────────────
        var regPayment = await paySvc.RecordManualPaymentAsync(enrollment.LearnerId, parent.Id, 2567m,
            PaymentType.Registration, "FNB-REG-001", null, "admin-1");
        Assert.Equal(PaymentStatus.Completed, regPayment.Status);

        // ── STEP 6: Record accommodation payment ─────────────────────────────
        var accPayment = await paySvc.RecordManualPaymentAsync(enrollment.LearnerId, parent.Id, 5439m,
            PaymentType.Accommodation, "FNB-ACC-001", null, "admin-1");
        Assert.Equal(PaymentStatus.Completed, accPayment.Status);

        // ── STEP 7: Generate proof ─────────────────────────────────────────────
        // Reload enrollment with all includes
        var fullEnrollment = await _fix.Db.Enrollments
            .Include(e => e.Learner).ThenInclude(l => l.Parent)
            .Include(e => e.Class).ThenInclude(c => c.Grade)
            .Include(e => e.EnrollmentSubjects).ThenInclude(es => es.Subject)
            .FirstAsync(e => e.Id == enrollment.Id);

        var pdfBytes = pdfMock.Object.GenerateRegistrationProof(fullEnrollment, regPayment, accPayment, alloc);
        Assert.NotEmpty(pdfBytes);
        Assert.Equal(0x25, pdfBytes[0]); // %PDF magic byte
    }

    public void Dispose() => _fix.Dispose();
}
