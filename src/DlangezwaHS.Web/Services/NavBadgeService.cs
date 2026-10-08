using System.Security.Claims;
using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// NAVIGATION "NEW" DOTS
// A menu item shows a dot when records the user should check were added after
// they last opened that page. Opening the page clears the dot — the visit time
// is kept in a per-user cookie by NavSeenFilter.
// ─────────────────────────────────────────────────────────────────────────────

public class NavBadgeService
{
    // Pages that can show a dot ("Controller.Action"); opening one marks it as read
    public static readonly HashSet<string> SeenPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin.Applications", "Admin.KitchenSchedule",
        "Boarding.LeaveRequests", "Boarding.DietaryProfiles", "Boarding.Dashboard",
        "KitchenStaff.DietaryProfiles", "KitchenStaff.PreOrders", "KitchenStaff.FeedbackDashboard",
        "KitchenStaff.MyTasks", "KitchenStaff.Inventory",
        "Learner.MealPlan"
    };

    private const string CookiePrefix = "dhs_seen_";
    // First visit on this browser: treat the last week as new
    private static readonly TimeSpan DefaultLookBack = TimeSpan.FromDays(7);

    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;

    public NavBadgeService(ApplicationDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    /// <summary>Pages ("Controller.Action") that have something new for the signed-in user.</summary>
    public async Task<HashSet<string>> GetNewPagesAsync(ClaimsPrincipal user)
    {
        var dots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return dots;

        async Task Check(string page, Func<DateTime, Task<bool>> hasNewSince)
        {
            if (await hasNewSince(LastSeen(page, userId))) dots.Add(page);
        }

        if (user.IsInRole("Admin"))
        {
            await Check("Admin.Applications", seen => _db.Applications.AnyAsync(a => a.SubmittedAt > seen));
            // Delays have no timestamp of their own, so compare with how many were delayed at the last visit
            var delayed = await _db.KitchenSchedules.CountAsync(s => s.Status == KitchenScheduleStatus.Delayed);
            if (delayed > LastSeenCount("Admin.KitchenSchedule", userId)) dots.Add("Admin.KitchenSchedule");
        }
        else if (user.IsInRole("Housemaster"))
        {
            await Check("Boarding.LeaveRequests", seen => _db.LeaveRequests.AnyAsync(l => l.CreatedAt > seen));
            await Check("Boarding.DietaryProfiles", seen => _db.DietaryProfiles
                .AnyAsync(p => p.Status == DietaryProfileStatus.Active && p.SubmittedAt > seen));
            await Check("Boarding.Dashboard", seen => _db.MealAbsenceAlerts.AnyAsync(a => a.AlertSentAt > seen));
        }
        else if (user.IsInRole("KitchenStaff"))
        {
            await Check("KitchenStaff.DietaryProfiles", seen => _db.DietaryProfiles
                .AnyAsync(p => p.Status == DietaryProfileStatus.Active && p.SubmittedAt > seen));
            await Check("KitchenStaff.PreOrders", seen => _db.MealPreOrders
                .AnyAsync(o => o.Status != PreOrderStatus.Cancelled && o.OrderedAt > seen));
            await Check("KitchenStaff.FeedbackDashboard", async seen =>
                await _db.MealFeedbacks.AnyAsync(f => f.SubmittedAt > seen)
                || await _db.MealQualityAlerts.AnyAsync(a => a.CreatedAt > seen));
            await Check("KitchenStaff.MyTasks", seen => _db.KitchenTasks
                .AnyAsync(t => t.AssignedToUserId == userId && t.KitchenSchedule.PublishedAt > seen));
            await Check("KitchenStaff.Inventory", seen => _db.Ingredients
                .AnyAsync(i => i.CurrentStock < i.MinimumStock && i.LastUpdatedAt > seen));
        }
        else if (user.IsInRole("Learner"))
        {
            await Check("Learner.MealPlan", seen => _db.MealPlans
                .AnyAsync(p => p.Status == MealPlanStatus.Published && p.PublishedAt > seen));
        }

        return dots;
    }

    // Cookie value: "{userId}|{utc ticks}|{count}" — count is only used for delayed schedules
    private string[]? ReadCookie(string page, string userId)
    {
        var parts = _http.HttpContext?.Request.Cookies[CookiePrefix + page]?.Split('|');
        return parts is { Length: >= 2 } && parts[0] == userId ? parts : null;
    }

    private DateTime LastSeen(string page, string userId)
    {
        if (_http.HttpContext?.Items[CookiePrefix + page] is DateTime justNow) return justNow;
        return ReadCookie(page, userId) is { } parts && long.TryParse(parts[1], out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : DateTime.UtcNow - DefaultLookBack;
    }

    private int LastSeenCount(string page, string userId)
    {
        if (_http.HttpContext?.Items[CookiePrefix + page] is DateTime) return int.MaxValue;
        return ReadCookie(page, userId) is { Length: 3 } parts && int.TryParse(parts[2], out var n) ? n : 0;
    }

    internal static async Task MarkSeenAsync(HttpContext ctx, string page, string userId)
    {
        var now = DateTime.UtcNow;
        var count = 0;
        if (page.Equals("Admin.KitchenSchedule", StringComparison.OrdinalIgnoreCase))
        {
            var db = ctx.RequestServices.GetRequiredService<ApplicationDbContext>();
            count = await db.KitchenSchedules.CountAsync(s => s.Status == KitchenScheduleStatus.Delayed);
        }
        ctx.Items[CookiePrefix + page] = now;
        ctx.Response.Cookies.Append(CookiePrefix + page, $"{userId}|{now.Ticks}|{count}", new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
    }
}

/// <summary>Records when a user opens a page that can show a "new" dot.</summary>
public class NavSeenFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method)
            && context.ActionDescriptor is ControllerActionDescriptor action
            && context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
        {
            var page = $"{action.ControllerName}.{action.ActionName}";
            if (NavBadgeService.SeenPages.Contains(page))
                await NavBadgeService.MarkSeenAsync(context.HttpContext, page, userId);
        }
        await next();
    }
}
