using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Controllers;

[Authorize]
public class TransportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITransportService _svc;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPdfService _pdf;
    private readonly ILogger<TransportController> _logger;

    public TransportController(ApplicationDbContext db, ITransportService svc,
        UserManager<ApplicationUser> userManager, IPdfService pdf, ILogger<TransportController> logger)
    {
        _db = db;
        _svc = svc;
        _userManager = userManager;
        _pdf = pdf;
        _logger = logger;
    }

    private string UserId => _userManager.GetUserId(User)!;
    private string UserName => User.Identity?.Name ?? "Staff";

    private async Task<Teacher?> GetCurrentTeacherAsync()
        => await _db.Teachers.FirstOrDefaultAsync(t => t.UserId == UserId);

    private static string TripLabel(Trip t) => $"{t.Purpose} — {t.Route} ({t.Bus?.RegistrationNumber})";

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher,Housemaster")]
    public async Task<IActionResult> Index()
    {
        var trips = await _svc.GetTripsAsync();

        return View(new TransportDashboardViewModel
        {
            IsAdmin = User.IsInRole("Admin"),
            IsHousemaster = User.IsInRole("Housemaster"),
            TotalBuses = (await _svc.GetBusesAsync()).Count,
            TotalDrivers = (await _svc.GetDriversAsync()).Count,
            ExpiringLicenses = (await _svc.GetExpiringLicensesAsync()).Count,
            UpcomingTrips = trips.Take(10).ToList()
        });
    }

    // ── Schedule Transport (UC1) ─────────────────────────────────────────────

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> ScheduleTransport()
    {
        var vm = new ScheduleTransportViewModel();
        await PopulateScheduleDropdownsAsync(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> ScheduleTransport(ScheduleTransportViewModel vm)
    {
        if (!vm.IsRecurring) vm.SelectedDays.Clear();

        if (!ModelState.IsValid)
        {
            await PopulateScheduleDropdownsAsync(vm);
            return View(vm);
        }

        var data = new CreateTripData(
            vm.BusId, vm.DriverId, vm.Route, vm.Purpose, vm.PurposeDetail,
            vm.DepartureTime, vm.ArrivalTime, vm.IsRecurring,
            vm.IsRecurring ? string.Join(",", vm.SelectedDays) : null,
            vm.IsRecurring ? null : vm.TripDate,
            vm.RecurrenceEndDate, vm.RosterIsAllBoardingLearners);

        try
        {
            await _svc.CreateTripAsync(data, vm.SelectedLearnerIds, UserId, UserName);
            TempData["Success"] = "Transport scheduled and boarding learners' parents notified.";
            return RedirectToAction(nameof(Trips));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling transport");
            TempData["Error"] = ex.Message;
            await PopulateScheduleDropdownsAsync(vm);
            return View(vm);
        }
    }

    private async Task PopulateScheduleDropdownsAsync(ScheduleTransportViewModel vm)
    {
        vm.Buses = await _svc.GetBusesAsync();
        vm.Drivers = await _svc.GetDriversAsync();
        var boardingLearnerIds = await _db.RoomAllocations.Where(r => r.IsActive)
            .Select(r => r.LearnerId).Distinct().ToListAsync();
        vm.BoardingLearners = await _db.Learners.Where(l => boardingLearnerIds.Contains(l.Id))
            .OrderBy(l => l.LastName).ToListAsync();
    }

    // ── Trips list ────────────────────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> Trips()
    {
        List<Trip> trips;
        if (User.IsInRole("Parent"))
        {
            var learnerIds = await _db.Learners.Where(l => l.ParentId == UserId).Select(l => l.Id).ToListAsync();
            var byId = new Dictionary<int, Trip>();
            foreach (var lid in learnerIds)
                foreach (var t in await _svc.GetTripsForLearnerAsync(lid))
                    byId[t.Id] = t;
            trips = byId.Values.OrderByDescending(t => t.CreatedAt).ToList();
        }
        else
        {
            trips = await _svc.GetTripsAsync();
        }
        return View(trips);
    }

    // ── Board the Bus (UC2) ──────────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> BoardTheBus(int tripId, DateTime? date = null)
    {
        var trip = await _svc.GetTripAsync(tripId);
        if (trip is null) return NotFound();

        var occ = await _svc.GetOrCreateOccurrenceAsync(tripId, date ?? DateTime.Today);
        var roster = await _svc.GetRosterAsync(occ.Id);

        bool readOnly = User.IsInRole("Parent");
        if (readOnly)
        {
            var learnerIds = (await _db.Learners.Where(l => l.ParentId == UserId)
                .Select(l => l.Id).ToListAsync()).ToHashSet();
            roster = roster.Where(r => learnerIds.Contains(r.LearnerId)).ToList();
            if (roster.Count == 0) return Forbid();
        }

        return View(new BoardTheBusViewModel
        {
            TripId = tripId,
            OccurrenceId = occ.Id,
            Date = occ.Date,
            TripLabel = TripLabel(trip),
            ReadOnly = readOnly,
            Learners = roster.Select(r => new BoardingMarkRow
            {
                LearnerId = r.LearnerId,
                FullName = r.FullName,
                Status = r.Status
            }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> ConfirmAttendance(BoardTheBusViewModel vm)
    {
        try
        {
            var marks = vm.Learners.Select(l => (l.LearnerId, l.Status)).ToList();
            await _svc.ConfirmAttendanceAsync(vm.OccurrenceId, marks, UserId);
            TempData["Success"] = $"Attendance confirmed for {vm.Date:dd MMM yyyy}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming boarding attendance");
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(BoardTheBus), new { tripId = vm.TripId, date = vm.Date.ToString("yyyy-MM-dd") });
    }

    // ── Drivers (UC3) ────────────────────────────────────────────────────────

    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> Drivers()
    {
        var drivers = await _svc.GetDriversAsync(activeOnly: false);
        return View(drivers);
    }

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DriverEdit(int? id)
    {
        if (!id.HasValue) return View(new DriverEditViewModel());

        var driver = await _svc.GetDriverAsync(id.Value);
        if (driver is null) return NotFound();

        return View(new DriverEditViewModel
        {
            Id = driver.Id,
            FullName = driver.FullName,
            IdNumber = driver.IdNumber,
            Phone = driver.Phone,
            Email = driver.Email,
            Address = driver.Address,
            LicenseNumber = driver.LicenseNumber,
            LicenseExpiryDate = driver.LicenseExpiryDate,
            EmploymentDate = driver.EmploymentDate,
            ShiftHours = driver.ShiftHours,
            ExistingPhotoPath = driver.PhotoPath,
            ExistingLicenseDocPath = driver.LicenseDocPath
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DriverEdit(DriverEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            var data = new DriverData(vm.Id, vm.FullName, vm.IdNumber, vm.Phone, vm.Email, vm.Address,
                vm.LicenseNumber, vm.LicenseExpiryDate, vm.EmploymentDate, vm.ShiftHours);
            await _svc.SaveDriverAsync(data, vm.Photo, vm.LicenseDoc);
            TempData["Success"] = "Driver saved.";
            return RedirectToAction(nameof(Drivers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving driver");
            TempData["Error"] = ex.Message;
            return View(vm);
        }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeactivateDriver(int id)
    {
        await _svc.DeactivateDriverAsync(id);
        TempData["Success"] = "Driver deactivated.";
        return RedirectToAction(nameof(Drivers));
    }

    // ── Notify Parent of Delay (UC4) ─────────────────────────────────────────

    [HttpGet, Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> ReportDelay(int tripId, DateTime? date = null)
    {
        var trip = await _svc.GetTripAsync(tripId);
        if (trip is null) return NotFound();

        var occ = await _svc.GetOrCreateOccurrenceAsync(tripId, date ?? DateTime.Today);
        var pastReports = await _db.DelayReports
            .Where(d => d.TripOccurrenceId == occ.Id)
            .OrderByDescending(d => d.ReportedAt)
            .ToListAsync();

        return View(new ReportDelayViewModel
        {
            TripId = tripId,
            OccurrenceId = occ.Id,
            Date = occ.Date,
            TripLabel = TripLabel(trip),
            NewEta = trip.ArrivalTime,
            PastReports = pastReports
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> ReportDelay(ReportDelayViewModel vm)
    {
        try
        {
            await _svc.ReportDelayAsync(vm.OccurrenceId, vm.Reason, vm.Notes, vm.NewEta, UserId, UserName);
            TempData["Success"] = "Delay reported. Parents of learners on this bus have been notified.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting delay");
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(ReportDelay), new { tripId = vm.TripId, date = vm.Date.ToString("yyyy-MM-dd") });
    }

    // ── Monitor Fuel Usage (UC5) ─────────────────────────────────────────────

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> FuelRecords(int? busId)
    {
        var records = await _svc.GetFuelRecordsAsync(busId);
        ViewBag.Buses = await _svc.GetBusesAsync();
        ViewBag.SelBus = busId;
        return View(records);
    }

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> FuelRecordCreate()
    {
        return View(new FuelRecordCreateViewModel { Buses = await _svc.GetBusesAsync() });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> FuelRecordCreate(FuelRecordCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Buses = await _svc.GetBusesAsync();
            return View(vm);
        }

        await _svc.AddFuelRecordAsync(vm.BusId, vm.Date, vm.Litres, vm.TotalCost, vm.OdometerReading, vm.StationName, UserId);
        TempData["Success"] = "Fuel record added.";
        return RedirectToAction(nameof(FuelRecords));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> FuelReport(int? busId, DateTime? from, DateTime? to)
    {
        var report = await _svc.GetFuelReportAsync(busId, from, to);
        return View(new FuelReportViewModel
        {
            BusId = busId,
            From = report.From,
            To = report.To,
            Buses = await _svc.GetBusesAsync(),
            Report = report
        });
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> FuelReportPdf(int? busId, DateTime? from, DateTime? to)
    {
        var report = await _svc.GetFuelReportAsync(busId, from, to);
        var bytes = _pdf.GenerateFuelReportPdf(report);
        return File(bytes, "application/pdf", $"fuel-report-{DateTime.Now:yyyyMMdd}.pdf");
    }

    // ── Buses ─────────────────────────────────────────────────────────────────

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Buses()
    {
        var buses = await _svc.GetBusesAsync(activeOnly: false);
        return View(buses);
    }

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> BusEdit(int? id)
    {
        if (!id.HasValue) return View(new BusEditViewModel());
        var bus = await _svc.GetBusAsync(id.Value);
        if (bus is null) return NotFound();
        return View(new BusEditViewModel { Id = bus.Id, RegistrationNumber = bus.RegistrationNumber, MakeModel = bus.MakeModel, Capacity = bus.Capacity });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> BusEdit(BusEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _svc.SaveBusAsync(vm.Id, vm.RegistrationNumber, vm.MakeModel, vm.Capacity);
        TempData["Success"] = "Bus saved.";
        return RedirectToAction(nameof(Buses));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeactivateBus(int id)
    {
        await _svc.DeactivateBusAsync(id);
        TempData["Success"] = "Bus deactivated.";
        return RedirectToAction(nameof(Buses));
    }

    // ── Housemaster Alerts ───────────────────────────────────────────────────

    [Authorize(Roles = "Housemaster")]
    public async Task<IActionResult> HousemasterAlerts()
    {
        var since = DateTime.Today.AddDays(-14);
        var occurrences = await _db.TripOccurrences
            .Include(o => o.Trip).ThenInclude(t => t.Bus)
            .Include(o => o.BoardingRecords).ThenInclude(b => b.Learner)
            .Where(o => o.Date >= since && o.BoardingConfirmedAt != null)
            .OrderByDescending(o => o.Date)
            .ToListAsync();

        var alerts = occurrences
            .Select(o => new HousemasterAlertRow
            {
                OccurrenceId = o.Id,
                Date = o.Date,
                BusReg = o.Trip.Bus.RegistrationNumber,
                MissingLearners = o.BoardingRecords
                    .Where(b => b.Status != BoardingStatus.Boarded)
                    .Select(b => b.Learner.FullName)
                    .ToList()
            })
            .Where(a => a.MissingLearners.Count > 0)
            .ToList();

        return View(alerts);
    }
}
