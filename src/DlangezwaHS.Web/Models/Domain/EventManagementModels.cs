using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DlangezwaHS.Web.Models.Domain;

// ─────────────────────────────────────────────────────────────────────────────
// ENUMS
// ─────────────────────────────────────────────────────────────────────────────

public enum SchoolEventStatus
{
    Draft     = 0,
    Published = 1,
    Ongoing   = 2,
    Completed = 3,
    Cancelled = 4
}

public enum TicketType   { RSVP = 0, Gate = 1 }
public enum TicketStatus { Unused = 0, Used = 1, Cancelled = 2 }
public enum ScanResult   { Valid = 0, AlreadyUsed = 1, Invalid = 2 }
public enum RSVPStatus   { Confirmed = 0, Cancelled = 1 }

// ─────────────────────────────────────────────────────────────────────────────
// COORDINATOR ROLE  (configurable by Admin)
// ─────────────────────────────────────────────────────────────────────────────

public class EventCoordinatorRole
{
    public int    Id        { get; set; }

    [Required, MaxLength(100)]
    public string Name      { get; set; } = string.Empty;

    public bool   IsDefault { get; set; } = false;
    public bool   IsActive  { get; set; } = true;

    public virtual ICollection<EventCoordinator> Coordinators { get; set; } = new List<EventCoordinator>();
}

// ─────────────────────────────────────────────────────────────────────────────
// SCHOOL EVENT  (rich version of a CalendarEvent of type SchoolEvent)
// ─────────────────────────────────────────────────────────────────────────────

public class SchoolEvent
{
    public int    Id              { get; set; }

    [Required]
    public int    CalendarEventId { get; set; }

    [MaxLength(200)]
    public string? Venue          { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Budget         { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal ActualSpend    { get; set; }

    public int    MaxParticipants { get; set; }

    [Required, MaxLength(64)]
    public string GateTicketHash  { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal GateTicketPrice { get; set; }

    public SchoolEventStatus Status { get; set; } = SchoolEventStatus.Draft;

    public DateTime?  PublishedAt       { get; set; }
    public string     CreatedByUserId   { get; set; } = string.Empty;
    public DateTime   CreatedAt         { get; set; } = DateTime.UtcNow;

    [MaxLength(2000)]
    public string? Notes { get; set; }

    // Navigation
    [ForeignKey(nameof(CalendarEventId))]
    public virtual CalendarEvent           CalendarEvent  { get; set; } = null!;
    public virtual ICollection<EventCoordinator> Coordinators { get; set; } = new List<EventCoordinator>();
    public virtual ICollection<EventTicket>      Tickets      { get; set; } = new List<EventTicket>();
    public virtual ICollection<EventRSVP>        RSVPs        { get; set; } = new List<EventRSVP>();
    public virtual ICollection<EventScan>        Scans        { get; set; } = new List<EventScan>();

    // Helpers
    [NotMapped]
    public int RSVPCount   => RSVPs.Count(r => r.Status == RSVPStatus.Confirmed);
    [NotMapped]
    public int ScannedCount => Tickets.Count(t => t.Status == TicketStatus.Used);
    [NotMapped]
    public bool IsPublished => Status == SchoolEventStatus.Published || Status == SchoolEventStatus.Ongoing;
    [NotMapped]
    public string StatusBadge => Status switch
    {
        SchoolEventStatus.Draft     => "bg-secondary",
        SchoolEventStatus.Published => "bg-success",
        SchoolEventStatus.Ongoing   => "bg-primary",
        SchoolEventStatus.Completed => "bg-dark",
        SchoolEventStatus.Cancelled => "bg-danger",
        _                           => "bg-light"
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// EVENT COORDINATOR
// ─────────────────────────────────────────────────────────────────────────────

public class EventCoordinator
{
    public int    Id            { get; set; }
    public int    SchoolEventId { get; set; }

    [Required, MaxLength(450)]
    public string UserId        { get; set; } = string.Empty;

    public int    RoleId        { get; set; }
    public DateTime AssignedAt  { get; set; } = DateTime.UtcNow;
    public bool   IsActive      { get; set; } = true;

    // Navigation
    [ForeignKey(nameof(SchoolEventId))]
    public virtual SchoolEvent          SchoolEvent { get; set; } = null!;
    [ForeignKey(nameof(RoleId))]
    public virtual EventCoordinatorRole Role        { get; set; } = null!;

    // Not mapped — populated in service layer
    [NotMapped] public string CoordinatorName  { get; set; } = string.Empty;
    [NotMapped] public string CoordinatorEmail { get; set; } = string.Empty;
    [NotMapped] public string CoordinatorType  { get; set; } = string.Empty; // Teacher/Learner
}

// ─────────────────────────────────────────────────────────────────────────────
// EVENT TICKET
// ─────────────────────────────────────────────────────────────────────────────

public class EventTicket
{
    public int    Id            { get; set; }
    public int    SchoolEventId { get; set; }

    [Required, MaxLength(64)]
    public string Hash          { get; set; } = string.Empty;

    public TicketType   TicketType  { get; set; } = TicketType.RSVP;
    public TicketStatus Status      { get; set; } = TicketStatus.Unused;

    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    [MaxLength(200)]
    public string? OwnerName   { get; set; }

    public DateTime  IssuedAt        { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt          { get; set; }

    [MaxLength(450)]
    public string? ScannedByUserId { get; set; }

    [ForeignKey(nameof(SchoolEventId))]
    public virtual SchoolEvent SchoolEvent { get; set; } = null!;

    // QR code URL via GoQR.me
    [NotMapped]
    public string QrCodeUrl => $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(Hash)}";
}

// ─────────────────────────────────────────────────────────────────────────────
// EVENT RSVP
// ─────────────────────────────────────────────────────────────────────────────

public class EventRSVP
{
    public int    Id            { get; set; }
    public int    SchoolEventId { get; set; }

    [Required, MaxLength(450)]
    public string UserId        { get; set; } = string.Empty;

    public int?   TicketId      { get; set; }
    public DateTime RSVPedAt    { get; set; } = DateTime.UtcNow;
    public RSVPStatus Status    { get; set; } = RSVPStatus.Confirmed;

    [ForeignKey(nameof(SchoolEventId))]
    public virtual SchoolEvent  SchoolEvent { get; set; } = null!;
    [ForeignKey(nameof(TicketId))]
    public virtual EventTicket? Ticket      { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// EVENT SCAN  (audit log)
// ─────────────────────────────────────────────────────────────────────────────

public class EventScan
{
    public int    Id            { get; set; }
    public int    SchoolEventId { get; set; }

    [Required, MaxLength(64)]
    public string Hash          { get; set; } = string.Empty;

    [Required, MaxLength(450)]
    public string ScannedByUserId { get; set; } = string.Empty;

    public DateTime ScannedAt  { get; set; } = DateTime.UtcNow;
    public ScanResult Result   { get; set; } = ScanResult.Valid;

    [MaxLength(500)]
    public string? DeviceInfo  { get; set; }

    [ForeignKey(nameof(SchoolEventId))]
    public virtual SchoolEvent SchoolEvent { get; set; } = null!;
}
