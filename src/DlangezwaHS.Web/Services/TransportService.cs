using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DlangezwaHS.Web.Services;

public interface ITransportService
{
    // Buses
    Task<List<Bus>> GetBusesAsync(bool activeOnly = true);
    Task<Bus?> GetBusAsync(int id);
    Task<Bus> SaveBusAsync(int? id, string registrationNumber, string makeModel, int capacity);
    Task DeactivateBusAsync(int id);

    // Drivers
    Task<List<Driver>> GetDriversAsync(bool activeOnly = true);
    Task<Driver?> GetDriverAsync(int id);
    Task<Driver> SaveDriverAsync(DriverData data, IFormFile? photo, IFormFile? licenseDoc);
    Task DeactivateDriverAsync(int id);
    Task<List<Driver>> GetExpiringLicensesAsync(int withinDays = 30);

    // Schedule Transport (UC1)
    Task<Trip> CreateTripAsync(CreateTripData data, List<int> learnerIds, string userId, string userName);
    Task<List<Trip>> GetTripsAsync(bool activeOnly = true);
    Task<Trip?> GetTripAsync(int id);
    Task<List<Trip>> GetTripsForLearnerAsync(int learnerId);

    // Board the Bus (UC2)
    Task<TripOccurrence> GetOrCreateOccurrenceAsync(int tripId, DateTime date);
    Task<TripOccurrence?> GetOccurrenceAsync(int occurrenceId);
    Task<List<BoardingRosterRow>> GetRosterAsync(int occurrenceId);
    Task ConfirmAttendanceAsync(int occurrenceId, List<(int LearnerId, BoardingStatus Status)> marks, string userId);

    // Notify Parent of Delay (UC4)
    Task ReportDelayAsync(int occurrenceId, DelayReason reason, string? notes, TimeSpan newEta, string userId, string userName);

    // Monitor Fuel Usage (UC5)
    Task<FuelRecord> AddFuelRecordAsync(int busId, DateTime date, decimal litres, decimal totalCost, int odometer, string? station, string userId);
    Task<List<FuelRecord>> GetFuelRecordsAsync(int? busId = null);
    Task<FuelReportDto> GetFuelReportAsync(int? busId, DateTime? from, DateTime? to);
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record DriverData(
    int? Id, string FullName, string IdNumber, string? Phone, string? Email, string? Address,
    string LicenseNumber, DateTime LicenseExpiryDate, DateTime EmploymentDate, string? ShiftHours
);

public record CreateTripData(
    int BusId, int DriverId, TransportRoute Route, TransportPurpose Purpose, string? PurposeDetail,
    TimeSpan DepartureTime, TimeSpan ArrivalTime, bool IsRecurring, string? DaysOfWeek,
    DateTime? TripDate, DateTime? RecurrenceEndDate, bool RosterIsAllBoardingLearners
);

public record BoardingRosterRow(int LearnerId, string FullName, BoardingStatus Status);

public record FuelReportDto(
    int? BusId, string? BusLabel, DateTime From, DateTime To, List<FuelRecord> Records,
    decimal TotalLitres, decimal TotalCost, decimal? AverageEfficiency
);

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class TransportService : ITransportService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly IAuditService _audit;
    private readonly IDocumentService _docs;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TransportService> _logger;

