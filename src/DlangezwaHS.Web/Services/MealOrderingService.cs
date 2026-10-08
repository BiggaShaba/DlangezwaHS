using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// UC5 — MEAL PRE-ORDERING
// ─────────────────────────────────────────────────────────────────────────────

public interface IMealOrderService
{
    Task<PreOrderWeekViewModel?> GetPreOrderScreenAsync(int learnerId);
    Task<(bool success, string message)> PlaceOrderAsync(int mealPlanItemId, int learnerId, string parentUserId, PortionSize portionSize);
    Task<(bool success, string message)> CancelOrderAsync(int mealPlanItemId, int learnerId);
    Task<IList<PreOrderSummaryRow>> GetKitchenPreOrderSummaryAsync(int mealPlanId);
    Task FinalizePreOrdersAsync(int mealPlanId, string kitchenStaffUserId);
}

public class MealOrderService : IMealOrderService
{
    public const int OrderDeadlineHours = 48;
    private static readonly string[] DayNames = { "", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    private readonly ApplicationDbContext _db;
    private readonly IMealPlanService _mealPlanSvc;
    private readonly IInventoryService _inventorySvc;

    public MealOrderService(ApplicationDbContext db, IMealPlanService mealPlanSvc, IInventoryService inventorySvc)
    {
        _db = db;
        _mealPlanSvc = mealPlanSvc;
        _inventorySvc = inventorySvc;
    }

    internal static DateTime ComputeServingDateTime(MealPlan plan, MealPlanItem item)
    {
        var date = plan.WeekStartDate.Date.AddDays(item.DayOfWeek - 1);
        if (TimeSpan.TryParse(item.ServingTime, out var time)) date = date.Add(time);
        return date;
    }

    public async Task<PreOrderWeekViewModel?> GetPreOrderScreenAsync(int learnerId)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId);
        if (learner is null) return null;

        var vm = new PreOrderWeekViewModel { LearnerId = learnerId, LearnerName = learner.FullName };

        // Every published plan that still has meals to come (this week and any upcoming weeks)
        var today = SchoolClock.Today;
        var plans = await _db.MealPlans
            .Include(p => p.Items).ThenInclude(i => i.Meal).ThenInclude(m => m!.Ingredients).ThenInclude(mi => mi.Ingredient)
            .Where(p => p.Status == MealPlanStatus.Published && p.WeekStartDate >= today.AddDays(-6))
            .ToListAsync();
        if (plans.Count == 0) return vm;

        var itemIds = plans.SelectMany(p => p.Items).Select(i => i.Id).ToList();
        var myOrders = await _db.MealPreOrders
            .Where(o => o.LearnerId == learnerId && itemIds.Contains(o.MealPlanItemId))
            .ToListAsync();
        var conflicts = new List<MealConflictRow>();
        foreach (var p in plans)
            conflicts.AddRange((await _mealPlanSvc.ScanAllergyConflictsAsync(p.Id))
                .Where(c => c.SevereLearners.Contains(learner.FullName)));

        var now = SchoolClock.Now;
        var servingDuration = ((await _db.BoardingSettings.FirstOrDefaultAsync()) ?? new BoardingSettings()).ServingDuration;
        foreach (var (plan, item) in plans.SelectMany(p => p.Items.Select(i => (p, i)))
                     .OrderBy(x => ComputeServingDateTime(x.p, x.i)).ThenBy(x => x.i.Id))
        {
            var servingAt = ComputeServingDateTime(plan, item);
            var deadline = servingAt.AddHours(-OrderDeadlineHours);

            // Earlier days are not shown; today's and later meals are, locked once ordering closes
            if (servingAt.Date < today) { if (servingAt.Date >= SchoolClock.WeekStart(today)) vm.ClosedCount++; continue; }

            var conflict = conflicts.FirstOrDefault(c => c.MealPlanItemId == item.Id);
            if (conflict is not null)
            {
                vm.HiddenForAllergy.Add($"{servingAt:ddd dd MMM} {item.MealType}: {item.MenuDescription} (contains {conflict.AllergenMatched})");
                continue;
            }

            var existing = myOrders.FirstOrDefault(o => o.MealPlanItemId == item.Id && o.Status != PreOrderStatus.Cancelled);
            vm.Meals.Add(new PreOrderMealRow
            {
                MealPlanItemId = item.Id,
                ServingDate = servingAt.Date,
                DayLabel = servingAt.ToString("dddd"),
                MealType = item.MealType.ToString(),
                MenuDescription = item.MenuDescription,
                Description = item.Meal?.Description,
                ImageUrl = item.Meal?.ImagePath,
                IngredientNames = item.Meal?.Ingredients.Select(i => i.Ingredient.Name).OrderBy(n => n).ToList()
                    ?? item.Ingredients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ServingTime = item.ServingTime,
                OrderDeadline = deadline,
                OptionsInSlot = plan.Items.Count(i => i.DayOfWeek == item.DayOfWeek && i.MealType == item.MealType),
                CurrentOrderStatus = existing?.Status.ToString(),
                CurrentPortionSize = existing?.PortionSize.ToString(),
                OrderedByLearner = existing is not null && learner.HasLogin && existing.OrderedByParentUserId == learner.UserId,
                IsOrderingClosed = now > deadline,
                IsServingOver = now >= servingAt + servingDuration
            });
        }

