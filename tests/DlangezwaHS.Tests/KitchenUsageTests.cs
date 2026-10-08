using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DlangezwaHS.Tests;

// Usage is recorded automatically when a meal's collection closes:
// prepared = confirmed pre-orders, leftovers = prepared − collected, stock deducted by recipe once.
public class KitchenUsageTests
{
    private static (ApplicationDbContext Db, InventoryService Svc, int ItemId) Setup()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var rice = new Ingredient { Id = 1, Name = "Rice", Unit = StockUnit.Kg, CurrentStock = 10m, MinimumStock = 1m, UnitCost = 20m };
        var meal = new Meal { Id = 1, Name = "Rice & chicken", MealType = MealType.Lunch };
        meal.Ingredients.Add(new MealIngredient { IngredientId = 1, QuantityPerServing = 0.5m });
        var plan = new MealPlan { Id = 1, WeekStartDate = new DateTime(2026, 10, 5), Status = MealPlanStatus.Published };
        var item = new MealPlanItem { Id = 1, MealPlanId = 1, DayOfWeek = 1, MealType = MealType.Lunch, MealId = 1, MenuDescription = "Rice & chicken", ServingTime = "13:00" };
        db.AddRange(rice, meal, plan, item);

        // Three learners ordered; two collected; one walk-in without an order was flagged
        for (var l = 1; l <= 3; l++)
            db.MealPreOrders.Add(new MealPreOrder { MealPlanItemId = 1, LearnerId = l, OrderedByParentUserId = "p", Status = PreOrderStatus.Confirmed });
        db.MealAttendances.AddRange(
            new MealAttendance { MealPlanItemId = 1, LearnerId = 1, Status = MealAttendanceStatus.Present },
            new MealAttendance { MealPlanItemId = 1, LearnerId = 2, Status = MealAttendanceStatus.Late },
            new MealAttendance { MealPlanItemId = 1, LearnerId = 3, Status = MealAttendanceStatus.Absent },
            new MealAttendance { MealPlanItemId = 1, LearnerId = 4, Status = MealAttendanceStatus.Unauthorised });
        db.SaveChanges();

        var svc = new InventoryService(db, Mock.Of<IWebHostEnvironment>(), null!);
        return (db, svc, item.Id);
    }

    [Fact]
    public async Task ClosingCollection_RecordsPreparedCollectedAndLeftovers()
    {
        var (db, svc, itemId) = Setup();

        var result = await svc.RecordServiceUsageAsync(new List<int> { itemId }, "chef");

        var record = await db.IngredientUsageRecords.Include(r => r.Lines).SingleAsync();
        Assert.Equal(3, record.ServingsPrepared);          // confirmed pre-orders
        Assert.Equal(1, record.LeftoverServings);          // 3 prepared − 2 collected (walk-in not counted)
        Assert.Equal(3, result.Prepared);
        Assert.Equal(1.5m, record.Lines.Single().QuantityUsed);
        Assert.Equal(8.5m, (await db.Ingredients.SingleAsync()).CurrentStock);
    }

    [Fact]
    public async Task ClosingCollectionTwice_DoesNotDeductStockAgain()
    {
        var (db, svc, itemId) = Setup();

        await svc.RecordServiceUsageAsync(new List<int> { itemId }, "chef");
        await svc.RecordServiceUsageAsync(new List<int> { itemId }, "chef");

        Assert.Single(db.IngredientUsageRecords);
        Assert.Equal(8.5m, (await db.Ingredients.SingleAsync()).CurrentStock);
    }

    [Fact]
    public async Task Collected_NeverExceedsPrepared()
    {
        var (db, svc, itemId) = Setup();
        // Extra walk-ins scanned as present without an order must not push collected above prepared
        db.MealAttendances.Add(new MealAttendance { MealPlanItemId = itemId, LearnerId = 9, Status = MealAttendanceStatus.Present });
        await db.SaveChangesAsync();

        await svc.RecordServiceUsageAsync(new List<int> { itemId }, "chef");

        var record = await db.IngredientUsageRecords.SingleAsync();
        Assert.True(record.LeftoverServings >= 0);
        Assert.Equal(1, record.LeftoverServings);
    }
}