    public TransportService(ApplicationDbContext db, IEmailService email, IAuditService audit,
        IDocumentService docs, UserManager<ApplicationUser> userManager,
        IServiceScopeFactory scopeFactory, ILogger<TransportService> logger)
    {
        _db = db;
        _email = email;
        _audit = audit;
        _docs = docs;
        _userManager = userManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ─── Buses ────────────────────────────────────────────────────────────────

    public async Task<List<Bus>> GetBusesAsync(bool activeOnly = true)
        => await (activeOnly ? _db.Buses.Where(b => b.IsActive) : _db.Buses)
            .OrderBy(b => b.RegistrationNumber).ToListAsync();

    public async Task<Bus?> GetBusAsync(int id) => await _db.Buses.FindAsync(id);

    public async Task<Bus> SaveBusAsync(int? id, string registrationNumber, string makeModel, int capacity)
    {
        Bus bus;
        if (id.HasValue)
        {
            bus = await _db.Buses.FindAsync(id.Value) ?? throw new InvalidOperationException("Bus not found.");
        }
        else
        {
            bus = new Bus();
            _db.Buses.Add(bus);
        }
        bus.RegistrationNumber = registrationNumber;
        bus.MakeModel = makeModel;
        bus.Capacity = capacity;
        await _db.SaveChangesAsync();
        return bus;
    }

    public async Task DeactivateBusAsync(int id)
    {
        var bus = await _db.Buses.FindAsync(id);
        if (bus is not null) { bus.IsActive = false; await _db.SaveChangesAsync(); }
    }

    // ─── Drivers ──────────────────────────────────────────────────────────────

    public async Task<List<Driver>> GetDriversAsync(bool activeOnly = true)
        => await (activeOnly ? _db.Drivers.Where(d => d.IsActive) : _db.Drivers)
            .OrderBy(d => d.FullName).ToListAsync();

    public async Task<Driver?> GetDriverAsync(int id) => await _db.Drivers.FindAsync(id);

    public async Task<Driver> SaveDriverAsync(DriverData data, IFormFile? photo, IFormFile? licenseDoc)
    {
        Driver driver;
        if (data.Id.HasValue)
        {
            driver = await _db.Drivers.FindAsync(data.Id.Value) ?? throw new InvalidOperationException("Driver not found.");
        }
        else
        {
            driver = new Driver();
            _db.Drivers.Add(driver);
        }

        driver.FullName = data.FullName;
        driver.IdNumber = data.IdNumber;
        driver.Phone = data.Phone;
        driver.Email = data.Email;
        driver.Address = data.Address;
        driver.LicenseNumber = data.LicenseNumber;
        driver.LicenseExpiryDate = data.LicenseExpiryDate;
        driver.EmploymentDate = data.EmploymentDate;
        driver.ShiftHours = data.ShiftHours;

        var photoPath = await _docs.SaveDocumentAsync(photo, "drivers");
        if (photoPath is not null) driver.PhotoPath = photoPath;

        var licensePath = await _docs.SaveDocumentAsync(licenseDoc, "drivers");
        if (licensePath is not null) driver.LicenseDocPath = licensePath;

        await _db.SaveChangesAsync();
        return driver;
    }

    public async Task DeactivateDriverAsync(int id)
    {
        var driver = await _db.Drivers.FindAsync(id);
        if (driver is not null) { driver.IsActive = false; await _db.SaveChangesAsync(); }
    }

    public async Task<List<Driver>> GetExpiringLicensesAsync(int withinDays = 30)
    {
        var cutoff = DateTime.Today.AddDays(withinDays);
        return await _db.Drivers
            .Where(d => d.IsActive && d.LicenseExpiryDate <= cutoff)
            .OrderBy(d => d.LicenseExpiryDate)
            .ToListAsync();
    }

    // ─── Schedule Transport (UC1) ────────────────────────────────────────────

    public async Task<Trip> CreateTripAsync(CreateTripData data, List<int> learnerIds, string userId, string userName)
    {
        var trip = new Trip
        {
            BusId = data.BusId,
            DriverId = data.DriverId,
            Route = data.Route,
            Purpose = data.Purpose,
            PurposeDetail = data.PurposeDetail,
            DepartureTime = data.DepartureTime,
            ArrivalTime = data.ArrivalTime,
            IsRecurring = data.IsRecurring,
            DaysOfWeek = data.DaysOfWeek,
            TripDate = data.TripDate,
            RecurrenceEndDate = data.RecurrenceEndDate,
            RosterIsAllBoardingLearners = data.RosterIsAllBoardingLearners,
            CreatedByUserId = userId
        };
        _db.Trips.Add(trip);
        await _db.SaveChangesAsync();

        if (!data.RosterIsAllBoardingLearners)
        {
            foreach (var lid in learnerIds)
                _db.TripLearners.Add(new TripLearner { TripId = trip.Id, LearnerId = lid });
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync(userId, userName, "TripScheduled", "Trip", trip.Id.ToString(),
            $"{data.Purpose} {data.Route} — {(data.IsRecurring ? data.DaysOfWeek : data.TripDate?.ToString("dd MMM yyyy"))}");

        _ = SendScheduleEmailsAsync(trip.Id);

        return trip;
    }

    // Runs in its own DI scope: the request's scoped ApplicationDbContext/EmailService are
    // disposed as soon as the HTTP response is returned, before this fire-and-forget task runs.
    private async Task SendScheduleEmailsAsync(int tripId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var trip = await db.Trips.Include(t => t.Bus).Include(t => t.Driver)
                .FirstOrDefaultAsync(t => t.Id == tripId);
            if (trip is null) return;

            var roster = await GetRosterLearnersAsync(db, trip);
            var when = trip.IsRecurring
                ? $"{trip.DaysOfWeek} · {trip.DepartureTime:hh\\:mm}"
                : $"{trip.TripDate:dddd, dd MMMM yyyy} · {trip.DepartureTime:hh\\:mm}";
            var details = $"Bus {trip.Bus?.RegistrationNumber} ({trip.Bus?.MakeModel}). " +
                $"Driver: {trip.Driver?.FullName}{(string.IsNullOrEmpty(trip.Driver?.Phone) ? "" : $" — {trip.Driver!.Phone}")}. " +
                $"Departs {when}. {trip.PurposeDetail}";

            var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var learner in roster)
            {
                if (learner.ParentId is null) continue;
                var parent = await userManager.FindByIdAsync(learner.ParentId);
                if (parent?.Email is null || !sent.Add(parent.Email)) continue;

                await email.SendCalendarNotificationAsync(
                    parent.Email, parent.FullName,
                    $"{trip.Purpose} — {trip.Route}", "Transport Schedule",
                    trip.TripDate ?? DateTime.Today, details);
            }

            var teachers = await db.Teachers.Where(t => t.IsActive && t.UserId != null).ToListAsync();
            foreach (var t in teachers)
            {
                var user = await userManager.FindByIdAsync(t.UserId!);
                if (user?.Email is null || !sent.Add(user.Email)) continue;

                await email.SendCalendarNotificationAsync(
                    user.Email, t.FullName,
                    $"{trip.Purpose} — {trip.Route}", "Transport Schedule",
                    trip.TripDate ?? DateTime.Today, details);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send trip schedule emails for trip {TripId}", tripId);
        }
    }

    public async Task<List<Trip>> GetTripsAsync(bool activeOnly = true)
    {
        var q = _db.Trips.Include(t => t.Bus).Include(t => t.Driver).AsQueryable();
        if (activeOnly) q = q.Where(t => t.Status != TripStatus.Cancelled);
        return await q.OrderByDescending(t => t.CreatedAt).ToListAsync();
    }

    public async Task<Trip?> GetTripAsync(int id)
        => await _db.Trips.Include(t => t.Bus).Include(t => t.Driver).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<List<Trip>> GetTripsForLearnerAsync(int learnerId)
    {
        var hasBed = await _db.RoomAllocations.AnyAsync(r => r.LearnerId == learnerId && r.IsActive);
        var trips = await _db.Trips.Include(t => t.Bus).Include(t => t.Driver)
            .Where(t => t.Status != TripStatus.Cancelled)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var result = new List<Trip>();
        foreach (var t in trips)
        {
            if (t.RosterIsAllBoardingLearners && hasBed)
                result.Add(t);
            else if (!t.RosterIsAllBoardingLearners &&
                     await _db.TripLearners.AnyAsync(tl => tl.TripId == t.Id && tl.LearnerId == learnerId))
                result.Add(t);
        }
        return result;
    }

    // ─── Board the Bus (UC2) ─────────────────────────────────────────────────

    public async Task<TripOccurrence> GetOrCreateOccurrenceAsync(int tripId, DateTime date)
    {
        date = date.Date;
        var occ = await _db.TripOccurrences.FirstOrDefaultAsync(o => o.TripId == tripId && o.Date == date);
        if (occ is null)
        {
            occ = new TripOccurrence { TripId = tripId, Date = date, Status = TripStatus.Scheduled };
            _db.TripOccurrences.Add(occ);
            await _db.SaveChangesAsync();
        }
        return occ;
    }

    public async Task<TripOccurrence?> GetOccurrenceAsync(int occurrenceId)
        => await _db.TripOccurrences
            .Include(o => o.Trip).ThenInclude(t => t.Bus)
            .Include(o => o.Trip).ThenInclude(t => t.Driver)
            .FirstOrDefaultAsync(o => o.Id == occurrenceId);

    public async Task<List<BoardingRosterRow>> GetRosterAsync(int occurrenceId)
    {
        var occ = await _db.TripOccurrences.Include(o => o.Trip).FirstOrDefaultAsync(o => o.Id == occurrenceId)
            ?? throw new InvalidOperationException("Occurrence not found.");

        var roster = await GetRosterLearnersAsync(_db, occ.Trip);
        var existing = await _db.BoardingRecords.Where(b => b.TripOccurrenceId == occurrenceId).ToListAsync();

        return roster.OrderBy(l => l.LastName).ThenBy(l => l.FirstName).Select(l =>
        {
            var rec = existing.FirstOrDefault(b => b.LearnerId == l.Id);
            return new BoardingRosterRow(l.Id, l.FullName, rec?.Status ?? BoardingStatus.NotBoarded);
        }).ToList();
    }

    public async Task ConfirmAttendanceAsync(int occurrenceId, List<(int LearnerId, BoardingStatus Status)> marks, string userId)
    {
        var occ = await _db.TripOccurrences.Include(o => o.Trip).FirstOrDefaultAsync(o => o.Id == occurrenceId)
            ?? throw new InvalidOperationException("Occurrence not found.");

        var existing = await _db.BoardingRecords.Where(b => b.TripOccurrenceId == occurrenceId).ToListAsync();
        foreach (var (learnerId, status) in marks)
        {
            var rec = existing.FirstOrDefault(b => b.LearnerId == learnerId);
            if (rec is null)
            {
                rec = new BoardingRecord { TripOccurrenceId = occurrenceId, LearnerId = learnerId };
                _db.BoardingRecords.Add(rec);
            }
            rec.Status = status;
            rec.BoardedAt = status == BoardingStatus.Boarded ? DateTime.UtcNow : null;
            rec.MarkedByUserId = userId;
        }

        occ.BoardingConfirmedAt = DateTime.UtcNow;
        occ.BoardingConfirmedByUserId = userId;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, null, "BoardingConfirmed", "TripOccurrence", occurrenceId.ToString());

        var roster = await GetRosterLearnersAsync(_db, occ.Trip);
        var boardedIds = marks.Where(m => m.Status == BoardingStatus.Boarded).Select(m => m.LearnerId).ToHashSet();
        var missing = roster.Where(l => !boardedIds.Contains(l.Id)).ToList();
        if (missing.Count > 0)
            _ = SendMissingLearnerAlertsAsync(occ.Id, missing.Select(m => m.Id).ToList());
    }

