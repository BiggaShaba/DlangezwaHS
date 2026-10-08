using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DlangezwaHS.Web.Services;

public interface IPdfService
{
    byte[] GenerateRegistrationProof(Enrollment enrollment, Payment? registrationPayment, Payment? accommodationPayment, RoomAllocation? allocation);
    byte[] GeneratePaymentReceipt(Payment payment);
    byte[] GenerateTimetablePdf(TimetableGridVm vm);
    byte[] GenerateTeacherTimetablePdf(TeacherDayVm vm);
    byte[] GenerateFuelReportPdf(FuelReportDto vm);
    byte[] GenerateActivityReportPdf(ActivityReportDto vm);
    byte[] GenerateMealComplianceReportPdf(MealComplianceReportDto vm);
}

public class PdfService : IPdfService
{
    private readonly ILogger<PdfService> _logger;

    // Brand colours
    private static readonly string Navy = "#0B2A4A";
    private static readonly string LightBlue = "#97C8E9";
    private static readonly string Grey = "#6B7280";

    public PdfService(ILogger<PdfService> logger)
    {
        // QuestPDF's licence is set once at startup (Program.cs). Touching QuestPDF here
        // would load its native libraries whenever a controller that merely injects this
        // service is created — so a PDF problem would break pages that never make a PDF.
        _logger = logger;
    }

    public byte[] GenerateRegistrationProof(Enrollment enrollment, Payment? registrationPayment,
        Payment? accommodationPayment, RoomAllocation? allocation)
    {
        try
        {
            var learner = enrollment.Learner!;
            var @class = enrollment.Class!;
            var subjects = enrollment.EnrollmentSubjects.Select(es => es.Subject?.Name ?? "").ToList();
            var now = DateTime.Now;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(content =>
                    {
                        content.Column(col =>
                        {
                            col.Item().PaddingTop(10).AlignCenter().Text("PROOF OF REGISTRATION")
                                .Bold().FontSize(18).FontColor(Navy);

                            col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(LightBlue);

                            col.Item().PaddingTop(8).Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Learner Details").Bold().FontColor(Navy);
                                    LabelValue(c, "Full Name", learner.FullName);
                                    LabelValue(c, "ID / Passport", learner.LearnerIdNumber);
                                    LabelValue(c, "Date of Birth", learner.DateOfBirth.ToString("dd MMM yyyy"));
                                    LabelValue(c, "Gender", learner.Gender);
                                });
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Academic Details").Bold().FontColor(Navy);
                                    LabelValue(c, "Class", @class.DisplayName);
                                    LabelValue(c, "Enrolled", enrollment.EnrolledAt.ToString("dd MMM yyyy"));
                                    LabelValue(c, "Subjects", string.Join(", ", subjects));
                                });
                            });

                            col.Item().PaddingTop(12).Text("Accommodation").Bold().FontColor(Navy);
                            col.Item().LineHorizontal(0.5f).LineColor(LightBlue);
                            if (allocation is not null)
                            {
                                col.Item().PaddingTop(4).Row(row =>
                                {
                                    row.RelativeItem().Column(c =>
                                    {
                                        LabelValue(c, "Room", allocation.Room?.Name ?? "");
                                        LabelValue(c, "Bed", allocation.Bed?.BedNumber ?? "");
                                    });
                                });
                            }
                            else
                            {
                                col.Item().Text("No boarding accommodation allocated.").FontColor(Grey);
                            }