        return vm;
    }

    public async Task<(bool success, string message)> PlaceOrderAsync(int mealPlanItemId, int learnerId, string parentUserId, PortionSize portionSize)
    {
        var item = await _db.MealPlanItems.Include(i => i.MealPlan).FirstOrDefaultAsync(i => i.Id == mealPlanItemId);
        if (item is null) return (false, "Meal not found.");

        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId);
        if (learner is null) return (false, "Learner not found.");

        var servingAt = ComputeServingDateTime(item.MealPlan, item);
        if (SchoolClock.Now > servingAt.AddHours(-OrderDeadlineHours))
            return (false, "Ordering for this meal has closed. Contact the Housemaster to request a manual override.");

        var conflict = await _mealPlanSvc.CheckItemConflictForLearnerAsync(mealPlanItemId, learner.FullName);
        if (conflict is not null)
            return (false, $"Allergy conflict: {conflict.AllergenMatched}. Please choose another option.");

        var settings = await _db.BoardingSettings.FirstOrDefaultAsync() ?? new BoardingSettings();
        var currentCount = await _db.MealPreOrders.CountAsync(o => o.MealPlanItemId == mealPlanItemId && o.LearnerId != learnerId
            && (o.Status == PreOrderStatus.Confirmed || o.Status == PreOrderStatus.Pending));
        var status = currentCount >= settings.MaxPreOrdersPerMeal ? PreOrderStatus.WaitListed : PreOrderStatus.Confirmed;

        // A learner eats one option per meal service — ordering a different option replaces the previous one
        var otherOptionIds = await _db.MealPlanItems
            .Where(i => i.MealPlanId == item.MealPlanId && i.DayOfWeek == item.DayOfWeek && i.MealType == item.MealType && i.Id != item.Id)
            .Select(i => i.Id)
            .ToListAsync();
        var replaced = await _db.MealPreOrders
            .Include(o => o.MealPlanItem)
            .Where(o => o.LearnerId == learnerId && otherOptionIds.Contains(o.MealPlanItemId) && o.Status != PreOrderStatus.Cancelled)
            .ToListAsync();
        foreach (var r in replaced) r.Status = PreOrderStatus.Cancelled;

        var existing = await _db.MealPreOrders.FirstOrDefaultAsync(o => o.MealPlanItemId == mealPlanItemId && o.LearnerId == learnerId);
        if (existing is not null)
        {
            existing.Status = status;
            existing.PortionSize = portionSize;
            existing.OrderedByParentUserId = parentUserId;
            existing.OrderedAt = DateTime.UtcNow;
            existing.BlockedReason = null;
        }
        else
        {
            _db.MealPreOrders.Add(new MealPreOrder
            {
                MealPlanItemId = mealPlanItemId,
                LearnerId = learnerId,
                OrderedByParentUserId = parentUserId,
                Status = status,
                PortionSize = portionSize
            });
        }
        await _db.SaveChangesAsync();

        var swapNote = replaced.Count > 0 ? $" (replaced {string.Join(", ", replaced.Select(r => r.MealPlanItem.MenuDescription))})" : "";
        return status == PreOrderStatus.WaitListed
            ? (true, $"{item.MenuDescription} is at capacity — you have been waitlisted{swapNote}.")
            : (true, $"{item.MenuDescription} ordered for {DayNames[item.DayOfWeek]} {item.MealType}{swapNote}.");
    }

    public async Task<(bool success, string message)> CancelOrderAsync(int mealPlanItemId, int learnerId)
    {
        var order = await _db.MealPreOrders.Include(o => o.MealPlanItem).ThenInclude(i => i.MealPlan)
            .FirstOrDefaultAsync(o => o.MealPlanItemId == mealPlanItemId && o.LearnerId == learnerId);
        if (order is null) return (false, "Order not found.");
        // Same rule as ordering: once the deadline passes the tile is locked
        if (SchoolClock.Now > ComputeServingDateTime(order.MealPlanItem.MealPlan, order.MealPlanItem).AddHours(-OrderDeadlineHours))
            return (false, "Ordering for this meal has closed, so the order can no longer be cancelled.");
        order.Status = PreOrderStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (true, "Pre-order cancelled.");
    }

    public async Task<IList<PreOrderSummaryRow>> GetKitchenPreOrderSummaryAsync(int mealPlanId)
    {
        var items = await _db.MealPlanItems.Include(i => i.MealPlan)
            .Where(i => i.MealPlanId == mealPlanId).OrderBy(i => i.DayOfWeek).ThenBy(i => i.MealType).ThenBy(i => i.Id).ToListAsync();
        var itemIds = items.Select(i => i.Id).ToList();

        var orders = await _db.MealPreOrders.Where(o => itemIds.Contains(o.MealPlanItemId)).ToListAsync();

        return items.Select(i => new PreOrderSummaryRow
        {
            MealPlanItemId = i.Id,
            DayLabel = DayNames[i.DayOfWeek],
            Date = i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1),
            ServingTime = i.ServingTime,
            MealType = i.MealType.ToString(),
            MenuDescription = i.MenuDescription,
            ConfirmedCount = orders.Count(o => o.MealPlanItemId == i.Id && o.Status == PreOrderStatus.Confirmed),
            WaitListedCount = orders.Count(o => o.MealPlanItemId == i.Id && o.Status == PreOrderStatus.WaitListed)
        }).ToList();
    }

    public async Task FinalizePreOrdersAsync(int mealPlanId, string kitchenStaffUserId)
    {
        var pendingForPlan = await _db.MealPreOrders
            .Where(o => o.Status == PreOrderStatus.Pending && _db.MealPlanItems.Any(i => i.Id == o.MealPlanItemId && i.MealPlanId == mealPlanId))
            .ToListAsync();
        foreach (var order in pendingForPlan) order.Status = PreOrderStatus.Confirmed;
        await _db.SaveChangesAsync();

        await _inventorySvc.GenerateRequisitionAsync(mealPlanId, kitchenStaffUserId);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC6 — INVENTORY & INGREDIENT MANAGEMENT
// Stock goes UP only through a captured receipt/invoice, and goes DOWN
// automatically when a meal service is finalised (recipe qty × servings).
// ─────────────────────────────────────────────────────────────────────────────

public interface IInventoryService
{
    Task<InventoryViewModel> GetInventoryAsync();
    Task<IList<IngredientRow>> GetIngredientsAsync();
    Task SaveIngredientAsync(IngredientCreateViewModel vm, string userId);
    Task<IList<PurchaseRequisitionRow>> GetRequisitionsAsync();
    Task<PurchaseRequisition> GenerateRequisitionAsync(int mealPlanId, string generatedByUserId);
    Task<PurchaseRequisitionDetailViewModel?> GetRequisitionDetailAsync(int id);
    Task SubmitRequisitionAsync(int id);

    Task<StockReceipt> CaptureReceiptAsync(StockReceiptCreateViewModel vm, string userId);
    Task<StockReceiptDetailViewModel?> GetReceiptDetailAsync(int id);
    Task<(string path, string contentType)?> GetReceiptDocumentAsync(int id);

    Task<IList<SupplierOption>> GetSupplierOptionsAsync();

    // Ingredient usage is recorded automatically when a meal's collection is closed:
    // prepared = confirmed pre-orders, leftovers = prepared − collected, stock deducted by recipe.
    Task<RecordUsageViewModel> GetRecordUsageScreenAsync(int? mealPlanItemId);
    Task<ServiceUsageResult> RecordServiceUsageAsync(IList<int> mealPlanItemIds, string userId);
}

public class InventoryService : IInventoryService
{
    private static readonly Dictionary<string, string> AllowedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".webp"] = "image/webp", [".pdf"] = "application/pdf"
    };
    private const long MaxDocumentBytes = 10 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly UserManager<ApplicationUser> _userManager;

    public InventoryService(ApplicationDbContext db, IWebHostEnvironment env, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _env = env;
        _userManager = userManager;
    }

    private string ReceiptsDir => Path.Combine(_env.ContentRootPath, "App_Data", "receipts");

    public async Task<InventoryViewModel> GetInventoryAsync()
    {
        var usage = await _db.StockUsageLogs
            .Include(u => u.Ingredient)
            .Include(u => u.UsedForMealPlanItem)
            .OrderByDescending(u => u.RecordedAt)
            .Take(400)
            .ToListAsync();

        return new InventoryViewModel
        {
            Ingredients = await GetIngredientsAsync(),
            Requisitions = await GetRequisitionsAsync(),
            Receipts = await _db.StockReceipts
                .OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.Id)
                .Take(100)
                .Select(r => new StockReceiptRow
                {
                    Id = r.Id, SupplierName = r.SupplierName, InvoiceNumber = r.InvoiceNumber,
                    ReceiptDate = r.ReceiptDate, TotalAmount = r.TotalAmount, LineCount = r.Lines.Count
                })
                .ToListAsync(),
            RecentUsage = usage.Select(u => new StockUsageRow
            {
                RecordedAt = u.RecordedAt,
                MealPlanItemId = u.UsedForMealPlanItemId,
                MealType = u.UsedForMealPlanItem?.MealType.ToString() ?? "",
                IngredientName = u.Ingredient.Name,
                Unit = u.Ingredient.Unit.ToString(),
                QuantityUsed = u.QuantityUsed,
                MealDescription = u.UsedForMealPlanItem?.MenuDescription ?? "—"
            }).ToList()
        };
    }

    public async Task<IList<IngredientRow>> GetIngredientsAsync()
    {
        return await _db.Ingredients
            .OrderBy(i => i.Name)
            .Select(i => new IngredientRow
            {
                Id = i.Id,
                Name = i.Name,
                Unit = i.Unit.ToString(),
                CurrentStock = i.CurrentStock,
                MinimumStock = i.MinimumStock,
                UnitCost = i.UnitCost,
                IsLowStock = i.CurrentStock < i.MinimumStock
            })
            .ToListAsync();
    }

    public async Task SaveIngredientAsync(IngredientCreateViewModel vm, string userId)
    {
        var name = vm.Name.Trim();
        if (await _db.Ingredients.AnyAsync(i => i.Name == name && i.Id != (vm.Id ?? 0)))
            throw new InvalidOperationException($"An ingredient called \"{name}\" already exists.");

        Ingredient ingredient;
        if (vm.Id.HasValue)
        {
            ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == vm.Id) ?? throw new InvalidOperationException("Ingredient not found.");
        }
        else
        {
            // New ingredients start at zero — stock is added by capturing a receipt
            ingredient = new Ingredient { CurrentStock = 0 };
            _db.Ingredients.Add(ingredient);
        }
        ingredient.Name = name;
        ingredient.Unit = vm.Unit;
        ingredient.MinimumStock = vm.MinimumStock;
        ingredient.LastUpdatedAt = DateTime.UtcNow;
        ingredient.LastUpdatedByUserId = userId;
        await _db.SaveChangesAsync();
    }

    public async Task<IList<PurchaseRequisitionRow>> GetRequisitionsAsync()
    {
        return await _db.PurchaseRequisitions
            .Include(r => r.Items)
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => new PurchaseRequisitionRow { Id = r.Id, GeneratedAt = r.GeneratedAt, Status = r.Status.ToString(), ItemCount = r.Items.Count })
            .ToListAsync();
    }

    public async Task<PurchaseRequisition> GenerateRequisitionAsync(int mealPlanId, string generatedByUserId)
    {
        var items = await _db.MealPlanItems
            .Include(i => i.Meal).ThenInclude(m => m!.Ingredients)
            .Where(i => i.MealPlanId == mealPlanId)
            .ToListAsync();
        var itemIds = items.Select(i => i.Id).ToList();

        var confirmedCounts = await _db.MealPreOrders
            .Where(o => itemIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
            .GroupBy(o => o.MealPlanItemId)
            .Select(g => new { MealPlanItemId = g.Key, Count = g.Count() })
            .ToListAsync();

        var ingredients = await _db.Ingredients.ToListAsync();
        var neededByIngredient = new Dictionary<int, decimal>();

        foreach (var item in items)
        {
            var count = confirmedCounts.FirstOrDefault(c => c.MealPlanItemId == item.Id)?.Count ?? 0;
            if (count == 0) continue;

            if (item.Meal is not null)
            {
                // Recipe quantities per serving × number of confirmed orders
                foreach (var mi in item.Meal.Ingredients)
                    neededByIngredient[mi.IngredientId] = neededByIngredient.GetValueOrDefault(mi.IngredientId) + mi.QuantityPerServing * count;
                continue;
            }

            // Legacy free-text meal — best-effort name matching, 1 unit per order
            var tokens = item.Ingredients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                var ingredient = ingredients.FirstOrDefault(ing =>
                    string.Equals(ing.Name, token, StringComparison.OrdinalIgnoreCase) ||
                    token.Contains(ing.Name, StringComparison.OrdinalIgnoreCase) ||
                    ing.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (ingredient is null) continue;

                neededByIngredient[ingredient.Id] = neededByIngredient.GetValueOrDefault(ingredient.Id) + count;
            }
        }

        var requisition = new PurchaseRequisition { GeneratedByUserId = generatedByUserId, Status = RequisitionStatus.Draft };
        foreach (var (ingredientId, needed) in neededByIngredient)
        {
            var ingredient = ingredients.First(i => i.Id == ingredientId);
            var shortfall = Math.Max(0, needed - ingredient.CurrentStock);
            if (shortfall <= 0) continue;

            requisition.Items.Add(new PurchaseRequisitionItem
            {
                IngredientId = ingredientId,
                QuantityRequired = needed,
                QuantityInStock = ingredient.CurrentStock,
                QuantityToOrder = shortfall,
                UnitCost = ingredient.UnitCost
            });
        }

        _db.PurchaseRequisitions.Add(requisition);
        await _db.SaveChangesAsync();
        return requisition;
    }

    public async Task<PurchaseRequisitionDetailViewModel?> GetRequisitionDetailAsync(int id)
    {
        var requisition = await _db.PurchaseRequisitions.Include(r => r.Items).ThenInclude(i => i.Ingredient)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return null;

        return new PurchaseRequisitionDetailViewModel
        {
            Id = requisition.Id,
            GeneratedAt = requisition.GeneratedAt,
            Status = requisition.Status.ToString(),
            Items = requisition.Items.Select(i => new PurchaseRequisitionItemRow
            {
                IngredientName = i.Ingredient.Name,
                Unit = i.Ingredient.Unit.ToString(),
                QuantityRequired = i.QuantityRequired,
                QuantityInStock = i.QuantityInStock,
                QuantityToOrder = i.QuantityToOrder
            }).ToList()
        };
    }

    public async Task SubmitRequisitionAsync(int id)
    {
        var requisition = await _db.PurchaseRequisitions.FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return;
        requisition.Status = RequisitionStatus.Submitted;
        await _db.SaveChangesAsync();
    }

    // ── Receipts / invoices ───────────────────────────────────────────────────

    public async Task<StockReceipt> CaptureReceiptAsync(StockReceiptCreateViewModel vm, string userId)
    {
        if (vm.Document is not { Length: > 0 })
            throw new InvalidOperationException("Scan or upload the receipt/invoice so the stock can be verified.");

        var ext = Path.GetExtension(vm.Document.FileName);
        if (!AllowedDocumentTypes.TryGetValue(ext, out var contentType))
            throw new InvalidOperationException("The receipt must be a photo (JPG, PNG, WEBP) or a PDF.");
        if (vm.Document.Length > MaxDocumentBytes)
            throw new InvalidOperationException("The receipt file must be smaller than 10 MB.");

        var lines = vm.Lines.Where(l => l.IngredientId > 0 && l.Quantity > 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Add at least one ingredient received on this receipt.");
        if (lines.GroupBy(l => l.IngredientId).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Each ingredient can only be listed once on a receipt.");

        Supplier? supplier = null;
        if (vm.SupplierId is int supplierId)
        {
            supplier = await _db.Suppliers.Include(s => s.Ingredients).FirstOrDefaultAsync(s => s.Id == supplierId && s.IsActive)
                ?? throw new InvalidOperationException("That supplier is no longer available.");
            vm.SupplierName = supplier.Name;
            // Prices for the supplier's own items come from the supplier list, not the browser
            foreach (var line in lines)
                if (supplier.Ingredients.FirstOrDefault(si => si.IngredientId == line.IngredientId) is { } si)
                    line.UnitCost = si.UnitPrice;
        }
        if (string.IsNullOrWhiteSpace(vm.SupplierName))
            throw new InvalidOperationException("Choose a supplier or type the supplier's name.");
        if (lines.Any(l => l.UnitCost <= 0))
            throw new InvalidOperationException("Every item needs a price per unit greater than R0.");

        var ingredientIds = lines.Select(l => l.IngredientId).Distinct().ToList();
        var ingredients = await _db.Ingredients.Where(i => ingredientIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        if (ingredients.Count != ingredientIds.Count)
            throw new InvalidOperationException("One of the selected ingredients no longer exists.");

        Directory.CreateDirectory(ReceiptsDir);
        var fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        await using (var stream = File.Create(Path.Combine(ReceiptsDir, fileName)))
            await vm.Document.CopyToAsync(stream);

        var receipt = new StockReceipt
        {
            SupplierId = supplier?.Id,
            SupplierName = vm.SupplierName.Trim(),
            InvoiceNumber = vm.InvoiceNumber?.Trim(),
            ReceiptDate = vm.ReceiptDate.Date,
            Notes = vm.Notes,
            DocumentPath = fileName,
            DocumentContentType = contentType,
            CapturedByUserId = userId,
            TotalAmount = lines.Sum(l => l.Quantity * l.UnitCost)
        };

        foreach (var line in lines)
        {
            receipt.Lines.Add(new StockReceiptLine { IngredientId = line.IngredientId, Quantity = line.Quantity, UnitCost = line.UnitCost });

            var ingredient = ingredients[line.IngredientId];
            ingredient.CurrentStock += line.Quantity;
            if (line.UnitCost > 0) ingredient.UnitCost = line.UnitCost;
            ingredient.LastUpdatedAt = DateTime.UtcNow;
            ingredient.LastUpdatedByUserId = userId;
        }

        _db.StockReceipts.Add(receipt);
        await _db.SaveChangesAsync();
        return receipt;
    }

    public async Task<StockReceiptDetailViewModel?> GetReceiptDetailAsync(int id)
    {
        var r = await _db.StockReceipts.Include(x => x.Lines).ThenInclude(l => l.Ingredient).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return null;
        var user = await _userManager.FindByIdAsync(r.CapturedByUserId);

        return new StockReceiptDetailViewModel
        {
            Id = r.Id,
            SupplierName = r.SupplierName,
            InvoiceNumber = r.InvoiceNumber,
            ReceiptDate = r.ReceiptDate,
            Notes = r.Notes,
            TotalAmount = r.TotalAmount,
            DocumentIsPdf = r.DocumentContentType == "application/pdf",
            CapturedBy = user?.FullName ?? "Unknown",
            CapturedAt = r.CapturedAt,
            Lines = r.Lines.Select(l => new StockReceiptLineRow
            {
                IngredientName = l.Ingredient.Name,
                Unit = l.Ingredient.Unit.ToString(),
                Quantity = l.Quantity,
                UnitCost = l.UnitCost
            }).ToList()
        };
    }

    public async Task<(string path, string contentType)?> GetReceiptDocumentAsync(int id)
    {
        var r = await _db.StockReceipts.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return null;
        var path = Path.Combine(ReceiptsDir, Path.GetFileName(r.DocumentPath));
        if (!File.Exists(path)) return null;
        return (path, r.DocumentContentType ?? "application/octet-stream");
    }

    // ── Record Ingredient Usage ───────────────────────────────────────────────

    private static readonly string[] UsageDayNames = { "", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public async Task<IList<SupplierOption>> GetSupplierOptionsAsync()
    {
        var suppliers = await _db.Suppliers.Include(s => s.Ingredients).ThenInclude(si => si.Ingredient)
            .Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        return suppliers.Select(s => new SupplierOption
        {
            Id = s.Id, Name = s.Name, ContactName = s.ContactName, Phone = s.Phone,
            Items = s.Ingredients.OrderBy(si => si.Ingredient.Name).Select(si => new SupplierItemOption
            {
                IngredientId = si.IngredientId, Name = si.Ingredient.Name, Unit = si.Ingredient.Unit.ToString(),
                UnitPrice = si.UnitPrice, DefaultQuantity = si.DefaultQuantity
            }).ToList()
        }).ToList();
    }

    public async Task<RecordUsageViewModel> GetRecordUsageScreenAsync(int? mealPlanItemId)
    {
        // Meals served from a week ago up to tomorrow
        var today = SchoolClock.Today;
        var items = await _db.MealPlanItems
            .Include(i => i.MealPlan)
            .Include(i => i.Meal).ThenInclude(m => m!.Ingredients).ThenInclude(mi => mi.Ingredient)
            .Where(i => i.MealPlan.Status == MealPlanStatus.Published && i.MealId != null
                     && i.MealPlan.WeekStartDate >= today.AddDays(-13) && i.MealPlan.WeekStartDate <= today.AddDays(1))
            .ToListAsync();
        items = items
            .Where(i => { var d = i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1); return d >= today.AddDays(-7) && d <= today.AddDays(1); })
            .OrderByDescending(i => i.MealPlan.WeekStartDate.AddDays(i.DayOfWeek - 1)).ThenBy(i => i.MealType).ThenBy(i => i.MenuDescription)
            .ToList();

        var ids = items.Select(i => i.Id).ToList();
        var counts = await ServiceCountsAsync(ids);
        var records = await _db.IngredientUsageRecords.Where(r => ids.Contains(r.MealPlanItemId)).ToListAsync();

        var vm = new RecordUsageViewModel
        {
            Meals = items.Select(i =>
            {
                var rec = records.FirstOrDefault(r => r.MealPlanItemId == i.Id);
                return new UsageMealOption
                {
                    MealPlanItemId = i.Id,
                    Date = i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1),
                    MealType = i.MealType.ToString(),
                    MenuDescription = i.MenuDescription,
                    ImageUrl = i.Meal?.ImagePath,
                    ConfirmedOrders = counts.Ordered.GetValueOrDefault(i.Id),
                    Collected = counts.Collected.GetValueOrDefault(i.Id),
                    IsRecorded = rec is not null,
                    Leftovers = rec?.LeftoverServings
                };
            }).ToList(),
            History = await GetUsageHistoryAsync()
        };

        var selected = items.FirstOrDefault(i => i.Id == mealPlanItemId) ?? items.FirstOrDefault(i => i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1) <= today);
        if (selected?.Meal is not null)
        {
            vm.MealPlanItemId = selected.Id;
            vm.Selected = vm.Meals.First(m => m.MealPlanItemId == selected.Id);
            vm.Recipe = selected.Meal.Ingredients
                .OrderBy(mi => mi.Ingredient.Name)
                .Select(mi => new RecipeLineRow
                {
                    IngredientName = mi.Ingredient.Name,
                    Unit = mi.Ingredient.Unit.ToString(),
                    QuantityPerServing = mi.QuantityPerServing,
                    InStock = mi.Ingredient.CurrentStock,
                    Deducted = mi.QuantityPerServing * vm.Selected.ConfirmedOrders
                }).ToList();
        }
        return vm;
    }

    // Confirmed orders per option, and how many of those learners collected it.
    // Walk-ins without an order are flagged at the scanner, not served from the prepared batch,
    // so "collected" can never be more than "prepared".
    private async Task<(Dictionary<int, int> Ordered, Dictionary<int, int> Collected)> ServiceCountsAsync(IList<int> itemIds)
    {
        var orders = await _db.MealPreOrders
            .Where(o => itemIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
            .Select(o => new { o.MealPlanItemId, o.LearnerId }).ToListAsync();
        var collected = await _db.MealAttendances
            .Where(a => itemIds.Contains(a.MealPlanItemId) && (a.Status == MealAttendanceStatus.Present || a.Status == MealAttendanceStatus.Late))
            .Select(a => new { a.MealPlanItemId, a.LearnerId }).ToListAsync();
        var orderSet = orders.Select(o => (o.MealPlanItemId, o.LearnerId)).ToHashSet();
        return (
            orders.GroupBy(o => o.MealPlanItemId).ToDictionary(g => g.Key, g => g.Count()),
            collected.Where(a => orderSet.Contains((a.MealPlanItemId, a.LearnerId)))
                .GroupBy(a => a.MealPlanItemId).ToDictionary(g => g.Key, g => g.Count()));
    }

    private async Task<IList<UsageRecordRow>> GetUsageHistoryAsync()
    {
        var records = await _db.IngredientUsageRecords
            .Include(r => r.MealPlanItem).ThenInclude(i => i.MealPlan)
            .Include(r => r.Lines).ThenInclude(l => l.Ingredient)
            .OrderByDescending(r => r.RecordedAt)
            .Take(120)
            .ToListAsync();

        var counts = await ServiceCountsAsync(records.Select(r => r.MealPlanItemId).Distinct().ToList());

        return records.Select(r => new UsageRecordRow
        {
            Id = r.Id,
            RecordedAt = r.RecordedAt,
            MealDate = r.MealPlanItem.MealPlan.WeekStartDate.Date.AddDays(r.MealPlanItem.DayOfWeek - 1),
            MealType = r.MealPlanItem.MealType.ToString(),
            MenuDescription = r.MealPlanItem.MenuDescription,
            ServingsPrepared = r.ServingsPrepared,
            LeftoverServings = r.LeftoverServings,
            Collected = counts.Collected.GetValueOrDefault(r.MealPlanItemId),
            Deducted = string.Join(", ", r.Lines.OrderBy(l => l.Ingredient.Name)
                .Select(l => $"{l.QuantityUsed.ToString("0.##")} {l.Ingredient.Unit} {l.Ingredient.Name}"))
        }).ToList();
    }

    /// <summary>
    /// Called when a meal's collection is closed. For each option: prepared = confirmed pre-orders,
    /// leftovers = prepared − collected, and the recipe quantities are deducted from stock.
    /// Running it again only applies the difference, so stock is never deducted twice.
    /// </summary>
    public async Task<ServiceUsageResult> RecordServiceUsageAsync(IList<int> mealPlanItemIds, string userId)
    {
        var items = await _db.MealPlanItems
            .Include(i => i.Meal).ThenInclude(m => m!.Ingredients).ThenInclude(mi => mi.Ingredient)
            .Where(i => mealPlanItemIds.Contains(i.Id)).ToListAsync();
        var counts = await ServiceCountsAsync(mealPlanItemIds);
        var records = await _db.IngredientUsageRecords.Include(r => r.Lines)
            .Where(r => mealPlanItemIds.Contains(r.MealPlanItemId)).ToListAsync();

        var result = new ServiceUsageResult();
        foreach (var item in items)
        {
            var prepared = counts.Ordered.GetValueOrDefault(item.Id);
            var collected = Math.Min(prepared, counts.Collected.GetValueOrDefault(item.Id));
            var record = records.FirstOrDefault(r => r.MealPlanItemId == item.Id);
            if (prepared == 0 && record is null) continue;

            if (record is null)
            {
                record = new IngredientUsageRecord
                {
                    MealPlanItemId = item.Id, RecordedByUserId = userId,
                    Notes = "Recorded automatically when collection was closed"
                };
                _db.IngredientUsageRecords.Add(record);
            }
            var previous = record.ServingsPrepared;
            record.ServingsPrepared = prepared;
            record.LeftoverServings = prepared - collected;
            result.Prepared += prepared;
            result.Leftovers += prepared - collected;

            foreach (var mi in item.Meal?.Ingredients ?? Enumerable.Empty<MealIngredient>())
            {
                var line = record.Lines.FirstOrDefault(l => l.IngredientId == mi.IngredientId);
                if (line is null)
                {
                    line = new StockUsageLog { IngredientId = mi.IngredientId, UsedForMealPlanItemId = item.Id, RecordedByUserId = userId };
                    record.Lines.Add(line);
                }
                line.QuantityUsed = mi.QuantityPerServing * prepared;

                var delta = mi.QuantityPerServing * (prepared - previous);
                if (delta == 0) continue;
                if (delta > mi.Ingredient.CurrentStock) result.Shortages.Add(mi.Ingredient.Name);
                // Stock can't go below zero — a shortage means a receipt was probably not captured
                mi.Ingredient.CurrentStock = Math.Max(0, mi.Ingredient.CurrentStock - delta);
                mi.Ingredient.LastUpdatedAt = DateTime.UtcNow;
                mi.Ingredient.LastUpdatedByUserId = userId;
            }
        }
        await _db.SaveChangesAsync();
        result.Shortages = result.Shortages.Distinct().OrderBy(n => n).ToList();
        return result;
    }

}