    private async Task SendMissingLearnerAlertsAsync(int occurrenceId, List<int> missingLearnerIds)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var occ = await db.TripOccurrences.Include(o => o.Trip).ThenInclude(t => t.Bus)
                .FirstOrDefaultAsync(o => o.Id == occurrenceId);
            if (occ is null) return;

            var missing = await db.Learners.Where(l => missingLearnerIds.Contains(l.Id)).ToListAsync();
            var listHtml = string.Join("", missing.Select(m => $"<li>{m.FullName}</li>"));

            var housemasters = await db.Housemasters
                .Where(h => h.IsActive && h.UserId != null)
                .ToListAsync();
            foreach (var hm in housemasters)
            {
                var user = await userManager.FindByIdAsync(hm.UserId!);
                if (user?.Email is null) continue;

                await email.SendAsync(user.Email, hm.FullName,
                    $"Missing Learners on Bus — {occ.Date:dd MMM yyyy}",
                    $"<p>Dear {hm.FullName},</p><p>The following boarding learner(s) were not marked as boarded on {occ.Date:dddd, dd MMMM yyyy}:</p><ul>{listHtml}</ul><p>Please follow up urgently.</p>");
            }

            foreach (var learner in missing)
            {
                if (learner.ParentId is null) continue;
                var parent = await userManager.FindByIdAsync(learner.ParentId);
                if (parent?.Email is null) continue;

                await email.SendAsync(parent.Email, parent.FullName,
                    $"{learner.FullName} Not Yet Boarded — {occ.Date:dd MMM yyyy}",
                    $"<p>Dear {parent.FullName},</p><p><strong>{learner.FullName}</strong> was not marked as boarded on the {occ.Trip.Bus?.RegistrationNumber} bus for {occ.Date:dddd, dd MMMM yyyy}.</p><p>Please contact the school if you believe this is an error.</p>");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send missing-learner alerts for occurrence {OccurrenceId}", occurrenceId);
        }
    }

    private static async Task<List<Learner>> GetRosterLearnersAsync(ApplicationDbContext db, Trip trip)
    {
        List<int> learnerIds;
        if (trip.RosterIsAllBoardingLearners)
            learnerIds = await db.RoomAllocations.Where(r => r.IsActive).Select(r => r.LearnerId).Distinct().ToListAsync();
        else
            learnerIds = await db.TripLearners.Where(tl => tl.TripId == trip.Id).Select(tl => tl.LearnerId).ToListAsync();

        return await db.Learners.Where(l => learnerIds.Contains(l.Id)).ToListAsync();
    }

    // ─── Notify Parent of Delay (UC4) ────────────────────────────────────────

    public async Task ReportDelayAsync(int occurrenceId, DelayReason reason, string? notes, TimeSpan newEta, string userId, string userName)
    {
        var occ = await _db.TripOccurrences
            .Include(o => o.Trip).ThenInclude(t => t.Bus)
            .FirstOrDefaultAsync(o => o.Id == occurrenceId)
            ?? throw new InvalidOperationException("Occurrence not found.");

        var report = new DelayReport
        {
            TripOccurrenceId = occurrenceId,
            Reason = reason,
            Notes = notes,
            NewEta = newEta,
            ReportedByUserId = userId,
            ReportedByName = userName
        };
        _db.DelayReports.Add(report);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, userName, "DelayReported", "TripOccurrence", occurrenceId.ToString(),
            $"{reason} — new ETA {newEta:hh\\:mm}");

        _ = SendDelayEmailsAsync(report.Id);
    }

    private async Task SendDelayEmailsAsync(int delayReportId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var report = await db.DelayReports
                .Include(r => r.TripOccurrence).ThenInclude(o => o.Trip).ThenInclude(t => t.Bus)
                .FirstOrDefaultAsync(r => r.Id == delayReportId);
            if (report is null) return;
            var occ = report.TripOccurrence;

            var roster = await GetRosterLearnersAsync(db, occ.Trip);
            var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var learner in roster)
            {
                if (learner.ParentId is null) continue;
                var parent = await userManager.FindByIdAsync(learner.ParentId);
                if (parent?.Email is null || !sent.Add(parent.Email)) continue;

                await email.SendAsync(parent.Email, parent.FullName,
                    $"Bus Delay Notice — {occ.Date:dd MMM yyyy}",
                    $"<p>Dear {parent.FullName},</p><p>The {occ.Trip.Bus.RegistrationNumber} bus for {learner.FullName} is delayed due to <strong>{report.Reason}</strong>.</p>" +
                    $"<p>New estimated arrival time: <strong>{report.NewEta:hh\\:mm}</strong></p>" +
                    (string.IsNullOrEmpty(report.Notes) ? "" : $"<p>{report.Notes}</p>"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send delay emails for delay report {DelayReportId}", delayReportId);
        }
    }

    // ─── Monitor Fuel Usage (UC5) ────────────────────────────────────────────

    public async Task<FuelRecord> AddFuelRecordAsync(int busId, DateTime date, decimal litres, decimal totalCost, int odometer, string? station, string userId)
    {
        var prev = await _db.FuelRecords
            .Where(f => f.BusId == busId && f.Date < date)
            .OrderByDescending(f => f.Date)
            .FirstOrDefaultAsync();

        decimal? efficiency = null;
        if (prev is not null && odometer > prev.OdometerReading && litres > 0)
            efficiency = Math.Round((odometer - prev.OdometerReading) / litres, 2);

        var record = new FuelRecord
        {
            BusId = busId,
            Date = date,
            Litres = litres,
            TotalCost = totalCost,
            OdometerReading = odometer,
            StationName = station,
            EfficiencyKmPerLitre = efficiency,
            RecordedByUserId = userId
        };
        _db.FuelRecords.Add(record);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, null, "FuelRecordAdded", "FuelRecord", record.Id.ToString(),
            $"Bus {busId}: {litres}L R{totalCost}");

        return record;
    }

    public async Task<List<FuelRecord>> GetFuelRecordsAsync(int? busId = null)
    {
        var q = _db.FuelRecords.Include(f => f.Bus).AsQueryable();
        if (busId.HasValue) q = q.Where(f => f.BusId == busId.Value);
        return await q.OrderByDescending(f => f.Date).ToListAsync();
    }

    public async Task<FuelReportDto> GetFuelReportAsync(int? busId, DateTime? from, DateTime? to)
    {
        var fromDate = (from ?? DateTime.Today.AddMonths(-1)).Date;
        var toDate = (to ?? DateTime.Today).Date;

        var q = _db.FuelRecords.Include(f => f.Bus)
            .Where(f => f.Date >= fromDate && f.Date <= toDate)
            .AsQueryable();
        if (busId.HasValue) q = q.Where(f => f.BusId == busId.Value);

        var records = await q.OrderBy(f => f.Date).ToListAsync();
        var bus = busId.HasValue ? await _db.Buses.FindAsync(busId.Value) : null;
        var withEfficiency = records.Where(r => r.EfficiencyKmPerLitre.HasValue).ToList();

        return new FuelReportDto(
            busId, bus?.RegistrationNumber, fromDate, toDate, records,
            records.Sum(r => r.Litres), records.Sum(r => r.TotalCost),
            withEfficiency.Count > 0 ? Math.Round(withEfficiency.Average(r => r.EfficiencyKmPerLitre!.Value), 2) : null);
    }
}
