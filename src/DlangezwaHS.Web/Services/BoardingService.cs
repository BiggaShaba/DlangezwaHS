using System.Security.Cryptography;
using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface IBoardingService
{
    // QR badge
    Task<string> GetOrCreateLearnerQrCodeAsync(int learnerId);

    // UC1 / UC2 — scanning
    Task<BoardingScanResult> ProcessScanAsync(string qrHash);

    // UC2 — leave requests
    Task<LeaveRequest> CreateLeaveRequestAsync(int learnerId, string parentUserId,
        string destination, string purpose, DateTime departureDate, DateTime expectedReturnDate);
    Task<IList<LeaveRequestRow>> GetPendingLeaveRequestsAsync();
    Task<IList<LeaveRequestRow>> GetAllLeaveRequestsAsync();
    Task ApproveLeaveRequestAsync(int leaveRequestId, string housemasterUserId, string? notes);
    Task RejectLeaveRequestAsync(int leaveRequestId, string housemasterUserId, string reason);
    Task<IList<ParentLeaveRequestRow>> GetLeaveRequestsForLearnerAsync(int learnerId);

    // Dashboards / reporting
    Task<IList<MovementLogRow>> GetMovementLogAsync(DateTime date);
    Task<BoardingDashboardViewModel> GetDashboardAsync();

    // UC10 — Meal compliance report
    Task<MealComplianceReportDto> GenerateComplianceReportAsync(DateTime from, DateTime to);
    Task<string> ArchiveReportAsync(DateTime from, DateTime to, byte[] pdfBytes, string userId, string webRootPath);
    Task<IList<ArchivedReportRow>> GetArchivedReportsAsync();
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class BoardingService : IBoardingService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly IMealPlanService _mealPlanSvc;

    public BoardingService(ApplicationDbContext db, UserManager<ApplicationUser> um, IEmailService email, IMealPlanService mealPlanSvc)
    {
        _db = db;
        _userManager = um;
        _email = email;
        _mealPlanSvc = mealPlanSvc;
    }

    private static string GenerateHash() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLower();

    // ── QR badge ─────────────────────────────────────────────────────────────

    public async Task<string> GetOrCreateLearnerQrCodeAsync(int learnerId)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == learnerId)
            ?? throw new InvalidOperationException("Learner not found.");

        if (string.IsNullOrEmpty(learner.BoardingQrCode))
        {
            learner.BoardingQrCode = GenerateHash();
            await _db.SaveChangesAsync();
        }
        return learner.BoardingQrCode;
    }

    // ── UC1 / UC2 — scanning ─────────────────────────────────────────────────

    public async Task<BoardingScanResult> ProcessScanAsync(string qrHash)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.BoardingQrCode == qrHash);
        if (learner is null)
            return new BoardingScanResult { IsValid = false, Status = "Invalid", Message = "QR code not recognised." };

        var lastMovement = await _db.BoardingMovements
            .Where(m => m.LearnerId == learner.Id)
            .OrderByDescending(m => m.ScannedAt)
            .FirstOrDefaultAsync();

        bool isCurrentlyOut = lastMovement is not null && lastMovement.MovementType == MovementType.CheckOut;

        return isCurrentlyOut
            ? await ProcessCheckInAsync(learner, lastMovement!, qrHash)
            : await ProcessCheckOutAsync(learner, qrHash);
    }

    private async Task<BoardingScanResult> ProcessCheckInAsync(Learner learner, BoardingMovement openCheckOut, string qrHash)
    {
        var now = DateTime.UtcNow;
        var expected = openCheckOut.ExpectedReturnTime;
        bool late = expected.HasValue && now > expected.Value;
        int lateMinutes = late ? (int)Math.Ceiling((now - expected!.Value).TotalMinutes) : 0;

        var movement = new BoardingMovement
        {
            LearnerId = learner.Id,
            MovementType = MovementType.CheckIn,
            QrHash = qrHash,
            ScannedAt = now,
            ExpectedReturnTime = expected,
            ActualReturnTime = now,
            IsLate = late,
            LateMinutes = lateMinutes,
            Status = late ? MovementStatus.Late : MovementStatus.OnTime,
            Destination = openCheckOut.Destination,
            Purpose = openCheckOut.Purpose,
            LeaveRequestId = openCheckOut.LeaveRequestId
        };
        _db.BoardingMovements.Add(movement);
        await _db.SaveChangesAsync();

        if (late) await NotifyLateCheckInAsync(learner, lateMinutes);

        return new BoardingScanResult
        {
            IsValid = true,
            LearnerName = learner.FullName,
            Status = late ? "Late" : "OnTime",
            Message = late ? $"Checked in {lateMinutes} minute(s) late." : "Checked in on time."
        };
    }

    private async Task<BoardingScanResult> ProcessCheckOutAsync(Learner learner, string qrHash)
    {
        var now = SchoolClock.Now;
        var today = now.Date;
        var settings = await _db.BoardingSettings.FirstOrDefaultAsync() ?? new BoardingSettings();
        var curfew = settings.IsCurfew(now);

        // An approved leave request covering today that hasn't been used to check out yet
        var leaveRequests = await _db.LeaveRequests
            .Where(r => r.LearnerId == learner.Id
                     && r.Status == LeaveRequestStatus.Approved
                     && r.DepartureDate.Date <= today
                     && r.ExpectedReturnDate.Date >= today)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        var usedIds = await _db.BoardingMovements
            .Where(m => m.LeaveRequestId != null && m.MovementType == MovementType.CheckOut && m.LearnerId == learner.Id)
            .Select(m => m.LeaveRequestId!.Value).ToListAsync();
        var leaveRequest = leaveRequests.FirstOrDefault(r => !usedIds.Contains(r.Id));

        if (leaveRequest is null)
        {
            // Curfew: nobody leaves without approved leave
            if (curfew)
                return new BoardingScanResult
                {
                    IsValid = false, LearnerName = learner.FullName, Status = "Rejected",
                    Message = leaveRequests.Any()
                        ? "Curfew is in force and this learner's leave has already been used. Checkout denied."
                        : $"Curfew is in force ({settings.CurfewStart}–{settings.CurfewEnd}). Only learners with approved leave may check out."
                };

            return await ProcessDayOutAsync(learner, qrHash, today.Add(settings.CurfewStartTime), settings.CurfewStart);
        }

        var movement = new BoardingMovement
        {
            LearnerId = learner.Id,
            MovementType = MovementType.CheckOut,
            QrHash = qrHash,
            ScannedAt = DateTime.UtcNow,
            ExpectedReturnTime = leaveRequest.ExpectedReturnDate,
            Status = MovementStatus.OnTime,
            Destination = leaveRequest.Destination,
            Purpose = leaveRequest.Purpose,
            LeaveRequestId = leaveRequest.Id
        };
        _db.BoardingMovements.Add(movement);
        await _db.SaveChangesAsync();

        await NotifyDepartureAsync(learner, leaveRequest);

        return new BoardingScanResult
        {
            IsValid = true, LearnerName = learner.FullName, Status = "OnTime",
            Message = $"Checked out. Expected back {leaveRequest.ExpectedReturnDate:dd MMM yyyy}."
        };
    }

    // Outside curfew a learner may go out without a leave request, but must be back by curfew
    private async Task<BoardingScanResult> ProcessDayOutAsync(Learner learner, string qrHash, DateTime backBySchoolTime, string curfewStart)
    {
        _db.BoardingMovements.Add(new BoardingMovement
        {
            LearnerId = learner.Id,
            MovementType = MovementType.CheckOut,
            QrHash = qrHash,
            ScannedAt = DateTime.UtcNow,
            // Movement times are stored in UTC; school time is UTC+2
            ExpectedReturnTime = backBySchoolTime.AddHours(-2),
            Status = MovementStatus.OnTime,
            Purpose = "Out before curfew"
        });
        await _db.SaveChangesAsync();

        return new BoardingScanResult
        {
            IsValid = true, LearnerName = learner.FullName, Status = "OnTime",
            Message = $"Checked out (no leave needed before curfew). Must be back by {curfewStart}."
        };
    }

    // ── UC2 — leave requests ─────────────────────────────────────────────────

    public async Task<LeaveRequest> CreateLeaveRequestAsync(int learnerId, string parentUserId,
        string destination, string purpose, DateTime departureDate, DateTime expectedReturnDate)
    {
        var leaveRequest = new LeaveRequest
        {
            LearnerId = learnerId,
            RequestedByParentUserId = parentUserId,
            Destination = destination,
            Purpose = purpose,
            DepartureDate = departureDate,
            ExpectedReturnDate = expectedReturnDate,
            Status = LeaveRequestStatus.PendingHousemaster
        };
        _db.LeaveRequests.Add(leaveRequest);
        await _db.SaveChangesAsync();

        await NotifyHousemastersOfNewRequestAsync(leaveRequest);
        return leaveRequest;
    }

    public async Task<IList<LeaveRequestRow>> GetPendingLeaveRequestsAsync()
        => await MapLeaveRequestsAsync(_db.LeaveRequests.Where(r => r.Status == LeaveRequestStatus.PendingHousemaster));

    public async Task<IList<LeaveRequestRow>> GetAllLeaveRequestsAsync()
        => await MapLeaveRequestsAsync(_db.LeaveRequests);

    private async Task<IList<LeaveRequestRow>> MapLeaveRequestsAsync(IQueryable<LeaveRequest> query)
    {
        return await query
            .Include(r => r.Learner)
            .Include(r => r.RequestedByParent)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new LeaveRequestRow
            {
                Id = r.Id,
                LearnerId = r.LearnerId,
                LearnerName = r.Learner.FullName,
                ParentName = r.RequestedByParent.FullName,
                Destination = r.Destination,
                Purpose = r.Purpose,
                DepartureDate = r.DepartureDate,
                ExpectedReturnDate = r.ExpectedReturnDate,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();
    }

    public async Task ApproveLeaveRequestAsync(int leaveRequestId, string housemasterUserId, string? notes)
    {
        var leaveRequest = await _db.LeaveRequests.Include(r => r.Learner).Include(r => r.RequestedByParent)
            .FirstOrDefaultAsync(r => r.Id == leaveRequestId);
        if (leaveRequest is null) return;

        var housemaster = await _db.Housemasters.FirstOrDefaultAsync(h => h.UserId == housemasterUserId);

        leaveRequest.Status = LeaveRequestStatus.Approved;
        leaveRequest.HousemasterNotes = notes;
        leaveRequest.ApprovedAt = DateTime.UtcNow;
        leaveRequest.ApprovedByHousemasterId = housemaster?.Id;
        await _db.SaveChangesAsync();

        await NotifyLeaveOutcomeAsync(leaveRequest, approved: true);
    }

    public async Task RejectLeaveRequestAsync(int leaveRequestId, string housemasterUserId, string reason)
    {
        var leaveRequest = await _db.LeaveRequests.Include(r => r.Learner).Include(r => r.RequestedByParent)
            .FirstOrDefaultAsync(r => r.Id == leaveRequestId);
        if (leaveRequest is null) return;

        var housemaster = await _db.Housemasters.FirstOrDefaultAsync(h => h.UserId == housemasterUserId);

        leaveRequest.Status = LeaveRequestStatus.Rejected;
        leaveRequest.HousemasterNotes = reason;
        leaveRequest.ApprovedAt = DateTime.UtcNow;
        leaveRequest.ApprovedByHousemasterId = housemaster?.Id;
        await _db.SaveChangesAsync();

        await NotifyLeaveOutcomeAsync(leaveRequest, approved: false);
    }

    public async Task<IList<ParentLeaveRequestRow>> GetLeaveRequestsForLearnerAsync(int learnerId)
    {
        return await _db.LeaveRequests
            .Where(r => r.LearnerId == learnerId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ParentLeaveRequestRow
            {
                Id = r.Id,
                Destination = r.Destination,
                Purpose = r.Purpose,
                DepartureDate = r.DepartureDate,
                ExpectedReturnDate = r.ExpectedReturnDate,
                Status = r.Status.ToString(),
                HousemasterNotes = r.HousemasterNotes
            })
            .ToListAsync();
    }

    // ── Dashboards / reporting ───────────────────────────────────────────────

    public async Task<IList<MovementLogRow>> GetMovementLogAsync(DateTime date)
    {
        var day = date.Date;
        return await _db.BoardingMovements
            .Include(m => m.Learner)
            .Where(m => m.ScannedAt.Date == day)
            .OrderByDescending(m => m.ScannedAt)
            .Select(m => new MovementLogRow
            {
                LearnerName = m.Learner.FullName,
                MovementType = m.MovementType.ToString(),
                ScannedAt = m.ScannedAt,
                Status = m.Status.ToString(),
                Destination = m.Destination,
                Purpose = m.Purpose
            })
            .ToListAsync();
    }

    public async Task<BoardingDashboardViewModel> GetDashboardAsync()
    {
        var totalBoarders = await _db.RoomAllocations.CountAsync(r => r.IsActive);
        var pendingLeave = await _db.LeaveRequests.CountAsync(r => r.Status == LeaveRequestStatus.PendingHousemaster);
        var today = DateTime.UtcNow.Date;
        var lateToday = await _db.BoardingMovements.CountAsync(m => m.ScannedAt.Date == today && m.IsLate);

        var latestPerLearner = await _db.BoardingMovements
            .GroupBy(m => m.LearnerId)
            .Select(g => g.OrderByDescending(m => m.ScannedAt).First())
            .ToListAsync();

        var now = DateTime.UtcNow;
        var currentlyOutMovements = latestPerLearner.Where(m => m.MovementType == MovementType.CheckOut).ToList();
        var learnerIds = currentlyOutMovements.Select(m => m.LearnerId).ToList();
        var learners = await _db.Learners.Where(l => learnerIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id);

        var currentlyOut = currentlyOutMovements.Select(m =>
        {
            bool late = m.ExpectedReturnTime.HasValue && now > m.ExpectedReturnTime.Value;
            return new BoardingLearnerStatusRow
            {
                LearnerId = m.LearnerId,
                LearnerName = learners.TryGetValue(m.LearnerId, out var l) ? l.FullName : "Unknown",
                Since = m.ScannedAt,
                IsLate = late,
                LateMinutes = late ? (int)Math.Ceiling((now - m.ExpectedReturnTime!.Value).TotalMinutes) : 0,
                Destination = m.Destination
            };
        }).OrderByDescending(r => r.IsLate).ThenBy(r => r.LearnerName).ToList();

        var absenceAlerts = await _db.MealAbsenceAlerts
            .Include(a => a.Learner)
            .Where(a => a.ResolvedAt == null)
            .OrderByDescending(a => a.AlertSentAt)
            .Select(a => new MealAbsenceAlertRow
            {
                Id = a.Id,
                LearnerId = a.LearnerId,
                LearnerName = a.Learner.FullName,
                ConsecutiveMissedMeals = a.ConsecutiveMissedMeals,
                AlertSentAt = a.AlertSentAt
            })
            .ToListAsync();

        return new BoardingDashboardViewModel
        {
            CurrentlyOut = currentlyOut,
            TotalBoarders = totalBoarders,
            PendingLeaveRequests = pendingLeave,
            LateTodayCount = lateToday,
            MealAbsenceAlerts = absenceAlerts
        };
    }

    // ── Email notifications ──────────────────────────────────────────────────

    private async Task NotifyLateCheckInAsync(Learner learner, int lateMinutes)
    {
        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is null) continue;
            await _email.SendAsync(user.Email, hm.FullName,
                $"Late Check-In — {learner.FullName}",
                $"<p>Dear {hm.FullName},</p><p><strong>{learner.FullName}</strong> checked in <strong>{lateMinutes} minute(s)</strong> late.</p>");
        }

        if (learner.ParentId is not null)
        {
            var parent = await _userManager.FindByIdAsync(learner.ParentId);
            if (parent?.Email is not null)
                await _email.SendAsync(parent.Email, parent.FullName,
                    $"{learner.FullName} Checked In Late",
                    $"<p>Dear {parent.FullName},</p><p><strong>{learner.FullName}</strong> checked in <strong>{lateMinutes} minute(s)</strong> late at the boarding house.</p>");
        }
    }

    private async Task NotifyHousemastersOfNewRequestAsync(LeaveRequest leaveRequest)
    {
        var learner = await _db.Learners.FirstOrDefaultAsync(l => l.Id == leaveRequest.LearnerId);
        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is null) continue;
            await _email.SendAsync(user.Email, hm.FullName,
                $"New Leave Request — {learner?.FullName}",
                $"<p>Dear {hm.FullName},</p><p>A new leave request has been submitted for <strong>{learner?.FullName}</strong>:</p>" +
                $"<p>Destination: {leaveRequest.Destination}<br/>Purpose: {leaveRequest.Purpose}<br/>" +
                $"From: {leaveRequest.DepartureDate:dd MMM yyyy} to {leaveRequest.ExpectedReturnDate:dd MMM yyyy}</p>" +
                $"<p>Please review it on the Boarding Leave Requests page.</p>");
        }
    }

    private async Task NotifyLeaveOutcomeAsync(LeaveRequest leaveRequest, bool approved)
    {
        if (leaveRequest.RequestedByParent?.Email is null) return;
        var outcome = approved ? "approved" : "rejected";
        await _email.SendAsync(leaveRequest.RequestedByParent.Email, leaveRequest.RequestedByParent.FullName,
            $"Leave Request {(approved ? "Approved" : "Rejected")} — {leaveRequest.Learner.FullName}",
            $"<p>Dear {leaveRequest.RequestedByParent.FullName},</p>" +
            $"<p>The leave request for <strong>{leaveRequest.Learner.FullName}</strong> to {leaveRequest.Destination} " +
            $"has been <strong>{outcome}</strong>.</p>" +
            (string.IsNullOrEmpty(leaveRequest.HousemasterNotes) ? "" : $"<p>Note: {leaveRequest.HousemasterNotes}</p>"));
    }

    private async Task NotifyDepartureAsync(Learner learner, LeaveRequest leaveRequest)
    {
        var housemasters = await _db.Housemasters.Where(h => h.IsActive && h.UserId != null).ToListAsync();
        foreach (var hm in housemasters)
        {
            var user = await _userManager.FindByIdAsync(hm.UserId!);
            if (user?.Email is null) continue;
            await _email.SendAsync(user.Email, hm.FullName,
                $"{learner.FullName} Departed on Leave",
                $"<p>Dear {hm.FullName},</p><p><strong>{learner.FullName}</strong> has checked out to {leaveRequest.Destination}. " +
                $"Expected back {leaveRequest.ExpectedReturnDate:dd MMM yyyy}.</p>");
        }

        if (learner.ParentId is not null)
        {
            var parent = await _userManager.FindByIdAsync(learner.ParentId);
            if (parent?.Email is not null)
                await _email.SendAsync(parent.Email, parent.FullName,
                    $"{learner.FullName} Has Departed on Leave",
                    $"<p>Dear {parent.FullName},</p><p><strong>{learner.FullName}</strong> has checked out of the boarding house to {leaveRequest.Destination}.</p>");
        }
    }

    // ── UC10 — Meal compliance report ────────────────────────────────────────

    public async Task<MealComplianceReportDto> GenerateComplianceReportAsync(DateTime from, DateTime to)
    {
        var fromDate = from.Date;
        var toDate = to.Date;

        var plansInRange = await _db.MealPlans.Include(p => p.Items)
            .Where(p => p.WeekStartDate <= toDate && p.WeekStartDate.AddDays(6) >= fromDate)
            .ToListAsync();
        var itemsInRange = plansInRange.SelectMany(p => p.Items.Select(i => (Plan: p, Item: i)))
            .Where(x =>
            {
                var mealDate = x.Plan.WeekStartDate.AddDays(x.Item.DayOfWeek - 1);
                return mealDate >= fromDate && mealDate <= toDate;
            })
            .ToList();
        var itemIds = itemsInRange.Select(x => x.Item.Id).ToList();

        // ── 1. Attendance summary per meal type ──────────────────────────────
        var attendances = await _db.MealAttendances.Where(a => itemIds.Contains(a.MealPlanItemId)).ToListAsync();
        var attendanceByMealType = itemsInRange.GroupBy(x => x.Item.MealType)
            .Select(g =>
            {
                var ids = g.Select(x => x.Item.Id).ToList();
                var relevant = attendances.Where(a => ids.Contains(a.MealPlanItemId)).ToList();
                var present = relevant.Count(a => a.Status == MealAttendanceStatus.Present);
                return new MealTypeAttendanceRow
                {
                    MealType = g.Key.ToString(),
                    TotalExpected = relevant.Count,
                    TotalPresent = present,
                    AttendancePct = relevant.Count > 0 ? Math.Round((decimal)present / relevant.Count * 100, 1) : 0
                };
            }).ToList();

        // ── 2. Pre-order vs actual ────────────────────────────────────────────
        var preOrders = await _db.MealPreOrders.Where(o => itemIds.Contains(o.MealPlanItemId) && o.Status == PreOrderStatus.Confirmed).ToListAsync();
        var preOrderVsActual = itemsInRange.Select(x =>
        {
            var preOrderedCount = preOrders.Count(o => o.MealPlanItemId == x.Item.Id);
            var attendedCount = attendances.Count(a => a.MealPlanItemId == x.Item.Id && a.Status == MealAttendanceStatus.Present);
            return new PreOrderVsActualRow
            {
                Date = x.Plan.WeekStartDate.AddDays(x.Item.DayOfWeek - 1),
                MealType = x.Item.MealType.ToString(),
                MenuDescription = x.Item.MenuDescription,
                PreOrdered = preOrderedCount,
                Attended = attendedCount,
                Variance = preOrderedCount - attendedCount
            };
        }).OrderBy(r => r.Date).ToList();

        // ── 3. Dietary compliance ────────────────────────────────────────────
        var dietaryConflicts = new List<string>();
        foreach (var plan in plansInRange)
        {
            var conflicts = await _mealPlanSvc.ScanAllergyConflictsAsync(plan.Id);
            foreach (var c in conflicts.Where(c => itemIds.Contains(c.MealPlanItemId)))
            {
                var wasServed = attendances.Any(a => a.MealPlanItemId == c.MealPlanItemId && a.Status == MealAttendanceStatus.Present
                    && c.SevereLearners.Contains(_db.Learners.First(l => l.Id == a.LearnerId).FullName));
                if (wasServed)
                    dietaryConflicts.Add($"{c.DayLabel} {c.MealType}: {c.MenuDescription} served containing {c.AllergenMatched} (affects {string.Join(", ", c.AffectedLearners)})");
            }
        }

        // ── 4. Food waste estimate ────────────────────────────────────────────
        // The kitchen prepares one portion per confirmed pre-order (portions aren't entered manually)
        var portionsPrepared = preOrders.Count;
        var portionsServed = attendances.Count(a => a.Status == MealAttendanceStatus.Present);

        // ── 5. Budget vs actual ───────────────────────────────────────────────
        var estimatedCost = itemsInRange.Sum(x => x.Item.EstimatedCostPerHead * preOrders.Count(o => o.MealPlanItemId == x.Item.Id));
        var requisitions = await _db.PurchaseRequisitions.Include(r => r.Items)
            .Where(r => r.GeneratedAt.Date >= fromDate && r.GeneratedAt.Date <= toDate)
            .ToListAsync();
        var actualSpend = requisitions.SelectMany(r => r.Items).Sum(i => i.QuantityToOrder * i.UnitCost);

        // ── 6. Satisfaction scores ────────────────────────────────────────────
        var feedback = await _db.MealFeedbacks.Where(f => itemIds.Contains(f.MealPlanItemId)).ToListAsync();
        var satisfactionByMealType = itemsInRange.GroupBy(x => x.Item.MealType)
            .Select(g =>
            {
                var ids = g.Select(x => x.Item.Id).ToList();
                var relevant = feedback.Where(f => ids.Contains(f.MealPlanItemId)).ToList();
                return new MealTypeSatisfactionRow
                {
                    MealType = g.Key.ToString(),
                    AverageRating = relevant.Count > 0 ? Math.Round((decimal)relevant.Average(f => f.Rating), 2) : 0,
                    Complaints = relevant.Count(f => f.Rating <= 2)
                };
            }).ToList();

        // ── 7. Anomaly flags ──────────────────────────────────────────────────
        var anomalies = new List<string>();
        var absenceAlerts = await _db.MealAbsenceAlerts.Include(a => a.Learner)
            .Where(a => a.AlertSentAt.Date >= fromDate && a.AlertSentAt.Date <= toDate)
            .ToListAsync();
        anomalies.AddRange(absenceAlerts.Select(a => $"{a.Learner.FullName} missed {a.ConsecutiveMissedMeals} consecutive meals ({a.AlertSentAt:dd MMM yyyy})"));

        var qualityAlerts = await _db.MealQualityAlerts.Include(a => a.MealPlanItem)
            .Where(a => a.CreatedAt.Date >= fromDate && a.CreatedAt.Date <= toDate)
            .ToListAsync();
        anomalies.AddRange(qualityAlerts.Select(a => $"{a.MealPlanItem.MenuDescription} rated {a.AverageRating:N1}/5 across {a.SubmissionCount} submissions"));

        anomalies.AddRange(plansInRange
            .Where(p => p.Items.Sum(i => i.EstimatedCostPerHead * preOrders.Count(o => o.MealPlanItemId == i.Id)) > p.TotalBudget)
            .Select(p => $"Meal plan for week of {p.WeekStartDate:dd MMM yyyy} is over budget"));

        return new MealComplianceReportDto
        {
            From = fromDate,
            To = toDate,
            AttendanceByMealType = attendanceByMealType,
            PreOrderVsActual = preOrderVsActual,
            DietaryConflictsServed = dietaryConflicts,
            PortionsPrepared = portionsPrepared,
            PortionsServed = portionsServed,
            WasteEstimate = Math.Max(0, portionsPrepared - portionsServed),
            EstimatedCost = estimatedCost,
            ActualSpend = actualSpend,
            SatisfactionByMealType = satisfactionByMealType,
            AnomalyFlags = anomalies,
            ArchivedReports = await GetArchivedReportsAsync()
        };
    }

    public async Task<string> ArchiveReportAsync(DateTime from, DateTime to, byte[] pdfBytes, string userId, string webRootPath)
    {
        var reportsDir = Path.Combine(webRootPath, "reports");
        Directory.CreateDirectory(reportsDir);
        var fileName = $"MealComplianceReport_{from:yyyyMMdd}_{to:yyyyMMdd}_{DateTime.UtcNow.Ticks}.pdf";
        var fullPath = Path.Combine(reportsDir, fileName);
        await File.WriteAllBytesAsync(fullPath, pdfBytes);

        var relativePath = $"/reports/{fileName}";
        _db.MealComplianceReportRecords.Add(new MealComplianceReportRecord
        {
            FromDate = from.Date,
            ToDate = to.Date,
            GeneratedByUserId = userId,
            PdfPath = relativePath
        });
        await _db.SaveChangesAsync();
        return relativePath;
    }

    public async Task<IList<ArchivedReportRow>> GetArchivedReportsAsync()
    {
        var records = await _db.MealComplianceReportRecords.OrderByDescending(r => r.GeneratedAt).Take(20).ToListAsync();
        var rows = new List<ArchivedReportRow>();
        foreach (var r in records)
        {
            var user = await _userManager.FindByIdAsync(r.GeneratedByUserId);
            rows.Add(new ArchivedReportRow
            {
                Id = r.Id,
                FromDate = r.FromDate,
                ToDate = r.ToDate,
                GeneratedAt = r.GeneratedAt,
                GeneratedBy = user?.FullName ?? "Unknown",
                PdfUrl = r.PdfPath
            });
        }
        return rows;
    }
}
