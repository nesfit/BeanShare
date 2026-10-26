using BeanShare.Application.Constants;
using BeanShare.Application.Features.Settlement.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BeanShare.Infrastructure.Services.Documents;

/// <summary>
/// Generates PDF settlement reports using QuestPDF.
/// Layout values are defined in <see cref="PdfLayoutConstants"/>.
/// </summary>
public sealed class SettlementPdfGenerator
{
    static SettlementPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Generates a PDF settlement report containing space info, summary statistics,
    /// a detailed member breakdown table, and payment confirmations.
    /// </summary>
    public byte[] GeneratePdf(SettlementReportData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(data.IsLedger ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(PdfLayoutConstants.PageMargin);
                page.DefaultTextStyle(x => x.FontSize(PdfLayoutConstants.BodyFontSize));

                page.Header().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(c => ComposeFooter(c, data));
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, SettlementReportData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("BeanShare")
                        .FontSize(PdfLayoutConstants.HeaderTitleFontSize)
                        .Bold()
                        .FontColor(Colors.Brown.Darken2);

                    col.Item().Text("Settlement Report")
                        .FontSize(PdfLayoutConstants.HeaderSubtitleFontSize)
                        .SemiBold();
                });

                row.ConstantItem(150).AlignRight().Column(col =>
                {
                    col.Item().Text($"Generated: {data.GeneratedAt:dd MMM yyyy}")
                        .FontSize(PdfLayoutConstants.LabelFontSize);
                    col.Item().Text($"By: {data.GeneratedByName}")
                        .FontSize(PdfLayoutConstants.LabelFontSize);
                });
            });

            column.Item().PaddingTop(PdfLayoutConstants.SectionSpacing)
                .LineHorizontal(PdfLayoutConstants.BorderWidth)
                .LineColor(Colors.Grey.Lighten1);
        });
    }

    private void ComposeContent(IContainer container, SettlementReportData data)
    {
        container.PaddingVertical(PdfLayoutConstants.SectionSpacing).Column(column =>
        {
            column.Item().Element(c => ComposeSpaceInfo(c, data));
            column.Item().PaddingVertical(PdfLayoutConstants.SectionPadding).Element(c => ComposeSummary(c, data));
            column.Item().Element(c => ComposeTable(c, data));
        });
    }

    private void ComposeSpaceInfo(IContainer container, SettlementReportData data)
    {
        container.Background(Colors.Grey.Lighten4).Padding(PdfLayoutConstants.SectionPadding).Column(column =>
        {
            column.Item().Text(data.SpaceName)
                .FontSize(PdfLayoutConstants.SpaceNameFontSize)
                .SemiBold();

            column.Item().PaddingTop(PdfLayoutConstants.SmallSpacing).Row(row =>
            {
                row.RelativeItem().Text($"Billing Period: {data.BillingPeriodName}");
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Period: {data.PeriodStart:dd MMM yyyy} - {data.PeriodEnd:dd MMM yyyy}");
            });

            if (data.IsLedger)
            {
                column.Item().Text($"Treasurer: {data.TreasurerName ?? "-"}");
            }

            if (data.IsCarriedForward)
            {
                column.Item().PaddingTop(PdfLayoutConstants.SmallSpacing)
                    .Text("Carried forward: the open lines of this settlement were carried into the next one.")
                    .FontColor(Colors.Orange.Darken2).SemiBold();
            }
        });
    }

    private void ComposeSummary(IContainer container, SettlementReportData data)
    {
        container.Border(PdfLayoutConstants.BorderWidth).BorderColor(Colors.Grey.Lighten1)
            .Padding(PdfLayoutConstants.SectionPadding).Row(row =>
        {
            ComposeSummaryItem(row.RelativeItem(), "Total Amount",
                $"{data.TotalAmount:N2} {data.Currency}", Colors.Brown.Darken2);
            ComposeSummaryItem(row.RelativeItem(), "Participants",
                $"{data.TotalParticipants}", null);
            ComposeSummaryItem(row.RelativeItem(), "Total Coffee",
                $"{data.TotalCoffeeGrams:N1}g", null);

            if (data.TotalMilkMl > 0)
            {
                ComposeSummaryItem(row.RelativeItem(), "Total Milk",
                    $"{data.TotalMilkMl:N1}ml", null);
            }
        });
    }

    private static void ComposeSummaryItem(IContainer container, string label, string value, string? color)
    {
        container.Column(col =>
        {
            col.Item().Text(label)
                .FontSize(PdfLayoutConstants.LabelFontSize)
                .FontColor(Colors.Grey.Darken1);

            var valueText = col.Item().Text(value)
                .FontSize(PdfLayoutConstants.SummaryValueFontSize)
                .Bold();

            if (color != null)
                valueText.FontColor(color);
        });
    }

    private void ComposeTable(IContainer container, SettlementReportData data)
    {
        if (data.IsLedger)
        {
            ComposeLedgerTable(container, data);
            return;
        }

        container.Column(outerColumn =>
        {
            outerColumn.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3); // Name
                    columns.RelativeColumn(2); // Coffee (g)
                    columns.RelativeColumn(2); // Milk (ml)
                    columns.RelativeColumn(2); // Share %
                    columns.RelativeColumn(2); // Amount Due
                    columns.RelativeColumn(2); // Status
                });

                var headerLabels = new[] { "Member", "Coffee (g)", "Milk (ml)", "Share %", "Amount Due", "Status" };
                table.Header(header =>
                {
                    foreach (var label in headerLabels)
                    {
                        header.Cell().Background(Colors.Brown.Darken2)
                            .Padding(PdfLayoutConstants.TableCellPadding)
                            .Text(label).FontColor(Colors.White).SemiBold();
                    }
                });

                foreach (var line in data.Lines.OrderByDescending(l => l.AmountDue))
                {
                    var bgColor = line.IsPaid ? Colors.Green.Lighten5 : Colors.White;

                    table.Cell().Background(bgColor).BorderBottom(PdfLayoutConstants.BorderWidth)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(PdfLayoutConstants.TableCellPadding).Column(col =>
                        {
                            col.Item().Text(line.UserName).SemiBold();
                            col.Item().Text(line.UserEmail)
                                .FontSize(PdfLayoutConstants.SmallFontSize)
                                .FontColor(Colors.Grey.Darken1);
                        });

                    ComposeTableCell(table, bgColor, $"{line.CoffeeGrams:N1}");
                    ComposeTableCell(table, bgColor, line.MilkMl.HasValue ? $"{line.MilkMl:N1}" : "-");
                    ComposeTableCell(table, bgColor, $"{line.ConsumptionPercentage:N1}%");
                    ComposeTableCell(table, bgColor, $"{line.AmountDue:N2} {data.Currency}", semiBold: true);

                    var statusColor = line.IsPaid ? Colors.Green.Darken1 : Colors.Orange.Darken1;
                    var statusText = line.IsPaid ? "Paid" : "Pending";
                    table.Cell().Background(bgColor).BorderBottom(PdfLayoutConstants.BorderWidth)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(PdfLayoutConstants.TableCellPadding)
                        .AlignCenter().Text(statusText).FontColor(statusColor).SemiBold();
                }
            });

            ComposePaymentConfirmations(outerColumn, data);
        });
    }

    /// <summary>Ledger lines: the Net breakdown per member, sorted by Net, then the treasurer's payment plan.</summary>
    private void ComposeLedgerTable(IContainer container, SettlementReportData data)
    {
        var showRounding = data.Lines.Any(l => (l.Rounding ?? 0) != 0);

        container.Column(outerColumn =>
        {
            outerColumn.Item().Table(table =>
            {
                var headerLabels = new List<string> { "Member", "Coffee (g)", "Cost", "Credit", "Carried", "Adj.", "Moved (merge)" };
                if (showRounding)
                    headerLabels.Add("Rounding");
                headerLabels.AddRange(["Net", "Status"]);

                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    for (var i = 1; i < headerLabels.Count; i++)
                        columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    foreach (var label in headerLabels)
                    {
                        header.Cell().Background(Colors.Brown.Darken2)
                            .Padding(PdfLayoutConstants.TableCellPadding)
                            .Text(label).FontColor(Colors.White).SemiBold();
                    }
                });

                foreach (var line in data.Lines.OrderBy(l => l.Net ?? 0).ThenBy(l => l.UserName))
                {
                    var bgColor = line.IsPaid ? Colors.Green.Lighten5 : Colors.White;

                    table.Cell().Background(bgColor).BorderBottom(PdfLayoutConstants.BorderWidth)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(PdfLayoutConstants.TableCellPadding).Column(col =>
                        {
                            col.Item().Text(line.IsTreasurer ? $"{line.UserName} (treasurer)" : line.UserName).SemiBold();
                            col.Item().Text(line.UserEmail)
                                .FontSize(PdfLayoutConstants.SmallFontSize)
                                .FontColor(Colors.Grey.Darken1);
                        });

                    ComposeTableCell(table, bgColor, $"{line.CoffeeGrams:N1}");
                    ComposeTableCell(table, bgColor, $"{line.AmountDue:N2}");
                    ComposeTableCell(table, bgColor, $"{line.Credit ?? 0:N2}");
                    ComposeTableCell(table, bgColor, $"{line.CarriedIn ?? 0:N2}");
                    ComposeTableCell(table, bgColor, $"{line.Adjustments ?? 0:N2}");
                    ComposeTableCell(table, bgColor, $"{line.MergeTransfer:N2}");
                    if (showRounding)
                        ComposeTableCell(table, bgColor, $"{line.Rounding ?? 0:N2}");
                    ComposeTableCell(table, bgColor, $"{line.Net ?? 0:N2} {data.Currency}", semiBold: true);

                    var status = LedgerStatus(line);
                    var statusColor = status switch
                    {
                        "Paid" or "Square" => Colors.Green.Darken1,
                        "Carried" => Colors.Grey.Darken1,
                        _ => Colors.Orange.Darken1
                    };
                    table.Cell().Background(bgColor).BorderBottom(PdfLayoutConstants.BorderWidth)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(PdfLayoutConstants.TableCellPadding)
                        .AlignCenter().Text(status).FontColor(statusColor).SemiBold();
                }
            });

            outerColumn.Item().PaddingTop(PdfLayoutConstants.SmallSpacing)
                .Text("Net = Credit − Cost + Carried + Adj. + Moved + Rounding; negative means the member pays.")
                .FontSize(PdfLayoutConstants.SmallFontSize)
                .FontColor(Colors.Grey.Darken1);

            if (data.Transfers is { Count: > 0 } transfers)
            {
                outerColumn.Item().PaddingTop(PdfLayoutConstants.SectionPadding).Column(column =>
                {
                    column.Item().Text($"Payments through the treasurer ({data.TreasurerName ?? "-"})")
                        .FontSize(PdfLayoutConstants.SectionTitleFontSize)
                        .SemiBold();

                    foreach (var transfer in transfers)
                    {
                        column.Item().PaddingTop(PdfLayoutConstants.SmallSpacing)
                            .Text($"  {transfer.FromName} pays {transfer.ToName}: {transfer.Amount:N2} {data.Currency}");
                    }
                });
            }

            ComposePaymentConfirmations(outerColumn, data);
        });
    }

    private static string LedgerStatus(SettlementLineReportData line) => line switch
    {
        { IsTreasurer: true } => "Treasurer",
        { Resolution: "Carried" } => "Carried",
        _ when (line.Net ?? 0) == 0 => "Square",
        { Resolution: "Confirmed" } => "Paid",
        _ => "Open"
    };

    private static void ComposeTableCell(TableDescriptor table, string bgColor, string text, bool semiBold = false)
    {
        var cell = table.Cell().Background(bgColor)
            .BorderBottom(PdfLayoutConstants.BorderWidth).BorderColor(Colors.Grey.Lighten2)
            .Padding(PdfLayoutConstants.TableCellPadding).AlignRight().Text(text);

        if (semiBold)
            cell.SemiBold();
    }

    private static void ComposePaymentConfirmations(ColumnDescriptor outerColumn, SettlementReportData data)
    {
        var paidLines = data.Lines.Where(l => l.IsPaid && l.ConfirmedAt.HasValue).ToList();
        if (!paidLines.Any()) return;

        outerColumn.Item().PaddingTop(PdfLayoutConstants.SectionPadding).Column(column =>
        {
            column.Item().Text("Payment Confirmations")
                .FontSize(PdfLayoutConstants.SectionTitleFontSize)
                .SemiBold();

            column.Item().PaddingTop(PdfLayoutConstants.SmallSpacing).Column(detailCol =>
            {
                foreach (var line in paidLines.OrderBy(l => l.ConfirmedAt))
                {
                    detailCol.Item()
                        .Text($"  {line.UserName}: Confirmed by {line.ConfirmedByName ?? "Self"} on {line.ConfirmedAt:dd MMM yyyy HH:mm}")
                        .FontSize(PdfLayoutConstants.SmallFontSize)
                        .FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private void ComposeFooter(IContainer container, SettlementReportData data)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(PdfLayoutConstants.BorderWidth).LineColor(Colors.Grey.Lighten1);

            column.Item().PaddingTop(PdfLayoutConstants.SmallSpacing).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(PdfLayoutConstants.SmallFontSize).FontColor(Colors.Grey.Darken1));
                    text.Span("Generated by BeanShare  |  ");
                    text.Span($"{data.GeneratedAt:dd MMM yyyy HH:mm}");
                });

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(PdfLayoutConstants.SmallFontSize).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }
}
