using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// UC7 — KITCHEN SCHEDULE
// Admin picks a meal + a kitchen team. Task times come from the pre-determined
// shift for the meal type (Settings → Boarding), so nobody types times.
// ─────────────────────────────────────────────────────────────────────────────

public interface IKitchenScheduleService
{
    Task<IList<SchedulableMealRow>> GetSchedulableMealsAsync();
    Task<KitchenScheduleCreateViewModel> GetCreateViewModelAsync(int mealPlanItemId, int? teamId = null);
    Task<(bool success, string message)> SaveScheduleAsync(KitchenScheduleCreateViewModel vm);
    Task<IList<KitchenScheduleRow>> GetSchedulesAsync();
    Task PublishScheduleAsync(int scheduleId, string adminUserId);
    Task<IList<KitchenTaskRow>> GetMyTasksAsync(string kitchenStaffUserId);
    Task<(bool success, string message)> UpdateTaskStatusAsync(int taskId, KitchenTaskStatus status, string? delayReason, string kitchenStaffUserId);

    // Planner: upcoming meals by day and serving time, scheduled in one step by picking a team
    Task<KitchenPlannerViewModel> GetPlannerAsync();
    Task<QuickScheduleResult> QuickScheduleAsync(int mealPlanItemId, int teamId, bool acknowledgeUnderstaff);

    // Teams — admins manage every team; head chefs create teams and edit the ones they lead
    Task<KitchenTeamsViewModel> GetTeamsAsync();
    Task<KitchenTeam> SaveTeamAsync(int? id, string name, IList<int> memberIds, int? headChefId);
    Task ToggleTeamAsync(int id);
    Task<int?> GetHeadChefStaffIdAsync(string userId);
    Task<bool> LeadsTeamAsync(string userId, int teamId);
}

