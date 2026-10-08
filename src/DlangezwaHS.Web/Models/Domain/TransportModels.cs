using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DlangezwaHS.Web.Models.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// ENUMS
// ─────────────────────────────────────────────────────────────────────────────

public enum TransportRoute { BoardingToSchool, SchoolToBoarding }
public enum TransportPurpose { DailyShuttle, Sport, Cultural, Other }
public enum TripStatus { Scheduled, Active, Completed, Cancelled }
public enum DelayReason { Traffic, Breakdown, Weather, Accident, Other }
public enum BoardingStatus { NotBoarded, Boarded, Absent }

// ─────────────────────────────────────────────────────────────────────────────
// BUS
// ─────────────────────────────────────────────────────────────────────────────

public class Bus
{
    public int Id { get; set; }
    [Required, StringLength(20)] public string RegistrationNumber { get; set; } = "";
    [Required, StringLength(100)] public string MakeModel { get; set; } = "";
    public int Capacity { get; set; } = 60;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
    public ICollection<FuelRecord> FuelRecords { get; set; } = new List<FuelRecord>();
}

// ─────────────────────────────────────────────────────────────────────────────
// DRIVER
// ─────────────────────────────────────────────────────────────────────────────

public class Driver
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, StringLength(30)] public string IdNumber { get; set; } = "";
    [StringLength(20)] public string? Phone { get; set; }
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [Required, StringLength(30)] public string LicenseNumber { get; set; } = "";
    public DateTime LicenseExpiryDate { get; set; }
    public DateTime EmploymentDate { get; set; }
    [StringLength(100)] public string? ShiftHours { get; set; }
    public string? PhotoPath { get; set; }
    public string? LicenseDocPath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped] public bool LicenseExpiringSoon => (LicenseExpiryDate - DateTime.Today).TotalDays is > 0 and <= 30;
    [NotMapped] public bool LicenseExpired => LicenseExpiryDate < DateTime.Today;

    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}

// ─────────────────────────────────────────────────────────────────────────────
// TRIP  (schedule definition — recurring daily shuttle OR a single trip)
// ─────────────────────────────────────────────────────────────────────────────

public class Trip
{
    public int Id { get; set; }

    public int BusId { get; set; }
    [ForeignKey(nameof(BusId))] public Bus Bus { get; set; } = null!;

    public int DriverId { get; set; }
    [ForeignKey(nameof(DriverId))] public Driver Driver { get; set; } = null!;

    public TransportRoute Route { get; set; }
    public TransportPurpose Purpose { get; set; }
    [StringLength(200)] public string? PurposeDetail { get; set; } // e.g. "Soccer vs St Mary's"

    public TimeSpan DepartureTime { get; set; }
    public TimeSpan ArrivalTime { get; set; }

    // Recurring daily shuttle: DaysOfWeek set (e.g. "Mon,Tue,Wed,Thu,Fri"), TripDate null.
    // One-off trip: TripDate set, DaysOfWeek null.
    public bool IsRecurring { get; set; }
    [StringLength(50)] public string? DaysOfWeek { get; set; }
    public DateTime? TripDate { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Scheduled;

    // true = roster resolves dynamically to all active boarding learners (RoomAllocation)
    // false = explicit roster via TripLearner (sport/cultural trips)
    public bool RosterIsAllBoardingLearners { get; set; } = true;

    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TripLearner> TripLearners { get; set; } = new List<TripLearner>();
    public ICollection<TripOccurrence> Occurrences { get; set; } = new List<TripOccurrence>();
}

// Explicit roster row — used when a Trip isn't "all boarding learners"
public class TripLearner
{
    public int Id { get; set; }
    public int TripId { get; set; }
    [ForeignKey(nameof(TripId))] public Trip Trip { get; set; } = null!;
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
}

// One dated "run" of a Trip — created on demand the first time Board-the-Bus /
// Report-Delay is opened for that Trip+Date. Gives each calendar day of a
// recurring shuttle its own independent boarding/delay log.
public class TripOccurrence
{
    public int Id { get; set; }
    public int TripId { get; set; }
    [ForeignKey(nameof(TripId))] public Trip Trip { get; set; } = null!;
    public DateTime Date { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Scheduled;
    public DateTime? BoardingConfirmedAt { get; set; }
    public string? BoardingConfirmedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BoardingRecord> BoardingRecords { get; set; } = new List<BoardingRecord>();
    public ICollection<DelayReport> DelayReports { get; set; } = new List<DelayReport>();
}

// Per learner per occurrence — the "Board the Bus" attendance log
public class BoardingRecord
{
    public int Id { get; set; }
    public int TripOccurrenceId { get; set; }
    [ForeignKey(nameof(TripOccurrenceId))] public TripOccurrence TripOccurrence { get; set; } = null!;
    public int LearnerId { get; set; }
    [ForeignKey(nameof(LearnerId))] public Learner Learner { get; set; } = null!;
    public BoardingStatus Status { get; set; } = BoardingStatus.NotBoarded;
    public DateTime? BoardedAt { get; set; }
    public string? MarkedByUserId { get; set; }
}

public class DelayReport
{
    public int Id { get; set; }
    public int TripOccurrenceId { get; set; }
    [ForeignKey(nameof(TripOccurrenceId))] public TripOccurrence TripOccurrence { get; set; } = null!;
    public DelayReason Reason { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public TimeSpan NewEta { get; set; }
    public string ReportedByUserId { get; set; } = "";
    [StringLength(150)] public string? ReportedByName { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
}

public class FuelRecord
{
    public int Id { get; set; }
    public int BusId { get; set; }
    [ForeignKey(nameof(BusId))] public Bus Bus { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Today;
    [Column(TypeName = "decimal(8,2)")] public decimal Litres { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal TotalCost { get; set; }
    public int OdometerReading { get; set; }
    [StringLength(150)] public string? StationName { get; set; }
    // Computed at save time from the previous record's odometer for the same bus
    [Column(TypeName = "decimal(6,2)")] public decimal? EfficiencyKmPerLitre { get; set; }
    public string RecordedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
