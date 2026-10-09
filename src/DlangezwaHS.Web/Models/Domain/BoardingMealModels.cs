using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DlangezwaHS.Web.Models.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// STAFF PROFILES (Increment 3 — Boarding & Meals)
// ─────────────────────────────────────────────────────────────────────────────

public class Housemaster
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(20)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public string FullName => $"{FirstName} {LastName}";
    // Linked Identity login account (created when Admin registers housemaster)
    public string? UserId { get; set; }
    [ForeignKey(nameof(UserId))] public ApplicationUser? User { get; set; }
}

public class KitchenStaffMember
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(20)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public string FullName => $"{FirstName} {LastName}";
    // Linked Identity login account (created when Admin registers kitchen staff)
    public string? UserId { get; set; }
    [ForeignKey(nameof(UserId))] public ApplicationUser? User { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC1 / UC2 — BOARDING CHECK-IN/OUT & LEAVE REQUESTS
// ─────────────────────────────────────────────────────────────────────────────

public enum MovementType { CheckIn, CheckOut }
public enum MovementStatus { OnTime, Late, Missing }

public class BoardingMovement
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public MovementType MovementType { get; set; }
    [StringLength(64)] public string? QrHash { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedReturnTime { get; set; }
    public DateTime? ActualReturnTime { get; set; }
    public bool IsLate { get; set; }
    public int LateMinutes { get; set; }
    public MovementStatus Status { get; set; } = MovementStatus.OnTime;
    [StringLength(500)] public string? Notes { get; set; }
    public int? ApprovedByHousemasterId { get; set; }
    [ForeignKey(nameof(ApprovedByHousemasterId))] public Housemaster? ApprovedByHousemaster { get; set; }
    public string? ApprovedByParentUserId { get; set; }
    [ForeignKey(nameof(ApprovedByParentUserId))] public ApplicationUser? ApprovedByParent { get; set; }
    [StringLength(200)] public string? Destination { get; set; }
    [StringLength(300)] public string? Purpose { get; set; }
    // Links a CheckOut/CheckIn pair back to the leave request that authorised it
    public int? LeaveRequestId { get; set; }
    [ForeignKey(nameof(LeaveRequestId))] public LeaveRequest? LeaveRequest { get; set; }
}

public enum LeaveRequestStatus { PendingHousemaster, Approved, Rejected }

public class LeaveRequest
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public string RequestedByParentUserId { get; set; } = "";
    [ForeignKey(nameof(RequestedByParentUserId))] public ApplicationUser RequestedByParent { get; set; } = null!;
    [Required, StringLength(200)] public string Destination { get; set; } = "";
    [Required, StringLength(300)] public string Purpose { get; set; } = "";
    public DateTime DepartureDate { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.PendingHousemaster;
    [StringLength(500)] public string? HousemasterNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByHousemasterId { get; set; }
    [ForeignKey(nameof(ApprovedByHousemasterId))] public Housemaster? ApprovedByHousemaster { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC3 — DIETARY PROFILES & ALLERGIES
// ─────────────────────────────────────────────────────────────────────────────

// Profiles are active as soon as a parent or learner saves them (no review step).
// Pending, Rejected and PendingParent are legacy values kept because enums are stored as ints.
public enum DietaryProfileStatus { Pending, Active, Rejected, Superseded, PendingParent }
public enum DietaryCategory { Allergy, DietaryRequirement, MedicalRestriction, Preference }
public enum AllergySeverity { Mild, Severe, Anaphylactic }

public class DietaryProfile
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public string SubmittedByParentUserId { get; set; } = "";
    [ForeignKey(nameof(SubmittedByParentUserId))] public ApplicationUser SubmittedByParent { get; set; } = null!;
    public DietaryProfileStatus Status { get; set; } = DietaryProfileStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    [StringLength(500)] public string? RejectionReason { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public ICollection<DietaryItem> Items { get; set; } = new List<DietaryItem>();
    public ICollection<DietaryProfileDocument> Documents { get; set; } = new List<DietaryProfileDocument>();
}

// Optional supporting documents (doctor's letter, allergy test results…) — stored privately
public class DietaryProfileDocument
{
    public int Id { get; set; }
    public int DietaryProfileId { get; set; }
    [ForeignKey(nameof(DietaryProfileId))] public DietaryProfile DietaryProfile { get; set; } = null!;
    [Required, StringLength(255)] public string OriginalFileName { get; set; } = "";
    [Required, StringLength(100)] public string StoredFileName { get; set; } = "";
    [StringLength(100)] public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class DietaryItem
{
    public int Id { get; set; }
    public int DietaryProfileId { get; set; }
    [ForeignKey(nameof(DietaryProfileId))] public DietaryProfile DietaryProfile { get; set; } = null!;
    public DietaryCategory Category { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public AllergySeverity? Severity { get; set; }
    [StringLength(300)] public string? Notes { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC4 — WEEKLY MEAL PLAN
// ─────────────────────────────────────────────────────────────────────────────

public enum MealPlanStatus { Draft, Published, Archived }
public enum MealType { Breakfast, Lunch, Dinner }

public class MealPlan
{
    public int Id { get; set; }
    public DateTime WeekStartDate { get; set; }
    public MealPlanStatus Status { get; set; } = MealPlanStatus.Draft;
    public decimal TotalBudget { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public ICollection<MealPlanItem> Items { get; set; } = new List<MealPlanItem>();
}

public class MealPlanItem
{
    public int Id { get; set; }
    public int MealPlanId { get; set; }
    [ForeignKey(nameof(MealPlanId))] public MealPlan MealPlan { get; set; } = null!;
    public int DayOfWeek { get; set; } // 1 (Mon) – 7 (Sun)
    public MealType MealType { get; set; }
    // The library meal this plan slot serves. MenuDescription/Ingredients are a snapshot
    // of the meal at the time it was added (used for allergy scanning and history).
    public int? MealId { get; set; }
    [ForeignKey(nameof(MealId))] public Meal? Meal { get; set; }
    [Required, StringLength(200)] public string MenuDescription { get; set; } = "";
    [StringLength(500)] public string Ingredients { get; set; } = ""; // comma-separated
    [StringLength(10)] public string ServingTime { get; set; } = ""; // "07:30"
    // Calculated from the meal's ingredient quantities × latest receipt prices
    public decimal EstimatedCostPerHead { get; set; }
    // Set when kitchen staff press "Finish Scanning" for this meal service (UTC)
    public DateTime? ScanningFinishedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC4 — MEAL LIBRARY (reusable meals with recipe quantities)
// ─────────────────────────────────────────────────────────────────────────────

public class Meal
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    // The meal type this dish is usually served as (used to filter pickers)
    public MealType MealType { get; set; }
    [StringLength(300)] public string? ImagePath { get; set; }
    // Hands-on kitchen labour per plate — used to work out how many staff a meal needs
    public decimal PrepMinutesPerServing { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MealIngredient> Ingredients { get; set; } = new List<MealIngredient>();
}

public class MealIngredient
{
    public int Id { get; set; }
    public int MealId { get; set; }
    [ForeignKey(nameof(MealId))] public Meal Meal { get; set; } = null!;
    public int IngredientId { get; set; }
    [ForeignKey(nameof(IngredientId))] public Ingredient Ingredient { get; set; } = null!;
    // Amount of the ingredient (in the ingredient's stock unit) used for ONE learner's serving
    public decimal QuantityPerServing { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC5 — MEAL PRE-ORDERING
// ─────────────────────────────────────────────────────────────────────────────

public enum PreOrderStatus { Pending, Confirmed, Cancelled, WaitListed }
public enum PortionSize { Small, Regular, Large }

public class MealPreOrder
{
    public int Id { get; set; }
    public int MealPlanItemId { get; set; }
    [ForeignKey(nameof(MealPlanItemId))] public MealPlanItem MealPlanItem { get; set; } = null!;
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public string OrderedByParentUserId { get; set; } = "";
    public PreOrderStatus Status { get; set; } = PreOrderStatus.Pending;
    public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
    public PortionSize PortionSize { get; set; } = PortionSize.Regular;
    [StringLength(200)] public string? BlockedReason { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC6 — INVENTORY & INGREDIENT MANAGEMENT
// ─────────────────────────────────────────────────────────────────────────────

public enum StockUnit { Kg, Litres, Units, Grams }
public enum RequisitionStatus { Draft, Submitted, Fulfilled }

public class Ingredient
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public StockUnit Unit { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal MinimumStock { get; set; }
    // Price per unit from the most recent verified receipt/invoice
    public decimal UnitCost { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public string? LastUpdatedByUserId { get; set; }
}

// Suppliers the kitchen buys from, each with the ingredients they deliver and their price per unit.
// Choosing a supplier on Receive Stock fills in their list so only quantities need adjusting.
public class Supplier
{
    public int Id { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [StringLength(100)] public string? ContactName { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(150)] public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<SupplierIngredient> Ingredients { get; set; } = new List<SupplierIngredient>();
}

public class SupplierIngredient
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    [ForeignKey(nameof(SupplierId))] public Supplier Supplier { get; set; } = null!;
    public int IngredientId { get; set; }
    [ForeignKey(nameof(IngredientId))] public Ingredient Ingredient { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    // The quantity usually delivered — pre-filled on Receive Stock and adjustable
    public decimal DefaultQuantity { get; set; }
}

// Stock is only added through a captured supplier receipt/invoice so every
// increase in inventory can be verified against a document.
public class StockReceipt
{
    public int Id { get; set; }
    // Set when the receipt was captured from a supplier on file; SupplierName keeps the name either way
    public int? SupplierId { get; set; }
    [ForeignKey(nameof(SupplierId))] public Supplier? Supplier { get; set; }
    [Required, StringLength(150)] public string SupplierName { get; set; } = "";
    [StringLength(60)] public string? InvoiceNumber { get; set; }
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    [Required, StringLength(300)] public string DocumentPath { get; set; } = "";
    [StringLength(100)] public string? DocumentContentType { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public string CapturedByUserId { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public ICollection<StockReceiptLine> Lines { get; set; } = new List<StockReceiptLine>();
}

public class StockReceiptLine
{
    public int Id { get; set; }
    public int StockReceiptId { get; set; }
    [ForeignKey(nameof(StockReceiptId))] public StockReceipt StockReceipt { get; set; } = null!;
    public int IngredientId { get; set; }
    [ForeignKey(nameof(IngredientId))] public Ingredient Ingredient { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class PurchaseRequisition
{
    public int Id { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string GeneratedByUserId { get; set; } = "";
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    [StringLength(500)] public string? Notes { get; set; }
    public ICollection<PurchaseRequisitionItem> Items { get; set; } = new List<PurchaseRequisitionItem>();
}

public class PurchaseRequisitionItem
{
    public int Id { get; set; }
    public int PurchaseRequisitionId { get; set; }
    [ForeignKey(nameof(PurchaseRequisitionId))] public PurchaseRequisition PurchaseRequisition { get; set; } = null!;
    public int IngredientId { get; set; }
    [ForeignKey(nameof(IngredientId))] public Ingredient Ingredient { get; set; } = null!;
    public decimal QuantityRequired { get; set; }
    public decimal QuantityInStock { get; set; }
    public decimal QuantityToOrder { get; set; }
    public decimal UnitCost { get; set; }
}

// Record Ingredient Usage — the chef records how many servings of a meal were prepared;
// the recipe turns that into one StockUsageLog line per ingredient.
public class IngredientUsageRecord
{
    public int Id { get; set; }
    public int MealPlanItemId { get; set; }
    [ForeignKey(nameof(MealPlanItemId))] public MealPlanItem MealPlanItem { get; set; } = null!;
    public int ServingsPrepared { get; set; }
    // Captured after service — plates prepared but not collected (meal quantity / waste)
    public int? LeftoverServings { get; set; }
    [StringLength(300)] public string? Notes { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string RecordedByUserId { get; set; } = "";
    public ICollection<StockUsageLog> Lines { get; set; } = new List<StockUsageLog>();
}

public class StockUsageLog
{
    public int Id { get; set; }
    public int? IngredientUsageRecordId { get; set; }
    [ForeignKey(nameof(IngredientUsageRecordId))] public IngredientUsageRecord? IngredientUsageRecord { get; set; }
    public int IngredientId { get; set; }
    [ForeignKey(nameof(IngredientId))] public Ingredient Ingredient { get; set; } = null!;
    public decimal QuantityUsed { get; set; }
    public int? UsedForMealPlanItemId { get; set; }
    [ForeignKey(nameof(UsedForMealPlanItemId))] public MealPlanItem? UsedForMealPlanItem { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string RecordedByUserId { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// BOARDING SETTINGS (Increment 3 — singleton config row)
// ─────────────────────────────────────────────────────────────────────────────

public class BoardingSettings
{
    public int Id { get; set; }
    public decimal WeeklyMealBudget { get; set; } = 15000m;
    public int MaxPreOrdersPerMeal { get; set; } = 200;
    // How many learners eat each meal — turns per-serving costs into a weekly cost to compare with the budget
    public int ExpectedDinersPerMeal { get; set; } = 40;

    // Pre-determined kitchen shifts: prep starts at *Start, meal is served at *Serve.
    // Meal plan serving times and kitchen task times are taken from these.
    [StringLength(5)] public string BreakfastStart { get; set; } = "05:00";
    [StringLength(5)] public string BreakfastServe { get; set; } = "07:00";
    [StringLength(5)] public string LunchStart { get; set; } = "10:00";
    [StringLength(5)] public string LunchServe { get; set; } = "13:00";
    [StringLength(5)] public string DinnerStart { get; set; } = "15:00";
    [StringLength(5)] public string DinnerServe { get; set; } = "18:00";

    // How long each meal is served for. Once it ends, pre-order tiles grey out and the
    // meal scanner moves on to the next serving time.
    public int ServingMinutes { get; set; } = 90;

    public TimeSpan ServingDuration => TimeSpan.FromMinutes(ServingMinutes > 0 ? ServingMinutes : 90);

    // Boarding curfew (school time). During curfew only learners with approved leave may
    // check out; outside it learners may go out without leave but must be back by CurfewStart.
    [StringLength(5)] public string CurfewStart { get; set; } = "19:00";
    [StringLength(5)] public string CurfewEnd { get; set; } = "06:00";

    public TimeSpan CurfewStartTime => TimeSpan.TryParse(CurfewStart, out var t) ? t : new TimeSpan(19, 0, 0);
    public TimeSpan CurfewEndTime => TimeSpan.TryParse(CurfewEnd, out var t) ? t : new TimeSpan(6, 0, 0);

    /// <summary>True when the given school time falls in the curfew (which may run past midnight).</summary>
    public bool IsCurfew(DateTime schoolTime)
    {
        var t = schoolTime.TimeOfDay;
        return CurfewStartTime <= CurfewEndTime
            ? t >= CurfewStartTime && t < CurfewEndTime
            : t >= CurfewStartTime || t < CurfewEndTime;
    }

    public (string Start, string Serve) ShiftFor(MealType type) => type switch
    {
        MealType.Breakfast => (BreakfastStart, BreakfastServe),
        MealType.Lunch => (LunchStart, LunchServe),
        _ => (DinnerStart, DinnerServe)
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// UC7 — KITCHEN SCHEDULE
// ─────────────────────────────────────────────────────────────────────────────

public enum KitchenScheduleStatus { Scheduled, InProgress, Completed, Delayed }
public enum KitchenTaskStatus { Assigned, InProgress, Done }

public class KitchenSchedule
{
    public int Id { get; set; }
    public int MealPlanItemId { get; set; }
    [ForeignKey(nameof(MealPlanItemId))] public MealPlanItem MealPlanItem { get; set; } = null!;
    public DateTime ScheduledDate { get; set; }
    public MealType MealType { get; set; }
    public int? KitchenTeamId { get; set; }
    [ForeignKey(nameof(KitchenTeamId))] public KitchenTeam? KitchenTeam { get; set; }
    public KitchenScheduleStatus Status { get; set; } = KitchenScheduleStatus.Scheduled;
    // Portions come from confirmed pre-orders; staff recommended = portions × prep minutes ÷ shift length
    public int PortionsPlanned { get; set; }
    public int StaffRecommended { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? PublishedByUserId { get; set; }
    public ICollection<KitchenTask> Tasks { get; set; } = new List<KitchenTask>();
}

public class KitchenTeam
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    // The head chef leads the team; head chefs can create teams and schedule them for meals
    public int? HeadChefId { get; set; }
    [ForeignKey(nameof(HeadChefId))] public KitchenStaffMember? HeadChef { get; set; }
    public ICollection<KitchenTeamMember> Members { get; set; } = new List<KitchenTeamMember>();
}

public class KitchenTeamMember
{
    public int KitchenTeamId { get; set; }
    [ForeignKey(nameof(KitchenTeamId))] public KitchenTeam KitchenTeam { get; set; } = null!;
    public int KitchenStaffMemberId { get; set; }
    [ForeignKey(nameof(KitchenStaffMemberId))] public KitchenStaffMember KitchenStaffMember { get; set; } = null!;
}

public class KitchenTask
{
    public int Id { get; set; }
    public int KitchenScheduleId { get; set; }
    [ForeignKey(nameof(KitchenScheduleId))] public KitchenSchedule KitchenSchedule { get; set; } = null!;
    public string AssignedToUserId { get; set; } = "";
    [Required, StringLength(300)] public string TaskDescription { get; set; } = "";
    [StringLength(10)] public string StartTime { get; set; } = "";
    [StringLength(10)] public string EndTime { get; set; } = "";
    public int PortionsRequired { get; set; }
    public KitchenTaskStatus Status { get; set; } = KitchenTaskStatus.Assigned;
    // Recorded when the task is started (In Progress) and finished (Done) → actual prep time per serving
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    [StringLength(300)] public string? DelayReason { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC8 — MEAL ATTENDANCE TRACKING
// ─────────────────────────────────────────────────────────────────────────────

public enum MealAttendanceStatus { Present, Absent, Late, Unauthorised }

public class MealAttendance
{
    public int Id { get; set; }
    public int MealPlanItemId { get; set; }
    [ForeignKey(nameof(MealPlanItemId))] public MealPlanItem MealPlanItem { get; set; } = null!;
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public MealAttendanceStatus Status { get; set; }
    public DateTime? ScannedAt { get; set; }
    public string? ScannedByUserId { get; set; }
    public bool IsManualOverride { get; set; }
    [StringLength(300)] public string? OverrideReason { get; set; }
}

public class MealAbsenceAlert
{
    public int Id { get; set; }
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public int ConsecutiveMissedMeals { get; set; }
    public DateTime AlertSentAt { get; set; } = DateTime.UtcNow;
    public int? AlertedHousemasterId { get; set; }
    [ForeignKey(nameof(AlertedHousemasterId))] public Housemaster? AlertedHousemaster { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC9 — MEAL QUALITY & FEEDBACK
// ─────────────────────────────────────────────────────────────────────────────

public enum PortionFeedback { TooLittle, JustRight, TooMuch }

public class MealFeedback
{
    public int Id { get; set; }
    public int MealPlanItemId { get; set; }
    [ForeignKey(nameof(MealPlanItemId))] public MealPlanItem MealPlanItem { get; set; } = null!;
    [Range(1, 5)] public int Rating { get; set; }
    [StringLength(500)] public string? Comment { get; set; }
    public PortionFeedback? Portion { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    // No LearnerId or UserId — fully anonymous
}

public class MealQualityAlert
{
    public int Id { get; set; }
    public int MealPlanItemId { get; set; }
    [ForeignKey(nameof(MealPlanItemId))] public MealPlanItem MealPlanItem { get; set; } = null!;
    public decimal AverageRating { get; set; }
    public int SubmissionCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByUserId { get; set; }
    [StringLength(500)] public string? ResolutionNotes { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC10 — MEAL COMPLIANCE REPORT (ARCHIVE)
// ─────────────────────────────────────────────────────────────────────────────

public class MealComplianceReportRecord
{
    public int Id { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string GeneratedByUserId { get; set; } = "";
    [StringLength(300)] public string PdfPath { get; set; } = "";
}