public class KitchenScheduleService : IKitchenScheduleService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;

    public KitchenScheduleService(ApplicationDbContext db, UserManager<ApplicationUser> um, IEmailService email)
    {
        _db = db;
        _userManager = um;
        _email = email;
    }

    private static bool TimesOverlap(string aStart, string aEnd, string bStart, string bEnd)
    {
        if (!TimeSpan.TryParse(aStart, out var s1) || !TimeSpan.TryParse(aEnd, out var e1)) return false;
        if (!TimeSpan.TryParse(bStart, out var s2) || !TimeSpan.TryParse(bEnd, out var e2)) return false;
        return s1 < e2 && s2 < e1;
    }

    private async Task<BoardingSettings> SettingsAsync()
        => await _db.BoardingSettings.FirstOrDefaultAsync() ?? new BoardingSettings();

    public static int ShiftMinutes(string start, string serve)
        => TimeSpan.TryParse(start, out var s) && TimeSpan.TryParse(serve, out var e) && e > s
            ? (int)(e - s).TotalMinutes
            : 180;

    /// <summary>Staff needed = portions × prep minutes per serving ÷ shift length, rounded up (minimum 1).</summary>
    public static int RecommendStaff(int portions, decimal prepMinutesPerServing, int shiftMinutes)
    {
        if (portions <= 0 || prepMinutesPerServing <= 0 || shiftMinutes <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(portions * prepMinutesPerServing / shiftMinutes));
    }

    public async Task<IList<SchedulableMealRow>> GetSchedulableMealsAsync()
    {
        var today = SchoolClock.Today;
        var items = await _db.MealPlanItems
            .Include(i => i.MealPlan)
            .Where(i => i.MealPlan.Status == MealPlanStatus.Published && i.MealPlan.WeekStartDate >= today.AddDays(-6))
            .ToListAsync();
        var scheduledIds = await _db.KitchenSchedules.Select(s => s.MealPlanItemId).ToListAsync();

        return items
            .Select(i => new SchedulableMealRow
            {
                MealPlanItemId = i.Id,
                Date = i.MealPlan.WeekStartDate.AddDays(i.DayOfWeek - 1),
                MealType = i.MealType.ToString(),
                MenuDescription = i.MenuDescription,
                AlreadyScheduled = scheduledIds.Contains(i.Id)
            })
            .Where(r => r.Date >= today)
            .OrderBy(r => r.Date).ThenBy(r => r.MealType == "Breakfast" ? 0 : r.MealType == "Lunch" ? 1 : 2).ThenBy(r => r.MenuDescription)
            .ToList();
    }

    private async Task<IList<KitchenTeamOption>> GetTeamOptionsAsync()
    {
        var teams = await _db.KitchenTeams
            .Include(t => t.Members).ThenInclude(m => m.KitchenStaffMember)
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();

        return teams.Select(t => new KitchenTeamOption
        {
            Id = t.Id,
            Name = t.Name,
            Members = t.Members
                .Where(m => m.KitchenStaffMember.IsActive && m.KitchenStaffMember.UserId != null)
                .OrderBy(m => m.KitchenStaffMember.FirstName)
                .Select(m => new StaffOption { Id = m.KitchenStaffMember.UserId!, Name = m.KitchenStaffMember.FullName })
                .ToList()
        }).ToList();
    }

    private async Task<IList<KitchenStaffOption>> GetAllStaffAsync()
        => await _db.KitchenStaffMembers.Where(k => k.IsActive)
            .OrderBy(k => k.FirstName).ThenBy(k => k.LastName)
            .Select(k => new KitchenStaffOption { Id = k.Id, Name = k.FirstName + " " + k.LastName })
            .ToListAsync();

    public async Task<KitchenScheduleCreateViewModel> GetCreateViewModelAsync(int mealPlanItemId, int? teamId = null)
    {
        var item = await _db.MealPlanItems.Include(i => i.MealPlan).Include(i => i.Meal).FirstOrDefaultAsync(i => i.Id == mealPlanItemId)
            ?? throw new InvalidOperationException("Meal not found.");

        var confirmedPortions = await _db.MealPreOrders
            .CountAsync(o => o.MealPlanItemId == mealPlanItemId && o.Status == PreOrderStatus.Confirmed);
        var (start, serve) = (await SettingsAsync()).ShiftFor(item.MealType);
        var prep = item.Meal?.PrepMinutesPerServing ?? 1m;
        var shiftMinutes = ShiftMinutes(start, serve);

        return new KitchenScheduleCreateViewModel
        {
            MealPlanItemId = item.Id,
            MenuDescription = item.MenuDescription,
            ScheduledDate = item.MealPlan.WeekStartDate.AddDays(item.DayOfWeek - 1),
            MealType = item.MealType.ToString(),
            ConfirmedPortions = confirmedPortions,
            ShiftStart = start,
            ShiftEnd = serve,
            PrepMinutesPerServing = prep,
            ShiftMinutes = shiftMinutes,
            LabourMinutes = confirmedPortions * prep,
            StaffRecommended = RecommendStaff(confirmedPortions, prep, shiftMinutes),
            KitchenTeamId = teamId,
            Teams = await GetTeamOptionsAsync(),
            AllStaff = await GetAllStaffAsync()
        };
    }

    public async Task<(bool success, string message)> SaveScheduleAsync(KitchenScheduleCreateViewModel vm)
    {
        var item = await _db.MealPlanItems.Include(i => i.MealPlan).Include(i => i.Meal).FirstOrDefaultAsync(i => i.Id == vm.MealPlanItemId);
        if (item is null) return (false, "Meal not found.");
        if (vm.KitchenTeamId is null) return (false, "Select the kitchen team that will prepare this meal.");

        var team = await _db.KitchenTeams.FirstOrDefaultAsync(t => t.Id == vm.KitchenTeamId);
        if (team is null) return (false, "Kitchen team not found.");

        var (start, end) = (await SettingsAsync()).ShiftFor(item.MealType);
        var scheduledDate = item.MealPlan.WeekStartDate.AddDays(item.DayOfWeek - 1);
        var validTasks = vm.Tasks.Where(t => t.Include && !string.IsNullOrWhiteSpace(t.AssignedToUserId)).ToList();
        if (validTasks.Count == 0) return (false, "Keep at least one team member on this schedule.");

        // Portions come from confirmed pre-orders — never typed in
        var portions = await _db.MealPreOrders
            .CountAsync(o => o.MealPlanItemId == item.Id && o.Status == PreOrderStatus.Confirmed);
        var recommended = RecommendStaff(portions, item.Meal?.PrepMinutesPerServing ?? 1m, ShiftMinutes(start, end));
        if (validTasks.Count < recommended && !vm.UnderstaffAcknowledged)
            return (false, $"{portions} portions need about {recommended} staff for this shift, but only {validTasks.Count} selected. Add staff or confirm the shortfall.");

        // Conflict check: same staff member already working an overlapping shift on the same date
        var existingTasksOnDate = await _db.KitchenTasks
            .Include(t => t.KitchenSchedule)
            .Where(t => t.KitchenSchedule.ScheduledDate == scheduledDate)
            .ToListAsync();

        foreach (var task in validTasks)
        {
            bool conflict = existingTasksOnDate.Any(t => t.AssignedToUserId == task.AssignedToUserId
                && TimesOverlap(t.StartTime, t.EndTime, start, end));
            if (conflict)
            {
                var staffName = (await _userManager.FindByIdAsync(task.AssignedToUserId))?.FullName ?? "This staff member";
                return (false, $"{staffName} is already on an overlapping kitchen shift on {scheduledDate:dd MMM yyyy}. Untick them or choose another team.");
            }
        }

        var schedule = new KitchenSchedule
        {
            MealPlanItemId = vm.MealPlanItemId,
            ScheduledDate = scheduledDate,
            MealType = item.MealType,
            KitchenTeamId = team.Id,
            Status = KitchenScheduleStatus.Scheduled,
            PortionsPlanned = portions,
            StaffRecommended = recommended
        };
        foreach (var task in validTasks)
        {
            schedule.Tasks.Add(new KitchenTask
            {
                AssignedToUserId = task.AssignedToUserId,
                TaskDescription = string.IsNullOrWhiteSpace(task.TaskDescription) ? $"Prepare {item.MenuDescription}" : task.TaskDescription.Trim(),
                StartTime = start,
                EndTime = end
            });
        }
        _db.KitchenSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return (true, $"{team.Name} scheduled for {item.MenuDescription} ({scheduledDate:ddd dd MMM}, {start}–{end}). Publish it to notify the team.");
    }

    public async Task<IList<KitchenScheduleRow>> GetSchedulesAsync()
    {
        var schedules = await _db.KitchenSchedules
            .Include(s => s.MealPlanItem)
            .Include(s => s.KitchenTeam)
            .Include(s => s.Tasks)
            .OrderByDescending(s => s.ScheduledDate).ThenBy(s => s.MealType)
            .ToListAsync();

        return schedules.Select(s => new KitchenScheduleRow
        {
            Id = s.Id,
            ScheduledDate = s.ScheduledDate,
            MealType = s.MealType.ToString(),
            MenuDescription = s.MealPlanItem.MenuDescription,
            TeamName = s.KitchenTeam?.Name,
            Shift = s.Tasks.FirstOrDefault() is { } t ? $"{t.StartTime}–{t.EndTime}" : "",
            Status = s.Status.ToString(),
            IsPublished = s.PublishedAt.HasValue,
            TaskCount = s.Tasks.Count,
            TasksDone = s.Tasks.Count(t => t.Status == KitchenTaskStatus.Done),
            TasksStarted = s.Tasks.Count(t => t.StartedAt != null),
            PortionsPlanned = s.PortionsPlanned,
            StaffRecommended = s.StaffRecommended,
            StartedAt = s.Tasks.Min(t => t.StartedAt),
            CompletedAt = s.Tasks.All(t => t.CompletedAt != null) && s.Tasks.Any() ? s.Tasks.Max(t => t.CompletedAt) : null,
            DelayReason = s.Tasks.Select(t => t.DelayReason).FirstOrDefault(r => r != null)
        }).ToList();
    }

    public async Task PublishScheduleAsync(int scheduleId, string adminUserId)
    {
        var schedule = await _db.KitchenSchedules.Include(s => s.Tasks).Include(s => s.MealPlanItem)
            .FirstOrDefaultAsync(s => s.Id == scheduleId);
        if (schedule is null) return;

        schedule.PublishedAt = DateTime.UtcNow;
        schedule.PublishedByUserId = adminUserId;
        await _db.SaveChangesAsync();

        foreach (var staffUserId in schedule.Tasks.Select(t => t.AssignedToUserId).Distinct())
        {
            var user = await _userManager.FindByIdAsync(staffUserId);
            if (user?.Email is null) continue;
            var myTasks = schedule.Tasks.Where(t => t.AssignedToUserId == staffUserId);
            var listHtml = string.Join("", myTasks.Select(t => $"<li>{t.TaskDescription} ({t.StartTime}–{t.EndTime})</li>"));
            await _email.SendAsync(user.Email, user.FullName,
                $"Kitchen Schedule Published — {schedule.ScheduledDate:dd MMM yyyy}",
                $"<p>Dear {user.FullName},</p><p>You have been assigned the following kitchen task(s) for {schedule.MealPlanItem.MenuDescription} on {schedule.ScheduledDate:dddd, dd MMMM yyyy}:</p><ul>{listHtml}</ul>");
        }
    }

    public async Task<IList<KitchenTaskRow>> GetMyTasksAsync(string kitchenStaffUserId)
    {
        var tasks = await _db.KitchenTasks
            .Include(t => t.KitchenSchedule).ThenInclude(s => s.MealPlanItem)
            .Include(t => t.KitchenSchedule).ThenInclude(s => s.KitchenTeam)
            .Where(t => t.AssignedToUserId == kitchenStaffUserId && t.KitchenSchedule.PublishedAt != null)
            .OrderBy(t => t.KitchenSchedule.ScheduledDate).ThenBy(t => t.StartTime)
            .ToListAsync();

        return tasks.Select(t => new KitchenTaskRow
        {
            Id = t.Id,
            MenuDescription = t.KitchenSchedule.MealPlanItem.MenuDescription,
            ScheduledDate = t.KitchenSchedule.ScheduledDate,
            MealType = t.KitchenSchedule.MealType.ToString(),
            TeamName = t.KitchenSchedule.KitchenTeam?.Name,
            TaskDescription = t.TaskDescription,
            StartTime = t.StartTime,
            EndTime = t.EndTime,
            Status = t.Status.ToString(),
            DelayReason = t.DelayReason,
            Portions = t.KitchenSchedule.PortionsPlanned,
            StartedAt = t.StartedAt,
            CompletedAt = t.CompletedAt
        }).ToList();
    }

    /// <summary>
    /// In Progress records the start time; Done records the finish time (only after the task was started),
    /// which gives the actual preparation time per serving. A delay reason flags the schedule as delayed.
    /// </summary>
    public async Task<(bool success, string message)> UpdateTaskStatusAsync(int taskId, KitchenTaskStatus status, string? delayReason, string kitchenStaffUserId)
    {
        var task = await _db.KitchenTasks.Include(t => t.KitchenSchedule).ThenInclude(s => s.Tasks)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.AssignedToUserId == kitchenStaffUserId);
        if (task is null) return (false, "Task not found.");
        var schedule = task.KitchenSchedule;

        if (!string.IsNullOrWhiteSpace(delayReason))
        {
            task.DelayReason = delayReason.Trim();
            schedule.Status = KitchenScheduleStatus.Delayed;
            await _db.SaveChangesAsync();
            await NotifyAdminsOfDelayAsync(task, delayReason);
            return (true, "Delay reported — the admin has been notified.");
        }

        var now = DateTime.UtcNow;
        string message;
        if (status == KitchenTaskStatus.InProgress)
        {
            if (task.Status == KitchenTaskStatus.Done) return (false, "This task is already done.");
            task.Status = KitchenTaskStatus.InProgress;
            task.StartedAt ??= now;
            if (schedule.Status == KitchenScheduleStatus.Scheduled) schedule.Status = KitchenScheduleStatus.InProgress;
            message = $"Started at {now.AddHours(2):HH:mm}. Press Done when the meal is ready.";
        }
        else if (status == KitchenTaskStatus.Done)
        {
            if (task.StartedAt is null) return (false, "Start the task (In Progress) before marking it done.");
            task.Status = KitchenTaskStatus.Done;
            task.CompletedAt = now;
            if (schedule.Tasks.All(t => t.Status == KitchenTaskStatus.Done) && schedule.Status != KitchenScheduleStatus.Delayed)
                schedule.Status = KitchenScheduleStatus.Completed;
            var minutes = (now - task.StartedAt.Value).TotalMinutes;
            message = schedule.PortionsPlanned > 0
                ? $"Done in {minutes:0} min — {minutes / schedule.PortionsPlanned:0.##} min per serving for {schedule.PortionsPlanned} portions."
                : $"Done in {minutes:0} min.";
        }
        else return (false, "Unknown status.");

        await _db.SaveChangesAsync();
        return (true, message);
    }

    private async Task NotifyAdminsOfDelayAsync(KitchenTask task, string reason)
    {
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins)
        {
            if (admin.Email is null) continue;
            await _email.SendAsync(admin.Email, admin.FullName,
                $"Kitchen Task Delayed — {task.TaskDescription}",
                $"<p>Dear {admin.FullName},</p><p>A kitchen task has been marked as delayed:</p><p><strong>{task.TaskDescription}</strong> ({task.StartTime}–{task.EndTime})</p><p>Reason: {reason}</p>");
        }
    }

    // ── Planner ───────────────────────────────────────────────────────────────

    public async Task<KitchenPlannerViewModel> GetPlannerAsync()
    {
        var today = SchoolClock.Today;
        var settings = await SettingsAsync();
        var items = await _db.MealPlanItems
            .Include(i => i.MealPlan).Include(i => i.Meal)
            .Where(i => i.MealPlan.Status == MealPlanStatus.Published && i.MealPlan.WeekStartDate >= today.AddDays(-6))
            .ToListAsync();
        items = items.Where(i => i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1) >= today).ToList();
        var ids = items.Select(i => i.Id).ToList();

        var portions = await _db.MealPreOrders
            .Where(o => ids.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
            .GroupBy(o => o.MealPlanItemId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var schedules = await _db.KitchenSchedules.Include(s => s.KitchenTeam).Include(s => s.Tasks)
            .Where(s => ids.Contains(s.MealPlanItemId)).ToListAsync();

        var vm = new KitchenPlannerViewModel { Today = today, Teams = await GetTeamOptionsAsync() };
        foreach (var day in items.GroupBy(i => i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1)).OrderBy(g => g.Key))
        {
            var dayVm = new KitchenPlannerDay { Date = day.Key };
            foreach (var service in day.GroupBy(i => i.MealType).OrderBy(g => g.Key))
            {
                var (start, serve) = settings.ShiftFor(service.Key);
                var shiftMinutes = ShiftMinutes(start, serve);
                var serviceVm = new KitchenPlannerService { MealType = service.Key.ToString(), ShiftStart = start, ShiftEnd = serve };
                foreach (var i in service.OrderBy(i => i.MenuDescription))
                {
                    var p = portions.GetValueOrDefault(i.Id);
                    var prep = i.Meal?.PrepMinutesPerServing ?? 1m;
                    serviceVm.Meals.Add(new KitchenPlannerMeal
                    {
                        MealPlanItemId = i.Id,
                        MenuDescription = i.MenuDescription,
                        ImageUrl = i.Meal?.ImagePath,
                        Portions = p,
                        PrepMinutesPerServing = prep,
                        StaffRecommended = RecommendStaff(p, prep, shiftMinutes),
                        Scheduled = schedules.Where(s => s.MealPlanItemId == i.Id).OrderBy(s => s.Id)
                            .Select(s => ToPlannerSchedule(s, prep, start, serve)).ToList()
                    });
                }
                dayVm.Services.Add(serviceVm);
            }
            vm.Days.Add(dayVm);
        }
        return vm;
    }

    // Estimated prep time = portions × prep minutes per serving, shared across the team's staff
    private static KitchenPlannerSchedule ToPlannerSchedule(KitchenSchedule s, decimal prepPerServing, string start, string serve) => new()
    {
        ScheduleId = s.Id,
        TeamName = s.KitchenTeam?.Name ?? "Team",
        StaffCount = s.Tasks.Count,
        StaffRecommended = s.StaffRecommended,
        Shift = $"{start}–{serve}",
        EstimatedMinutes = s.Tasks.Count > 0 ? (int)Math.Ceiling(s.PortionsPlanned * prepPerServing / s.Tasks.Count) : 0,
        IsPublished = s.PublishedAt.HasValue,
        Status = s.Status.ToString()
    };

    public async Task<QuickScheduleResult> QuickScheduleAsync(int mealPlanItemId, int teamId, bool acknowledgeUnderstaff)
    {
        var team = await _db.KitchenTeams.Include(t => t.Members).ThenInclude(m => m.KitchenStaffMember)
            .FirstOrDefaultAsync(t => t.Id == teamId && t.IsActive);
        if (team is null) return new QuickScheduleResult { Message = "That team isn't available." };
        if (await _db.KitchenSchedules.AnyAsync(s => s.MealPlanItemId == mealPlanItemId && s.KitchenTeamId == teamId))
            return new QuickScheduleResult { Message = $"{team.Name} is already scheduled for this meal." };

        var members = team.Members.Where(m => m.KitchenStaffMember.IsActive && m.KitchenStaffMember.UserId != null).ToList();
        var vm = await GetCreateViewModelAsync(mealPlanItemId, teamId);
        if (members.Count < vm.StaffRecommended && !acknowledgeUnderstaff)
            return new QuickScheduleResult
            {
                NeedsConfirm = true,
                Message = $"{vm.ConfirmedPortions} portions need about {vm.StaffRecommended} staff, but {team.Name} has {members.Count}. Schedule anyway?"
            };

        vm.UnderstaffAcknowledged = true;
        vm.Tasks = members.Select(m => new KitchenTaskInput
        {
            AssignedToUserId = m.KitchenStaffMember.UserId!,
            Include = true,
            TaskDescription = $"Prepare {vm.MenuDescription}"
        }).ToList();

        var (success, message) = await SaveScheduleAsync(vm);
        if (!success) return new QuickScheduleResult { Message = message };

        var saved = await _db.KitchenSchedules.Include(s => s.KitchenTeam).Include(s => s.Tasks)
            .Where(s => s.MealPlanItemId == mealPlanItemId && s.KitchenTeamId == teamId).OrderByDescending(s => s.Id).FirstAsync();
        return new QuickScheduleResult
        {
            Success = true,
            Message = message,
            Schedule = ToPlannerSchedule(saved, vm.PrepMinutesPerServing, vm.ShiftStart, vm.ShiftEnd)
        };
    }

    // ── Teams ─────────────────────────────────────────────────────────────────

    public async Task<KitchenTeamsViewModel> GetTeamsAsync()
    {
        var teams = await _db.KitchenTeams
            .Include(t => t.Members).ThenInclude(m => m.KitchenStaffMember)
            .Include(t => t.HeadChef)
            .OrderByDescending(t => t.IsActive).ThenBy(t => t.Name)
            .ToListAsync();

        return new KitchenTeamsViewModel
        {
            Teams = teams.Select(t => new KitchenTeamRow
            {
                Id = t.Id,
                Name = t.Name,
                IsActive = t.IsActive,
                HeadChefId = t.HeadChefId,
                HeadChefName = t.HeadChef?.FullName,
                MemberIds = t.Members.Select(m => m.KitchenStaffMemberId).ToList(),
                MemberNames = t.Members.Select(m => m.KitchenStaffMember.FullName).OrderBy(n => n).ToList()
            }).ToList(),
            AllStaff = await GetAllStaffAsync()
        };
    }

    public async Task<KitchenTeam> SaveTeamAsync(int? id, string name, IList<int> memberIds, int? headChefId)
    {
        memberIds = memberIds.ToList();
        // The head chef is always part of the team they lead
        if (headChefId is int chef && !memberIds.Contains(chef)) memberIds.Add(chef);
        name = name?.Trim() ?? "";
        if (name.Length == 0) throw new InvalidOperationException("Give the team a name.");
        if (memberIds.Count == 0) throw new InvalidOperationException("Select at least one staff member for the team.");
        if (await _db.KitchenTeams.AnyAsync(t => t.Name == name && t.Id != (id ?? 0)))
            throw new InvalidOperationException($"A team called \"{name}\" already exists.");

        KitchenTeam team;
        if (id.HasValue)
        {
            team = await _db.KitchenTeams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == id)
                ?? throw new InvalidOperationException("Team not found.");
            _db.KitchenTeamMembers.RemoveRange(team.Members);
            team.Members.Clear();
        }
        else
        {
            team = new KitchenTeam();
            _db.KitchenTeams.Add(team);
        }

        team.Name = name;
        team.HeadChefId = headChefId is int h && await _db.KitchenStaffMembers.AnyAsync(k => k.Id == h && k.IsActive) ? h : null;
        var validIds = await _db.KitchenStaffMembers.Where(k => memberIds.Contains(k.Id)).Select(k => k.Id).ToListAsync();
        foreach (var staffId in validIds.Distinct())
            team.Members.Add(new KitchenTeamMember { KitchenStaffMemberId = staffId });

        await _db.SaveChangesAsync();
        return team;
    }

    public async Task<int?> GetHeadChefStaffIdAsync(string userId)
        => await _db.KitchenTeams.Where(t => t.IsActive && t.HeadChef != null && t.HeadChef.UserId == userId)
            .Select(t => t.HeadChefId).FirstOrDefaultAsync();

    public async Task<bool> LeadsTeamAsync(string userId, int teamId)
        => await _db.KitchenTeams.AnyAsync(t => t.Id == teamId && t.HeadChef != null && t.HeadChef.UserId == userId);

    public async Task ToggleTeamAsync(int id)
    {
        var team = await _db.KitchenTeams.FirstOrDefaultAsync(t => t.Id == id);
        if (team is null) return;
        team.IsActive = !team.IsActive;
        await _db.SaveChangesAsync();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// UC8 — MEAL ATTENDANCE TRACKING
// The scanner runs per meal service (plan + day + meal type), which may have
// several options. Each scan tells the server what that learner ordered.
// ─────────────────────────────────────────────────────────────────────────────

public interface IMealAttendanceService
{
    Task<IList<TodayMealOption>> GetTodaysMealsAsync();
    Task<string?> DescribeNextMealServiceAsync();
    Task<MealScanResult> RecordScanAsync(int mealPlanItemId, string qrHash, string scannedByUserId);
    Task<MealAttendanceLiveViewModel> GetLiveDashboardAsync(int mealPlanItemId);
    Task RecordManualOverrideAsync(int mealPlanItemId, int learnerId, MealAttendanceStatus status, string reason, string scannedByUserId);
    Task<IList<MealAttendanceHistoryRow>> GetHistoryAsync(DateTime date);
    Task<(int Absent, ServiceUsageResult Usage)> FinalizeAttendanceAsync(int mealPlanItemId, string userId);
}

public class MealAttendanceService : IMealAttendanceService
{
    private static readonly string[] DayNames = { "", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly IInventoryService _inventorySvc;

    public MealAttendanceService(ApplicationDbContext db, UserManager<ApplicationUser> um, IEmailService email, IInventoryService inventorySvc)
    {
        _db = db;
        _userManager = um;
        _email = email;
        _inventorySvc = inventorySvc;
    }

    /// <summary>All options in the same meal service as the given item (same plan, day and meal type).</summary>
    private async Task<List<MealPlanItem>> GetServiceItemsAsync(int mealPlanItemId)
    {
        var item = await _db.MealPlanItems.FirstOrDefaultAsync(i => i.Id == mealPlanItemId)
            ?? throw new InvalidOperationException("Meal not found.");
        return await _db.MealPlanItems
            .Include(i => i.Meal).Include(i => i.MealPlan)
            .Where(i => i.MealPlanId == item.MealPlanId && i.DayOfWeek == item.DayOfWeek && i.MealType == item.MealType)
            .OrderBy(i => i.Id)
            .ToListAsync();
    }

    private async Task<TimeSpan> ServingDurationAsync()
        => ((await _db.BoardingSettings.FirstOrDefaultAsync()) ?? new BoardingSettings()).ServingDuration;

    // Learner id → active allergies, for the learners given
    private async Task<Dictionary<int, List<string>>> AllergiesForAsync(IList<int> learnerIds)
        => (await _db.DietaryItems
                .Where(d => learnerIds.Contains(d.DietaryProfile.LearnerId) && d.DietaryProfile.Status == DietaryProfileStatus.Active)
                .Select(d => new { d.DietaryProfile.LearnerId, Text = d.Name + (d.Severity != null ? " (" + d.Severity.ToString() + ")" : "") })
                .ToListAsync())
            .GroupBy(x => x.LearnerId).ToDictionary(g => g.Key, g => g.Select(x => x.Text).Distinct().ToList());

    // Published meals placed on their actual calendar date (plan start + day offset)
    private async Task<List<(DateTime Date, MealPlanItem Item)>> PublishedMealsBetweenAsync(DateTime from, DateTime to)
    {
        var plans = await _db.MealPlans.Include(p => p.Items)
            .Where(p => p.Status == MealPlanStatus.Published && p.WeekStartDate <= to && p.WeekStartDate >= from.AddDays(-6))
            .ToListAsync();
        return plans.SelectMany(p => p.Items.Select(i => (Date: p.WeekStartDate.Date.AddDays(i.DayOfWeek - 1), Item: i)))
            .Where(x => x.Date >= from && x.Date <= to)
            .ToList();
    }

    public async Task<IList<TodayMealOption>> GetTodaysMealsAsync()
    {
        var today = SchoolClock.Today;
        var meals = await PublishedMealsBetweenAsync(today, today);
        var ids = meals.Select(m => m.Item.Id).ToList();
        var orderCounts = await _db.MealPreOrders
            .Where(o => ids.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
            .GroupBy(o => o.MealPlanItemId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var duration = await ServingDurationAsync();

        return meals.Select(m => m.Item)
            .GroupBy(i => (i.MealPlanId, i.MealType))
            .OrderBy(g => g.Key.MealType).ThenBy(g => g.Key.MealPlanId)
            .Select(g =>
            {
                var options = g.OrderBy(i => i.Id).ToList();
                var start = TimeSpan.TryParse(options[0].ServingTime, out var t) ? today.Add(t) : today;
                return new TodayMealOption
                {
                    MealPlanItemId = options[0].Id,
                    MealType = g.Key.MealType.ToString(),
                    MenuDescription = string.Join(" / ", options.Select(i => i.MenuDescription)),
                    ServingTime = options[0].ServingTime,
                    OptionCount = options.Count,
                    PreOrderCount = options.Sum(i => orderCounts.GetValueOrDefault(i.Id)),
                    ServingStart = start,
                    ServingEnd = start + duration,
                    IsFinished = options.Any(i => i.ScanningFinishedAt != null)
                };
            })
            .ToList();
    }

    public async Task<string?> DescribeNextMealServiceAsync()
    {
        var today = SchoolClock.Today;
        var upcoming = await PublishedMealsBetweenAsync(today.AddDays(1), today.AddDays(56));
        if (upcoming.Count == 0) return null;

        var nextDate = upcoming.Min(m => m.Date);
        var day = upcoming.Where(m => m.Date == nextDate).Select(m => m.Item).ToList();
        var ids = day.Select(i => i.Id).ToList();
        var orders = await _db.MealPreOrders.CountAsync(o => ids.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed);
        var services = string.Join(", ", day.GroupBy(i => i.MealType).OrderBy(g => g.Key)
            .Select(g => $"{g.Key} {g.First().ServingTime}"));
        return $"{nextDate:dddd dd MMMM}: {services} ({orders} pre-order(s) so far)";
    }

    public async Task<MealScanResult> RecordScanAsync(int mealPlanItemId, string qrHash, string scannedByUserId)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.BoardingQrCode == qrHash);
        if (learner is null) return new MealScanResult { Success = false, Message = "QR code not recognised." };

        var serviceItems = await GetServiceItemsAsync(mealPlanItemId);
        var serviceIds = serviceItems.Select(i => i.Id).ToList();
        if (serviceItems.Any(i => i.ScanningFinishedAt != null))
            return new MealScanResult { Success = false, LearnerName = learner.FullName, Message = "Scanning for this meal has finished." };

        var allergies = (await AllergiesForAsync(new[] { learner.Id })).GetValueOrDefault(learner.Id) ?? new List<string>();
        var room = await _db.RoomAllocations.Where(r => r.LearnerId == learner.Id && r.IsActive)
            .Select(r => r.Room.Name).FirstOrDefaultAsync();

        var order = await _db.MealPreOrders
            .Where(o => serviceIds.Contains(o.MealPlanItemId) && o.LearnerId == learner.Id && o.Status == PreOrderStatus.Confirmed)
            .OrderByDescending(o => o.OrderedAt)
            .FirstOrDefaultAsync();
        var orderedItem = order is null ? null : serviceItems.First(i => i.Id == order.MealPlanItemId);

        var result = new MealScanResult
        {
            LearnerName = learner.FullName,
            HasOrder = order is not null,
            OrderedMeal = orderedItem?.MenuDescription,
            PortionSize = order?.PortionSize.ToString(),
            ImageUrl = orderedItem?.Meal?.ImagePath,
            Allergies = allergies,
            Room = room
        };

        var existing = await _db.MealAttendances.FirstOrDefaultAsync(a => serviceIds.Contains(a.MealPlanItemId) && a.LearnerId == learner.Id);
        if (existing is not null)
        {
            result.Success = false;
            result.CollectedAt = existing.ScannedAt is { } at ? SchoolClock.FromUtc(at).ToString("HH:mm") : null;
            result.Message = result.CollectedAt is null ? "Already served for this meal." : $"Already served at {result.CollectedAt}.";
            return result;
        }

        var scannedAt = DateTime.UtcNow;
        _db.MealAttendances.Add(new MealAttendance
        {
            // Record against the option they ordered, otherwise the service's main item
            MealPlanItemId = orderedItem?.Id ?? mealPlanItemId,
            LearnerId = learner.Id,
            Status = order is not null ? MealAttendanceStatus.Present : MealAttendanceStatus.Unauthorised,
            ScannedAt = scannedAt,
            ScannedByUserId = scannedByUserId
        });
        await _db.SaveChangesAsync();
        result.CollectedAt = SchoolClock.FromUtc(scannedAt).ToString("HH:mm");

        result.Success = true;
        result.Message = order is not null
            ? "Serve the meal shown."
            : "No pre-order found for this learner — flagged for follow-up.";
        return result;
    }

    public async Task<MealAttendanceLiveViewModel> GetLiveDashboardAsync(int mealPlanItemId)
    {
        var serviceItems = await GetServiceItemsAsync(mealPlanItemId);
        var serviceIds = serviceItems.Select(i => i.Id).ToList();
        var first = serviceItems.First();

        var orders = await _db.MealPreOrders
            .Where(o => serviceIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
            .Select(o => new { o.LearnerId, o.MealPlanItemId })
            .ToListAsync();

        var attendances = await _db.MealAttendances.Include(a => a.Learner)
            .Where(a => serviceIds.Contains(a.MealPlanItemId)).ToListAsync();
        // Learners marked absent when scanning finished still count as "didn't collect"
        var arrivedIds = attendances.Where(a => a.Status != MealAttendanceStatus.Absent).Select(a => a.LearnerId).ToHashSet();

        var notYetArrivedIds = orders.Select(o => o.LearnerId).Where(id => !arrivedIds.Contains(id)).Distinct().ToList();
        var notYetArrivedNames = await _db.Learners.Where(l => notYetArrivedIds.Contains(l.Id))
            .OrderBy(l => l.FirstName).Select(l => l.FirstName + " " + l.LastName).ToListAsync();

        // Everyone scanned so far (absences recorded at finish have no scan time)
        var scanned = attendances.Where(a => a.ScannedAt != null).OrderByDescending(a => a.ScannedAt).ToList();
        var scannedIds = scanned.Select(a => a.LearnerId).Distinct().ToList();
        var allergies = await AllergiesForAsync(scannedIds);
        var portions = await _db.MealPreOrders
            .Where(o => serviceIds.Contains(o.MealPlanItemId) && scannedIds.Contains(o.LearnerId) && o.Status == PreOrderStatus.Confirmed)
            .Select(o => new { o.LearnerId, o.PortionSize }).ToListAsync();

        var servingStart = MealOrderService.ComputeServingDateTime(first.MealPlan, first);
        var servingEnd = servingStart + await ServingDurationAsync();
        var finishedAt = serviceItems.Select(i => i.ScanningFinishedAt).FirstOrDefault(f => f != null);
        var leftovers = finishedAt is null ? 0 : await _db.IngredientUsageRecords
            .Where(r => serviceIds.Contains(r.MealPlanItemId)).SumAsync(r => r.LeftoverServings ?? 0);

        return new MealAttendanceLiveViewModel
        {
            MealPlanItemId = first.Id,
            MenuDescription = string.Join(" / ", serviceItems.Select(i => i.MenuDescription)),
            MealType = first.MealType.ToString(),
            DayLabel = DayNames[first.DayOfWeek],
            TotalExpected = orders.Count,
            ScannedCount = attendances.Count(a => a.Status is MealAttendanceStatus.Present or MealAttendanceStatus.Late),
            NotYetArrived = notYetArrivedNames,
            FlaggedCount = attendances.Count(a => a.Status == MealAttendanceStatus.Unauthorised),
            IsFinalised = finishedAt is not null,
            FinishedAt = finishedAt is { } f ? SchoolClock.FromUtc(f) : null,
            Leftovers = leftovers,
            ServingStart = servingStart,
            ServingEnd = servingEnd,
            IsServingOver = SchoolClock.Now >= servingEnd,
            Collected = scanned.Select(a => new MealCollectedRow
            {
                LearnerName = a.Learner.FullName,
                Meal = a.Status == MealAttendanceStatus.Unauthorised
                    ? "No pre-order"
                    : serviceItems.First(i => i.Id == a.MealPlanItemId).MenuDescription,
                PortionSize = portions.FirstOrDefault(o => o.LearnerId == a.LearnerId)?.PortionSize.ToString(),
                CollectedAt = SchoolClock.FromUtc(a.ScannedAt!.Value).ToString("HH:mm"),
                HasOrder = a.Status != MealAttendanceStatus.Unauthorised,
                Allergies = allergies.GetValueOrDefault(a.LearnerId) ?? new List<string>()
            }).ToList(),
            Options = serviceItems.Select(i => new MealServiceOptionRow
            {
                MealPlanItemId = i.Id,
                MenuDescription = i.MenuDescription,
                ImageUrl = i.Meal?.ImagePath,
                Ordered = orders.Count(o => o.MealPlanItemId == i.Id),
                Served = attendances.Count(a => a.MealPlanItemId == i.Id && a.Status is MealAttendanceStatus.Present or MealAttendanceStatus.Late)
            }).ToList()
        };
    }

    public async Task RecordManualOverrideAsync(int mealPlanItemId, int learnerId, MealAttendanceStatus status, string reason, string scannedByUserId)
    {
        var existing = await _db.MealAttendances.FirstOrDefaultAsync(a => a.MealPlanItemId == mealPlanItemId && a.LearnerId == learnerId);
        if (existing is not null)
        {
            existing.Status = status;
            existing.IsManualOverride = true;
            existing.OverrideReason = reason;
            existing.ScannedByUserId = scannedByUserId;
        }
        else
        {
            _db.MealAttendances.Add(new MealAttendance
            {
                MealPlanItemId = mealPlanItemId,
                LearnerId = learnerId,
                Status = status,
                ScannedAt = DateTime.UtcNow,
                ScannedByUserId = scannedByUserId,
                IsManualOverride = true,
                OverrideReason = reason
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<IList<MealAttendanceHistoryRow>> GetHistoryAsync(DateTime date)
    {
        var plan = await _db.MealPlans.Include(p => p.Items)
            .Where(p => p.WeekStartDate <= date && p.WeekStartDate.AddDays(6) >= date)
            .OrderByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync();
        if (plan is null) return new List<MealAttendanceHistoryRow>();

        var dayOfWeek = (int)(date.Date - plan.WeekStartDate.Date).TotalDays + 1;
        var itemIds = plan.Items.Where(i => i.DayOfWeek == dayOfWeek).Select(i => i.Id).ToList();

        var attendances = await _db.MealAttendances.Include(a => a.Learner).Include(a => a.MealPlanItem)
            .Where(a => itemIds.Contains(a.MealPlanItemId))
            .OrderBy(a => a.MealPlanItem.MealType)
            .ToListAsync();

        return attendances.Select(a => new MealAttendanceHistoryRow
        {
            LearnerName = a.Learner.FullName,
            MenuDescription = a.MealPlanItem.MenuDescription,
            MealType = a.MealPlanItem.MealType.ToString(),
            Status = a.Status.ToString(),
            ScannedAt = a.ScannedAt,
            IsManualOverride = a.IsManualOverride
        }).ToList();
    }

    /// <summary>
    /// Marks learners who ordered but never arrived as absent and deducts the ingredients
    /// used for each option from inventory. Returns a summary of what was deducted.
    /// </summary>
    public async Task<(int Absent, ServiceUsageResult Usage)> FinalizeAttendanceAsync(int mealPlanItemId, string userId)
    {
        var serviceItems = await GetServiceItemsAsync(mealPlanItemId);
        var serviceIds = serviceItems.Select(i => i.Id).ToList();

        var orders = await _db.MealPreOrders
            .Where(o => serviceIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed)
            .Select(o => new { o.LearnerId, o.MealPlanItemId })
            .ToListAsync();

        var alreadyRecordedIds = await _db.MealAttendances
            .Where(a => serviceIds.Contains(a.MealPlanItemId))
            .Select(a => a.LearnerId)
            .ToListAsync();

        var missing = orders.Where(o => !alreadyRecordedIds.Contains(o.LearnerId)).ToList();
        foreach (var o in missing)
            _db.MealAttendances.Add(new MealAttendance { MealPlanItemId = o.MealPlanItemId, LearnerId = o.LearnerId, Status = MealAttendanceStatus.Absent });
        await _db.SaveChangesAsync();

        foreach (var learnerId in missing.Select(m => m.LearnerId).Distinct())
            await CheckConsecutiveAbsencesAsync(learnerId);

        // Servings prepared, leftovers and the stock deduction are recorded automatically
        var usage = await _inventorySvc.RecordServiceUsageAsync(serviceIds, userId);

        var finishedAt = DateTime.UtcNow;
        foreach (var item in serviceItems.Where(i => i.ScanningFinishedAt == null)) item.ScanningFinishedAt = finishedAt;
        await _db.SaveChangesAsync();
        return (missing.Count, usage);
    }

    private async Task CheckConsecutiveAbsencesAsync(int learnerId)
    {
        var recent = await _db.MealAttendances
            .Where(a => a.LearnerId == learnerId)
            .OrderByDescending(a => a.Id)
            .Take(3)
            .ToListAsync();

        if (recent.Count < 3 || recent.Any(a => a.Status != MealAttendanceStatus.Absent)) return;

        var hasUnresolvedAlert = await _db.MealAbsenceAlerts.AnyAsync(a => a.LearnerId == learnerId && a.ResolvedAt == null);
        if (hasUnresolvedAlert) return;

        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId);
        if (learner is null) return;

        var housemaster = await _db.Housemasters.FirstOrDefaultAsync(h => h.IsActive && h.UserId != null);

        _db.MealAbsenceAlerts.Add(new MealAbsenceAlert
        {
            LearnerId = learnerId,
            ConsecutiveMissedMeals = 3,
            AlertedHousemasterId = housemaster?.Id
        });
        await _db.SaveChangesAsync();

        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is null) continue;
            await _email.SendAsync(user.Email, hm.FullName,
                $"Meal Absence Alert — {learner.FullName}",
                $"<p>Dear {hm.FullName},</p><p><strong>{learner.FullName}</strong> has missed 3 consecutive meals. Please follow up.</p>");
        }
    }
}
