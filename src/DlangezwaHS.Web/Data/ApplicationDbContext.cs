using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using static DlangezwaHS.Web.Models.Domain.QuestionPaper;

namespace DlangezwaHS.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // Academic
    public DbSet<Learner> Learners => Set<Learner>();
    public DbSet<LearnerApplication> Applications => Set<LearnerApplication>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SubjectGrade> SubjectGrades => Set<SubjectGrade>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<EnrollmentSubject> EnrollmentSubjects => Set<EnrollmentSubject>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<ClassTeacher> ClassTeachers => Set<ClassTeacher>();

    // Boarding
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<RoomAllocation> RoomAllocations => Set<RoomAllocation>();

    // Finance
    public DbSet<FeeType> FeeTypes => Set<FeeType>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<RegistrationProof> RegistrationProofs => Set<RegistrationProof>();

    // Teacher feature
    public DbSet<TeacherClassSubject> TeacherClassSubjects => Set<TeacherClassSubject>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Mark> Marks => Set<Mark>();

    // System
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // UC14 – Question Papers
    public DbSet<QuestionPaper> QuestionPapers => Set<QuestionPaper>();

    // UC16 – Timetable
    public DbSet<TimetableSlot> TimetableSlots => Set<TimetableSlot>();
    public DbSet<TimetableSubjectColor> TimetableSubjectColors => Set<TimetableSubjectColor>();

    //Calendar 
    public DbSet<CalendarEvent> CalendarEvents { get; set; }

    //Event Management 

    public DbSet<SchoolEvent> SchoolEvents { get; set; }
    public DbSet<EventCoordinator> EventCoordinators { get; set; }
    public DbSet<EventCoordinatorRole> EventCoordinatorRoles { get; set; }
    public DbSet<EventTicket> EventTickets { get; set; }
    public DbSet<EventRSVP> EventRSVPs { get; set; }
    public DbSet<EventScan> EventScans { get; set; }

    // Transport
    public DbSet<Bus> Buses { get; set; }
    public DbSet<Driver> Drivers { get; set; }
    public DbSet<Trip> Trips { get; set; }
    public DbSet<TripLearner> TripLearners { get; set; }
    public DbSet<TripOccurrence> TripOccurrences { get; set; }
    public DbSet<BoardingRecord> BoardingRecords { get; set; }
    public DbSet<DelayReport> DelayReports { get; set; }
    public DbSet<FuelRecord> FuelRecords { get; set; }

    // Boarding & Meals (Increment 3) — staff profiles
    public DbSet<Housemaster> Housemasters => Set<Housemaster>();
    public DbSet<KitchenStaffMember> KitchenStaffMembers => Set<KitchenStaffMember>();

    // Boarding & Meals (Increment 3) — UC1/UC2 check-in/out & leave requests
    public DbSet<BoardingMovement> BoardingMovements => Set<BoardingMovement>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    // Boarding & Meals (Increment 3) — UC3/UC4 dietary profiles & meal plans
    public DbSet<DietaryProfile> DietaryProfiles => Set<DietaryProfile>();
    public DbSet<DietaryProfileDocument> DietaryProfileDocuments => Set<DietaryProfileDocument>();
    public DbSet<DietaryItem> DietaryItems => Set<DietaryItem>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<MealPlanItem> MealPlanItems => Set<MealPlanItem>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealIngredient> MealIngredients => Set<MealIngredient>();

    // Boarding & Meals (Increment 3) — UC5/UC6 pre-ordering & inventory
    public DbSet<MealPreOrder> MealPreOrders => Set<MealPreOrder>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionItem> PurchaseRequisitionItems => Set<PurchaseRequisitionItem>();
    public DbSet<StockUsageLog> StockUsageLogs => Set<StockUsageLog>();
    public DbSet<IngredientUsageRecord> IngredientUsageRecords => Set<IngredientUsageRecord>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierIngredient> SupplierIngredients => Set<SupplierIngredient>();
    public DbSet<StockReceiptLine> StockReceiptLines => Set<StockReceiptLine>();
    public DbSet<BoardingSettings> BoardingSettings => Set<BoardingSettings>();

    // Boarding & Meals (Increment 3) — UC7/UC8 kitchen schedule & meal attendance
    public DbSet<KitchenSchedule> KitchenSchedules => Set<KitchenSchedule>();
    public DbSet<KitchenTask> KitchenTasks => Set<KitchenTask>();
    public DbSet<KitchenTeam> KitchenTeams => Set<KitchenTeam>();
    public DbSet<KitchenTeamMember> KitchenTeamMembers => Set<KitchenTeamMember>();
    public DbSet<MealAttendance> MealAttendances => Set<MealAttendance>();
    public DbSet<MealAbsenceAlert> MealAbsenceAlerts => Set<MealAbsenceAlert>();

    // Boarding & Meals (Increment 3) — UC9 meal feedback
    public DbSet<MealFeedback> MealFeedbacks => Set<MealFeedback>();
    public DbSet<MealQualityAlert> MealQualityAlerts => Set<MealQualityAlert>();

    // Boarding & Meals (Increment 3) — UC10 compliance report archive
    public DbSet<MealComplianceReportRecord> MealComplianceReportRecords => Set<MealComplianceReportRecord>();

    // Extracurricular
    public DbSet<Activity> Activities { get; set; }
    public DbSet<ActivityCoach> ActivityCoaches { get; set; }
    public DbSet<ActivityRegistration> ActivityRegistrations { get; set; }
    public DbSet<ActivitySession> ActivitySessions { get; set; }
    public DbSet<ActivityAttendance> ActivityAttendances { get; set; }
    public DbSet<Achievement> Achievements { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Composite keys for join tables
        builder.Entity<SubjectGrade>()
            .HasKey(sg => new { sg.SubjectId, sg.GradeId });

        builder.Entity<EnrollmentSubject>()
            .HasKey(es => new { es.EnrollmentId, es.SubjectId });

        builder.Entity<TeacherSubject>()
            .HasKey(ts => new { ts.TeacherId, ts.SubjectId });

        builder.Entity<ClassTeacher>()
            .HasKey(ct => new { ct.TeacherId, ct.ClassId });

        // Prevent cascade deletes that would cause circular references
        builder.Entity<LearnerApplication>()
            .HasOne(a => a.Learner)
            .WithMany(l => l.Applications)
            .HasForeignKey(a => a.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LearnerApplication>()
            .HasOne(a => a.Parent)
            .WithMany(u => u.Applications)
            .HasForeignKey(a => a.ParentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Learner>()
            .HasOne(l => l.Parent)
            .WithMany(u => u.Learners)
            .HasForeignKey(l => l.ParentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Enrollment>()
            .HasOne(e => e.Learner)
            .WithMany(l => l.Enrollments)
            .HasForeignKey(e => e.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Payment>()
            .HasOne(p => p.Learner)
            .WithMany(l => l.Payments)
            .HasForeignKey(p => p.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Payment>()
            .HasOne(p => p.Parent)
            .WithMany()
            .HasForeignKey(p => p.ParentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<RoomAllocation>()
            .HasOne(r => r.Learner)
            .WithMany(l => l.Allocations)
            .HasForeignKey(r => r.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RoomAllocation>()
            .HasOne(r => r.Bed)
            .WithMany(b => b.Allocations)
            .HasForeignKey(r => r.BedId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.Email).IsUnique();

        builder.Entity<Learner>()
            .HasIndex(l => l.LearnerIdNumber).IsUnique();

        builder.Entity<Subject>()
            .HasIndex(s => s.Code).IsUnique();

        builder.Entity<AuditLog>()
            .HasIndex(a => a.Timestamp);

        builder.Entity<AuditLog>()
            .HasIndex(a => a.UserId);

        // ── Teacher feature ───────────────────────────────────────────────────

        builder.Entity<TeacherClassSubject>()
            .HasOne(t => t.Teacher)
            .WithMany(t => t.TeacherClassSubjects)
            .HasForeignKey(t => t.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TeacherClassSubject>()
            .HasOne(t => t.Class)
            .WithMany()
            .HasForeignKey(t => t.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TeacherClassSubject>()
            .HasOne(t => t.Subject)
            .WithMany()
            .HasForeignKey(t => t.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Attendance>()
            .HasOne(a => a.Learner)
            .WithMany(l => l.Attendances)
            .HasForeignKey(a => a.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Attendance>()
            .HasOne(a => a.Teacher)
            .WithMany(t => t.Attendances)
            .HasForeignKey(a => a.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Attendance>()
            .HasOne(a => a.Class)
            .WithMany()
            .HasForeignKey(a => a.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique: one attendance record per learner per day per class
        builder.Entity<Attendance>()
            .HasIndex(a => new { a.LearnerId, a.ClassId, a.Date })
            .IsUnique();

        builder.Entity<Assessment>()
            .HasOne(a => a.Teacher)
            .WithMany(t => t.Assessments)
            .HasForeignKey(a => a.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Assessment>()
            .HasOne(a => a.Class)
            .WithMany()
            .HasForeignKey(a => a.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Assessment>()
            .HasOne(a => a.Subject)
            .WithMany()
            .HasForeignKey(a => a.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Mark>()
            .HasOne(m => m.Learner)
            .WithMany(l => l.Marks)
            .HasForeignKey(m => m.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Mark>()
            .HasOne(m => m.Assessment)
            .WithMany(a => a.Marks)
            .HasForeignKey(m => m.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique: one mark per learner per assessment
        builder.Entity<Mark>()
            .HasIndex(m => new { m.LearnerId, m.AssessmentId })
            .IsUnique();

        builder.Entity<Teacher>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Boarding & Meals (Increment 3) — staff profiles ───────────────────

        builder.Entity<Housemaster>()
            .HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<KitchenStaffMember>()
            .HasOne(k => k.User)
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Boarding & Meals (Increment 3) — UC1/UC2 ──────────────────────────

        builder.Entity<Learner>()
            .HasIndex(l => l.BoardingQrCode);

        builder.Entity<BoardingMovement>()
            .HasOne(m => m.Learner)
            .WithMany()
            .HasForeignKey(m => m.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BoardingMovement>()
            .HasOne(m => m.LeaveRequest)
            .WithMany()
            .HasForeignKey(m => m.LeaveRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BoardingMovement>()
            .HasOne(m => m.ApprovedByHousemaster)
            .WithMany()
            .HasForeignKey(m => m.ApprovedByHousemasterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<BoardingMovement>()
            .HasOne(m => m.ApprovedByParent)
            .WithMany()
            .HasForeignKey(m => m.ApprovedByParentUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<BoardingMovement>()
            .HasIndex(m => new { m.LearnerId, m.ScannedAt });

        builder.Entity<LeaveRequest>()
            .HasOne(r => r.Learner)
            .WithMany()
            .HasForeignKey(r => r.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LeaveRequest>()
            .HasOne(r => r.RequestedByParent)
            .WithMany()
            .HasForeignKey(r => r.RequestedByParentUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LeaveRequest>()
            .HasOne(r => r.ApprovedByHousemaster)
            .WithMany()
            .HasForeignKey(r => r.ApprovedByHousemasterId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Boarding & Meals (Increment 3) — UC3/UC4 ──────────────────────────

        builder.Entity<DietaryProfile>()
            .HasOne(d => d.Learner)
            .WithMany()
            .HasForeignKey(d => d.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DietaryProfile>()
            .HasOne(d => d.SubmittedByParent)
            .WithMany()
            .HasForeignKey(d => d.SubmittedByParentUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DietaryProfile>()
            .HasIndex(d => new { d.LearnerId, d.Status });

        builder.Entity<DietaryItem>()
            .HasOne(i => i.DietaryProfile)
            .WithMany(d => d.Items)
            .HasForeignKey(i => i.DietaryProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MealPlan>()
            .HasIndex(m => m.WeekStartDate);

        builder.Entity<MealPlanItem>()
            .HasOne(i => i.MealPlan)
            .WithMany(m => m.Items)
            .HasForeignKey(i => i.MealPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MealPlanItem>()
            .HasOne(i => i.Meal)
            .WithMany()
            .HasForeignKey(i => i.MealId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Meal>()
            .HasIndex(m => m.Name);

        builder.Entity<MealIngredient>()
            .HasOne(mi => mi.Meal)
            .WithMany(m => m.Ingredients)
            .HasForeignKey(mi => mi.MealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MealIngredient>()
            .HasOne(mi => mi.Ingredient)
            .WithMany()
            .HasForeignKey(mi => mi.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MealIngredient>()
            .Property(mi => mi.QuantityPerServing).HasPrecision(18, 3);

        builder.Entity<Meal>()
            .Property(m => m.PrepMinutesPerServing).HasPrecision(6, 2);

        builder.Entity<DietaryProfileDocument>()
            .HasOne(d => d.DietaryProfile)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.DietaryProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Boarding & Meals (Increment 3) — UC5/UC6 ──────────────────────────

        builder.Entity<MealPreOrder>()
            .HasOne(o => o.MealPlanItem)
            .WithMany()
            .HasForeignKey(o => o.MealPlanItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MealPreOrder>()
            .HasOne(o => o.Learner)
            .WithMany()
            .HasForeignKey(o => o.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MealPreOrder>()
            .HasIndex(o => new { o.MealPlanItemId, o.LearnerId }).IsUnique();

        builder.Entity<Ingredient>()
            .HasIndex(i => i.Name).IsUnique();

        builder.Entity<PurchaseRequisitionItem>()
            .HasOne(i => i.PurchaseRequisition)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.PurchaseRequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseRequisitionItem>()
            .HasOne(i => i.Ingredient)
            .WithMany()
            .HasForeignKey(i => i.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockUsageLog>()
            .HasOne(s => s.Ingredient)
            .WithMany()
            .HasForeignKey(s => s.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockUsageLog>()
            .HasOne(s => s.UsedForMealPlanItem)
            .WithMany()
            .HasForeignKey(s => s.UsedForMealPlanItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<StockUsageLog>()
            .HasOne(s => s.IngredientUsageRecord)
            .WithMany(r => r.Lines)
            .HasForeignKey(s => s.IngredientUsageRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<IngredientUsageRecord>()
            .HasOne(r => r.MealPlanItem)
            .WithMany()
            .HasForeignKey(r => r.MealPlanItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<IngredientUsageRecord>()
            .HasIndex(r => r.RecordedAt);

        builder.Entity<StockReceiptLine>()
            .HasOne(l => l.StockReceipt)
            .WithMany(r => r.Lines)
            .HasForeignKey(l => l.StockReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockReceiptLine>()
            .HasOne(l => l.Ingredient)
            .WithMany()
            .HasForeignKey(l => l.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockReceiptLine>()
            .Property(l => l.Quantity).HasPrecision(18, 3);

        builder.Entity<StockReceipt>()
            .HasIndex(r => r.ReceiptDate);
        builder.Entity<StockReceipt>()
            .HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<KitchenTeam>()
            .HasOne(t => t.HeadChef).WithMany().HasForeignKey(t => t.HeadChefId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Supplier>().HasIndex(s => s.Name).IsUnique();
        builder.Entity<SupplierIngredient>()
            .HasOne(si => si.Supplier).WithMany(s => s.Ingredients).HasForeignKey(si => si.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierIngredient>()
            .HasOne(si => si.Ingredient).WithMany().HasForeignKey(si => si.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierIngredient>().HasIndex(si => new { si.SupplierId, si.IngredientId }).IsUnique();
        builder.Entity<SupplierIngredient>().Property(si => si.UnitPrice).HasPrecision(18, 2);
        builder.Entity<SupplierIngredient>().Property(si => si.DefaultQuantity).HasPrecision(18, 3);

        // ── Boarding & Meals (Increment 3) — UC7/UC8 ──────────────────────────

        builder.Entity<KitchenSchedule>()
            .HasOne(s => s.MealPlanItem)
            .WithMany()
            .HasForeignKey(s => s.MealPlanItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<KitchenSchedule>()
            .HasOne(s => s.KitchenTeam)
            .WithMany()
            .HasForeignKey(s => s.KitchenTeamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<KitchenTeamMember>()
            .HasKey(m => new { m.KitchenTeamId, m.KitchenStaffMemberId });

        builder.Entity<KitchenTeamMember>()
            .HasOne(m => m.KitchenTeam)
            .WithMany(t => t.Members)
            .HasForeignKey(m => m.KitchenTeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<KitchenTeamMember>()
            .HasOne(m => m.KitchenStaffMember)
            .WithMany()
            .HasForeignKey(m => m.KitchenStaffMemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<KitchenTask>()
            .HasOne(t => t.KitchenSchedule)
            .WithMany(s => s.Tasks)
            .HasForeignKey(t => t.KitchenScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MealAttendance>()
            .HasOne(a => a.MealPlanItem)
            .WithMany()
            .HasForeignKey(a => a.MealPlanItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MealAttendance>()
            .HasOne(a => a.Learner)
            .WithMany()
            .HasForeignKey(a => a.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MealAttendance>()
            .HasIndex(a => new { a.MealPlanItemId, a.LearnerId }).IsUnique();

        builder.Entity<MealAbsenceAlert>()
            .HasOne(a => a.Learner)
            .WithMany()
            .HasForeignKey(a => a.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MealAbsenceAlert>()
            .HasOne(a => a.AlertedHousemaster)
            .WithMany()
            .HasForeignKey(a => a.AlertedHousemasterId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Boarding & Meals (Increment 3) — UC9 ──────────────────────────────

        builder.Entity<MealFeedback>()
            .HasOne(f => f.MealPlanItem)
            .WithMany()
            .HasForeignKey(f => f.MealPlanItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MealQualityAlert>()
            .HasOne(a => a.MealPlanItem)
            .WithMany()
            .HasForeignKey(a => a.MealPlanItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── UC14 – Question Papers ────────────────────────────────────────────

        builder.Entity<QuestionPaper>()
            .HasOne(qp => qp.Subject)
            .WithMany()
            .HasForeignKey(qp => qp.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QuestionPaper>()
            .HasOne(qp => qp.Grade)
            .WithMany()
            .HasForeignKey(qp => qp.GradeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QuestionPaper>()
            .HasOne(qp => qp.UploadedBy)
            .WithMany()
            .HasForeignKey(qp => qp.UploadedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<QuestionPaper>()
            .HasOne(qp => qp.UploadedByTeacher)
            .WithMany()
            .HasForeignKey(qp => qp.UploadedByTeacherId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<QuestionPaper>()
            .HasIndex(qp => new { qp.SubjectId, qp.GradeId, qp.Term, qp.AcademicYear });

        builder.Entity<QuestionPaper>()
            .HasIndex(qp => qp.ExpiresAt);

        // ── UC16 – Timetable ──────────────────────────────────────────────────

        // Unique constraint: one subject per period per day per class per term/year
        builder.Entity<TimetableSlot>()
            .HasIndex(ts => new { ts.ClassId, ts.Day, ts.PeriodNumber, ts.Term, ts.AcademicYear })
            .IsUnique();

        builder.Entity<TimetableSlot>()
            .HasOne(ts => ts.Class)
            .WithMany()
            .HasForeignKey(ts => ts.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TimetableSlot>()
            .HasOne(ts => ts.Subject)
            .WithMany()
            .HasForeignKey(ts => ts.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TimetableSlot>()
            .HasOne(ts => ts.Teacher)
            .WithMany()
            .HasForeignKey(ts => ts.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TimetableSubjectColor>()
            .HasIndex(c => c.SubjectId)
            .IsUnique();

        builder.Entity<TimetableSubjectColor>()
            .HasOne(c => c.Subject)
            .WithMany()
            .HasForeignKey(c => c.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Transport Module ──────────────────────────────────────────────────

        builder.Entity<Trip>()
            .HasOne(t => t.Bus)
            .WithMany(b => b.Trips)
            .HasForeignKey(t => t.BusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Trip>()
            .HasOne(t => t.Driver)
            .WithMany(d => d.Trips)
            .HasForeignKey(t => t.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TripLearner>()
            .HasOne(tl => tl.Trip)
            .WithMany(t => t.TripLearners)
            .HasForeignKey(tl => tl.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TripLearner>()
            .HasOne(tl => tl.Learner)
            .WithMany()
            .HasForeignKey(tl => tl.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TripLearner>()
            .HasIndex(tl => new { tl.TripId, tl.LearnerId }).IsUnique();

        builder.Entity<TripOccurrence>()
            .HasOne(o => o.Trip)
            .WithMany(t => t.Occurrences)
            .HasForeignKey(o => o.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TripOccurrence>()
            .HasIndex(o => new { o.TripId, o.Date }).IsUnique();

        builder.Entity<BoardingRecord>()
            .HasOne(b => b.TripOccurrence)
            .WithMany(o => o.BoardingRecords)
            .HasForeignKey(b => b.TripOccurrenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BoardingRecord>()
            .HasOne(b => b.Learner)
            .WithMany()
            .HasForeignKey(b => b.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BoardingRecord>()
            .HasIndex(b => new { b.TripOccurrenceId, b.LearnerId }).IsUnique();

        builder.Entity<DelayReport>()
            .HasOne(d => d.TripOccurrence)
            .WithMany(o => o.DelayReports)
            .HasForeignKey(d => d.TripOccurrenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<FuelRecord>()
            .HasOne(f => f.Bus)
            .WithMany(b => b.FuelRecords)
            .HasForeignKey(f => f.BusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FuelRecord>()
            .HasIndex(f => new { f.BusId, f.Date });

        builder.Entity<Bus>()
            .HasIndex(b => b.RegistrationNumber).IsUnique();

        builder.Entity<Driver>()
            .HasIndex(d => d.IdNumber).IsUnique();

        builder.Entity<Driver>()
            .HasIndex(d => d.LicenseExpiryDate);

        // ── Extracurricular Module ────────────────────────────────────────────

        builder.Entity<ActivityCoach>()
            .HasOne(c => c.Activity)
            .WithMany(a => a.Coaches)
            .HasForeignKey(c => c.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ActivityCoach>()
            .HasOne(c => c.Teacher)
            .WithMany()
            .HasForeignKey(c => c.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ActivityRegistration>()
            .HasOne(r => r.Activity)
            .WithMany(a => a.Registrations)
            .HasForeignKey(r => r.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ActivityRegistration>()
            .HasOne(r => r.Learner)
            .WithMany()
            .HasForeignKey(r => r.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ActivityRegistration>()
            .HasIndex(r => new { r.ActivityId, r.LearnerId }).IsUnique();

        builder.Entity<ActivitySession>()
            .HasOne(s => s.Activity)
            .WithMany(a => a.Sessions)
            .HasForeignKey(s => s.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ActivitySession>()
            .HasIndex(s => new { s.ActivityId, s.Date }).IsUnique();

        builder.Entity<ActivityAttendance>()
            .HasOne(a => a.ActivitySession)
            .WithMany(s => s.Attendances)
            .HasForeignKey(a => a.ActivitySessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ActivityAttendance>()
            .HasOne(a => a.Learner)
            .WithMany()
            .HasForeignKey(a => a.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ActivityAttendance>()
            .HasIndex(a => new { a.ActivitySessionId, a.LearnerId }).IsUnique();

        builder.Entity<Achievement>()
            .HasOne(a => a.Learner)
            .WithMany()
            .HasForeignKey(a => a.LearnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Achievement>()
            .HasOne(a => a.Activity)
            .WithMany(act => act.Achievements)
            .HasForeignKey(a => a.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}