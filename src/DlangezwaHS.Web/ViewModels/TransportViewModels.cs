using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using System.ComponentModel.DataAnnotations;

namespace DlangezwaHS.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// DASHBOARD
// ─────────────────────────────────────────────────────────────────────────────

public class TransportDashboardViewModel
{
    public bool IsAdmin { get; set; }
    public bool IsHousemaster { get; set; }
    public int TotalBuses { get; set; }
    public int TotalDrivers { get; set; }
    public int ExpiringLicenses { get; set; }
    public IList<Trip> UpcomingTrips { get; set; } = new List<Trip>();
}

// ─────────────────────────────────────────────────────────────────────────────
// SCHEDULE TRANSPORT (UC1)
// ─────────────────────────────────────────────────────────────────────────────

public class ScheduleTransportViewModel
{
    [Required] public int BusId { get; set; }
    [Required] public int DriverId { get; set; }
    public TransportRoute Route { get; set; }
    public TransportPurpose Purpose { get; set; } = TransportPurpose.DailyShuttle;
    [StringLength(200)] public string? PurposeDetail { get; set; }
    public TimeSpan DepartureTime { get; set; } = new TimeSpan(7, 0, 0);
    public TimeSpan ArrivalTime { get; set; } = new TimeSpan(7, 30, 0);

    public bool IsRecurring { get; set; } = true;
    public List<string> SelectedDays { get; set; } = new();
    public DateTime? TripDate { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }

    public bool RosterIsAllBoardingLearners { get; set; } = true;
    public List<int> SelectedLearnerIds { get; set; } = new();

    public IList<Bus> Buses { get; set; } = new List<Bus>();
    public IList<Driver> Drivers { get; set; } = new List<Driver>();
    public IList<Learner> BoardingLearners { get; set; } = new List<Learner>();
}

// ─────────────────────────────────────────────────────────────────────────────
// BOARD THE BUS (UC2)
// ─────────────────────────────────────────────────────────────────────────────

public class BoardTheBusViewModel
{
    public int TripId { get; set; }
    public int OccurrenceId { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string TripLabel { get; set; } = "";
    public bool ReadOnly { get; set; }
    public IList<BoardingMarkRow> Learners { get; set; } = new List<BoardingMarkRow>();
    public int BoardedCount => Learners.Count(l => l.Status == BoardingStatus.Boarded);
    public int TotalCount => Learners.Count;
}

public class BoardingMarkRow
{
    public int LearnerId { get; set; }
    public string FullName { get; set; } = "";
    public BoardingStatus Status { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// DRIVERS (UC3)
// ─────────────────────────────────────────────────────────────────────────────

public class DriverEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, StringLength(30)] public string IdNumber { get; set; } = "";
    [StringLength(20)] public string? Phone { get; set; }
    [EmailAddress] public string? Email { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [Required, StringLength(30)] public string LicenseNumber { get; set; } = "";
    [Required] public DateTime LicenseExpiryDate { get; set; } = DateTime.Today.AddYears(1);
    [Required] public DateTime EmploymentDate { get; set; } = DateTime.Today;
    [StringLength(100)] public string? ShiftHours { get; set; }

    public IFormFile? Photo { get; set; }
    public IFormFile? LicenseDoc { get; set; }
    public string? ExistingPhotoPath { get; set; }
    public string? ExistingLicenseDocPath { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// REPORT DELAY (UC4)
// ─────────────────────────────────────────────────────────────────────────────

public class ReportDelayViewModel
{
    public int TripId { get; set; }
    public int OccurrenceId { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string TripLabel { get; set; } = "";
    public DelayReason Reason { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public TimeSpan NewEta { get; set; }
    public IList<DelayReport> PastReports { get; set; } = new List<DelayReport>();
}

// ─────────────────────────────────────────────────────────────────────────────
// FUEL (UC5)
// ─────────────────────────────────────────────────────────────────────────────

public class FuelRecordCreateViewModel
{
    [Required] public int BusId { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Litres { get; set; }
    public decimal TotalCost { get; set; }
    public int OdometerReading { get; set; }
    [StringLength(150)] public string? StationName { get; set; }
    public IList<Bus> Buses { get; set; } = new List<Bus>();
}

public class FuelReportViewModel
{
    public int? BusId { get; set; }
    public DateTime From { get; set; } = DateTime.Today.AddMonths(-1);
    public DateTime To { get; set; } = DateTime.Today;
    public IList<Bus> Buses { get; set; } = new List<Bus>();
    public FuelReportDto? Report { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// BUSES
// ─────────────────────────────────────────────────────────────────────────────

public class BusEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(20)] public string RegistrationNumber { get; set; } = "";
    [Required, StringLength(100)] public string MakeModel { get; set; } = "";
    public int Capacity { get; set; } = 60;
}

// ─────────────────────────────────────────────────────────────────────────────
// HOUSEMASTER ALERTS
// ─────────────────────────────────────────────────────────────────────────────

public class HousemasterAlertRow
{
    public int OccurrenceId { get; set; }
    public DateTime Date { get; set; }
    public string BusReg { get; set; } = "";
    public List<string> MissingLearners { get; set; } = new();
}
