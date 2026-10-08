using DlangezwaHS.Web.Models.Domain;
using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// ADMIN — REGISTER HOUSEMASTER / KITCHEN STAFF (Increment 3)
// ─────────────────────────────────────────────────────────────────────────────

public class RegisterHousemasterViewModel
{
    public int?   Id        { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName  { get; set; } = "";
    [Required, EmailAddress]      public string Email     { get; set; } = "";
    [Phone]                       public string? Phone    { get; set; }
}

public class RegisterKitchenStaffViewModel
{
    public int?   Id        { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName  { get; set; } = "";
    [Required, EmailAddress]      public string Email     { get; set; } = "";
    [Phone]                       public string? Phone    { get; set; }
}

public class HousemasterSummaryRow
{
    public int    Id       { get; set; }
    public string FullName { get; set; } = "";
    public string Email    { get; set; } = "";
    public string Phone    { get; set; } = "";
    public bool   HasLogin { get; set; }
    public bool   IsActive { get; set; }
}

public class KitchenStaffSummaryRow
{
    public int    Id       { get; set; }
    public string FullName { get; set; } = "";
    public string Email    { get; set; } = "";
    public string Phone    { get; set; } = "";
    public bool   HasLogin { get; set; }
    public bool   IsActive { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC1 / UC2 — BOARDING CHECK-IN/OUT & LEAVE REQUESTS
// ─────────────────────────────────────────────────────────────────────────────

public class BoardingScanResult
{
    public bool   IsValid    { get; set; }
    public string LearnerName { get; set; } = "";
    public string Status     { get; set; } = ""; // OnTime / Late / Rejected
    public string Message    { get; set; } = "";
}

public class BoardingLearnerStatusRow
{
    public int      LearnerId       { get; set; }
    public string   LearnerName     { get; set; } = "";
    public DateTime? Since          { get; set; }
    public bool      IsLate         { get; set; }
    public int       LateMinutes    { get; set; }
    public string?   Destination    { get; set; }
}

public class BoardingDashboardViewModel
{
    public IList<BoardingLearnerStatusRow> CurrentlyOut { get; set; } = new List<BoardingLearnerStatusRow>();
    public int TotalBoarders     { get; set; }
    public int PendingLeaveRequests { get; set; }
    public int LateTodayCount    { get; set; }
    public IList<MealAbsenceAlertRow> MealAbsenceAlerts { get; set; } = new List<MealAbsenceAlertRow>();
}

// Placeholder row shape used by the Boarding dashboard until UC8 (Stage 5) populates it
public class MealAbsenceAlertRow
{
    public int    Id                     { get; set; }
    public int    LearnerId              { get; set; }
    public string LearnerName            { get; set; } = "";
    public int    ConsecutiveMissedMeals { get; set; }
    public DateTime AlertSentAt          { get; set; }
}

public class MovementLogRow
{
    public string LearnerName { get; set; } = "";
    public string MovementType { get; set; } = "";
    public DateTime ScannedAt { get; set; }
    public string Status { get; set; } = "";
    public string? Destination { get; set; }
    public string? Purpose { get; set; }
}

public class LeaveRequestRow
{
    public int      Id                 { get; set; }
    public int      LearnerId          { get; set; }
    public string   LearnerName        { get; set; } = "";
    public string   ParentName         { get; set; } = "";
    public string   Destination        { get; set; } = "";
    public string   Purpose            { get; set; } = "";
    public DateTime DepartureDate      { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public string   Status             { get; set; } = "";
    public DateTime CreatedAt          { get; set; }
}

public class LeaveRequestCreateViewModel
{
    [Required] public int    LearnerId   { get; set; }
    public string LearnerName { get; set; } = "";
    [Required, StringLength(200)] public string Destination { get; set; } = "";
    [Required, StringLength(300)] public string Purpose     { get; set; } = "";
    [Required] public DateTime DepartureDate      { get; set; } = DateTime.Today.AddDays(1);
    [Required] public DateTime ExpectedReturnDate { get; set; } = DateTime.Today.AddDays(2);
}

public class ParentLeaveRequestRow
{
    public int      Id                 { get; set; }
    public string   Destination        { get; set; } = "";
    public string   Purpose            { get; set; } = "";
    public DateTime DepartureDate      { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public string   Status             { get; set; } = "";
    public string?  HousemasterNotes   { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC3 — DIETARY PROFILES & ALLERGIES
// ─────────────────────────────────────────────────────────────────────────────

public class DietaryItemInput
{
    [Required] public DietaryCategory Category { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public AllergySeverity? Severity { get; set; }
    [StringLength(300)] public string? Notes { get; set; }
}

public class DietaryProfileSubmitViewModel
{
    [Required] public int LearnerId { get; set; }
    public string LearnerName { get; set; } = "";
    public List<DietaryItemInput> Items { get; set; } = new() { new DietaryItemInput() };
    // Optional supporting documents (doctor's letter, test results)
    public List<IFormFile> Documents { get; set; } = new();
}

public class DietaryDocumentRow
{
    public int    Id            { get; set; }
    public string FileName      { get; set; } = "";
    public long   FileSizeBytes { get; set; }
}

public class DietaryItemRow
{
    public string Category { get; set; } = "";
    public string Name     { get; set; } = "";
    public string? Severity { get; set; }
    public string? Notes    { get; set; }
}

public class DietaryProfileRow
{
    public int      Id              { get; set; }
    public int      LearnerId       { get; set; }
    public string   LearnerName     { get; set; } = "";
    public string   ParentName      { get; set; } = "";
    public bool     SubmittedByLearner { get; set; }
    public string?  RejectionReason { get; set; }
    public DateTime SubmittedAt     { get; set; }
    public string   Status          { get; set; } = "";
    public bool     HasSevereAllergy { get; set; }
    public IList<DietaryItemRow> Items { get; set; } = new List<DietaryItemRow>();
    public IList<DietaryDocumentRow> Documents { get; set; } = new List<DietaryDocumentRow>();
}

public class ParentDietaryProfileViewModel
{
    public int    LearnerId   { get; set; }
    public string LearnerName { get; set; } = "";
    public DietaryProfileRow? ActiveProfile { get; set; }
    public IList<DietaryProfileRow> History { get; set; } = new List<DietaryProfileRow>();
}

// ─────────────────────────────────────────────────────────────────────────────
// UC4 — WEEKLY MEAL PLAN
// ─────────────────────────────────────────────────────────────────────────────

public class MealPlanSummaryRow
{
    public int      Id             { get; set; }
    public DateTime WeekStartDate  { get; set; }
    public string   Status         { get; set; } = "";
    public decimal  TotalBudget    { get; set; }
    public decimal  EstimatedCost  { get; set; }
    public int      ItemCount      { get; set; }
    // "This week" / "Next week" / "Upcoming" / "Past" relative to the school's date
    public string   WeekRelation   { get; set; } = "";
    public bool     IsPast         => WeekRelation == "Past";
    // Other plans covering some of the same days (only possible for data saved before the overlap check)
    public IList<int> DuplicateOfPlanIds { get; set; } = new List<int>();
}

public class MealPlanWeekChoice
{
    public DateTime Monday         { get; set; }
    public string   Label          { get; set; } = "";
    public int?     ExistingPlanId { get; set; }
}

public class MealPlanItemInput
{
    public int? Id { get; set; }          // existing MealPlanItem id (null = new option)
    public int DayOfWeek { get; set; }
    public MealType MealType { get; set; }
    public int? MealId { get; set; }      // library meal
    public bool Locked { get; set; }      // has orders/attendance — can't be removed (display only)
    // Only used for legacy items that were typed in before the meal library existed
    [StringLength(200)] public string MenuDescription { get; set; } = "";
    [StringLength(500)] public string Ingredients { get; set; } = "";
}

public class MealPlanCreateViewModel
{
    public int?     Id            { get; set; }
    [Required] public DateTime WeekStartDate { get; set; } = DateTime.Today;
    public decimal  TotalBudget   { get; set; }
    public bool     IsPublished   { get; set; }
    // Learners per meal used for the weekly cost estimate (from settings)
    public int      ExpectedDiners { get; set; }
    public List<MealPlanItemInput> Items { get; set; } = new();

    // One plan per Monday–Sunday week, from this week up to 8 weeks ahead
    public IList<MealPlanWeekChoice> WeekChoices { get; set; } = new List<MealPlanWeekChoice>();
    // Set on a new plan when the chosen week already has one (the editor opens that plan instead)
    public int?     ExistingPlanIdForWeek { get; set; }
    // Days before this (1 = Monday … 8 = whole week) have passed and are locked
    public int      FirstEditableDay { get; set; } = 1;
    public bool     IsReadOnly => FirstEditableDay > 7;

    // Picker data
    public IList<MealOption> MealOptions { get; set; } = new List<MealOption>();
    public Dictionary<MealType, string> ServingTimes { get; set; } = new();
    public IList<IngredientOption> IngredientOptions { get; set; } = new List<IngredientOption>();
}

// ── Meal library ────────────────────────────────────────────────────────────

public class MealOption
{
    public int     Id              { get; set; }
    public string  Name            { get; set; } = "";
    public string  MealType        { get; set; } = "";
    public string? Description     { get; set; }
    public string? ImageUrl        { get; set; }
    public decimal CostPerServing  { get; set; }
    public decimal PrepMinutesPerServing { get; set; }
    public IList<string> IngredientLines { get; set; } = new List<string>(); // "Onions — 0.05 Kg"
    public bool    IsActive        { get; set; } = true;
}

public class IngredientOption
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
}

public class MealIngredientInput
{
    public int     IngredientId       { get; set; }
    public decimal QuantityPerServing { get; set; }
}

public class MealEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    [Required] public MealType MealType { get; set; }
    [Range(0.1, 60, ErrorMessage = "Prep time must be between 0.1 and 60 minutes per serving.")]
    public decimal PrepMinutesPerServing { get; set; } = 1m;
    public IFormFile? Image { get; set; }
    public string? ExistingImageUrl { get; set; }
    public List<MealIngredientInput> Ingredients { get; set; } = new();
    public IList<IngredientOption> IngredientOptions { get; set; } = new List<IngredientOption>();
}

public class MealLibraryViewModel
{
    public IList<MealOption> Meals { get; set; } = new List<MealOption>();
    public MealEditViewModel NewMeal { get; set; } = new();
}

public class MealConflictRow
{
    public int    MealPlanItemId   { get; set; }
    public string DayLabel         { get; set; } = "";
    public string MealType         { get; set; } = "";
    public string MenuDescription  { get; set; } = "";
    public string AllergenMatched  { get; set; } = "";
    public string Severity         { get; set; } = "";
    public bool   IsSevere         { get; set; }
    public IList<string> AffectedLearners { get; set; } = new List<string>();
    // Learners whose allergy to this item is Severe/Anaphylactic (they can't pre-order it)
    public IList<string> SevereLearners   { get; set; } = new List<string>();
}

public class MealPlanPublishViewModel
{
    public int      MealPlanId    { get; set; }
    public DateTime WeekStartDate { get; set; }
    public decimal  TotalBudget   { get; set; }
    public decimal  EstimatedCost { get; set; }
    public int      ExpectedDiners { get; set; }
    public IList<MealConflictRow> Conflicts { get; set; } = new List<MealConflictRow>();
    public IList<string> MissingServings { get; set; } = new List<string>();
    public bool     IsPublished   { get; set; }
    public bool     IsOverBudget  => EstimatedCost > TotalBudget;
    public bool     CanPublish    => !IsPublished && !IsOverBudget && MissingServings.Count == 0;
}

public class ParentMealPlanDayViewModel
{
    public int DayOfWeek { get; set; }
    public DateTime Date { get; set; }
    public string DayLabel { get; set; } = "";
    public IList<MealPlanItem> Meals { get; set; } = new List<MealPlanItem>();
}

public class ParentMealPlanWeekViewModel
{
    public int      WeekOffset      { get; set; }   // 0 = this week, 1 = next week
    public DateTime WeekStartDate   { get; set; }
    public bool     HasPlan         { get; set; }
    public bool     NextWeekPublished { get; set; }
    public DateTime Today           { get; set; }
    public IList<ParentMealPlanDayViewModel> Days { get; set; } = new List<ParentMealPlanDayViewModel>();
    // Meals served in the last 24 hours — the only ones that can be rated
    public ISet<int> RateableItemIds { get; set; } = new HashSet<int>();
}

// ─────────────────────────────────────────────────────────────────────────────
// UC5 — MEAL PRE-ORDERING
// ─────────────────────────────────────────────────────────────────────────────

public class PreOrderMealRow
{
    public int    MealPlanItemId  { get; set; }
    public DateTime ServingDate   { get; set; }
    public string DayLabel        { get; set; } = "";
    public string MealType        { get; set; } = "";
    public string MenuDescription { get; set; } = "";
    public string? Description    { get; set; }
    public string? ImageUrl       { get; set; }
    public IList<string> IngredientNames { get; set; } = new List<string>();
    public string ServingTime     { get; set; } = "";
    public DateTime OrderDeadline { get; set; }
    public int    OptionsInSlot   { get; set; } = 1;
    public string? CurrentOrderStatus  { get; set; }
    public string? CurrentPortionSize  { get; set; }
    public bool    OrderedByLearner    { get; set; }
}

public class PreOrderWeekViewModel
{
    public int    LearnerId   { get; set; }
    public string LearnerName { get; set; } = "";
    // Only meals that can still be ordered (deadline not passed, no allergy conflict)
    public IList<PreOrderMealRow> Meals { get; set; } = new List<PreOrderMealRow>();
    public int    ClosedCount { get; set; }
    public IList<string> HiddenForAllergy { get; set; } = new List<string>();
}

public class PreOrderSummaryRow
{
    public int    MealPlanItemId   { get; set; }
    public string DayLabel         { get; set; } = "";
    public DateTime Date           { get; set; }
    public string ServingTime      { get; set; } = "";
    public string MealType         { get; set; } = "";
    public string MenuDescription  { get; set; } = "";
    public int    ConfirmedCount   { get; set; }
    public int    WaitListedCount  { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC6 — INVENTORY & INGREDIENT MANAGEMENT
// ─────────────────────────────────────────────────────────────────────────────

public class IngredientRow
{
    public int     Id            { get; set; }
    public string  Name          { get; set; } = "";
    public string  Unit          { get; set; } = "";
    public decimal CurrentStock  { get; set; }
    public decimal MinimumStock  { get; set; }
    public decimal UnitCost      { get; set; }
    public bool    IsLowStock    { get; set; }
}

public class IngredientCreateViewModel
{
    public int?    Id           { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required] public StockUnit Unit { get; set; }
    public decimal MinimumStock { get; set; }
}

public class InventoryViewModel
{
    public IList<IngredientRow>          Ingredients  { get; set; } = new List<IngredientRow>();
    public IList<StockReceiptRow>        Receipts     { get; set; } = new List<StockReceiptRow>();
    public IList<PurchaseRequisitionRow> Requisitions { get; set; } = new List<PurchaseRequisitionRow>();
    public IList<StockUsageRow>          RecentUsage  { get; set; } = new List<StockUsageRow>();
}

public class StockReceiptLineInput
{
    public int     IngredientId { get; set; }
    public decimal Quantity     { get; set; }
    public decimal UnitCost     { get; set; }
}

public class StockReceiptCreateViewModel
{
    public int? SupplierId { get; set; }
    [StringLength(150), Display(Name = "Supplier")] public string SupplierName { get; set; } = "";
    [StringLength(60), Display(Name = "Invoice / receipt number")] public string? InvoiceNumber { get; set; }
    [Required, DataType(DataType.Date)] public DateTime ReceiptDate { get; set; } = DateTime.Today;
    [StringLength(500)] public string? Notes { get; set; }
    public IFormFile? Document { get; set; }
    public List<StockReceiptLineInput> Lines { get; set; } = new() { new StockReceiptLineInput() };
    public IList<IngredientOption> IngredientOptions { get; set; } = new List<IngredientOption>();
    public IList<SupplierOption> Suppliers { get; set; } = new List<SupplierOption>();
}

public class StockReceiptRow
{
    public int      Id            { get; set; }
    public string   SupplierName  { get; set; } = "";
    public string?  InvoiceNumber { get; set; }
    public DateTime ReceiptDate   { get; set; }
    public decimal  TotalAmount   { get; set; }
    public int      LineCount     { get; set; }
}

public class StockReceiptDetailViewModel
{
    public int      Id            { get; set; }
    public string   SupplierName  { get; set; } = "";
    public string?  InvoiceNumber { get; set; }
    public DateTime ReceiptDate   { get; set; }
    public string?  Notes         { get; set; }
    public decimal  TotalAmount   { get; set; }
    public string   DocumentUrl   { get; set; } = "";
    public bool     DocumentIsPdf { get; set; }
    public string   CapturedBy    { get; set; } = "";
    public DateTime CapturedAt    { get; set; }
    public IList<StockReceiptLineRow> Lines { get; set; } = new List<StockReceiptLineRow>();
}

public class StockReceiptLineRow
{
    public string  IngredientName { get; set; } = "";
    public string  Unit           { get; set; } = "";
    public decimal Quantity       { get; set; }
    public decimal UnitCost       { get; set; }
    public decimal LineTotal => Quantity * UnitCost;
}

public class RecordUsageViewModel
{
    public int? MealPlanItemId { get; set; }
    public UsageMealOption? Selected { get; set; }
    public IList<RecipeLineRow> Recipe { get; set; } = new List<RecipeLineRow>();
    public IList<UsageMealOption> Meals { get; set; } = new List<UsageMealOption>();
    public IList<UsageRecordRow> History { get; set; } = new List<UsageRecordRow>();
}

public class UsageMealOption
{
    public int      MealPlanItemId  { get; set; }
    public DateTime Date            { get; set; }
    public string   MealType        { get; set; } = "";
    public string   MenuDescription { get; set; } = "";
    public string?  ImageUrl        { get; set; }
    // Prepared = confirmed pre-orders
    public int      ConfirmedOrders { get; set; }
    public int      Collected       { get; set; }
    public bool     IsRecorded      { get; set; }
    public int?     Leftovers       { get; set; }
}

public class RecipeLineRow
{
    public string  IngredientName     { get; set; } = "";
    public string  Unit               { get; set; } = "";
    public decimal QuantityPerServing { get; set; }
    public decimal InStock            { get; set; }
    public decimal Deducted           { get; set; }
}

public class UsageRecordRow
{
    public int      Id               { get; set; }
    public DateTime RecordedAt       { get; set; }
    public DateTime MealDate         { get; set; }
    public string   MealType         { get; set; } = "";
    public string   MenuDescription  { get; set; } = "";
    public int      ServingsPrepared { get; set; }
    public int?     LeftoverServings { get; set; }
    public int      Collected        { get; set; }
    public string   Deducted         { get; set; } = "";
}

public class ServiceUsageResult
{
    public int Prepared  { get; set; }
    public int Leftovers { get; set; }
    public List<string> Shortages { get; set; } = new();
}

public class SupplierOption
{
    public int     Id          { get; set; }
    public string  Name        { get; set; } = "";
    public string? ContactName { get; set; }
    public string? Phone       { get; set; }
    public IList<SupplierItemOption> Items { get; set; } = new List<SupplierItemOption>();
}

public class SupplierItemOption
{
    public int     IngredientId    { get; set; }
    public string  Name            { get; set; } = "";
    public string  Unit            { get; set; } = "";
    public decimal UnitPrice       { get; set; }
    public decimal DefaultQuantity { get; set; }
}

public class StockUsageRow
{
    public DateTime RecordedAt      { get; set; }
    public int?     MealPlanItemId  { get; set; }
    public string   MealType        { get; set; } = "";
    public string   IngredientName  { get; set; } = "";
    public string   Unit            { get; set; } = "";
    public decimal  QuantityUsed    { get; set; }
    public string   MealDescription { get; set; } = "";
}

public class PurchaseRequisitionRow
{
    public int      Id          { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string   Status      { get; set; } = "";
    public int      ItemCount   { get; set; }
}

public class PurchaseRequisitionItemRow
{
    public string  IngredientName    { get; set; } = "";
    public string  Unit              { get; set; } = "";
    public decimal QuantityRequired  { get; set; }
    public decimal QuantityInStock   { get; set; }
    public decimal QuantityToOrder   { get; set; }
}

public class PurchaseRequisitionDetailViewModel
{
    public int      Id          { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string   Status      { get; set; } = "";
    public IList<PurchaseRequisitionItemRow> Items { get; set; } = new List<PurchaseRequisitionItemRow>();
}

public class BoardingSettingsViewModel
{
    public decimal WeeklyMealBudget    { get; set; }
    public int     MaxPreOrdersPerMeal { get; set; }
    public int     ExpectedDinersPerMeal { get; set; } = 40;
    public string  BreakfastStart { get; set; } = "05:00";
    public string  BreakfastServe { get; set; } = "07:00";
    public string  LunchStart     { get; set; } = "10:00";
    public string  LunchServe     { get; set; } = "13:00";
    public string  DinnerStart    { get; set; } = "15:00";
    public string  DinnerServe    { get; set; } = "18:00";
}

// ─────────────────────────────────────────────────────────────────────────────
// UC7 — KITCHEN SCHEDULE
// ─────────────────────────────────────────────────────────────────────────────

public class StaffOption
{
    public string Id   { get; set; } = "";
    public string Name { get; set; } = "";
}

public class KitchenTaskInput
{
    public string AssignedToUserId { get; set; } = "";
    [StringLength(300)] public string TaskDescription { get; set; } = "";
    public bool Include { get; set; } = true;
}

public class KitchenTeamOption
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
    public IList<StaffOption> Members { get; set; } = new List<StaffOption>();
}

public class KitchenScheduleCreateViewModel
{
    [Required] public int MealPlanItemId { get; set; }
    public string MenuDescription { get; set; } = "";
    public DateTime ScheduledDate { get; set; }
    public string MealType { get; set; } = "";
    public int ConfirmedPortions { get; set; }
    // Pre-determined shift (from Settings → Boarding) — not entered per task
    public string ShiftStart { get; set; } = "";
    public string ShiftEnd   { get; set; } = "";
    // Staffing = portions × prep minutes per serving ÷ shift minutes (rounded up)
    public decimal PrepMinutesPerServing { get; set; }
    public int     ShiftMinutes          { get; set; }
    public decimal LabourMinutes         { get; set; }
    public int     StaffRecommended      { get; set; }
    // Admin confirms when scheduling fewer staff than recommended
    public bool    UnderstaffAcknowledged { get; set; }
    public int? KitchenTeamId { get; set; }
    public List<KitchenTaskInput> Tasks { get; set; } = new();
    public IList<KitchenTeamOption> Teams { get; set; } = new List<KitchenTeamOption>();
    public IList<KitchenStaffOption> AllStaff { get; set; } = new List<KitchenStaffOption>();
}

// Kitchen staff profile option (Id = KitchenStaffMember.Id), used to build teams
public class KitchenStaffOption
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}

public class SchedulableMealRow
{
    public int      MealPlanItemId  { get; set; }
    public DateTime Date            { get; set; }
    public string   MealType        { get; set; } = "";
    public string   MenuDescription { get; set; } = "";
    public bool     AlreadyScheduled { get; set; }
}

public class KitchenTeamRow
{
    public int    Id       { get; set; }
    public string Name     { get; set; } = "";
    public bool   IsActive { get; set; }
    public int?   HeadChefId   { get; set; }
    public string? HeadChefName { get; set; }
    public IList<string> MemberNames { get; set; } = new List<string>();
    public IList<int>    MemberIds   { get; set; } = new List<int>();
}

public class KitchenTeamsViewModel
{
    public IList<KitchenTeamRow> Teams { get; set; } = new List<KitchenTeamRow>();
    public IList<KitchenStaffOption> AllStaff { get; set; } = new List<KitchenStaffOption>();
    // Set when a head chef (not an admin) is managing teams: they can only edit teams they lead
    public int? HeadChefStaffId { get; set; }
    public bool IsAdmin => HeadChefStaffId is null;
}

public class KitchenPlannerViewModel
{
    public DateTime Today { get; set; }
    public IList<KitchenPlannerDay> Days { get; set; } = new List<KitchenPlannerDay>();
    public IList<KitchenTeamOption> Teams { get; set; } = new List<KitchenTeamOption>();
}

public class KitchenPlannerDay
{
    public DateTime Date { get; set; }
    public IList<KitchenPlannerService> Services { get; set; } = new List<KitchenPlannerService>();
}

public class KitchenPlannerService
{
    public string MealType   { get; set; } = "";
    public string ShiftStart { get; set; } = "";
    public string ShiftEnd   { get; set; } = "";
    public IList<KitchenPlannerMeal> Meals { get; set; } = new List<KitchenPlannerMeal>();
}

public class KitchenPlannerMeal
{
    public int     MealPlanItemId        { get; set; }
    public string  MenuDescription       { get; set; } = "";
    public string? ImageUrl              { get; set; }
    public int     Portions              { get; set; }
    public decimal PrepMinutesPerServing { get; set; }
    public int     StaffRecommended      { get; set; }
    public IList<KitchenPlannerSchedule> Scheduled { get; set; } = new List<KitchenPlannerSchedule>();
}

public class KitchenPlannerSchedule
{
    public int    ScheduleId       { get; set; }
    public string TeamName         { get; set; } = "";
    public int    StaffCount       { get; set; }
    public int    StaffRecommended { get; set; }
    public string Shift            { get; set; } = "";
    public int    EstimatedMinutes { get; set; }
    public bool   IsPublished      { get; set; }
    public string Status           { get; set; } = "";
}

public class QuickScheduleResult
{
    public bool    Success      { get; set; }
    public bool    NeedsConfirm { get; set; }
    public string  Message      { get; set; } = "";
    public KitchenPlannerSchedule? Schedule { get; set; }
}

public class KitchenScheduleRow
{
    public int      Id             { get; set; }
    public DateTime ScheduledDate  { get; set; }
    public string   MealType       { get; set; } = "";
    public string   MenuDescription { get; set; } = "";
    public string?  TeamName       { get; set; }
    public string   Shift          { get; set; } = "";
    public string   Status         { get; set; } = "";
    public bool     IsPublished    { get; set; }
    public int      TaskCount      { get; set; }
    public int      TasksDone      { get; set; }
    public int      TasksStarted   { get; set; }
    public int      PortionsPlanned  { get; set; }
    public int      StaffRecommended { get; set; }
    public DateTime? StartedAt     { get; set; }
    public DateTime? CompletedAt   { get; set; }
    public string?  DelayReason    { get; set; }
    // Actual minutes per serving once every task is done
    public double? MinutesPerServing => StartedAt is not null && CompletedAt is not null && PortionsPlanned > 0
        ? (CompletedAt.Value - StartedAt.Value).TotalMinutes / PortionsPlanned : null;
}

public class KitchenTaskRow
{
    public int      Id              { get; set; }
    public string   MenuDescription { get; set; } = "";
    public DateTime ScheduledDate   { get; set; }
    public string   MealType        { get; set; } = "";
    public string?  TeamName        { get; set; }
    public string   TaskDescription { get; set; } = "";
    public string   StartTime       { get; set; } = "";
    public string   EndTime         { get; set; } = "";
    public string   Status          { get; set; } = "";
    public string?  DelayReason     { get; set; }
    public int      Portions        { get; set; }
    public DateTime? StartedAt      { get; set; }
    public DateTime? CompletedAt    { get; set; }
    public double?  MinutesTaken    => StartedAt is not null && CompletedAt is not null ? (CompletedAt.Value - StartedAt.Value).TotalMinutes : null;
    public double?  MinutesPerServing => MinutesTaken is double m && Portions > 0 ? m / Portions : null;
}

// ─────────────────────────────────────────────────────────────────────────────
// UC8 — MEAL ATTENDANCE TRACKING
// ─────────────────────────────────────────────────────────────────────────────

// The scanner works on a whole meal service (e.g. Monday Breakfast), which can
// contain several options. Each scan shows the option the learner pre-ordered.
public class MealAttendanceLiveViewModel
{
    public int    MealPlanItemId  { get; set; }
    public string MenuDescription { get; set; } = "";
    public string MealType        { get; set; } = "";
    public string DayLabel        { get; set; } = "";
    public int    TotalExpected   { get; set; }
    public int    ScannedCount    { get; set; }
    public IList<string> NotYetArrived { get; set; } = new List<string>();
    public int    FlaggedCount    { get; set; }
    public IList<MealServiceOptionRow> Options { get; set; } = new List<MealServiceOptionRow>();
    public bool   IsFinalised     { get; set; }
}

public class MealServiceOptionRow
{
    public int    MealPlanItemId  { get; set; }
    public string MenuDescription { get; set; } = "";
    public string? ImageUrl       { get; set; }
    public int    Ordered         { get; set; }
    public int    Served          { get; set; }
}

public class MealScanResult
{
    public bool    Success      { get; set; }
    public string  Message      { get; set; } = "";
    public string  LearnerName  { get; set; } = "";
    public bool    HasOrder     { get; set; }
    public string? OrderedMeal  { get; set; }
    public string? PortionSize  { get; set; }
    public string? ImageUrl     { get; set; }
    public IList<string> Allergies { get; set; } = new List<string>();
}

public class TodayMealOption
{
    public int    MealPlanItemId  { get; set; }   // first item in the service — used as the scanner key
    public string MealType        { get; set; } = "";
    public string MenuDescription { get; set; } = "";   // options joined with " / "
    public string ServingTime     { get; set; } = "";
    public int    OptionCount     { get; set; } = 1;
    public int    PreOrderCount   { get; set; }
}

public class MealAttendanceHistoryRow
{
    public string   LearnerName     { get; set; } = "";
    public string   MenuDescription { get; set; } = "";
    public string   MealType        { get; set; } = "";
    public string   Status          { get; set; } = "";
    public DateTime? ScannedAt      { get; set; }
    public bool     IsManualOverride { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC9 — MEAL QUALITY & FEEDBACK
// ─────────────────────────────────────────────────────────────────────────────

public class MealFeedbackSubmitViewModel
{
    [Required] public int MealPlanItemId { get; set; }
    public string MenuDescription { get; set; } = "";
    public string MealType { get; set; } = "";
    [Required, Range(1, 5)] public int Rating { get; set; }
    [StringLength(500)] public string? Comment { get; set; }
    public PortionFeedback? Portion { get; set; }
}

public class MealFeedbackMealRow
{
    public int DayOfWeek { get; set; }
    public int     MealPlanItemId  { get; set; }
    public string  DayLabel        { get; set; } = "";
    public string  MealType        { get; set; } = "";
    public string  MenuDescription { get; set; } = "";
    public decimal AverageRating   { get; set; }
    public int     SubmissionCount { get; set; }
    public bool    HasActiveAlert  { get; set; }
    // Portion opinions
    public int     TooLittle       { get; set; }
    public int     JustRight       { get; set; }
    public int     TooMuch         { get; set; }
    // Meal quantity — ordered vs prepared vs collected vs leftovers
    public int     Ordered         { get; set; }
    public int     Prepared        { get; set; }
    public int     Collected       { get; set; }
    public int?    Leftovers       { get; set; }
}

public class MealFeedbackCommentRow
{
    public DateTime SubmittedAt     { get; set; }
    public string   MealLabel       { get; set; } = "";
    public int      Rating          { get; set; }
    public string   Comment         { get; set; } = "";
    public string?  Portion         { get; set; }
}

public class MealPlanWeekOption
{
    public int      Id            { get; set; }
    public DateTime WeekStartDate { get; set; }
}

public class MealQualityAlertRow
{
    public int      Id              { get; set; }
    public string   MenuDescription { get; set; } = "";
    public decimal  AverageRating   { get; set; }
    public int      SubmissionCount { get; set; }
    public DateTime CreatedAt       { get; set; }
    public bool     Resolved        { get; set; }
    public DateTime? ResolvedAt     { get; set; }
    public string?  ResolutionNotes { get; set; }
}

public class WeeklyTrendPoint
{
    public string  WeekLabel     { get; set; } = "";
    public decimal AverageRating { get; set; }
}

public class MealFeedbackDashboardViewModel
{
    public int? SelectedPlanId { get; set; }
    public DateTime? WeekStartDate { get; set; }
    public IList<MealPlanWeekOption> Weeks { get; set; } = new List<MealPlanWeekOption>();
    public IList<MealFeedbackMealRow> Meals { get; set; } = new List<MealFeedbackMealRow>();
    public IList<MealFeedbackCommentRow> RecentComments { get; set; } = new List<MealFeedbackCommentRow>();
    public IList<MealQualityAlertRow> Alerts { get; set; } = new List<MealQualityAlertRow>();
    public IList<WeeklyTrendPoint> WeeklyTrend { get; set; } = new List<WeeklyTrendPoint>();
    // Week totals
    public decimal WeekAverage    { get; set; }
    public int     WeekSubmissions { get; set; }
    public int     WeekPrepared   { get; set; }
    public int     WeekCollected  { get; set; }
    public int     WeekLeftovers  { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC10 — MEAL COMPLIANCE REPORT
// ─────────────────────────────────────────────────────────────────────────────

public class MealTypeAttendanceRow
{
    public string  MealType       { get; set; } = "";
    public int     TotalExpected  { get; set; }
    public int     TotalPresent   { get; set; }
    public decimal AttendancePct  { get; set; }
}

public class PreOrderVsActualRow
{
    public DateTime Date            { get; set; }
    public string   MealType        { get; set; } = "";
    public string   MenuDescription { get; set; } = "";
    public int      PreOrdered      { get; set; }
    public int      Attended        { get; set; }
    public int      Variance        { get; set; }
}

public class MealTypeSatisfactionRow
{
    public string  MealType      { get; set; } = "";
    public decimal AverageRating { get; set; }
    public int      Complaints   { get; set; }
}

public class ArchivedReportRow
{
    public int      Id            { get; set; }
    public DateTime FromDate      { get; set; }
    public DateTime ToDate        { get; set; }
    public DateTime GeneratedAt   { get; set; }
    public string   GeneratedBy   { get; set; } = "";
    public string   PdfUrl        { get; set; } = "";
}

public class MealComplianceReportDto
{
    public DateTime From { get; set; }
    public DateTime To   { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string   GeneratedByName { get; set; } = "";

    public IList<MealTypeAttendanceRow> AttendanceByMealType { get; set; } = new List<MealTypeAttendanceRow>();
    public IList<PreOrderVsActualRow>   PreOrderVsActual     { get; set; } = new List<PreOrderVsActualRow>();
    public IList<string>                DietaryConflictsServed { get; set; } = new List<string>();

    public int PortionsPrepared { get; set; }
    public int PortionsServed   { get; set; }
    public int WasteEstimate    { get; set; }

    public decimal EstimatedCost { get; set; }
    public decimal ActualSpend   { get; set; }

    public IList<MealTypeSatisfactionRow> SatisfactionByMealType { get; set; } = new List<MealTypeSatisfactionRow>();

    public IList<string> AnomalyFlags { get; set; } = new List<string>();

    public IList<ArchivedReportRow> ArchivedReports { get; set; } = new List<ArchivedReportRow>();
}
