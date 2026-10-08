using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// UC3 — DIETARY PROFILES
// A parent (or the learner) adds the profile and it is active immediately — no review.
// Kitchen staff and the housemaster are notified and can view it with any supporting documents.
// ─────────────────────────────────────────────────────────────────────────────

public interface IDietaryService
{
    /// <summary>Returns an error message if the optional documents are not acceptable.</summary>
    string? ValidateDocuments(IList<IFormFile>? files);
    Task<DietaryProfile> AddProfileAsync(int learnerId, string submittedByUserId, List<DietaryItemInput> items, IList<IFormFile>? documents);
    Task<IList<DietaryProfileRow>> GetActiveProfilesAsync();
    Task<DietaryProfileRow?> GetProfileDetailAsync(int id);
    Task<ParentDietaryProfileViewModel> GetParentViewAsync(int learnerId);
    Task<(string path, string contentType, string fileName, int learnerId)?> GetDocumentAsync(int documentId);
}

public class DietaryService : IDietaryService
{
    public const int MaxDocuments = 5;
    private const long MaxDocumentBytes = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".png"] = "image/png", [".webp"] = "image/webp"
    };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly IMealPlanService _mealPlanSvc;
    private readonly IWebHostEnvironment _env;

    public DietaryService(ApplicationDbContext db, UserManager<ApplicationUser> um, IEmailService email,
        IMealPlanService mealPlanSvc, IWebHostEnvironment env)
    {
        _db = db;
        _userManager = um;
        _email = email;
        _mealPlanSvc = mealPlanSvc;
        _env = env;
    }

    // Medical documents are kept out of wwwroot and only served through access-checked actions
    private string DocumentsDir => Path.Combine(_env.ContentRootPath, "App_Data", "dietary-documents");

    public string? ValidateDocuments(IList<IFormFile>? files)
    {
        var real = files?.Where(f => f is { Length: > 0 }).ToList() ?? new List<IFormFile>();
        if (real.Count > MaxDocuments) return $"You can attach up to {MaxDocuments} documents.";
        foreach (var f in real)
        {
            if (!AllowedDocumentTypes.ContainsKey(Path.GetExtension(f.FileName)))
                return $"\"{f.FileName}\" is not supported. Upload PDF, JPG, PNG or WEBP files.";
            if (f.Length > MaxDocumentBytes)
                return $"\"{f.FileName}\" is larger than 5 MB.";
        }
        return null;
    }

    public async Task<DietaryProfile> AddProfileAsync(int learnerId, string submittedByUserId, List<DietaryItemInput> items, IList<IFormFile>? documents)
    {
        // The new profile replaces whatever was active before
        var previous = await _db.DietaryProfiles
            .Where(p => p.LearnerId == learnerId && p.Status == DietaryProfileStatus.Active)
            .ToListAsync();
        foreach (var old in previous) old.Status = DietaryProfileStatus.Superseded;

        var profile = new DietaryProfile
        {
            LearnerId = learnerId,
            SubmittedByParentUserId = submittedByUserId,
            Status = DietaryProfileStatus.Active,
            ApprovedAt = DateTime.UtcNow,
            Items = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new DietaryItem { Category = i.Category, Name = i.Name.Trim(), Severity = i.Severity, Notes = i.Notes })
                .ToList()
        };

        Directory.CreateDirectory(DocumentsDir);
        foreach (var file in documents?.Where(f => f is { Length: > 0 }) ?? Enumerable.Empty<IFormFile>())
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var stored = $"{Guid.NewGuid():N}{ext}";
            await using (var stream = File.Create(Path.Combine(DocumentsDir, stored)))
                await file.CopyToAsync(stream);
            profile.Documents.Add(new DietaryProfileDocument
            {
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = stored,
                ContentType = AllowedDocumentTypes[ext],
                FileSizeBytes = file.Length
            });
        }

        _db.DietaryProfiles.Add(profile);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Don't leave orphaned medical documents on disk if the profile wasn't saved
            foreach (var d in profile.Documents)
                File.Delete(Path.Combine(DocumentsDir, d.StoredFileName));
            throw;
        }

        await NotifyOfNewProfileAsync(profile.Id);
        return profile;
    }

    public async Task<IList<DietaryProfileRow>> GetActiveProfilesAsync()
        => await MapProfilesAsync(_db.DietaryProfiles.Where(p => p.Status == DietaryProfileStatus.Active));

    public async Task<DietaryProfileRow?> GetProfileDetailAsync(int id)
        => (await MapProfilesAsync(_db.DietaryProfiles.Where(p => p.Id == id))).FirstOrDefault();

    public async Task<(string path, string contentType, string fileName, int learnerId)?> GetDocumentAsync(int documentId)
    {
        var doc = await _db.DietaryProfileDocuments.Include(d => d.DietaryProfile)
            .FirstOrDefaultAsync(d => d.Id == documentId);
        if (doc is null) return null;
        var path = Path.Combine(DocumentsDir, Path.GetFileName(doc.StoredFileName));
        if (!File.Exists(path)) return null;
        return (path, doc.ContentType, doc.OriginalFileName, doc.DietaryProfile.LearnerId);
    }

    private async Task<IList<DietaryProfileRow>> MapProfilesAsync(IQueryable<DietaryProfile> query)
    {
        var profiles = await query
            .Include(p => p.Learner).ThenInclude(l => l.Parent)
            .Include(p => p.SubmittedByParent)
            .Include(p => p.Items)
            .Include(p => p.Documents)
            .OrderByDescending(p => p.SubmittedAt)
            .ToListAsync();

        return profiles.Select(p => new DietaryProfileRow
        {
            Id = p.Id,
            LearnerId = p.LearnerId,
            LearnerName = p.Learner.FullName,
            // Learner submissions show the guardian, with a "by learner" tag
            ParentName = (IsLearnerSubmission(p) ? p.Learner.Parent?.FullName : null) ?? p.SubmittedByParent.FullName,
            SubmittedByLearner = IsLearnerSubmission(p),
            RejectionReason = p.RejectionReason,
            SubmittedAt = p.SubmittedAt,
            Status = p.Status.ToString(),
            HasSevereAllergy = p.Items.Any(i => i.Severity is AllergySeverity.Severe or AllergySeverity.Anaphylactic),
            Items = p.Items.Select(i => new DietaryItemRow
            {
                Category = i.Category.ToString(),
                Name = i.Name,
                Severity = i.Severity?.ToString(),
                Notes = i.Notes
            }).ToList(),
            Documents = p.Documents.OrderBy(d => d.Id).Select(d => new DietaryDocumentRow
            {
                Id = d.Id, FileName = d.OriginalFileName, FileSizeBytes = d.FileSizeBytes
            }).ToList()
        }).ToList();
    }

    public static byte[] ToCsv(IEnumerable<DietaryProfileRow> rows)
        => CsvExport.Build(
            new[] { "Learner", "Parent / guardian", "Added by", "Submitted", "Category", "Item", "Severity", "Notes", "Documents" },
            rows.SelectMany(p => (p.Items.Any() ? p.Items : new List<DietaryItemRow> { new() }).Select(i => new object?[]
            {
                p.LearnerName, p.ParentName, p.SubmittedByLearner ? "Learner" : "Parent", p.SubmittedAt.AddHours(2),
                i.Category, i.Name, i.Severity, i.Notes, p.Documents.Count
            })));

    private static bool IsLearnerSubmission(DietaryProfile p)
        => p.Learner.HasLogin && p.SubmittedByParentUserId == p.Learner.UserId;

    public async Task<ParentDietaryProfileViewModel> GetParentViewAsync(int learnerId)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId);
        var all = await MapProfilesAsync(_db.DietaryProfiles.Where(p => p.LearnerId == learnerId));
        return new ParentDietaryProfileViewModel
        {
            LearnerId = learnerId,
            LearnerName = learner?.FullName ?? "",
            ActiveProfile = all.FirstOrDefault(p => p.Status == nameof(DietaryProfileStatus.Active)),
            History = all.ToList()
        };
    }

    // Kitchen staff and housemasters are told straight away; if the learner added it, the parent is informed too
    private async Task NotifyOfNewProfileAsync(int profileId)
    {
        var profile = await _db.DietaryProfiles
            .Include(p => p.Learner).ThenInclude(l => l.Parent)
            .Include(p => p.Items).Include(p => p.Documents)
            .FirstAsync(p => p.Id == profileId);

        var itemsHtml = string.Join("", profile.Items.Select(i => $"<li>{i.Name} ({i.Category}{(i.Severity is null ? "" : $", {i.Severity}")})</li>"));
        var docsNote = profile.Documents.Any() ? $"<p>{profile.Documents.Count} supporting document(s) attached.</p>" : "";
        var body = $"<p>A dietary profile for <strong>{profile.Learner.FullName}</strong> is now active:</p><ul>{itemsHtml}</ul>{docsNote}";

        await EmailKitchenStaffAsync($"Dietary Profile Updated — {profile.Learner.FullName}", name => $"<p>Dear {name},</p>{body}");

        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is not null)
                await _email.SendAsync(user.Email, hm.FullName, $"Dietary Profile Updated — {profile.Learner.FullName}", $"<p>Dear {hm.FullName},</p>{body}");
        }

        var parent = profile.Learner.Parent;
        if (IsLearnerSubmission(profile) && parent?.Email is not null)
            await _email.SendAsync(parent.Email, parent.FullName, $"Dietary Profile Updated — {profile.Learner.FullName}",
                $"<p>Dear {parent.FullName},</p><p><strong>{profile.Learner.FullName}</strong> updated their dietary profile from their learner account.</p><ul>{itemsHtml}</ul>");

        await ScanUpcomingPlansAndAlertAsync(profile);
    }

    private async Task EmailKitchenStaffAsync(string subject, Func<string, string> body)
    {
        var kitchenStaff = await _db.KitchenStaffMembers.Where(k => k.IsActive && k.UserId != null).ToListAsync();
        foreach (var k in kitchenStaff)
        {
            var user = await _userManager.FindByIdAsync(k.UserId!);
            if (user?.Email is not null)
                await _email.SendAsync(user.Email, k.FullName, subject, body(k.FullName));
        }
    }

    private async Task ScanUpcomingPlansAndAlertAsync(DietaryProfile profile)
    {
        var today = SchoolClock.Today;
        var upcomingPlans = await _db.MealPlans
            .Where(p => p.Status == MealPlanStatus.Published && p.WeekStartDate >= today.AddDays(-7))
            .ToListAsync();

        var conflictMeals = new List<string>();
        foreach (var plan in upcomingPlans)
        {
            var conflicts = await _mealPlanSvc.ScanAllergyConflictsAsync(plan.Id);
            conflictMeals.AddRange(conflicts
                .Where(c => c.AffectedLearners.Contains(profile.Learner.FullName))
                .Select(c => $"{c.DayLabel} {c.MealType}: {c.MenuDescription} (contains {c.AllergenMatched})"));
        }
        if (conflictMeals.Count == 0) return;

        var listHtml = string.Join("", conflictMeals.Select(m => $"<li>{m}</li>"));
        var body = $"<p>The new allergy profile for <strong>{profile.Learner.FullName}</strong> conflicts with these upcoming published meals:</p><ul>{listHtml}</ul>";

        await EmailKitchenStaffAsync($"Allergy Conflict — {profile.Learner.FullName}", name => $"<p>Dear {name},</p>{body}");

        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is not null)
                await _email.SendAsync(user.Email, hm.FullName, $"Allergy Conflict — {profile.Learner.FullName}", $"<p>Dear {hm.FullName},</p>{body}");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC4 — MEAL LIBRARY
