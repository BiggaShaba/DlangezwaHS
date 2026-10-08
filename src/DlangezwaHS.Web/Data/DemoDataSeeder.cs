using System.Text;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Data;

// ─────────────────────────────────────────────────────────────────────────────
// DEMO DATA — a few weeks of realistic kitchen activity so reports and dashboards
// have something to show. Enabled with "DemoData:Enabled": true. Runs once: the
// demo supplier receipts are tagged, and their presence means it already ran.
//
// Everything is generated the same way the app would record it:
//   supplier receipt (stock in) → pre-orders → chef records usage (stock out by recipe)
//   → learners collect (scan) → leftovers recorded → anonymous feedback → alerts
// ─────────────────────────────────────────────────────────────────────────────

public static class DemoDataSeeder
{
    public const string DemoTag = "[demo data]";
    private const int DemoWeeks = 4;
    private const int MinLearners = 30;
    private const int MaxLearners = 60;

    // Prep minutes per serving for the starter meal library
    private static readonly Dictionary<string, decimal> PrepMinutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Maize meal porridge"] = 0.5m, ["Oats porridge"] = 0.5m, ["Scrambled eggs & toast"] = 1.2m,
        ["Toast with jam"] = 0.4m, ["Pap & beef stew"] = 1.5m, ["Rice & chicken curry"] = 1.5m,
        ["Samp & beans"] = 1.0m, ["Vegetable soup & bread"] = 1.0m, ["Spaghetti bolognese"] = 1.2m,
        ["Hake, potatoes & spinach"] = 2.0m, ["Chicken, rice & butternut"] = 1.6m, ["Pap, chakalaka & beans"] = 1.0m
    };

    // The meal that "goes wrong" so a quality alert shows up
    private const string PoorlyRatedMeal = "Hake, potatoes & spinach";

    private static readonly string[] Suppliers =
        { "Zululand Wholesale Foods", "Empangeni Fresh Produce", "uMhlathuze Meat & Poultry", "Richards Bay Cash & Carry" };

    private static readonly string[] GoodComments =
    {
        "Really tasty, thank you!", "Best meal this week.", "Nice and hot when we got it.",
        "Loved the stew.", "Good portion, I was full.", "Please make this again."
    };
    private static readonly string[] OkComments =
    {
        "It was okay.", "Could use a bit more salt.", "A bit cold by the time I got it.", "Fine but same as last week."
    };
    private static readonly string[] BadComments =
    {
        "The fish was dry and had bones.", "Too little food, still hungry.", "Tasted burnt.", "Not cooked properly."
    };

    private static readonly (string First, string Last, string Gender)[] DemoNames =
    {
        ("Sipho","Mthembu","Male"), ("Nomvula","Zulu","Female"), ("Thabo","Ndlovu","Male"), ("Ayanda","Khumalo","Female"),
        ("Lungelo","Dlamini","Male"), ("Zanele","Mkhize","Female"), ("Sibusiso","Ngcobo","Male"), ("Nokuthula","Shabalala","Female"),
        ("Mandla","Buthelezi","Male"), ("Thandeka","Cele","Female"), ("Bongani","Mhlongo","Male"), ("Lindiwe","Gumede","Female"),
        ("Siyabonga","Nxumalo","Male"), ("Nompumelelo","Majola","Female"), ("Kwanele","Hlongwane","Male"), ("Snenhlanhla","Zungu","Female"),
        ("Andile","Mbatha","Male"), ("Philisiwe","Ntuli","Female"), ("Njabulo","Sithole","Male"), ("Busisiwe","Mazibuko","Female"),
        ("Mfanafuthi","Xulu","Male"), ("Hlengiwe","Msomi","Female"), ("Sanele","Biyela","Male"), ("Nosipho","Chiliza","Female"),
        ("Lwazi","Mdlalose","Male"), ("Ntombi","Masondo","Female"), ("Khaya","Zwane","Male"), ("Asanda","Khanyile","Female"),
        ("Musa","Ngubane","Male"), ("Londiwe","Shange","Female")
    };

    public static async Task SeedAsync(ApplicationDbContext db, string contentRootPath, ILogger logger)
    {
        if (await db.StockReceipts.AnyAsync(r => r.Notes == DemoTag)) return;

        var meals = await db.Meals.Include(m => m.Ingredients).ThenInclude(i => i.Ingredient)
            .Where(m => m.IsActive).ToListAsync();
        if (meals.Count == 0)
        {
            logger.LogWarning("Demo data skipped: the meal library is empty.");
            return;
        }

        var rng = new Random(2026);
        var settings = await db.BoardingSettings.FirstOrDefaultAsync() ?? new BoardingSettings();
        var kitchenUserIds = await db.KitchenStaffMembers.Where(k => k.IsActive && k.UserId != null).Select(k => k.UserId!).ToListAsync();
        var adminId = (await db.Users.FirstOrDefaultAsync(u => u.Email == SeedData.AdminEmail))?.Id ?? "";
        var chefId = kitchenUserIds.FirstOrDefault() ?? adminId;

        // ── 1. Prep times on the starter meals ─────────────────────────────────
        foreach (var m in meals.Where(m => m.PrepMinutesPerServing == 1m && PrepMinutes.ContainsKey(m.Name)))
            m.PrepMinutesPerServing = PrepMinutes[m.Name];

        // ── 2. Learners who eat in the dining hall ─────────────────────────────
        var learners = await EnsureLearnersAsync(db, rng, logger);
        if (learners.Count == 0)
        {
            logger.LogWarning("Demo data skipped: no classes to enrol demo learners into.");
            return;
        }

        // ── 3. Published meal plans for the past weeks (unless that week already has a plan) ──
        var today = DateTime.Today;
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var existingPlans = await db.MealPlans.ToListAsync();

        var created = 0;
        for (var w = 1; w <= DemoWeeks; w++)
        {
            var monday = thisMonday.AddDays(-7 * w);
            if (existingPlans.Any(p => p.WeekStartDate.Date == monday)) continue;
            var plan = BuildPlan(monday, meals, settings, chefId, rng);
            db.MealPlans.Add(plan);
            existingPlans.Add(plan);
            created++;
        }
        if (!existingPlans.Any(p => p.Status == MealPlanStatus.Published && p.WeekStartDate.Date <= today && p.WeekStartDate.Date.AddDays(6) >= today))
        {
            var plan = BuildPlan(thisMonday, meals, settings, chefId, rng);
            db.MealPlans.Add(plan);
            existingPlans.Add(plan);
        }
        await db.SaveChangesAsync();

        // ── 4. Every published meal from the demo window ───────────────────────
        var windowStart = thisMonday.AddDays(-7 * DemoWeeks);
        var windowEnd = today.AddDays(7);
        var items = await db.MealPlanItems
            .Include(i => i.MealPlan)
            .Include(i => i.Meal).ThenInclude(m => m!.Ingredients).ThenInclude(mi => mi.Ingredient)
            .Where(i => i.MealPlan.Status == MealPlanStatus.Published
                     && i.MealPlan.WeekStartDate >= windowStart.AddDays(-6) && i.MealPlan.WeekStartDate <= windowEnd)
            .ToListAsync();
        items = items.Where(i =>
        {
            var d = Day(i);
            return d >= windowStart && d <= windowEnd;
        }).ToList();

        var itemIds = items.Select(i => i.Id).ToList();
        var existingOrders = await db.MealPreOrders.Where(o => itemIds.Contains(o.MealPlanItemId)).ToListAsync();
        var collectedItemIds = (await db.MealAttendances.Where(a => itemIds.Contains(a.MealPlanItemId))
            .Select(a => a.MealPlanItemId).Distinct().ToListAsync()).ToHashSet();
        var usedItemIds = (await db.IngredientUsageRecords.Where(r => itemIds.Contains(r.MealPlanItemId))
            .Select(r => r.MealPlanItemId).Distinct().ToListAsync()).ToHashSet();

        var newOrders = new List<MealPreOrder>();
        var attendances = new List<MealAttendance>();
        var usageRecords = new List<IngredientUsageRecord>();
        var feedback = new List<MealFeedback>();
        var now = DateTime.Now;

        // A meal service = all options for the same plan, day and meal type
        var services = items.GroupBy(i => (i.MealPlanId, i.DayOfWeek, i.MealType))
            .OrderBy(g => Serving(g.First()));

        foreach (var service in services)
        {
            var options = service.OrderBy(i => i.Id).ToList();
            var servingAt = Serving(options[0]);
            var isPast = servingAt < now.AddHours(-1);

            // ── Pre-orders: one option per learner per service ─────────────────
            var orderRate = service.Key.MealType == MealType.Breakfast ? 0.75 : 0.88;
            var alreadyOrdered = existingOrders.Where(o => options.Any(i => i.Id == o.MealPlanItemId)).Select(o => o.LearnerId).ToHashSet();
            var serviceOrders = new List<MealPreOrder>();
            foreach (var l in learners)
            {
                if (alreadyOrdered.Contains(l.Id) || rng.NextDouble() > orderRate) continue;
                var option = options.Count == 1 || rng.NextDouble() < 0.6 ? options[0] : options[rng.Next(1, options.Count)];
                var r = rng.NextDouble();
                var order = new MealPreOrder
                {
                    MealPlanItemId = option.Id,
                    LearnerId = l.Id,
                    OrderedByParentUserId = l.ParentId ?? adminId,
                    Status = PreOrderStatus.Confirmed,
                    OrderedAt = servingAt.AddDays(-3).AddHours(-rng.Next(0, 48)),
                    PortionSize = r < 0.1 ? PortionSize.Small : r < 0.8 ? PortionSize.Regular : PortionSize.Large
                };
                serviceOrders.Add(order);
            }
            newOrders.AddRange(serviceOrders);
            if (!isPast) continue;

            // ── Collection (scan) ─────────────────────────────────────────────
            if (options.All(i => !collectedItemIds.Contains(i.Id)))
            {
                foreach (var o in serviceOrders)
                {
                    var r = rng.NextDouble();
                    var status = r < 0.88 ? MealAttendanceStatus.Present : r < 0.92 ? MealAttendanceStatus.Late : MealAttendanceStatus.Absent;
                    attendances.Add(new MealAttendance
                    {
                        MealPlanItemId = o.MealPlanItemId,
                        LearnerId = o.LearnerId,
                        Status = status,
                        ScannedAt = status == MealAttendanceStatus.Absent ? null
                            : servingAt.AddMinutes(status == MealAttendanceStatus.Late ? rng.Next(35, 55) : rng.Next(0, 25)),
                        ScannedByUserId = status == MealAttendanceStatus.Absent ? null : Pick(kitchenUserIds, rng) ?? chefId
                    });
                }
                // The odd walk-in without a pre-order
                var orderedIds = serviceOrders.Select(o => o.LearnerId).ToHashSet();
                foreach (var walkIn in learners.Where(l => !orderedIds.Contains(l.Id)).OrderBy(_ => rng.Next()).Take(rng.Next(0, 3)))
                {
                    attendances.Add(new MealAttendance
                    {
                        MealPlanItemId = options[0].Id, LearnerId = walkIn.Id, Status = MealAttendanceStatus.Unauthorised,
                        ScannedAt = servingAt.AddMinutes(rng.Next(5, 30)), ScannedByUserId = chefId
                    });
                }
            }

            // ── Chef records usage, then leftovers after service ──────────────
            foreach (var option in options.Where(i => i.Meal is not null && !usedItemIds.Contains(i.Id)))
            {
                var ordered = serviceOrders.Count(o => o.MealPlanItemId == option.Id);
                if (ordered == 0) continue;
                var prepared = (int)Math.Ceiling(ordered * 1.05);
                var collected = attendances.Count(a => a.MealPlanItemId == option.Id && a.Status != MealAttendanceStatus.Absent);
                usageRecords.Add(new IngredientUsageRecord
                {
                    MealPlanItem = option,
                    MealPlanItemId = option.Id,
                    ServingsPrepared = prepared,
                    LeftoverServings = Math.Max(0, prepared - collected),
                    RecordedAt = servingAt.AddHours(-1),
                    RecordedByUserId = chefId
                });
            }

            // ── Anonymous feedback ────────────────────────────────────────────
            foreach (var option in options)
            {
                var poor = option.MenuDescription.Equals(PoorlyRatedMeal, StringComparison.OrdinalIgnoreCase);
                var mean = poor ? 2.0 : service.Key.MealType == MealType.Breakfast ? 3.6 : 4.0;
                var count = rng.Next(poor ? 6 : 2, poor ? 10 : 8);
                for (var n = 0; n < count; n++)
                {
                    var rating = Math.Clamp((int)Math.Round(mean + Normal(rng) * 0.9), 1, 5);
                    var p = rng.NextDouble();
                    var portion = rating <= 2
                        ? (p < 0.6 ? PortionFeedback.TooLittle : PortionFeedback.JustRight)
                        : (p < 0.12 ? PortionFeedback.TooLittle : p < 0.88 ? PortionFeedback.JustRight : PortionFeedback.TooMuch);
                    var pool = rating >= 4 ? GoodComments : rating == 3 ? OkComments : BadComments;
                    feedback.Add(new MealFeedback
                    {
                        MealPlanItemId = option.Id,
                        Rating = rating,
                        Portion = rng.NextDouble() < 0.8 ? portion : null,
                        Comment = rng.NextDouble() < 0.3 ? pool[rng.Next(pool.Length)] : null,
                        SubmittedAt = servingAt.AddHours(rng.Next(1, 20))
                    });
                }
            }
        }

        // ── 5. Two learners miss three meals in a row → absence alerts ─────────
        var housemasterId = (await db.Housemasters.FirstOrDefaultAsync(h => h.IsActive))?.Id;
        var absenceAlerts = new List<MealAbsenceAlert>();
        var orderByKey = newOrders.ToDictionary(o => (o.MealPlanItemId, o.LearnerId));
        var itemById = items.ToDictionary(i => i.Id);
        foreach (var (learner, idx) in learners.Take(2).Select((l, i) => (l, i)))
        {
            var lastThree = attendances
                .Where(a => a.LearnerId == learner.Id && orderByKey.ContainsKey((a.MealPlanItemId, a.LearnerId)))
                .OrderByDescending(a => Serving(itemById[a.MealPlanItemId]))
                .Skip(idx * 6)   // the second learner's streak is a week older
                .Take(3).ToList();
            if (lastThree.Count < 3) continue;
            foreach (var a in lastThree) { a.Status = MealAttendanceStatus.Absent; a.ScannedAt = null; a.ScannedByUserId = null; }
            var alertAt = Serving(itemById[lastThree[0].MealPlanItemId]).AddHours(1);
            absenceAlerts.Add(new MealAbsenceAlert
            {
                LearnerId = learner.Id, ConsecutiveMissedMeals = 3, AlertSentAt = alertAt,
                AlertedHousemasterId = housemasterId, ResolvedAt = idx == 1 ? alertAt.AddDays(1) : null
            });
        }
        // Leftovers must reflect the final collection numbers
        foreach (var u in usageRecords)
        {
            var collected = attendances.Count(a => a.MealPlanItemId == u.MealPlanItemId && a.Status != MealAttendanceStatus.Absent);
            u.LeftoverServings = Math.Max(0, u.ServingsPrepared - collected);
        }

        // ── 6. Supplier receipts before each week, then deduct usage by recipe ─
        var ingredients = await db.Ingredients.ToDictionaryAsync(i => i.Id);
        var receiptsDir = Path.Combine(contentRootPath, "App_Data", "receipts");
        Directory.CreateDirectory(receiptsDir);
        var weekNo = 0;
        foreach (var week in usageRecords.GroupBy(u => Monday(Day(u.MealPlanItem))).OrderBy(g => g.Key))
        {
            var need = new Dictionary<int, decimal>();
            foreach (var u in week)
                foreach (var mi in u.MealPlanItem.Meal!.Ingredients)
                    need[mi.IngredientId] = need.GetValueOrDefault(mi.IngredientId) + mi.QuantityPerServing * u.ServingsPrepared;

            var supplier = Suppliers[weekNo++ % Suppliers.Length];
            var receiptDate = week.Key.AddDays(-1);
            var receipt = new StockReceipt
            {
                SupplierName = supplier,
                InvoiceNumber = $"INV-{receiptDate:yyMMdd}-{rng.Next(100, 999)}",
                ReceiptDate = receiptDate,
                Notes = DemoTag,
                CapturedByUserId = chefId,
                CapturedAt = receiptDate.AddHours(9),
                DocumentContentType = "application/pdf"
            };
            foreach (var (ingredientId, qty) in need.Where(n => n.Value > 0))
            {
                var ing = ingredients[ingredientId];
                // Order ~15% more than needed, in sensible pack sizes
                var ordered = ing.Unit == StockUnit.Units ? Math.Ceiling(qty * 1.15m) : Math.Ceiling(qty * 1.15m * 2) / 2;
                var unitCost = Math.Round(ing.UnitCost * (decimal)(0.95 + rng.NextDouble() * 0.1), 2);
                receipt.Lines.Add(new StockReceiptLine { IngredientId = ingredientId, Quantity = ordered, UnitCost = unitCost });
                ing.CurrentStock += ordered;
                ing.UnitCost = unitCost;
            }
            receipt.TotalAmount = receipt.Lines.Sum(l => l.Quantity * l.UnitCost);

            var fileName = $"demo-receipt-{receiptDate:yyyyMMdd}.pdf";
            await File.WriteAllBytesAsync(Path.Combine(receiptsDir, fileName), ReceiptPdf(receipt, ingredients));
            receipt.DocumentPath = fileName;
            db.StockReceipts.Add(receipt);

            foreach (var u in week.OrderBy(u => u.RecordedAt))
            {
                foreach (var mi in u.MealPlanItem.Meal!.Ingredients)
                {
                    var used = mi.QuantityPerServing * u.ServingsPrepared;
                    var ing = ingredients[mi.IngredientId];
                    ing.CurrentStock = Math.Max(0, ing.CurrentStock - used);
                    ing.LastUpdatedAt = u.RecordedAt;
                    ing.LastUpdatedByUserId = chefId;
                    u.Lines.Add(new StockUsageLog
                    {
                        IngredientId = mi.IngredientId, QuantityUsed = used, UsedForMealPlanItemId = u.MealPlanItemId,
                        RecordedAt = u.RecordedAt, RecordedByUserId = chefId
                    });
                }
            }
        }

        db.MealPreOrders.AddRange(newOrders);
        db.MealAttendances.AddRange(attendances);
        db.IngredientUsageRecords.AddRange(usageRecords);
        db.MealFeedbacks.AddRange(feedback);
        db.MealAbsenceAlerts.AddRange(absenceAlerts);
        await db.SaveChangesAsync();

        // ── 7. Quality alerts for poorly rated meals (older ones already resolved) ──
        var lowRated = feedback.GroupBy(f => f.MealPlanItemId)
            .Where(g => g.Count() >= 5 && g.Average(f => f.Rating) < 2.5)
            .Select(g => (Item: itemById[g.Key], Avg: (decimal)g.Average(f => f.Rating), Count: g.Count()))
            .OrderBy(x => Serving(x.Item)).ToList();
        foreach (var (low, i) in lowRated.Select((x, i) => (x, i)))
        {
            var raisedAt = Serving(low.Item).AddDays(1);
            var resolved = i < lowRated.Count - 1;
            db.MealQualityAlerts.Add(new MealQualityAlert
            {
                MealPlanItemId = low.Item.Id,
                AverageRating = Math.Round(low.Avg, 2),
                SubmissionCount = low.Count,
                CreatedAt = raisedAt,
                ResolvedAt = resolved ? raisedAt.AddDays(2) : null,
                ResolvedByUserId = resolved ? chefId : null,
                ResolutionNotes = resolved ? "Switched hake supplier and shortened frying time; bigger potato portion." : null
            });
        }

        // ── 8. Kitchen schedules (one team per meal service) ───────────────────
        await SeedSchedulesAsync(db, items, newOrders.Concat(existingOrders).ToList(), settings, rng);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded demo data: {Plans} meal plan(s), {Orders} pre-orders, {Scans} collections, {Usage} usage records, {Feedback} ratings.",
            created, newOrders.Count, attendances.Count, usageRecords.Count, feedback.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTime Day(MealPlanItem i) => i.MealPlan.WeekStartDate.Date.AddDays(i.DayOfWeek - 1);
    private static DateTime Serving(MealPlanItem i) => MealOrderService.ComputeServingDateTime(i.MealPlan, i);
    private static DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    private static string? Pick(IList<string> list, Random rng) => list.Count == 0 ? null : list[rng.Next(list.Count)];

    private static double Normal(Random rng)
    {
        // Box–Muller
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static MealPlan BuildPlan(DateTime monday, IList<Meal> meals, BoardingSettings settings, string userId, Random rng)
    {
        var plan = new MealPlan
        {
            WeekStartDate = monday,
            Status = MealPlanStatus.Published,
            TotalBudget = settings.WeeklyMealBudget,
            CreatedByUserId = userId,
            CreatedAt = monday.AddDays(-5),
            PublishedAt = monday.AddDays(-4)
        };
        foreach (var type in new[] { MealType.Breakfast, MealType.Lunch, MealType.Dinner })
        {
            var pool = meals.Where(m => m.MealType == type).OrderBy(_ => rng.Next()).ToList();
            if (pool.Count == 0) continue;
            var optionsPerDay = type == MealType.Breakfast ? 1 : Math.Min(2, pool.Count);
            for (var day = 1; day <= 7; day++)
            {
                for (var o = 0; o < optionsPerDay; o++)
                {
                    var meal = pool[((day - 1) * optionsPerDay + o) % pool.Count];
                    var item = new MealPlanItem { DayOfWeek = day, MealType = type, ServingTime = settings.ShiftFor(type).Serve };
                    MealPlanService.ApplyMealSnapshot(item, meal);
                    plan.Items.Add(item);
                }
            }
        }
        return plan;
    }

    private static async Task<List<Learner>> EnsureLearnersAsync(ApplicationDbContext db, Random rng, ILogger logger)
    {
        var enrolled = await db.Enrollments.Where(e => e.IsActive).Select(e => e.Learner).Distinct()
            .OrderBy(l => l.Id).Take(MaxLearners).ToListAsync();
        if (enrolled.Count >= MinLearners) return enrolled;

        var classes = await db.Classes.ToListAsync();
        if (classes.Count == 0) return enrolled;

        var toAdd = MinLearners - enrolled.Count;
        var n = 0;
        foreach (var (first, last, gender) in DemoNames)
        {
            if (n >= toAdd) break;
            var idNumber = $"DEMO{++n:0000}";
            if (await db.Learners.AnyAsync(l => l.LearnerIdNumber == idNumber)) continue;
            var learner = new Learner
            {
                FirstName = first, LastName = last, Gender = gender, LearnerIdNumber = idNumber,
                DateOfBirth = new DateTime(2009 + rng.Next(0, 5), rng.Next(1, 13), rng.Next(1, 28))
            };
            learner.Enrollments.Add(new Enrollment { ClassId = classes[rng.Next(classes.Count)].Id, IsActive = true });
            db.Learners.Add(learner);
            enrolled.Add(learner);
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Added {Count} demo learners for meal demo data.", n);
        return enrolled;
    }

    private static async Task SeedSchedulesAsync(ApplicationDbContext db, IList<MealPlanItem> items,
        IList<MealPreOrder> orders, BoardingSettings settings, Random rng)
    {
        var teams = await db.KitchenTeams
            .Include(t => t.Members).ThenInclude(m => m.KitchenStaffMember)
            .Where(t => t.IsActive).OrderBy(t => t.Id).ToListAsync();
        teams = teams.Where(t => t.Members.Any(m => m.KitchenStaffMember.UserId != null)).ToList();
        if (teams.Count == 0) return;

        var scheduledIds = (await db.KitchenSchedules.Select(s => s.MealPlanItemId).ToListAsync()).ToHashSet();
        var today = DateTime.Today;
        var services = items
            .Where(i => Day(i) >= today.AddDays(-14) && Day(i) <= today.AddDays(7))
            .GroupBy(i => (i.MealPlanId, i.DayOfWeek, i.MealType));

        foreach (var service in services)
        {
            var options = service.OrderBy(i => i.Id).ToList();
            if (options.Any(i => scheduledIds.Contains(i.Id))) continue;

            var (start, serve) = settings.ShiftFor(service.Key.MealType);
            var portions = orders.Count(o => options.Any(i => i.Id == o.MealPlanItemId));
            var prep = options.Max(i => i.Meal?.PrepMinutesPerServing ?? 1m);
            var recommended = KitchenScheduleService.RecommendStaff(portions, prep, KitchenScheduleService.ShiftMinutes(start, serve));

            // Breakfast, lunch and dinner shifts don't overlap, so teams rotate by day
            var team = teams[(service.Key.DayOfWeek + (int)service.Key.MealType) % teams.Count];
            var members = team.Members.Where(m => m.KitchenStaffMember.UserId != null).Take(Math.Max(1, recommended)).ToList();
            var isPast = Serving(options[0]) < DateTime.Now;

            var schedule = new KitchenSchedule
            {
                MealPlanItemId = options[0].Id,
                ScheduledDate = Day(options[0]),
                MealType = service.Key.MealType,
                KitchenTeamId = team.Id,
                Status = isPast ? KitchenScheduleStatus.Completed : KitchenScheduleStatus.Scheduled,
                PortionsPlanned = portions,
                StaffRecommended = recommended,
                PublishedAt = Day(options[0]).AddDays(-2)
            };
            foreach (var m in members)
            {
                schedule.Tasks.Add(new KitchenTask
                {
                    AssignedToUserId = m.KitchenStaffMember.UserId!,
                    TaskDescription = $"Prepare {string.Join(" / ", options.Select(i => i.MenuDescription))}",
                    StartTime = start,
                    EndTime = serve,
                    PortionsRequired = portions,
                    Status = isPast ? KitchenTaskStatus.Done : KitchenTaskStatus.Assigned,
                    CompletedAt = isPast ? Serving(options[0]).AddMinutes(-rng.Next(0, 15)) : null
                });
            }
            db.KitchenSchedules.Add(schedule);
        }
    }

    // A simple one-page PDF so "view receipt document" works for demo receipts
    private static byte[] ReceiptPdf(StockReceipt r, IDictionary<int, Ingredient> ingredients)
    {
        var lines = new List<string>
        {
            r.SupplierName, $"Tax invoice {r.InvoiceNumber}", $"Date: {r.ReceiptDate:dd MMM yyyy}",
            "Deliver to: Dlangezwa High School Kitchen", ""
        };
        lines.AddRange(r.Lines.Select(l =>
        {
            var ing = ingredients[l.IngredientId];
            return $"{ing.Name,-22} {l.Quantity,8:0.##} {ing.Unit,-6} @ R{l.UnitCost,7:0.00}  = R{l.Quantity * l.UnitCost,9:0.00}";
        }));
        lines.Add("");
        lines.Add($"TOTAL: R{r.Lines.Sum(l => l.Quantity * l.UnitCost):0.00}");
        lines.Add("(Demo document generated for testing)");

        static string Esc(string s) => new string(s.Select(c => c < 128 ? c : '-').ToArray())
            .Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = new StringBuilder("BT /F1 10 Tf 50 800 Td 14 TL\n");
        foreach (var l in lines) content.Append('(').Append(Esc(l)).Append(") Tj T*\n");
        content.Append("ET");

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) pdf.Append($"{o:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
