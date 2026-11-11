using BurhanNexus.Vaccinator.Application;
using ClosedXML.Excel;

namespace BurhanNexus.Vaccinator.Reporting;

/// <summary>Excel export (.xlsx) of any list shown on screen.</summary>
public static class Excel
{
    public static void Export<T>(string path, string title, IEnumerable<T> rows, params (string Header, Func<T, object?> Value)[] columns)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(title));
        var center = SettingsService.Load();
        ws.Cell(1, 1).Value = center.CenterName;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"{title} – exported {Fmt.DT(DateTime.Now)} – Burhan Nexus Vaccinator";

        for (var c = 0; c < columns.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5F2");
        }

        var r = 5;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Length; c++)
            {
                var v = columns[c].Value(row);
                var cell = ws.Cell(r, c + 1);
                switch (v)
                {
                    case null: break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case decimal d: cell.Value = d; break;
                    case double db: cell.Value = db; break;
                    default: cell.Value = v.ToString(); break;
                }
            }
            r++;
        }
        ws.Columns().AdjustToContents(4, r, 8, 60);
        ws.SheetView.FreezeRows(4);
        wb.SaveAs(path);
    }

    private static string SafeSheetName(string s)
    {
        var clean = new string(s.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        return clean.Length > 31 ? clean[..31] : (clean.Length == 0 ? "Sheet1" : clean);
    }
}
