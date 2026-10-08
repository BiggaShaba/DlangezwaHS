using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize(Roles = "KitchenStaff")]
public class KitchenStaffController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IDietaryService _dietarySvc;
    private readonly IMealPlanService _mealPlanSvc;
    private readonly IMealLibraryService _mealLibrarySvc;
    private readonly IMealOrderService _mealOrderSvc;
    private readonly IInventoryService _inventorySvc;
    private readonly IKitchenScheduleService _kitchenScheduleSvc;
    private readonly IMealAttendanceService _attendanceSvc;
    private readonly IMealFeedbackService _feedbackSvc;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<KitchenStaffController> _logger;

    public KitchenStaffController(ApplicationDbContext db, IDietaryService dietarySvc,
        IMealPlanService mealPlanSvc, IMealLibraryService mealLibrarySvc, IMealOrderService mealOrderSvc, IInventoryService inventorySvc,
        IKitchenScheduleService kitchenScheduleSvc, IMealAttendanceService attendanceSvc,
        IMealFeedbackService feedbackSvc,
        UserManager<ApplicationUser> userManager, ILogger<KitchenStaffController> logger)
    {
        _db = db;
        _dietarySvc = dietarySvc;
        _mealPlanSvc = mealPlanSvc;
        _mealLibrarySvc = mealLibrarySvc;
        _mealOrderSvc = mealOrderSvc;
        _inventorySvc = inventorySvc;
        _kitchenScheduleSvc = kitchenScheduleSvc;
        _attendanceSvc = attendanceSvc;
        _feedbackSvc = feedbackSvc;
        _userManager = userManager;
        _logger = logger;
    }

    private string UserId => _userManager.GetUserId(User)!;

    // ── Dashboard ────────────────────────────────────────────────────────────

    public async Task<IActionResult> Dashboard()
    {
        var today = SchoolClock.Today;
        var user = await _userManager.GetUserAsync(User);
        var vm = new KitchenDashboardViewModel { FirstName = user?.FirstName ?? "", Today = today };

        // Meals being served today (published plans only) and the confirmed orders for them
        var plans = await _db.MealPlans.Include(p => p.Items)
            .Where(p => p.Status == MealPlanStatus.Published && p.WeekStartDate <= today && p.WeekStartDate >= today.AddDays(-6))
            .ToListAsync();
        var todayItemIds = plans
            .SelectMany(p => p.Items.Where(i => i.DayOfWeek == (today - p.WeekStartDate.Date).Days + 1))
            .Select(i => i.Id).ToList();
        vm.MealsToday = todayItemIds.Count;
        vm.OrdersToday = await _db.MealPreOrders.CountAsync(o => todayItemIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed);

        vm.TasksPending = await _db.KitchenTasks.CountAsync(t => t.AssignedToUserId == UserId
            && t.Status != KitchenTaskStatus.Done && t.KitchenSchedule.PublishedAt != null && t.KitchenSchedule.ScheduledDate >= today);
        vm.LowStockItems = await _db.Ingredients.CountAsync(i => i.CurrentStock < i.MinimumStock);
        vm.OpenQualityAlerts = await _db.MealQualityAlerts.CountAsync(a => a.ResolvedAt == null);

        // Most ordered meals (confirmed pre-orders, all weeks)
        vm.MostOrdered = await _db.MealPreOrders
            .Where(o => o.Status == PreOrderStatus.Confirmed)
            .GroupBy(o => o.MealPlanItem.MenuDescription)
            .Select(g => new MealOrderCount { Meal = g.Key, Orders = g.Count() })
            .OrderByDescending(x => x.Orders).ThenBy(x => x.Meal)
            .Take(6).ToListAsync();

        // Average rating per meal (anonymous feedback)
        vm.TopRated = await _db.MealFeedbacks
            .GroupBy(f => f.MealPlanItem.MenuDescription)
            .Select(g => new MealRatingAverage { Meal = g.Key, Average = Math.Round(g.Average(f => (double)f.Rating), 1), Ratings = g.Count() })
            .OrderByDescending(x => x.Average).ThenByDescending(x => x.Ratings)
            .Take(6).ToListAsync();
        vm.TotalRatings = await _db.MealFeedbacks.CountAsync();
        vm.OverallRating = vm.TotalRatings == 0 ? 0 : Math.Round(await _db.MealFeedbacks.AverageAsync(f => (double)f.Rating), 1);

        return View(vm);
    }

    // ── Dietary Profiles (UC3) — read-only; added by parents/learners without review ─

    public async Task<IActionResult> DietaryProfiles()
        => View(await _dietarySvc.GetActiveProfilesAsync());

    public async Task<IActionResult> DietaryProfileReview(int id)
    {
        var profile = await _dietarySvc.GetProfileDetailAsync(id);
        if (profile is null || profile.Status != nameof(DietaryProfileStatus.Active)) return NotFound();
        return View(profile);
    }

    public async Task<IActionResult> DietaryProfilesCsv(DateTime? from, DateTime? to)
    {
        var rows = (await _dietarySvc.GetActiveProfilesAsync())
            .Where(p => (from is null || p.SubmittedAt.AddHours(2).Date >= from.Value.Date)
                     && (to is null || p.SubmittedAt.AddHours(2).Date <= to.Value.Date))
            .ToList();
        var name = $"dietary-profiles-{from:yyyyMMdd}-{to:yyyyMMdd}.csv".Replace("--", "-all-");
        return File(DietaryService.ToCsv(rows), "text/csv", name);
    }

    public async Task<IActionResult> DietaryDocument(int id)
    {
        var doc = await _dietarySvc.GetDocumentAsync(id);
        if (doc is null) return NotFound();
        return PhysicalFile(doc.Value.path, doc.Value.contentType);
    }

    // ── Meal Library (UC4) ───────────────────────────────────────────────────

    public async Task<IActionResult> Meals()
    {
        return View(new MealLibraryViewModel
        {
            Meals = await _mealLibrarySvc.GetMealsAsync(includeInactive: true),
            NewMeal = new MealEditViewModel { IngredientOptions = await _mealLibrarySvc.GetIngredientOptionsAsync() }
        });
    }

    [HttpGet]
    public async Task<IActionResult> MealEdit(int id)
    {
        var vm = await _mealLibrarySvc.GetEditViewModelAsync(id);
        if (vm is null) return NotFound();
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMeal(MealEditViewModel vm)
    {
        var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            if (isAjax) return Json(new { success = false, message = errors });
            TempData["Error"] = errors;
            return vm.Id.HasValue ? RedirectToAction(nameof(MealEdit), new { id = vm.Id }) : RedirectToAction(nameof(Meals));
        }

        try
        {
            var meal = await _mealLibrarySvc.SaveMealAsync(vm);
            if (isAjax)
            {
                var option = (await _mealLibrarySvc.GetMealsAsync()).First(m => m.Id == meal.Id);
                return Json(new { success = true, meal = option });
            }
            TempData["Success"] = $"\"{meal.Name}\" saved to the meal library.";
            return RedirectToAction(nameof(Meals));
        }
        catch (InvalidOperationException ex)
        {
            if (isAjax) return Json(new { success = false, message = ex.Message });
            TempData["Error"] = ex.Message;
            return vm.Id.HasValue ? RedirectToAction(nameof(MealEdit), new { id = vm.Id }) : RedirectToAction(nameof(Meals));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleMeal(int id)
    {
        await _mealLibrarySvc.ToggleActiveAsync(id);
        return RedirectToAction(nameof(Meals));
    }

    // ── Meal Plans (UC4) ─────────────────────────────────────────────────────

    public async Task<IActionResult> MealPlan()
        => View(await _mealPlanSvc.GetMealPlansAsync());

    [HttpGet]
    public async Task<IActionResult> MealPlanCreate(int? id, DateTime? week)
    {
        try
        {
            // A week that already has a plan still opens here: the page offers "Update Existing Plan"
            // instead of saving until the user picks an available week (saving is also blocked on POST)
            return View(await _mealPlanSvc.GetCreateViewModelAsync(id, week));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MealPlanCreate(MealPlanCreateViewModel vm)
    {
        if (!vm.Id.HasValue && await _mealPlanSvc.FindPlanForWeekAsync(vm.WeekStartDate) is { } existing)
        {
            TempData["Error"] = $"A meal plan for the week of {existing.WeekStartDate:dd MMM yyyy} already exists. Nothing was saved — add your meals to this plan.";
            return RedirectToAction(nameof(MealPlanCreate), new { id = existing.Id });
        }
        try
        {
            var plan = await _mealPlanSvc.SaveDraftAsync(vm, UserId);
            if (plan.Status == MealPlanStatus.Published)
            {
                TempData["Success"] = "Published meal plan updated — parents can now see the changes.";
                return RedirectToAction(nameof(MealPlan));
            }
            TempData["Success"] = "Meal plan saved.";
            return RedirectToAction(nameof(MealPlanPublish), new { id = plan.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving meal plan");
            ModelState.AddModelError("", ex.Message);
            var fresh = await _mealPlanSvc.GetCreateViewModelAsync(vm.Id, vm.WeekStartDate);
            fresh.Items = vm.Items;
            fresh.TotalBudget = vm.TotalBudget;
            return View(fresh);
        }
    }

    public async Task<IActionResult> MealPlanPublish(int id)
        => View(await _mealPlanSvc.GetPublishViewAsync(id));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishMealPlan(int id, bool conflictsAcknowledged)
    {
        try
        {
            await _mealPlanSvc.PublishAsync(id, conflictsAcknowledged);
            TempData["Success"] = "Meal plan published.";
            return RedirectToAction(nameof(MealPlan));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(MealPlanPublish), new { id });
        }
    }

    // ── Pre-Orders (UC5) ─────────────────────────────────────────────────────

    public async Task<IActionResult> PreOrders(int? mealPlanId)
    {
        var today = SchoolClock.Today;
        var plans = await _db.MealPlans.Where(p => p.Status == MealPlanStatus.Published)
            .OrderByDescending(p => p.WeekStartDate).ToListAsync();
        // This week's plan first, otherwise the next one coming up, otherwise the latest
        var planId = mealPlanId
            ?? plans.FirstOrDefault(p => p.WeekStartDate.Date <= today && p.WeekStartDate.Date.AddDays(6) >= today)?.Id
            ?? plans.Where(p => p.WeekStartDate.Date > today).OrderBy(p => p.WeekStartDate).FirstOrDefault()?.Id
            ?? plans.FirstOrDefault()?.Id ?? 0;

        ViewBag.MealPlanId = planId;
        ViewBag.MealPlans = plans;
        ViewBag.Today = today;
        if (planId == 0) return View(new List<PreOrderSummaryRow>());

        return View(await _mealOrderSvc.GetKitchenPreOrderSummaryAsync(planId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizePreOrders(int mealPlanId)
    {
        await _mealOrderSvc.FinalizePreOrdersAsync(mealPlanId, UserId);
        TempData["Success"] = "Pre-orders finalised and a purchase requisition has been generated.";
        return RedirectToAction(nameof(Inventory));
    }

    // ── Inventory (UC6) ──────────────────────────────────────────────────────

    public async Task<IActionResult> Inventory()
        => View(await _inventorySvc.GetInventoryAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveIngredient(IngredientCreateViewModel vm)
    {
        try
        {
            await _inventorySvc.SaveIngredientAsync(vm, UserId);
            TempData["Success"] = vm.Id.HasValue
                ? "Ingredient updated."
                : $"\"{vm.Name}\" added. Capture a receipt to add stock.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Inventory));
    }

    [HttpGet]
    public async Task<IActionResult> ReceiveStock(int? ingredientId)
    {
        var vm = new StockReceiptCreateViewModel
        {
            IngredientOptions = await _mealLibrarySvc.GetIngredientOptionsAsync(),
            Suppliers = await _inventorySvc.GetSupplierOptionsAsync()
        };
        if (ingredientId.HasValue) vm.Lines[0].IngredientId = ingredientId.Value;
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceiveStock(StockReceiptCreateViewModel vm)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var receipt = await _inventorySvc.CaptureReceiptAsync(vm, UserId);
                TempData["Success"] = $"Receipt from {receipt.SupplierName} captured — {receipt.Lines.Count} item(s) added to stock.";
                return RedirectToAction(nameof(Receipt), new { id = receipt.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }
        vm.IngredientOptions = await _mealLibrarySvc.GetIngredientOptionsAsync();
        vm.Suppliers = await _inventorySvc.GetSupplierOptionsAsync();
        if (vm.Lines.Count == 0) vm.Lines.Add(new StockReceiptLineInput());
        return View(vm);
    }

    public async Task<IActionResult> Receipt(int id)
    {
        var vm = await _inventorySvc.GetReceiptDetailAsync(id);
        if (vm is null) return NotFound();
        vm.DocumentUrl = Url.Action(nameof(ReceiptDocument), new { id })!;
        return View(vm);
    }

    public async Task<IActionResult> ReceiptDocument(int id)
    {
        var doc = await _inventorySvc.GetReceiptDocumentAsync(id);
        if (doc is null) return NotFound();
        return PhysicalFile(doc.Value.path, doc.Value.contentType);
    }

    public async Task<IActionResult> InventoryRequisition(int id)
    {
        var detail = await _inventorySvc.GetRequisitionDetailAsync(id);
        if (detail is null) return NotFound();
        return View(detail);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitRequisition(int id)
    {
        await _inventorySvc.SubmitRequisitionAsync(id);
        TempData["Success"] = "Requisition submitted.";
        return RedirectToAction(nameof(InventoryRequisition), new { id });
    }

    // ── Ingredient Usage & Leftovers (UC6b) — recorded automatically when collection closes ──

    [HttpGet]
    public async Task<IActionResult> RecordUsage(int? mealPlanItemId)
        => View(await _inventorySvc.GetRecordUsageScreenAsync(mealPlanItemId));

    // ── My Tasks (UC7) ───────────────────────────────────────────────────────

    public async Task<IActionResult> MyTasks()
    {
        ViewBag.IsHeadChef = await _kitchenScheduleSvc.GetHeadChefStaffIdAsync(UserId) is not null;
        return View(await _kitchenScheduleSvc.GetMyTasksAsync(UserId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTaskStatus(int taskId, KitchenTaskStatus status, string? delayReason)
    {
        var (success, message) = await _kitchenScheduleSvc.UpdateTaskStatusAsync(taskId, status, delayReason, UserId);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(MyTasks));
    }

    // ── Head chefs: teams and scheduling ─────────────────────────────────────
    // Only kitchen staff who lead a team (its head chef) can create teams and schedule them.

    private async Task<int?> HeadChefIdAsync() => await _kitchenScheduleSvc.GetHeadChefStaffIdAsync(UserId);

    private IActionResult NotHeadChef()
    {
        TempData["Error"] = "Only a team's head chef can manage kitchen teams and schedules.";
        return RedirectToAction(nameof(MyTasks));
    }

    public async Task<IActionResult> KitchenTeams()
    {
        if (await HeadChefIdAsync() is not int chefId) return NotHeadChef();
        var vm = await _kitchenScheduleSvc.GetTeamsAsync();
        vm.HeadChefStaffId = chefId;
        ViewBag.BackAction = nameof(MyTasks);
        return View("~/Views/Shared/KitchenTeams.cshtml", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveKitchenTeam(int? id, string name, List<int> memberIds, int? headChefId)
    {
        if (await HeadChefIdAsync() is not int chefId) return NotHeadChef();
        // Head chefs edit only the teams they lead; a new team is led by whoever they choose (themselves by default)
        if (id.HasValue && !await _kitchenScheduleSvc.LeadsTeamAsync(UserId, id.Value)) return NotHeadChef();
        try
        {
            var team = await _kitchenScheduleSvc.SaveTeamAsync(id, name, memberIds, headChefId ?? chefId);
            TempData["Success"] = $"Team \"{team.Name}\" saved.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(KitchenTeams));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleKitchenTeam(int id)
    {
        if (!await _kitchenScheduleSvc.LeadsTeamAsync(UserId, id)) return NotHeadChef();
        await _kitchenScheduleSvc.ToggleTeamAsync(id);
        return RedirectToAction(nameof(KitchenTeams));
    }

    public async Task<IActionResult> KitchenScheduleCreate()
    {
        if (await HeadChefIdAsync() is null) return NotHeadChef();
        ViewBag.BackAction = nameof(MyTasks);
        return View("~/Views/Shared/KitchenPlanner.cshtml", await _kitchenScheduleSvc.GetPlannerAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickSchedule(int mealPlanItemId, int teamId, bool acknowledgeUnderstaff)
    {
        if (await HeadChefIdAsync() is null) return Json(new QuickScheduleResult { Message = "Only head chefs can schedule teams." });
        return Json(await _kitchenScheduleSvc.QuickScheduleAsync(mealPlanItemId, teamId, acknowledgeUnderstaff));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishKitchenSchedule(int id)
    {
        if (await HeadChefIdAsync() is null) return Json(new { success = false });
        await _kitchenScheduleSvc.PublishScheduleAsync(id, UserId);
        return Json(new { success = true });
    }

    // ── Meal Scanner (UC8) ───────────────────────────────────────────────────

    public async Task<IActionResult> MealScanner(int? mealPlanItemId)
    {
        if (mealPlanItemId is null or 0)
        {
            ViewBag.Today = SchoolClock.Today;
            ViewBag.NextService = await _attendanceSvc.DescribeNextMealServiceAsync();
            return View("MealScannerPicker", await _attendanceSvc.GetTodaysMealsAsync());
        }

        ViewBag.Live = await _attendanceSvc.GetLiveDashboardAsync(mealPlanItemId.Value);
        return View(mealPlanItemId.Value);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordMealScan(int mealPlanItemId, string hash)
        => Json(await _attendanceSvc.RecordScanAsync(mealPlanItemId, hash?.Trim() ?? "", UserId));

    [HttpGet]
    public async Task<IActionResult> MealScannerLive(int mealPlanItemId)
        => Json(await _attendanceSvc.GetLiveDashboardAsync(mealPlanItemId));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeAttendance(int mealPlanItemId)
    {
        var (absent, usage) = await _attendanceSvc.FinalizeAttendanceAsync(mealPlanItemId, UserId);
        var msg = absent > 0
            ? $"Meal collection closed. {absent} learner(s) who ordered but did not collect were marked absent."
            : "Meal collection closed — everyone who ordered collected their meal.";
        if (usage.Prepared > 0)
            msg += $" {usage.Prepared} serving(s) deducted from stock by recipe; {usage.Leftovers} left over.";
        if (usage.Shortages.Any())
            msg += $" Stock records were short for: {string.Join(", ", usage.Shortages)} — check that all supplier receipts were captured.";
        TempData["Success"] = msg;
        return RedirectToAction(nameof(MealScanner), new { mealPlanItemId });
    }

    public async Task<IActionResult> MealAttendance(DateTime? date)
        => View(await _attendanceSvc.GetHistoryAsync(date ?? DateTime.Today));

    // ── Feedback Dashboard (UC9) ─────────────────────────────────────────────

    public async Task<IActionResult> FeedbackDashboard(int? mealPlanId)
        => View(await _feedbackSvc.GetDashboardAsync(mealPlanId));

    public async Task<IActionResult> FeedbackMealsCsv(int? mealPlanId)
    {
        var vm = await _feedbackSvc.GetDashboardAsync(mealPlanId);
        var csv = CsvExport.Build(
            new[] { "Date", "Meal", "Menu", "Average rating", "Ratings", "Too little", "Just right", "Too much", "Ordered", "Collected", "Leftover", "Open alert" },
            vm.Meals.Select(m => new object?[]
            {
                vm.WeekStartDate?.AddDays(m.DayOfWeek - 1), m.MealType, m.MenuDescription,
                m.SubmissionCount > 0 ? m.AverageRating : null, m.SubmissionCount, m.TooLittle, m.JustRight, m.TooMuch,
                m.Ordered, m.Collected, m.Leftovers, m.HasActiveAlert ? "Yes" : "No"
            }));
        return File(csv, "text/csv", $"meal-feedback-{vm.WeekStartDate:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> FeedbackCommentsCsv(int? mealPlanId)
    {
        var vm = await _feedbackSvc.GetDashboardAsync(mealPlanId);
        var csv = CsvExport.Build(
            new[] { "Submitted", "Meal", "Rating", "Portion", "Comment" },
            vm.RecentComments.Select(c => new object?[] { c.SubmittedAt.AddHours(2), c.MealLabel, c.Rating, c.Portion, c.Comment }));
        return File(csv, "text/csv", $"meal-comments-{vm.WeekStartDate:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> QualityAlertsCsv()
    {
        var vm = await _feedbackSvc.GetDashboardAsync(null);
        var csv = CsvExport.Build(
            new[] { "Raised", "Meal", "Average rating", "Ratings", "Status", "Resolved", "Action taken" },
            vm.Alerts.Select(a => new object?[]
            {
                a.CreatedAt.AddHours(2), a.MenuDescription, a.AverageRating, a.SubmissionCount,
                a.Resolved ? "Resolved" : "Open", a.ResolvedAt?.AddHours(2), a.ResolutionNotes
            }));
        return File(csv, "text/csv", "meal-quality-alerts.csv");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveQualityAlert(int alertId, string notes, int? mealPlanId)
    {
        if (string.IsNullOrWhiteSpace(notes))
            TempData["Error"] = "Describe what was done to fix the meal before resolving the alert.";
        else if (await _feedbackSvc.ResolveAlertAsync(alertId, notes, UserId))
            TempData["Success"] = "Quality alert resolved.";
        return RedirectToAction(nameof(FeedbackDashboard), new { mealPlanId });
    }
}