// ─────────────────────────────────────────────────────────────────────────────

public interface IMealLibraryService
{
    Task<IList<MealOption>> GetMealsAsync(bool includeInactive = false);
    Task<MealEditViewModel?> GetEditViewModelAsync(int id);
    Task<IList<IngredientOption>> GetIngredientOptionsAsync();
    Task<Meal> SaveMealAsync(MealEditViewModel vm);
    Task ToggleActiveAsync(int id);
}

public class MealLibraryService : IMealLibraryService
{
    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxImageBytes = 5 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public MealLibraryService(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public static decimal CostPerServing(Meal meal)
        => meal.Ingredients.Sum(i => i.QuantityPerServing * (i.Ingredient?.UnitCost ?? 0));

    internal static MealOption ToOption(Meal m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        MealType = m.MealType.ToString(),
        Description = m.Description,
        ImageUrl = m.ImagePath,
        CostPerServing = Math.Round(CostPerServing(m), 2),
        PrepMinutesPerServing = m.PrepMinutesPerServing,
        IngredientLines = m.Ingredients
            .OrderBy(i => i.Ingredient.Name)
            .Select(i => $"{i.Ingredient.Name} — {i.QuantityPerServing.ToString("0.###")} {i.Ingredient.Unit}")
            .ToList(),
        IsActive = m.IsActive
    };

    public async Task<IList<MealOption>> GetMealsAsync(bool includeInactive = false)
    {
        var meals = await _db.Meals
            .Include(m => m.Ingredients).ThenInclude(i => i.Ingredient)
            .Where(m => includeInactive || m.IsActive)
            .OrderBy(m => m.MealType).ThenBy(m => m.Name)
            .ToListAsync();
        return meals.Select(ToOption).ToList();
    }

    public async Task<IList<IngredientOption>> GetIngredientOptionsAsync()
        => await _db.Ingredients.OrderBy(i => i.Name)
            .Select(i => new IngredientOption { Id = i.Id, Name = i.Name, Unit = i.Unit.ToString() })
            .ToListAsync();

    public async Task<MealEditViewModel?> GetEditViewModelAsync(int id)
    {
        var meal = await _db.Meals.Include(m => m.Ingredients).FirstOrDefaultAsync(m => m.Id == id);
        if (meal is null) return null;
        return new MealEditViewModel
        {
            Id = meal.Id,
            Name = meal.Name,
            Description = meal.Description,
            MealType = meal.MealType,
            PrepMinutesPerServing = meal.PrepMinutesPerServing,
            ExistingImageUrl = meal.ImagePath,
            Ingredients = meal.Ingredients
                .Select(i => new MealIngredientInput { IngredientId = i.IngredientId, QuantityPerServing = i.QuantityPerServing })
                .ToList(),
            IngredientOptions = await GetIngredientOptionsAsync()
        };
    }

    public async Task<Meal> SaveMealAsync(MealEditViewModel vm)
    {
        var lines = vm.Ingredients.Where(i => i.IngredientId > 0 && i.QuantityPerServing > 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Add at least one ingredient with the quantity used per serving.");
        if (lines.GroupBy(l => l.IngredientId).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Each ingredient can only be listed once per meal.");

        Meal meal;
        if (vm.Id.HasValue)
        {
            meal = await _db.Meals.Include(m => m.Ingredients).FirstOrDefaultAsync(m => m.Id == vm.Id)
                ?? throw new InvalidOperationException("Meal not found.");
            _db.MealIngredients.RemoveRange(meal.Ingredients);
            meal.Ingredients.Clear();
        }
        else
        {
            meal = new Meal();
            _db.Meals.Add(meal);
        }

        meal.Name = vm.Name.Trim();
        meal.Description = vm.Description?.Trim();
        meal.MealType = vm.MealType;
        meal.PrepMinutesPerServing = vm.PrepMinutesPerServing;
        foreach (var l in lines)
            meal.Ingredients.Add(new MealIngredient { IngredientId = l.IngredientId, QuantityPerServing = l.QuantityPerServing });

        if (vm.Image is { Length: > 0 })
            meal.ImagePath = await SaveImageAsync(vm.Image);
        else if (string.IsNullOrEmpty(meal.ImagePath))
            throw new InvalidOperationException("Add a photo of the meal so parents and learners can see what it looks like.");

        await _db.SaveChangesAsync();

        // Keep plan items that serve this meal in sync (name, ingredients, cost) so allergy
        // checks and the parent menu reflect the recipe change.
        await _db.Entry(meal).Collection(m => m.Ingredients).Query().Include(i => i.Ingredient).LoadAsync();
        var planItems = await _db.MealPlanItems.Where(i => i.MealId == meal.Id).ToListAsync();
        foreach (var item in planItems)
            MealPlanService.ApplyMealSnapshot(item, meal);
        await _db.SaveChangesAsync();

        return meal;
    }

    public async Task ToggleActiveAsync(int id)
    {
        var meal = await _db.Meals.FirstOrDefaultAsync(m => m.Id == id);
        if (meal is null) return;
        meal.IsActive = !meal.IsActive;
        await _db.SaveChangesAsync();
    }

    private async Task<string> SaveImageAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            throw new InvalidOperationException("Meal image must be a JPG, PNG, WEBP or GIF file.");
        if (file.Length > MaxImageBytes)
            throw new InvalidOperationException("Meal image must be smaller than 5 MB.");

        var dir = Path.Combine(_env.WebRootPath, "uploads", "meals");
        Directory.CreateDirectory(dir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using var stream = File.Create(Path.Combine(dir, fileName));
        await file.CopyToAsync(stream);
        return $"/uploads/meals/{fileName}";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC4 — WEEKLY MEAL PLAN
// ─────────────────────────────────────────────────────────────────────────────

public interface IMealPlanService
{
    Task<MealPlanCreateViewModel> GetCreateViewModelAsync(int? id, DateTime? week = null);
    Task<MealPlan?> FindPlanForWeekAsync(DateTime monday);
    Task PopulatePickerDataAsync(MealPlanCreateViewModel vm);
    Task<MealPlan> SaveDraftAsync(MealPlanCreateViewModel vm, string createdByUserId);
    Task<IList<MealPlanSummaryRow>> GetMealPlansAsync();
    Task<MealPlan?> GetPlanAsync(int id);
    Task<MealPlanPublishViewModel> GetPublishViewAsync(int id);
    Task<IList<MealConflictRow>> ScanAllergyConflictsAsync(int mealPlanId);
    Task<MealConflictRow?> CheckItemConflictForLearnerAsync(int mealPlanItemId, string learnerName);
    Task PublishAsync(int mealPlanId, bool conflictsAcknowledged);
    Task<ParentMealPlanWeekViewModel> GetWeekForParentAsync(int weekOffset);
}

public class MealPlanService : IMealPlanService
{
    private static readonly string[] DayNames =
        { "", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    private readonly ApplicationDbContext _db;

    public MealPlanService(ApplicationDbContext db)
    {
        _db = db;
    }

    // Copies the library meal's name/ingredients/cost onto a plan item.
    // Requires meal.Ingredients (with Ingredient) to be loaded.
    internal static void ApplyMealSnapshot(MealPlanItem item, Meal meal)
    {
        item.MealId = meal.Id;
        item.MenuDescription = meal.Name;
        var names = string.Join(", ", meal.Ingredients.Select(i => i.Ingredient.Name));
        item.Ingredients = names.Length > 500 ? names[..500] : names;
        item.EstimatedCostPerHead = Math.Round(MealLibraryService.CostPerServing(meal), 2);
    }

    /// <summary>
    /// Weekly cost of a plan: every learner eats one option per serving time, so each
    /// day/meal costs the average of its options' per-serving cost × learners eating.
    /// </summary>
    public static decimal EstimateWeekCost(IEnumerable<(int Day, MealType Type, decimal CostPerServing)> options, int diners)
        => Math.Round(options.GroupBy(o => (o.Day, o.Type)).Sum(g => g.Average(o => o.CostPerServing)) * diners, 2);

    public static decimal EstimateWeekCost(IEnumerable<MealPlanItem> items, int diners)
        => EstimateWeekCost(items.Select(i => (i.DayOfWeek, i.MealType, i.EstimatedCostPerHead)), diners);

    private async Task<int> ExpectedDinersAsync()
        => Math.Max(1, (await _db.BoardingSettings.FirstOrDefaultAsync())?.ExpectedDinersPerMeal ?? 40);

    // New plans can be made for this week and up to 7 weeks ahead
    public const int WeeksAhead = 8;

    // Any plan whose 7 days overlap the Monday–Sunday week. Older plans don't always start on a
    // Monday, so an exact date match would let a second plan (and duplicate pre-order options) in.
    public async Task<MealPlan?> FindPlanForWeekAsync(DateTime monday)
    {
        var from = monday.Date.AddDays(-6);
        var to = monday.Date.AddDays(6);
        return await _db.MealPlans.Where(p => p.WeekStartDate >= from && p.WeekStartDate <= to)
            .OrderBy(p => p.WeekStartDate == monday.Date ? 0 : 1).ThenBy(p => p.Id)
            .FirstOrDefaultAsync();
    }

    // Plans are only created one at a time, so two quick submits can't both pass the week check
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    /// <summary>1–7 = first day that can still change; 8 = the whole week has passed.</summary>
    public static int FirstEditableDay(DateTime weekStart, DateTime today)
    {
        if (today < weekStart.Date) return 1;
        var days = (today - weekStart.Date).Days;
        return days > 6 ? 8 : days + 1;
    }

    private async Task<List<MealPlanWeekChoice>> GetWeekChoicesAsync()
    {
        var thisMonday = SchoolClock.WeekStart(SchoolClock.Today);
        var lastMonday = thisMonday.AddDays(7 * (WeeksAhead - 1));
        var nearby = await _db.MealPlans
            .Where(p => p.WeekStartDate >= thisMonday.AddDays(-6) && p.WeekStartDate <= lastMonday.AddDays(6))
            .Select(p => new { p.Id, p.WeekStartDate })
            .ToListAsync();
        // A week counts as planned if any plan overlaps it
        var existing = Enumerable.Range(0, WeeksAhead).Select(w => thisMonday.AddDays(7 * w))
            .Select(m => (Monday: m, Plan: nearby.Where(p => Math.Abs((p.WeekStartDate.Date - m).Days) < 7)
                .OrderBy(p => p.WeekStartDate.Date == m ? 0 : 1).ThenBy(p => p.Id).FirstOrDefault()))
            .Where(x => x.Plan is not null)
            .ToDictionary(x => x.Monday, x => x.Plan!.Id);

        return Enumerable.Range(0, WeeksAhead).Select(w =>
        {
            var monday = thisMonday.AddDays(7 * w);
            var when = w == 0 ? "This week" : w == 1 ? "Next week" : $"In {w} weeks";
            return new MealPlanWeekChoice
            {
                Monday = monday,
                Label = $"{when}: {monday:ddd dd MMM} – {monday.AddDays(6):ddd dd MMM yyyy}",
                ExistingPlanId = existing.TryGetValue(monday, out var id) ? id : null
            };
        }).ToList();
    }

    public async Task<MealPlanCreateViewModel> GetCreateViewModelAsync(int? id, DateTime? week = null)
    {
        var today = SchoolClock.Today;
        var choices = await GetWeekChoicesAsync();
        var vm = new MealPlanCreateViewModel
        {
            WeekChoices = choices,
            TotalBudget = (await _db.BoardingSettings.FirstOrDefaultAsync())?.WeeklyMealBudget ?? 0,
            ExpectedDiners = await ExpectedDinersAsync()
        };

        if (!id.HasValue)
        {
            // Requested week if it is allowed, otherwise the first week that has no plan yet
            var chosen = choices.FirstOrDefault(c => week.HasValue && c.Monday == week.Value.Date)
                      ?? choices.FirstOrDefault(c => c.ExistingPlanId is null)
                      ?? choices[0];
            vm.WeekStartDate = chosen.Monday;
            vm.ExistingPlanIdForWeek = chosen.ExistingPlanId;
            vm.FirstEditableDay = FirstEditableDay(chosen.Monday, today);
        }
        else
        {
            var plan = await _db.MealPlans.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id.Value)
                ?? throw new InvalidOperationException("Meal plan not found.");
            vm.Id = plan.Id;
            vm.WeekStartDate = plan.WeekStartDate;
            vm.TotalBudget = plan.TotalBudget;
            vm.IsPublished = plan.Status == MealPlanStatus.Published;
            vm.FirstEditableDay = FirstEditableDay(plan.WeekStartDate, today);

            var itemIds = plan.Items.Select(i => i.Id).ToList();
            var lockedIds = (await _db.MealPreOrders.Where(o => itemIds.Contains(o.MealPlanItemId) && o.Status != PreOrderStatus.Cancelled).Select(o => o.MealPlanItemId).ToListAsync())
                .Concat(await _db.MealAttendances.Where(a => itemIds.Contains(a.MealPlanItemId)).Select(a => a.MealPlanItemId).ToListAsync())
                .Concat(await _db.KitchenSchedules.Where(s => itemIds.Contains(s.MealPlanItemId)).Select(s => s.MealPlanItemId).ToListAsync())
                .ToHashSet();

            vm.Items = plan.Items.OrderBy(i => i.DayOfWeek).ThenBy(i => i.MealType).ThenBy(i => i.Id)
                .Select(i => new MealPlanItemInput
                {
                    Id = i.Id, DayOfWeek = i.DayOfWeek, MealType = i.MealType, MealId = i.MealId,
                    MenuDescription = i.MenuDescription, Ingredients = i.Ingredients,
                    Locked = lockedIds.Contains(i.Id) || i.DayOfWeek < vm.FirstEditableDay
                }).ToList();
        }

        await PopulatePickerDataAsync(vm);
        return vm;
    }

    public async Task PopulatePickerDataAsync(MealPlanCreateViewModel vm)
    {
        var meals = await _db.Meals
            .Include(m => m.Ingredients).ThenInclude(i => i.Ingredient)
            .OrderBy(m => m.Name)
            .ToListAsync();
        vm.MealOptions = meals.Select(MealLibraryService.ToOption).ToList();

        var settings = await _db.BoardingSettings.FirstOrDefaultAsync() ?? new BoardingSettings();
        vm.ServingTimes = Enum.GetValues<MealType>().ToDictionary(t => t, t => settings.ShiftFor(t).Serve);

        vm.IngredientOptions = await _db.Ingredients.OrderBy(i => i.Name)
            .Select(i => new IngredientOption { Id = i.Id, Name = i.Name, Unit = i.Unit.ToString() })
            .ToListAsync();
    }

    public async Task<MealPlan> SaveDraftAsync(MealPlanCreateViewModel vm, string createdByUserId)
    {
        if (vm.Id.HasValue) return await SaveDraftCoreAsync(vm, createdByUserId);
        await CreateLock.WaitAsync();
        try { return await SaveDraftCoreAsync(vm, createdByUserId); }
        finally { CreateLock.Release(); }
    }

    private async Task<MealPlan> SaveDraftCoreAsync(MealPlanCreateViewModel vm, string createdByUserId)
    {
        var today = SchoolClock.Today;
        var thisMonday = SchoolClock.WeekStart(today);
        if (!vm.Id.HasValue)
        {
            var monday = vm.WeekStartDate.Date;
            if (monday.DayOfWeek != DayOfWeek.Monday)
                throw new InvalidOperationException("A meal plan covers one week, Monday to Sunday — choose a week from the list.");
            if (monday < thisMonday)
                throw new InvalidOperationException("You can't create a meal plan for a week that has already passed.");
            if (monday > thisMonday.AddDays(7 * (WeeksAhead - 1)))
                throw new InvalidOperationException($"Meal plans can only be created up to {WeeksAhead} weeks ahead.");
            if (await FindPlanForWeekAsync(monday) is { } clash)
                throw new InvalidOperationException(
                    $"There is already a meal plan for the week of {clash.WeekStartDate:dd MMM yyyy}. Only one plan is allowed per week — edit that plan instead, otherwise learners would see duplicate meal options when pre-ordering.");
        }

        var settings = await _db.BoardingSettings.FirstOrDefaultAsync() ?? new BoardingSettings();
        var mealIds = vm.Items.Where(i => i.MealId.HasValue).Select(i => i.MealId!.Value).Distinct().ToList();
        var meals = await _db.Meals.Include(m => m.Ingredients).ThenInclude(i => i.Ingredient)
            .Where(m => mealIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        MealPlan plan;
        if (vm.Id.HasValue)
        {
            plan = await _db.MealPlans.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == vm.Id)
                ?? throw new InvalidOperationException("Meal plan not found.");
            if (FirstEditableDay(plan.WeekStartDate, today) > 7)
                throw new InvalidOperationException("This week has already passed — its meal plan can no longer be changed.");
            // A plan always stays on its own week
            plan.TotalBudget = vm.TotalBudget;
        }
        else
        {
            plan = new MealPlan { WeekStartDate = vm.WeekStartDate, TotalBudget = vm.TotalBudget, CreatedByUserId = createdByUserId };
        }

        // Days that have already passed are locked: changes to them are ignored and their meals kept
        var firstEditable = FirstEditableDay(plan.WeekStartDate, today);
        var postedItems = vm.Items
            .Where(i => i.DayOfWeek >= firstEditable && i.DayOfWeek <= 7 && (i.MealId.HasValue || !string.IsNullOrWhiteSpace(i.MenuDescription)))
            .ToList();

        // Every serving time on every day that can still change needs at least one meal
        var missing = Enumerable.Range(firstEditable, Math.Max(0, 8 - firstEditable))
            .SelectMany(d => Enum.GetValues<MealType>()
                .Where(t => !postedItems.Any(i => i.DayOfWeek == d && i.MealType == t))
                .Select(t => $"{DayNames[d]} {t}"))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Add at least one meal to every serving time before saving. Missing: {string.Join(", ", missing)}.");

        // The plan may not cost more than its weekly budget
        if (vm.TotalBudget <= 0)
            throw new InvalidOperationException("Enter a weekly budget greater than R0.");
        var existingCost = plan.Items.Where(i => i.Id != 0).ToDictionary(i => i.Id, i => i.EstimatedCostPerHead);
        var estimate = EstimateWeekCost(
            plan.Items.Where(i => i.DayOfWeek < firstEditable).Select(i => (i.DayOfWeek, i.MealType, i.EstimatedCostPerHead))
                .Concat(postedItems.Select(i => (i.DayOfWeek, i.MealType,
                    i.MealId.HasValue && meals.TryGetValue(i.MealId.Value, out var m) ? Math.Round(MealLibraryService.CostPerServing(m), 2)
                    : i.Id.HasValue ? existingCost.GetValueOrDefault(i.Id.Value) : 0m))),
            await ExpectedDinersAsync());
        if (estimate > vm.TotalBudget)
            throw new InvalidOperationException(
                $"The estimated cost (R{estimate:N2}) is more than the weekly budget (R{vm.TotalBudget:N2}). Remove or swap meals, or increase the budget, before saving.");

        if (plan.Id == 0) _db.MealPlans.Add(plan);

        // ── Remove options that were taken off the plan (only when nothing depends on them) ──
        var keptIds = postedItems.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();
        var removed = plan.Items.Where(i => i.Id != 0 && i.DayOfWeek >= firstEditable && !keptIds.Contains(i.Id)).ToList();
        foreach (var item in removed)
        {
            var inUse = await _db.MealPreOrders.AnyAsync(o => o.MealPlanItemId == item.Id && o.Status != PreOrderStatus.Cancelled)
                     || await _db.MealAttendances.AnyAsync(a => a.MealPlanItemId == item.Id)
                     || await _db.KitchenSchedules.AnyAsync(s => s.MealPlanItemId == item.Id)
                     || await _db.MealQualityAlerts.AnyAsync(a => a.MealPlanItemId == item.Id);
            if (inUse)
                throw new InvalidOperationException(
                    $"\"{item.MenuDescription}\" on {DayNames[item.DayOfWeek]} ({item.MealType}) already has pre-orders, attendance or a kitchen schedule and can't be removed.");

            var cancelled = await _db.MealPreOrders.Where(o => o.MealPlanItemId == item.Id).ToListAsync();
            _db.MealPreOrders.RemoveRange(cancelled);
            _db.MealPlanItems.Remove(item);
        }

        // ── Update existing / add new options ──
        foreach (var input in postedItems)
        {
            var item = input.Id.HasValue ? plan.Items.FirstOrDefault(i => i.Id == input.Id.Value) : null;
            if (item is null)
            {
                item = new MealPlanItem();
                plan.Items.Add(item);
            }
            item.DayOfWeek = input.DayOfWeek;
            item.MealType = input.MealType;
            item.ServingTime = settings.ShiftFor(input.MealType).Serve;

            if (input.MealId.HasValue && meals.TryGetValue(input.MealId.Value, out var meal))
            {
                ApplyMealSnapshot(item, meal);
            }
            else if (!input.MealId.HasValue)
            {
                // Legacy free-text item — keep as is
                item.MenuDescription = input.MenuDescription.Trim();
                item.Ingredients = input.Ingredients?.Trim() ?? "";
            }
        }

        await _db.SaveChangesAsync();
        return plan;
    }

    public async Task<IList<MealPlanSummaryRow>> GetMealPlansAsync()
    {
        var thisMonday = SchoolClock.WeekStart(SchoolClock.Today);
        var diners = await ExpectedDinersAsync();
        var plans = await _db.MealPlans.Include(p => p.Items).OrderByDescending(p => p.WeekStartDate).ToListAsync();
        var rows = plans.Select(p => new MealPlanSummaryRow
        {
            Id = p.Id,
            WeekStartDate = p.WeekStartDate,
            Status = p.Status.ToString(),
            TotalBudget = p.TotalBudget,
            EstimatedCost = EstimateWeekCost(p.Items, diners),
            ItemCount = p.Items.Count
        }).ToList();

        foreach (var r in rows)
        {
            r.DuplicateOfPlanIds = rows.Where(o => o.Id != r.Id && Math.Abs((o.WeekStartDate.Date - r.WeekStartDate.Date).Days) < 7)
                .Select(o => o.Id).ToList();
            var start = r.WeekStartDate.Date;
            r.WeekRelation = start.AddDays(6) < SchoolClock.Today ? "Past"
                : start <= SchoolClock.Today ? "This week"
                : start == thisMonday.AddDays(7) ? "Next week"
                : "Upcoming";
        }
        return rows;
    }

    public async Task<MealPlan?> GetPlanAsync(int id)
        => await _db.MealPlans.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IList<MealConflictRow>> ScanAllergyConflictsAsync(int mealPlanId)
    {
        var items = await _db.MealPlanItems.Where(i => i.MealPlanId == mealPlanId).ToListAsync();

        // Every allergy on an active profile is reported, whatever its severity
        // (an allergy saved without a severity is treated as Severe to be safe)
        var allergyRows = await _db.DietaryItems
            .Include(i => i.DietaryProfile)
            .Where(i => i.DietaryProfile.Status == DietaryProfileStatus.Active && i.Category == DietaryCategory.Allergy)
            .Select(i => new { i.Name, Severity = i.Severity ?? AllergySeverity.Severe, LearnerId = i.DietaryProfile.LearnerId })
            .ToListAsync();

        if (allergyRows.Count == 0) return new List<MealConflictRow>();

        var learnerIds = allergyRows.Select(a => a.LearnerId).Distinct().ToList();
        var learnerNames = await _db.Learners.Where(l => learnerIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.FullName);

        var conflicts = new List<MealConflictRow>();
        foreach (var item in items.OrderBy(i => i.DayOfWeek).ThenBy(i => i.MealType))
        {
            // Check the meal's name as well as its ingredients ("Oats porridge" matches a porridge allergy)
            var mealWords = WordVariants($"{item.MenuDescription} {item.Ingredients}");
            if (mealWords.Count == 0) continue;

            foreach (var group in allergyRows.GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                if (!AllergenMatches(group.Key, mealWords)) continue;

                conflicts.Add(new MealConflictRow
                {
                    MealPlanItemId = item.Id,
                    DayLabel = DayNames[item.DayOfWeek],
                    MealType = item.MealType.ToString(),
                    MenuDescription = item.MenuDescription,
                    AllergenMatched = group.Key,
                    Severity = group.Max(a => a.Severity).ToString(),
                    IsSevere = group.Any(a => a.Severity != AllergySeverity.Mild),
                    AffectedLearners = group.Select(a => learnerNames.GetValueOrDefault(a.LearnerId, "Unknown")).Distinct().ToList(),
                    SevereLearners = group.Where(a => a.Severity != AllergySeverity.Mild)
                        .Select(a => learnerNames.GetValueOrDefault(a.LearnerId, "Unknown")).Distinct().ToList()
                });
            }
        }
        return conflicts;
    }

    // Every word in the allergen ("peanuts", "cow milk") must appear in the meal text.
    // Simple plural endings are ignored, so "Peanuts" matches "Peanut butter".
    internal static bool AllergenMatches(string allergen, HashSet<string> mealWords)
    {
        var words = Words(allergen);
        return words.Count > 0 && words.All(w => Variants(w).Any(mealWords.Contains));
    }

    internal static HashSet<string> WordVariants(string text)
        => Words(text).SelectMany(Variants).ToHashSet();

    private static List<string> Words(string text)
        => System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(w => w.Length > 1).ToList();

    private static IEnumerable<string> Variants(string word)
    {
        yield return word;
        if (word.Length > 3 && word.EndsWith("es")) yield return word[..^2];
        if (word.Length > 3 && word.EndsWith('s')) yield return word[..^1];
    }

    public async Task<MealConflictRow?> CheckItemConflictForLearnerAsync(int mealPlanItemId, string learnerName)
    {
        var item = await _db.MealPlanItems.FirstOrDefaultAsync(i => i.Id == mealPlanItemId);
        if (item is null) return null;

        var conflicts = await ScanAllergyConflictsAsync(item.MealPlanId);
        return conflicts.FirstOrDefault(c => c.MealPlanItemId == mealPlanItemId && c.SevereLearners.Contains(learnerName));
    }

    // Serving times (on days that haven't passed) with no meal, e.g. "Tuesday Lunch"
    private static List<string> MissingServings(DateTime weekStart, IEnumerable<MealPlanItem> items)
    {
        var first = FirstEditableDay(weekStart, SchoolClock.Today);
        return Enumerable.Range(first, Math.Max(0, 8 - first))
            .SelectMany(d => Enum.GetValues<MealType>()
                .Where(t => !items.Any(i => i.DayOfWeek == d && i.MealType == t))
                .Select(t => $"{DayNames[d]} {t}"))
            .ToList();
    }

    public async Task<MealPlanPublishViewModel> GetPublishViewAsync(int id)
    {
        var plan = await _db.MealPlans.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("Meal plan not found.");

        return new MealPlanPublishViewModel
        {
            MealPlanId = plan.Id,
            WeekStartDate = plan.WeekStartDate,
            TotalBudget = plan.TotalBudget,
            ExpectedDiners = await ExpectedDinersAsync(),
            EstimatedCost = EstimateWeekCost(plan.Items, await ExpectedDinersAsync()),
            Conflicts = await ScanAllergyConflictsAsync(id),
            MissingServings = MissingServings(plan.WeekStartDate, plan.Items),
            IsPublished = plan.Status == MealPlanStatus.Published
        };
    }

    public async Task PublishAsync(int mealPlanId, bool conflictsAcknowledged)
    {
        var plan = await _db.MealPlans.FirstOrDefaultAsync(p => p.Id == mealPlanId)
            ?? throw new InvalidOperationException("Meal plan not found.");

        var items = await _db.MealPlanItems.Where(i => i.MealPlanId == mealPlanId).ToListAsync();
        var estimate = EstimateWeekCost(items, await ExpectedDinersAsync());
        if (estimate > plan.TotalBudget)
            throw new InvalidOperationException(
                $"This plan is over budget (estimated R{estimate:N2}, budget R{plan.TotalBudget:N2}). Edit the plan before publishing.");
        var empty = MissingServings(plan.WeekStartDate, items);
        if (empty.Count > 0)
            throw new InvalidOperationException(
                $"Every serving time needs at least one meal before publishing. Missing: {string.Join(", ", empty)}.");

        var conflicts = await ScanAllergyConflictsAsync(mealPlanId);
        if (conflicts.Count > 0 && !conflictsAcknowledged)
            throw new InvalidOperationException("You must acknowledge the allergy conflicts before publishing.");

        plan.Status = MealPlanStatus.Published;
        plan.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<ParentMealPlanWeekViewModel> GetWeekForParentAsync(int weekOffset)
    {
        var now = SchoolClock.Now;
        var monday = SchoolClock.WeekStart(now).AddDays(7 * weekOffset);
        var sunday = monday.AddDays(6);

        // Every published plan that serves meals in this week, placed on their actual dates
        var plans = await _db.MealPlans
            .Include(p => p.Items).ThenInclude(i => i.Meal)
            .Where(p => p.Status == MealPlanStatus.Published && p.WeekStartDate <= sunday && p.WeekStartDate >= monday.AddDays(-6))
            .ToListAsync();
        var items = plans.SelectMany(p => p.Items.Select(i => (Date: p.WeekStartDate.Date.AddDays(i.DayOfWeek - 1), Item: i)))
            .Where(x => x.Date >= monday && x.Date <= sunday)
            .ToList();

        var nextMonday = SchoolClock.WeekStart(now).AddDays(7);
        var vm = new ParentMealPlanWeekViewModel
        {
            WeekOffset = weekOffset,
            WeekStartDate = monday,
            Today = now.Date,
            HasPlan = items.Any(),
            NextWeekPublished = await _db.MealPlans.AnyAsync(p => p.Status == MealPlanStatus.Published
                && p.WeekStartDate <= nextMonday.AddDays(6) && p.WeekStartDate >= nextMonday.AddDays(-6))
        };

        for (var d = 0; d < 7; d++)
        {
            var date = monday.AddDays(d);
            vm.Days.Add(new ParentMealPlanDayViewModel
            {
                DayOfWeek = d + 1,
                Date = date,
                DayLabel = date.ToString("dddd"),
                Meals = items.Where(x => x.Date == date).Select(x => x.Item)
                    .OrderBy(i => i.MealType).ThenBy(i => i.Id).ToList()
            });
        }

        // Feedback is only accepted within 24 hours after a meal is served
        foreach (var (date, item) in items)
        {
            var servedAt = TimeSpan.TryParse(item.ServingTime, out var t) ? date.Add(t) : date;
            if (now >= servedAt && now <= servedAt.AddHours(24)) vm.RateableItemIds.Add(item.Id);
        }
        return vm;
    }
}
