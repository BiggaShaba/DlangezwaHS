using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// UC9 — MEAL QUALITY & FEEDBACK
// ─────────────────────────────────────────────────────────────────────────────

public interface IMealFeedbackService
{
    Task<MealPlanItem?> GetMealForFeedbackAsync(int mealPlanItemId);
    bool IsWithinFeedbackWindow(MealPlan plan, MealPlanItem item);
    Task<(bool success, string message)> SubmitFeedbackAsync(int mealPlanItemId, int rating, string? comment, PortionFeedback? portion = null);
    Task<MealFeedbackDashboardViewModel> GetDashboardAsync(int? mealPlanId = null);
    Task<bool> ResolveAlertAsync(int alertId, string notes, string userId);
}

public class MealFeedbackService : IMealFeedbackService
{
    private static readonly string[] DayNames = { "", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;

    public MealFeedbackService(ApplicationDbContext db, UserManager<ApplicationUser> um, IEmailService email)
    {
        _db = db;
        _userManager = um;
        _email = email;
    }

    public async Task<MealPlanItem?> GetMealForFeedbackAsync(int mealPlanItemId)
        => await _db.MealPlanItems.Include(i => i.MealPlan).FirstOrDefaultAsync(i => i.Id == mealPlanItemId);

    public bool IsWithinFeedbackWindow(MealPlan plan, MealPlanItem item)
    {
        var servingDate = plan.WeekStartDate.Date.AddDays(item.DayOfWeek - 1);
        if (TimeSpan.TryParse(item.ServingTime, out var time)) servingDate = servingDate.Add(time);
        var now = SchoolClock.Now;
        return now >= servingDate && now <= servingDate.AddHours(24);
    }

    public async Task<(bool success, string message)> SubmitFeedbackAsync(int mealPlanItemId, int rating, string? comment, PortionFeedback? portion = null)
    {
        var item = await GetMealForFeedbackAsync(mealPlanItemId);
        if (item is null) return (false, "Meal not found.");
        if (!IsWithinFeedbackWindow(item.MealPlan, item))
            return (false, "Feedback can only be submitted within 24 hours of the meal's serving time.");

        _db.MealFeedbacks.Add(new MealFeedback
        {
            MealPlanItemId = mealPlanItemId, Rating = rating, Portion = portion,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()
        });
        await _db.SaveChangesAsync();

        await CheckQualityThresholdAsync(item);
        return (true, "Thank you — your feedback has been submitted anonymously.");
    }

    private async Task CheckQualityThresholdAsync(MealPlanItem item)
    {
        var feedback = await _db.MealFeedbacks.Where(f => f.MealPlanItemId == item.Id).ToListAsync();
        if (feedback.Count < 5) return;

        var average = (decimal)feedback.Average(f => f.Rating);
        if (average >= 2.5m) return;

        var hasAlert = await _db.MealQualityAlerts.AnyAsync(a => a.MealPlanItemId == item.Id && a.ResolvedAt == null);
        if (hasAlert) return;

        _db.MealQualityAlerts.Add(new MealQualityAlert
        {
            MealPlanItemId = item.Id,
            AverageRating = Math.Round(average, 2),
            SubmissionCount = feedback.Count
        });
        await _db.SaveChangesAsync();

        var body = $"<p>The average rating for <strong>{item.MenuDescription}</strong> has dropped to <strong>{average:N1}/5</strong> across {feedback.Count} submissions.</p>";

        var kitchenStaff = await _db.KitchenStaffMembers.Where(k => k.IsActive && k.UserId != null).ToListAsync();
        foreach (var k in kitchenStaff)
        {
            var user = await _userManager.FindByIdAsync(k.UserId!);
            if (user?.Email is not null)
                await _email.SendAsync(user.Email, k.FullName, $"Low Meal Rating — {item.MenuDescription}", $"<p>Dear {k.FullName},</p>{body}");
        }

        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is not null)
                await _email.SendAsync(user.Email, hm.FullName, $"Low Meal Rating — {item.MenuDescription}", $"<p>Dear {hm.FullName},</p>{body}");
        }
    }

    public async Task<MealFeedbackDashboardViewModel> GetDashboardAsync(int? mealPlanId = null)
    {
        var today = SchoolClock.Today;
        var weeks = await _db.MealPlans
            .Where(p => p.Status != MealPlanStatus.Draft && p.WeekStartDate <= today)
            .OrderByDescending(p => p.WeekStartDate)
            .Select(p => new MealPlanWeekOption { Id = p.Id, WeekStartDate = p.WeekStartDate })
            .ToListAsync();

        // Default to the most recent week that has started
        var planId = mealPlanId ?? weeks.FirstOrDefault()?.Id;
        var plan = planId is null ? null : await _db.MealPlans.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == planId);

        var vm = new MealFeedbackDashboardViewModel { Weeks = weeks, SelectedPlanId = plan?.Id, WeekStartDate = plan?.WeekStartDate };

        if (plan is not null)
        {
            var itemIds = plan.Items.Select(i => i.Id).ToList();
            var feedback = await _db.MealFeedbacks.Where(f => itemIds.Contains(f.MealPlanItemId)).ToListAsync();
            var activeAlertIds = await _db.MealQualityAlerts
                .Where(a => itemIds.Contains(a.MealPlanItemId) && a.ResolvedAt == null)
                .Select(a => a.MealPlanItemId).ToListAsync();
            var ordered = await _db.MealPreOrders
                .Where(o => itemIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
                .GroupBy(o => o.MealPlanItemId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
            // Collected = learners who pre-ordered the option and were scanned (walk-ins are flagged separately)
            var orderedPairs = (await _db.MealPreOrders
                .Where(o => itemIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
                .Select(o => new { o.MealPlanItemId, o.LearnerId }).ToListAsync())
                .Select(o => (o.MealPlanItemId, o.LearnerId)).ToHashSet();
            var collected = (await _db.MealAttendances
                .Where(a => itemIds.Contains(a.MealPlanItemId) && (a.Status == MealAttendanceStatus.Present || a.Status == MealAttendanceStatus.Late))
                .Select(a => new { a.MealPlanItemId, a.LearnerId }).ToListAsync())
                .Where(a => orderedPairs.Contains((a.MealPlanItemId, a.LearnerId)))
                .GroupBy(a => a.MealPlanItemId).ToDictionary(g => g.Key, g => g.Count());
            var usage = await _db.IngredientUsageRecords
                .Where(r => itemIds.Contains(r.MealPlanItemId))
                .ToListAsync();

            foreach (var item in plan.Items.OrderBy(i => i.DayOfWeek).ThenBy(i => i.MealType).ThenBy(i => i.Id))
            {
                var fb = feedback.Where(f => f.MealPlanItemId == item.Id).ToList();
                var used = usage.Where(r => r.MealPlanItemId == item.Id).ToList();
                vm.Meals.Add(new MealFeedbackMealRow
                {
                    MealPlanItemId = item.Id,
                    DayOfWeek = item.DayOfWeek,
                    DayLabel = DayNames[item.DayOfWeek],
                    MealType = item.MealType.ToString(),
                    MenuDescription = item.MenuDescription,
                    AverageRating = fb.Count > 0 ? Math.Round((decimal)fb.Average(f => f.Rating), 2) : 0,
                    SubmissionCount = fb.Count,
                    HasActiveAlert = activeAlertIds.Contains(item.Id),
                    TooLittle = fb.Count(f => f.Portion == PortionFeedback.TooLittle),
                    JustRight = fb.Count(f => f.Portion == PortionFeedback.JustRight),
                    TooMuch = fb.Count(f => f.Portion == PortionFeedback.TooMuch),
                    Ordered = ordered.GetValueOrDefault(item.Id),
                    Prepared = used.Sum(r => r.ServingsPrepared),
                    Collected = collected.GetValueOrDefault(item.Id),
                    Leftovers = used.Any(r => r.LeftoverServings.HasValue) ? used.Sum(r => r.LeftoverServings ?? 0) : null
                });
            }

            vm.WeekSubmissions = feedback.Count;
            vm.WeekAverage = feedback.Count > 0 ? Math.Round((decimal)feedback.Average(f => f.Rating), 2) : 0;
            vm.WeekPrepared = vm.Meals.Sum(m => m.Prepared);
            vm.WeekCollected = vm.Meals.Sum(m => m.Collected);
            vm.WeekLeftovers = vm.Meals.Sum(m => m.Leftovers ?? 0);

            var itemLookup = plan.Items.ToDictionary(i => i.Id);
            vm.RecentComments = feedback
                .Where(f => !string.IsNullOrWhiteSpace(f.Comment))
                .OrderByDescending(f => f.SubmittedAt)
                .Select(f => new MealFeedbackCommentRow
                {
                    SubmittedAt = f.SubmittedAt,
                    MealLabel = $"{DayNames[itemLookup[f.MealPlanItemId].DayOfWeek]} {itemLookup[f.MealPlanItemId].MealType} — {itemLookup[f.MealPlanItemId].MenuDescription}",
                    Rating = f.Rating,
                    Comment = f.Comment!,
                    Portion = f.Portion switch
                    {
                        PortionFeedback.TooLittle => "Too little",
                        PortionFeedback.TooMuch => "Too much",
                        PortionFeedback.JustRight => "Just right",
                        _ => null
                    }
                }).ToList();
        }

        vm.Alerts = await _db.MealQualityAlerts
            .Include(a => a.MealPlanItem)
            .OrderBy(a => a.ResolvedAt != null).ThenByDescending(a => a.CreatedAt)
            .Select(a => new MealQualityAlertRow
            {
                Id = a.Id,
                MenuDescription = a.MealPlanItem.MenuDescription,
                AverageRating = a.AverageRating,
                SubmissionCount = a.SubmissionCount,
                CreatedAt = a.CreatedAt,
                Resolved = a.ResolvedAt != null,
                ResolvedAt = a.ResolvedAt,
                ResolutionNotes = a.ResolutionNotes
            })
            .ToListAsync();

        var plans = await _db.MealPlans.Include(p => p.Items)
            .Where(p => p.WeekStartDate <= today)
            .OrderBy(p => p.WeekStartDate).ToListAsync();
        foreach (var p in plans.TakeLast(8))
        {
            var ids = p.Items.Select(i => i.Id).ToList();
            var ratings = await _db.MealFeedbacks.Where(f => ids.Contains(f.MealPlanItemId)).Select(f => f.Rating).ToListAsync();
            if (ratings.Count == 0) continue;
            vm.WeeklyTrend.Add(new WeeklyTrendPoint { WeekLabel = p.WeekStartDate.ToString("dd MMM"), AverageRating = Math.Round((decimal)ratings.Average(), 2) });
        }

        return vm;
    }

    public async Task<bool> ResolveAlertAsync(int alertId, string notes, string userId)
    {
        var alert = await _db.MealQualityAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ResolvedAt == null);
        if (alert is null) return false;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolvedByUserId = userId;
        alert.ResolutionNotes = notes.Trim();
        await _db.SaveChangesAsync();
        return true;
    }
}
