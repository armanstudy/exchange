using ClosedXML.Excel;
using LenjExchange.Application.Documents;
using LenjExchange.Application.Reporting;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Shamsi;
using LenjExchange.Domain.Transfers;

namespace LenjExchange.Infrastructure.Excel;

public sealed class ClosedXmlExporter : IExcelExporter
{
    private static readonly XLColor Primary = XLColor.FromHtml("#0B2F5B");
    private static readonly XLColor Alt = XLColor.FromHtml("#F5F8FB");

    public byte[] ExportStatement(StatementReport report)
    {
        using var wb = new XLWorkbook();
        var summary = Sheet(wb, "خلاصه");
        summary.Cell(1, 1).Value = $"صورتحساب {report.AccountName} ({report.AccountCode})";
        summary.Cell(2, 1).Value = report.Range.Describe();
        Header(summary, 4, "ارز", "مانده ابتدای دوره", "جمع بدهکار", "جمع بستانکار", "مانده پایان دوره", "تشخیص");
        var row = 5;
        foreach (var s in report.Sections)
        {
            summary.Cell(row, 1).Value = s.Currency.ToString();
            Num(summary.Cell(row, 2), s.Opening, s.Currency);
            Num(summary.Cell(row, 3), s.TotalDebit, s.Currency);
            Num(summary.Cell(row, 4), s.TotalCredit, s.Currency);
            Num(summary.Cell(row, 5), s.Closing, s.Currency);
            summary.Cell(row, 6).Value = StatementCurrencySection.Side(s.Closing);
            row++;
        }
        summary.Columns().AdjustToContents();

        foreach (var s in report.Sections)
        {
            var ws = Sheet(wb, $"گردش {s.Currency}");
            Header(ws, 1, "شماره سند", "تاریخ", "شرح", "ارز", "بدهکار", "بستانکار", "مانده");
            ws.Cell(2, 3).Value = "مانده ابتدای دوره";
            Num(ws.Cell(2, 7), s.Opening, s.Currency);
            var r = 3;
            foreach (var line in s.Rows)
            {
                ws.Cell(r, 1).Value = line.EntryNumber;
                ws.Cell(r, 2).Value = line.ShamsiDate;
                ws.Cell(r, 3).Value = line.Description;
                ws.Cell(r, 4).Value = line.Currency.ToString();
                if (line.Debit != 0) Num(ws.Cell(r, 5), line.Debit, s.Currency);
                if (line.Credit != 0) Num(ws.Cell(r, 6), line.Credit, s.Currency);
                Num(ws.Cell(r, 7), line.Balance, s.Currency);
                if (r % 2 == 0) ws.Range(r, 1, r, 7).Style.Fill.BackgroundColor = Alt;
                r++;
            }
            ws.Cell(r, 3).Value = "جمع دوره / مانده پایان دوره";
            Num(ws.Cell(r, 5), s.TotalDebit, s.Currency);
            Num(ws.Cell(r, 6), s.TotalCredit, s.Currency);
            Num(ws.Cell(r, 7), s.Closing, s.Currency);
            ws.Row(r).Style.Font.Bold = true;
            ws.Columns().AdjustToContents();
        }
        return Save(wb);
    }

    public byte[] ExportAllCustomers(AllCustomersReport report)
    {
        using var wb = new XLWorkbook();
        var ws = Sheet(wb, "همه مشتریان");
        ws.Cell(1, 1).Value = "گزارش مانده همه مشتریان — " + report.Range.Describe();
        Header(ws, 3, "کد", "نام مشتری", "ارز", "تعداد تراکنش", "جمع دریافتی (بستانکار)", "جمع پرداختی (بدهکار)", "مانده", "تشخیص", "آخرین تراکنش");
        var r = 4;
        foreach (var x in report.Rows)
        {
            ws.Cell(r, 1).Value = x.Code;
            ws.Cell(r, 2).Value = x.FullName;
            ws.Cell(r, 3).Value = x.Currency.ToString();
            ws.Cell(r, 4).Value = x.TransactionCount;
            Num(ws.Cell(r, 5), x.TotalReceived, x.Currency);
            Num(ws.Cell(r, 6), x.TotalPaid, x.Currency);
            Num(ws.Cell(r, 7), x.Balance, x.Currency);
            ws.Cell(r, 8).Value = x.Side;
            ws.Cell(r, 9).Value = x.LastTransactionUtc is { } d ? ShamsiDate.Format(d.ToLocalTime()) : "";
            r++;
        }
        r++;
        Header(ws, r++, "ارز", "جمع دریافتی", "جمع پرداختی", "مانده خالص");
        foreach (var (cur, rec, paid, bal) in report.Totals)
        {
            ws.Cell(r, 1).Value = cur.ToString();
            Num(ws.Cell(r, 2), rec, cur);
            Num(ws.Cell(r, 3), paid, cur);
            Num(ws.Cell(r, 4), bal, cur);
            r++;
        }
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    public byte[] ExportOpenCases(IReadOnlyList<OpenCaseRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = Sheet(wb, "پرونده‌های باز");
        Header(ws, 1, "شماره پرونده", "مشتری", "وضعیت", "ارز", "مبلغ تعهد", "تعهد صرافی", "تحویل به نماینده", "در مسیر", "دریافت در چین", "مانده تسویه‌نشده", "مغایرت‌ها");
        var r = 2;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.CaseNumber;
            ws.Cell(r, 2).Value = x.CustomerName;
            ws.Cell(r, 3).Value = TransferStatusText.Persian(x.Status);
            ws.Cell(r, 4).Value = x.Currency.ToString();
            Num(ws.Cell(r, 5), x.Target, x.Currency);
            Num(ws.Cell(r, 6), x.Committed, x.Currency);
            Num(ws.Cell(r, 7), x.DeliveredToAgents, x.Currency);
            Num(ws.Cell(r, 8), x.InTransit, x.Currency);
            Num(ws.Cell(r, 9), x.ReceivedInChina, x.Currency);
            Num(ws.Cell(r, 10), x.Unsettled, x.Currency);
            ws.Cell(r, 11).Value = string.Join("؛ ", x.Issues);
            r++;
        }
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    private static IXLWorksheet Sheet(XLWorkbook wb, string name)
    {
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        ws.RightToLeft = true;
        ws.Style.Font.FontName = "Vazirmatn";
        return ws;
    }

    private static void Header(IXLWorksheet ws, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            var c = ws.Cell(row, i + 1);
            c.Value = titles[i];
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = Primary;
        }
    }

    private static void Num(IXLCell cell, decimal value, CurrencyCode currency)
    {
        cell.Value = value;
        var d = Currencies.Info(currency).MaxDisplayDecimals;
        cell.Style.NumberFormat.Format = d == 0 ? "#,##0" : "#,##0." + new string('0', Math.Min(d, 2)) + new string('#', Math.Max(0, d - 2));
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
