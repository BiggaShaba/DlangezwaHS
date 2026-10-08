using DlangezwaHS.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DlangezwaHS.Web.Migrations
{
    /// <summary>
    /// The calendar and event-management tables are in the model snapshot, but the migration that
    /// created them was lost — older databases have them, fresh databases (e.g. hosted ones) don't.
    /// Each table is created only when it's missing, so existing databases are left untouched.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261008020000_CalendarAndEventTables")]
    public partial class CalendarAndEventTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[EventCoordinatorRoles]') IS NULL
BEGIN
    CREATE TABLE [EventCoordinatorRoles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_EventCoordinatorRoles] PRIMARY KEY ([Id])
    );
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[CalendarEvents]') IS NULL
BEGIN
    CREATE TABLE [CalendarEvents] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NULL,
        [Type] int NOT NULL,
        [GradeId] int NULL,
        [ClassId] int NULL,
        [AssessmentId] int NULL,
        [NotificationSent] bit NOT NULL,
        [CreatedByUserId] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_CalendarEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CalendarEvents_Assessments_AssessmentId] FOREIGN KEY ([AssessmentId]) REFERENCES [Assessments] ([Id]),
        CONSTRAINT [FK_CalendarEvents_Classes_ClassId] FOREIGN KEY ([ClassId]) REFERENCES [Classes] ([Id]),
        CONSTRAINT [FK_CalendarEvents_Grades_GradeId] FOREIGN KEY ([GradeId]) REFERENCES [Grades] ([Id])
    );
    CREATE INDEX [IX_CalendarEvents_AssessmentId] ON [CalendarEvents] ([AssessmentId]);
    CREATE INDEX [IX_CalendarEvents_ClassId] ON [CalendarEvents] ([ClassId]);
    CREATE INDEX [IX_CalendarEvents_GradeId] ON [CalendarEvents] ([GradeId]);
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[SchoolEvents]') IS NULL
BEGIN
    CREATE TABLE [SchoolEvents] (
        [Id] int NOT NULL IDENTITY,
        [CalendarEventId] int NOT NULL,
        [Venue] nvarchar(200) NULL,
        [Budget] decimal(10,2) NOT NULL,
        [ActualSpend] decimal(10,2) NOT NULL,
        [MaxParticipants] int NOT NULL,
        [GateTicketHash] nvarchar(64) NOT NULL,
        [GateTicketPrice] decimal(10,2) NOT NULL,
        [Status] int NOT NULL,
        [PublishedAt] datetime2 NULL,
        [CreatedByUserId] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [Notes] nvarchar(2000) NULL,
        CONSTRAINT [PK_SchoolEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SchoolEvents_CalendarEvents_CalendarEventId] FOREIGN KEY ([CalendarEventId]) REFERENCES [CalendarEvents] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_SchoolEvents_CalendarEventId] ON [SchoolEvents] ([CalendarEventId]);
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[EventCoordinators]') IS NULL
BEGIN
    CREATE TABLE [EventCoordinators] (
        [Id] int NOT NULL IDENTITY,
        [SchoolEventId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] int NOT NULL,
        [AssignedAt] datetime2 NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_EventCoordinators] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventCoordinators_EventCoordinatorRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [EventCoordinatorRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EventCoordinators_SchoolEvents_SchoolEventId] FOREIGN KEY ([SchoolEventId]) REFERENCES [SchoolEvents] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_EventCoordinators_RoleId] ON [EventCoordinators] ([RoleId]);
    CREATE INDEX [IX_EventCoordinators_SchoolEventId] ON [EventCoordinators] ([SchoolEventId]);
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[EventScans]') IS NULL
BEGIN
    CREATE TABLE [EventScans] (
        [Id] int NOT NULL IDENTITY,
        [SchoolEventId] int NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        [ScannedByUserId] nvarchar(450) NOT NULL,
        [ScannedAt] datetime2 NOT NULL,
        [Result] int NOT NULL,
        [DeviceInfo] nvarchar(500) NULL,
        CONSTRAINT [PK_EventScans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventScans_SchoolEvents_SchoolEventId] FOREIGN KEY ([SchoolEventId]) REFERENCES [SchoolEvents] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_EventScans_SchoolEventId] ON [EventScans] ([SchoolEventId]);
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[EventTickets]') IS NULL
BEGIN
    CREATE TABLE [EventTickets] (
        [Id] int NOT NULL IDENTITY,
        [SchoolEventId] int NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        [TicketType] int NOT NULL,
        [Status] int NOT NULL,
        [OwnerUserId] nvarchar(450) NULL,
        [OwnerName] nvarchar(200) NULL,
        [IssuedAt] datetime2 NOT NULL,
        [UsedAt] datetime2 NULL,
        [ScannedByUserId] nvarchar(450) NULL,
        CONSTRAINT [PK_EventTickets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventTickets_SchoolEvents_SchoolEventId] FOREIGN KEY ([SchoolEventId]) REFERENCES [SchoolEvents] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_EventTickets_SchoolEventId] ON [EventTickets] ([SchoolEventId]);
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[EventRSVPs]') IS NULL
BEGIN
    CREATE TABLE [EventRSVPs] (
        [Id] int NOT NULL IDENTITY,
        [SchoolEventId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [TicketId] int NULL,
        [RSVPedAt] datetime2 NOT NULL,
        [Status] int NOT NULL,
        CONSTRAINT [PK_EventRSVPs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventRSVPs_EventTickets_TicketId] FOREIGN KEY ([TicketId]) REFERENCES [EventTickets] ([Id]),
        CONSTRAINT [FK_EventRSVPs_SchoolEvents_SchoolEventId] FOREIGN KEY ([SchoolEventId]) REFERENCES [SchoolEvents] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_EventRSVPs_SchoolEventId] ON [EventRSVPs] ([SchoolEventId]);
    CREATE INDEX [IX_EventRSVPs_TicketId] ON [EventRSVPs] ([TicketId]);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: on older databases these tables existed before this migration,
            // so rolling it back must not drop them (or their data).
        }
    }
}
