namespace DlangezwaHS.Web.Services;

/// <summary>
/// The school's local clock. Meal plan dates and serving times ("07:00") are South African
/// times, but servers (including Azure) usually run on UTC — so compare against this, not
/// DateTime.Now / DateTime.UtcNow. South Africa (SAST) is UTC+2 with no daylight saving.
/// </summary>
public static class SchoolClock
{
    public static DateTime Now => DateTime.UtcNow.AddHours(2);
    public static DateTime Today => Now.Date;

    /// <summary>The Monday of the week containing the given date.</summary>
    public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
