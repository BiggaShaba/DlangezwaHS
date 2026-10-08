using System.Text;

namespace DlangezwaHS.Web.Services;

/// <summary>Builds small CSV downloads (Excel-friendly: UTF-8 with BOM, quoted fields).</summary>
public static class CsvExport
{
    public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Quote)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(v => Quote(Format(v)))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm"),
        decimal m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        double m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    private static string Quote(string s)
    {
        // Neutralise spreadsheet formulas in user-entered text
        if (s.Length > 0 && "=+-@".Contains(s[0]) && !decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
