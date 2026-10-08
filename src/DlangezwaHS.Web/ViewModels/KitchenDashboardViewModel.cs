namespace DlangezwaHS.Web.ViewModels;

public class KitchenDashboardViewModel
{
    public string FirstName { get; set; } = "";
    public DateTime Today { get; set; }

    // Quick-action sub-labels and summary chips
    public int MealsToday { get; set; }
    public int OrdersToday { get; set; }
    public int TasksPending { get; set; }
    public int LowStockItems { get; set; }
    public int OpenQualityAlerts { get; set; }

    // Charts
    public IList<MealOrderCount> MostOrdered { get; set; } = new List<MealOrderCount>();
    public IList<MealRatingAverage> TopRated { get; set; } = new List<MealRatingAverage>();
    public double OverallRating { get; set; }
    public int TotalRatings { get; set; }
}

public class MealOrderCount
{
    public string Meal { get; set; } = "";
    public int Orders { get; set; }
}

public class MealRatingAverage
{
    public string Meal { get; set; } = "";
    public double Average { get; set; }
    public int Ratings { get; set; }
}