                            col.Item().PaddingTop(12).Text("Payments").Bold().FontColor(Navy);
                            col.Item().LineHorizontal(0.5f).LineColor(LightBlue);
                            col.Item().PaddingTop(4).Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                });
                                table.Header(h =>
                                {
                                    h.Cell().Text("Description").Bold();
                                    h.Cell().Text("Amount").Bold();
                                    h.Cell().Text("Reference").Bold();
                                    h.Cell().Text("Date").Bold();
                                });
                                if (registrationPayment is not null)
                                    PayRow(table, "Registration Fee",
                                        registrationPayment.Amount,
                                        registrationPayment.ProviderRef ?? registrationPayment.BankRef ?? "-",
                                        registrationPayment.PaidAt);
                                if (accommodationPayment is not null)
                                    PayRow(table, "Accommodation Fee",
                                        accommodationPayment.Amount,
                                        accommodationPayment.ProviderRef ?? accommodationPayment.BankRef ?? "-",
                                        accommodationPayment.PaidAt);
                            });

                            col.Item().PaddingTop(20).AlignRight().Text($"Document generated: {now:dd MMM yyyy HH:mm}")
                                .FontSize(9).FontColor(Grey);
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Dlangezwa High School | KwaZulu-Natal | ")
                            .FontSize(9).FontColor(Grey);
                        text.Span("Page ").FontSize(9);
                        text.CurrentPageNumber().FontSize(9);
                        text.Span(" of ").FontSize(9);
                        text.TotalPages().FontSize(9);
                    });
                });
            });

            return doc.GeneratePdf();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating registration proof PDF");
            throw;
        }
    }

    public byte[] GeneratePaymentReceipt(Payment payment)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11));
                page.Header().Element(ComposeHeader);
                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(10).AlignCenter().Text("PAYMENT RECEIPT")
                        .Bold().FontSize(16).FontColor(Navy);
                    col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(LightBlue);
                    LabelValue(col, "Learner", payment.Learner?.FullName ?? "");
                    LabelValue(col, "Type", payment.Type.ToString());
                    LabelValue(col, "Amount", $"R {payment.Amount:N2}");
                    LabelValue(col, "Reference", payment.ProviderRef ?? payment.BankRef ?? payment.Id.ToString());
                    LabelValue(col, "Method", payment.Method.ToString());
                    LabelValue(col, "Date", (payment.PaidAt ?? payment.CreatedAt).ToString("dd MMM yyyy HH:mm"));
                    LabelValue(col, "Status", payment.Status.ToString());
                });
            });
        });
        return doc.GeneratePdf();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static void ComposeHeader(IContainer c)
    {
        c.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("DLANGEZWA HIGH SCHOOL").Bold().FontSize(14).FontColor(Navy);
                col.Item().Text("KwaZulu-Natal, South Africa").FontSize(9).FontColor(Grey);
            });
        });
    }

    private static void LabelValue(ColumnDescriptor col, string label, string value)
    {
        col.Item().PaddingTop(3).Row(row =>
        {
            row.ConstantItem(140).Text(label + ":").SemiBold();
            row.RelativeItem().Text(value);
        });
    }

    private static void PayRow(TableDescriptor table, string desc, decimal amount, string @ref, DateTime? date)
    {
        table.Cell().Text(desc);
        table.Cell().Text($"R {amount:N2}");
        table.Cell().Text(@ref);
        table.Cell().Text(date.HasValue ? date.Value.ToString("dd MMM yyyy") : "-");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TIMETABLE PDF  (class grid — for Learner and Parent download)
    // ─────────────────────────────────────────────────────────────────────────

    public byte[] GenerateTimetablePdf(TimetableGridVm vm)
    {
        var days = new[] { (1, "Monday"), (2, "Tuesday"), (3, "Wednesday"), (4, "Thursday"), (5, "Friday") };
        var periods = SchoolDay.Periods;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("DLANGEZWA HIGH SCHOOL")
                                .FontSize(14).Bold().FontColor(Navy);
                            c.Item().Text($"Class Timetable — {vm.SelectedClass?.DisplayName ?? ""} · {vm.Term} {vm.AcademicYear}")
                                .FontSize(10).FontColor(Grey);
                        });
                        row.ConstantItem(120).AlignRight().Text($"Generated: {DateTime.Now:dd MMM yyyy}")
                            .FontSize(8).FontColor(Grey);
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Navy);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    // Columns: Time label + 5 days
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(80);
                        for (int i = 0; i < 5; i++) cols.RelativeColumn();
                    });

                    // Header row
                    table.Header(h =>
                    {
                        h.Cell().Background(Navy).Padding(4)
                            .Text("Time").FontColor("#ffffff").Bold().FontSize(8);
                        foreach (var (_, dName) in days)
                            h.Cell().Background(Navy).Padding(4)
                                .AlignCenter().Text(dName).FontColor("#ffffff").Bold().FontSize(8);
                    });

                    // Assembly row
                    void AssemblyRow()
                    {
                        table.Cell().Background("#e8f0fe").Padding(4)
                            .Text("08:30–09:00").FontSize(7).FontColor("#1a56db").Bold();
                        foreach (var (d, _) in days)
                        {
                            var bg = (d == 1 || d == 5) ? "#e8f0fe" : "#fafafa";
                            table.Cell().Background(bg).Padding(4).AlignCenter()
                                .Text(d == 1 || d == 5 ? "Assembly" : "")
                                .FontSize(8).FontColor("#1a56db").Bold();
                        }
                    }
                    AssemblyRow();

                    // Period rows + break rows
                    foreach (var period in periods)
                    {
                        var breakBefore = SchoolDay.Breaks.FirstOrDefault(b => b.AfterPeriod == period.Number - 1);
                        if (breakBefore != null)
                        {
                            table.Cell().Background("#fef9e7").Padding(3)
                                .Text($"{breakBefore.Start}–{breakBefore.End}")
                                .FontSize(7).FontColor("#b7791f").Bold();
                            table.Cell().ColumnSpan(5).Background("#fef9e7").Padding(3).AlignCenter()
                                .Text($"{breakBefore.Label} — 15 minutes")
                                .FontSize(8).FontColor("#b7791f").Bold();
                        }

                        // Time cell
                        table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(4).Column(c =>
                        {
                            c.Item().Text(period.Label).Bold().FontSize(8);
                            c.Item().Text($"{period.Start} – {period.End}").FontSize(7).FontColor(Grey);
                        });

                        foreach (var (d, _) in days)
                        {
                            var available = SchoolDay.IsPeriodAvailable(period.Number, d);
                            if (!available)
                            {
                                table.Cell().Background("#f1f5f9").BorderBottom(1).BorderColor("#e2e8f0")
                                    .Padding(4).AlignCenter().Text("—").FontColor("#94a3b8").FontSize(8);
                                continue;
                            }
                            var slot = vm.Grid[d, period.Number];
                            if (slot == null)
                            {
                                table.Cell().Background("#f8fafc").BorderBottom(1).BorderColor("#e2e8f0").Padding(4);
                                continue;
                            }
                            var color = vm.SubjectColors.TryGetValue(slot.SubjectId, out var c2) ? c2 : "#5ea8d3";
                            table.Cell().Background(color).BorderBottom(1).BorderColor("#e2e8f0")
                                .Padding(4).Column(c =>
                                {
                                    c.Item().Text(slot.Subject?.Name ?? "").Bold().FontSize(8).FontColor("#ffffff");
                                    c.Item().Text(slot.Teacher?.FullName ?? "").FontSize(7).FontColor("#ffffffcc");
                                });
                        }
                    }
                });

                page.Footer().AlignCenter()
                    .Text($"Dlangezwa High School · {vm.Term} {vm.AcademicYear} · Confidential")
                    .FontSize(7).FontColor(Grey);
            });
        });

        return doc.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEACHER TIMETABLE PDF
    // ─────────────────────────────────────────────────────────────────────────

    public byte[] GenerateTeacherTimetablePdf(TeacherDayVm vm)
    {
        var days = new[] { (1, "Monday"), (2, "Tuesday"), (3, "Wednesday"), (4, "Thursday"), (5, "Friday") };
        var periods = SchoolDay.Periods;
        var slotMap = vm.Slots.ToDictionary(s => $"{s.Day}_{s.PeriodNumber}");

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("DLANGEZWA HIGH SCHOOL")
                                .FontSize(14).Bold().FontColor(Navy);
                            c.Item().Text($"Teacher Timetable — {vm.Teacher.FullName} · {vm.Term} {vm.AcademicYear}")
                                .FontSize(10).FontColor(Grey);
                        });
                        row.ConstantItem(120).AlignRight().Text($"Generated: {DateTime.Now:dd MMM yyyy}")
                            .FontSize(8).FontColor(Grey);
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Navy);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(80);
                        for (int i = 0; i < 5; i++) cols.RelativeColumn();
                    });

                    table.Header(h =>
                    {
                        h.Cell().Background(Navy).Padding(4)
                            .Text("Time").FontColor("#ffffff").Bold().FontSize(8);
                        foreach (var (_, dName) in days)
                            h.Cell().Background(Navy).Padding(4)
                                .AlignCenter().Text(dName).FontColor("#ffffff").Bold().FontSize(8);
                    });

                    // Assembly
                    table.Cell().Background("#e8f0fe").Padding(4)
                        .Text("08:30–09:00").FontSize(7).FontColor("#1a56db").Bold();
                    foreach (var (d, _) in days)
                    {
                        table.Cell().Background(d == 1 || d == 5 ? "#e8f0fe" : "#fafafa")
                            .Padding(4).AlignCenter()
                            .Text(d == 1 || d == 5 ? "Assembly" : "")
                            .FontSize(8).FontColor("#1a56db").Bold();
                    }

                    foreach (var period in periods)
                    {
                        var breakBefore = SchoolDay.Breaks.FirstOrDefault(b => b.AfterPeriod == period.Number - 1);
                        if (breakBefore != null)
                        {
                            table.Cell().Background("#fef9e7").Padding(3)
                                .Text($"{breakBefore.Start}–{breakBefore.End}")
                                .FontSize(7).FontColor("#b7791f").Bold();
                            table.Cell().ColumnSpan(5).Background("#fef9e7").Padding(3).AlignCenter()
                                .Text($"{breakBefore.Label} — 15 minutes")
                                .FontSize(8).FontColor("#b7791f").Bold();
                        }

                        table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(4).Column(c =>
                        {
                            c.Item().Text(period.Label).Bold().FontSize(8);
                            c.Item().Text($"{period.Start}–{period.End}").FontSize(7).FontColor(Grey);
                        });

                        foreach (var (d, _) in days)
                        {
                            var available = SchoolDay.IsPeriodAvailable(period.Number, d);
                            if (!available)
                            {
                                table.Cell().Background("#f1f5f9").BorderBottom(1).BorderColor("#e2e8f0")
                                    .Padding(4).AlignCenter().Text("—").FontColor("#94a3b8").FontSize(8);
                                continue;
                            }
                            var key = $"{d}_{period.Number}";
                            if (!slotMap.TryGetValue(key, out var slot))
                            {
                                table.Cell().Background("#f8fafc").BorderBottom(1).BorderColor("#e2e8f0").Padding(4);
                                continue;
                            }
                            var color = vm.SubjectColors.TryGetValue(slot.SubjectId, out var c2) ? c2 : "#5ea8d3";
                            table.Cell().Background(color).BorderBottom(1).BorderColor("#e2e8f0")
                                .Padding(4).Column(c =>
                                {
                                    c.Item().Text(slot.Subject?.Name ?? "").Bold().FontSize(8).FontColor("#ffffff");
                                    c.Item().Text(slot.Class?.DisplayName ?? "").FontSize(7).FontColor("#ffffffcc");
                                });
                        }
                    }
                });

                page.Footer().AlignCenter()
                    .Text($"Dlangezwa High School · {vm.Teacher.FullName} · {vm.Term} {vm.AcademicYear}")
                    .FontSize(7).FontColor(Grey);
            });
        });

        return doc.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FUEL REPORT PDF  (Transport Module)
    // ─────────────────────────────────────────────────────────────────────────

    public byte[] GenerateFuelReportPdf(FuelReportDto vm)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));
                page.Header().Element(ComposeHeader);

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(10).AlignCenter().Text("FUEL USAGE REPORT")
                        .Bold().FontSize(16).FontColor(Navy);
                    col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(LightBlue);

                    col.Item().PaddingTop(6).Text($"Bus: {vm.BusLabel ?? "All Buses"}    Period: {vm.From:dd MMM yyyy} – {vm.To:dd MMM yyyy}")
                        .FontSize(10).FontColor(Grey);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text($"Total Litres: {vm.TotalLitres:N1} L").SemiBold();
                        row.RelativeItem().Text($"Total Cost: R {vm.TotalCost:N2}").SemiBold();
                        row.RelativeItem().Text($"Avg Efficiency: {(vm.AverageEfficiency.HasValue ? $"{vm.AverageEfficiency:N2} km/L" : "—")}").SemiBold();
                    });

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                            c.RelativeColumn(3);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Background(Navy).Padding(4).Text("Date").FontColor("#ffffff").Bold().FontSize(9);
                            h.Cell().Background(Navy).Padding(4).Text("Bus").FontColor("#ffffff").Bold().FontSize(9);
                            h.Cell().Background(Navy).Padding(4).Text("Litres").FontColor("#ffffff").Bold().FontSize(9);
                            h.Cell().Background(Navy).Padding(4).Text("Cost").FontColor("#ffffff").Bold().FontSize(9);
                            h.Cell().Background(Navy).Padding(4).Text("Odometer").FontColor("#ffffff").Bold().FontSize(9);
                            h.Cell().Background(Navy).Padding(4).Text("Station").FontColor("#ffffff").Bold().FontSize(9);
                        });
                        foreach (var r in vm.Records)
                        {
                            table.Cell().Padding(3).Text(r.Date.ToString("dd MMM yyyy")).FontSize(9);
                            table.Cell().Padding(3).Text(r.Bus?.RegistrationNumber ?? "").FontSize(9);
                            table.Cell().Padding(3).Text($"{r.Litres:N1} L").FontSize(9);
                            table.Cell().Padding(3).Text($"R {r.TotalCost:N2}").FontSize(9);
                            table.Cell().Padding(3).Text(r.OdometerReading.ToString("N0")).FontSize(9);
                            table.Cell().Padding(3).Text(r.StationName ?? "-").FontSize(9);
                        }
                    });

                    col.Item().PaddingTop(20).AlignRight().Text($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                        .FontSize(9).FontColor(Grey);
                });

                page.Footer().AlignCenter().Text("Dlangezwa High School — Transport Module")
                    .FontSize(9).FontColor(Grey);
            });
        });
        return doc.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ACTIVITY REPORT PDF  (Extracurricular Module)
    // ─────────────────────────────────────────────────────────────────────────

    public byte[] GenerateActivityReportPdf(ActivityReportDto vm)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));
                page.Header().Element(ComposeHeader);

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(10).AlignCenter().Text("EXTRACURRICULAR ACTIVITY REPORT")
                        .Bold().FontSize(16).FontColor(Navy);
                    col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(LightBlue);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text($"Activities: {vm.TotalActivities}").SemiBold();
                        row.RelativeItem().Text($"Participants: {vm.TotalParticipants}").SemiBold();
                        row.RelativeItem().Text($"Avg Attendance: {vm.AverageAttendancePct:N1}%").SemiBold();
                        row.RelativeItem().Text($"Achievements: {vm.TotalAchievements}").SemiBold();
                    });

                    col.Item().PaddingTop(14).Text("Attendance by Activity").Bold().FontColor(Navy);
                    col.Item().LineHorizontal(0.5f).LineColor(LightBlue);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Activity").Bold().FontSize(9);
                            h.Cell().Text("Sessions").Bold().FontSize(9);
                            h.Cell().Text("Attendance %").Bold().FontSize(9);
                        });
                        foreach (var (name, sessions, pct) in vm.AttendanceByActivity)
                        {
                            table.Cell().Padding(2).Text(name).FontSize(9);
                            table.Cell().Padding(2).Text(sessions.ToString()).FontSize(9);
                            table.Cell().Padding(2).Text($"{pct:N1}%").FontSize(9);
                        }
                    });

                    col.Item().PaddingTop(14).Text("Top Achievers").Bold().FontColor(Navy);
                    col.Item().LineHorizontal(0.5f).LineColor(LightBlue);
                    col.Item().PaddingTop(4).Column(c =>
                    {
                        foreach (var (name, count) in vm.TopAchievers)
                            c.Item().Text($"{name} — {count} achievement(s)").FontSize(9);
                    });

                    col.Item().PaddingTop(14).Text("Coach Summary").Bold().FontColor(Navy);
                    col.Item().LineHorizontal(0.5f).LineColor(LightBlue);
                    col.Item().PaddingTop(4).Column(c =>
                    {
                        foreach (var (coach, activities) in vm.CoachSummary)
                            c.Item().Text($"{coach}: {string.Join(", ", activities)}").FontSize(9);
                    });

                    col.Item().PaddingTop(20).AlignRight().Text($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                        .FontSize(9).FontColor(Grey);
                });

                page.Footer().AlignCenter().Text("Dlangezwa High School — Extracurricular Module")
                    .FontSize(9).FontColor(Grey);
            });
        });
        return doc.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MEAL COMPLIANCE REPORT PDF  (Boarding & Meals Module, UC10)
    // ─────────────────────────────────────────────────────────────────────────

    public byte[] GenerateMealComplianceReportPdf(MealComplianceReportDto vm)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(35);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
                page.Header().Element(ComposeHeader);

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(10).AlignCenter().Text("MEAL COMPLIANCE REPORT")
                        .Bold().FontSize(16).FontColor(Navy);
                    col.Item().AlignCenter().Text($"{vm.From:dd MMM yyyy} – {vm.To:dd MMM yyyy}")
                        .FontSize(10).FontColor(Grey);
                    col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(LightBlue);

                    col.Item().PaddingTop(10).Text("1. Attendance Summary").Bold().FontColor(Navy);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Meal Type").Bold();
                            h.Cell().Text("Expected").Bold();
                            h.Cell().Text("Present").Bold();
                            h.Cell().Text("Attendance %").Bold();
                        });
                        foreach (var r in vm.AttendanceByMealType)
                        {
                            table.Cell().Text(r.MealType);
                            table.Cell().Text(r.TotalExpected.ToString());
                            table.Cell().Text(r.TotalPresent.ToString());
                            table.Cell().Text($"{r.AttendancePct:N1}%");
                        }
                    });

                    col.Item().PaddingTop(12).Text("2. Pre-Order vs Actual").Bold().FontColor(Navy);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Date").Bold();
                            h.Cell().Text("Meal").Bold();
                            h.Cell().Text("Menu").Bold();
                            h.Cell().Text("Pre-Ordered").Bold();
                            h.Cell().Text("Attended").Bold();
                            h.Cell().Text("Variance").Bold();
                        });
                        foreach (var r in vm.PreOrderVsActual)
                        {
                            table.Cell().Text(r.Date.ToString("dd MMM"));
                            table.Cell().Text(r.MealType);
                            table.Cell().Text(r.MenuDescription);
                            table.Cell().Text(r.PreOrdered.ToString());
                            table.Cell().Text(r.Attended.ToString());
                            table.Cell().Text(r.Variance.ToString());
                        }
                    });

                    col.Item().PaddingTop(12).Text("3. Dietary Compliance").Bold().FontColor(Navy);
                    if (!vm.DietaryConflictsServed.Any())
                        col.Item().Text("No allergy conflicts detected in meals served.").FontColor(Grey);
                    else
                        foreach (var c in vm.DietaryConflictsServed)
                            col.Item().Text($"• {c}").FontColor("#b91c1c");

                    col.Item().PaddingTop(12).Text("4. Food Waste Estimate").Bold().FontColor(Navy);
                    col.Item().Text($"Portions prepared: {vm.PortionsPrepared}   Portions served: {vm.PortionsServed}   Estimated waste: {vm.WasteEstimate}");

                    col.Item().PaddingTop(12).Text("5. Budget vs Actual").Bold().FontColor(Navy);
                    col.Item().Text($"Estimated cost: R {vm.EstimatedCost:N2}   Actual spend (submitted requisitions): R {vm.ActualSpend:N2}");

                    col.Item().PaddingTop(12).Text("6. Satisfaction Scores").Bold().FontColor(Navy);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Meal Type").Bold();
                            h.Cell().Text("Avg Rating").Bold();
                            h.Cell().Text("Complaints").Bold();
                        });
                        foreach (var r in vm.SatisfactionByMealType)
                        {
                            table.Cell().Text(r.MealType);
                            table.Cell().Text(r.AverageRating > 0 ? $"{r.AverageRating:N1}/5" : "-");
                            table.Cell().Text(r.Complaints.ToString());
                        }
                    });

                    col.Item().PaddingTop(12).Text("7. Anomaly Flags").Bold().FontColor(Navy);
                    if (!vm.AnomalyFlags.Any())
                        col.Item().Text("No anomalies detected.").FontColor(Grey);
                    else
                        foreach (var a in vm.AnomalyFlags)
                            col.Item().Text($"• {a}").FontColor("#b45309");

                    col.Item().PaddingTop(20).AlignRight().Text($"Generated by {vm.GeneratedByName} on {vm.GeneratedAt:dd MMM yyyy HH:mm}")
                        .FontSize(9).FontColor(Grey);
                });

                page.Footer().AlignCenter().Text("Dlangezwa High School — Boarding & Meals Module")
                    .FontSize(9).FontColor(Grey);
            });
        });
        return doc.GeneratePdf();
    }
}
