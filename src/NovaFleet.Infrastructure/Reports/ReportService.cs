using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Reports;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NovaFleet.Infrastructure.Reports;

/// <summary>هشت گزارش مصوب؛ اجرای هر گزارش مجوز خودش را می‌خواهد و خروجی فقط با مجوز صادر می‌شود.</summary>
public sealed class ReportService(NovaFleetDbContext db, ICurrentUser currentUser) : IReportService
{
    private static readonly IReadOnlyList<ReportDefinitionDto> Definitions =
    [
        new("missions", "گزارش مأموریت‌ها", "فهرست مأموریت‌ها با فیلتر وضعیت، نوع، راننده، خودرو و بازه زمانی."),
        new("missions-distance", "کارکرد کیلومتری", "مجموع مسافت و تعداد مأموریت به تفکیک خودرو در بازه انتخابی."),
        new("driver-activity", "فعالیت رانندگان", "تعداد مأموریت و مسافت طی‌شده هر راننده در بازه انتخابی."),
        new("vehicle-usage", "بهره‌برداری خودروها", "نرخ بهره‌برداری و روزهای درگیر مأموریت برای هر خودرو."),
        new("org-unit", "مأموریت واحدهای سازمانی", "تعداد و مسافت مأموریت‌ها به تفکیک واحد سازمانی درخواست‌کننده."),
        new("mission-status", "توزیع وضعیت مأموریت‌ها", "شمارش مأموریت‌ها بر اساس وضعیت چرخه عمر در بازه انتخابی."),
        new("sms-log", "گزارش پیامک‌ها", "سوابق صف پیامک همراه وضعیت تحویل."),
        new("sessions", "گزارش نشست‌ها", "سوابق ورود و نشست کاربران همراه روش ورود و وضعیت.")
    ];

    public IReadOnlyList<ReportDefinitionDto> GetDefinitions() => Definitions;

    public async Task<ReportTableDto> RunAsync(string key, ReportQuery query, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Reports.View);
        return key switch
        {
            "missions" => await MissionsReport(query, cancellationToken),
            "missions-distance" => await DistanceReport(query, cancellationToken),
            "driver-activity" => await DriverActivityReport(query, cancellationToken),
            "vehicle-usage" => await VehicleUsageReport(query, cancellationToken),
            "org-unit" => await OrgUnitReport(query, cancellationToken),
            "mission-status" => await StatusReport(query, cancellationToken),
            "sms-log" => await SmsLogReport(query, cancellationToken),
            "sessions" => await SessionsReport(query, cancellationToken),
            _ => throw new NotFoundAppException("گزارش موردنظر تعریف نشده است.")
        };
    }

    public async Task<byte[]> ExportExcelAsync(string key, ReportQuery query, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Reports.Export);
        var table = await RunAsync(key, query, cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("گزارش");
        sheet.RightToLeft = true;

        var titleCell = sheet.Cell(1, 1);
        titleCell.Value = table.Title;
        titleCell.Style.Font.SetBold().Font.SetFontSize(14);
        titleCell.Style.Font.SetFontColor(XLColor.FromHtml("#0F766E"));
        if (!string.IsNullOrWhiteSpace(table.Subtitle))
        {
            sheet.Cell(2, 1).Value = table.Subtitle;
        }

        var headerRow = string.IsNullOrWhiteSpace(table.Subtitle) ? 4 : 5;
        for (var column = 0; column < table.Columns.Count; column++)
        {
            var cell = sheet.Cell(headerRow, column + 1);
            cell.Value = table.Columns[column].Title;
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
            cell.Style.Font.SetFontColor(XLColor.White);
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var column = 0; column < table.Columns.Count; column++)
            {
                var cell = sheet.Cell(headerRow + 1 + row, column + 1);
                var value = table.Rows[row][column];
                if (value is null)
                {
                    cell.Value = string.Empty;
                }
                else if (value is double number)
                {
                    cell.Value = number;
                }
                else if (value is int integer)
                {
                    cell.Value = integer;
                }
                else if (value is long longNumber)
                {
                    cell.Value = longNumber;
                }
                else
                {
                    cell.Value = value.ToString();
                }
            }
        }

        var totalsRow = headerRow + 1 + table.Rows.Count + 1;
        var totals = new List<string>();
        foreach (var total in table.Totals)
        {
            totals.Add($"{total.Key}: {total.Value}");
        }

        if (totals.Count > 0)
        {
            var cell = sheet.Cell(totalsRow, 1);
            cell.Value = string.Join("   |   ", totals);
            cell.Style.Font.SetBold();
        }

        sheet.Columns().AdjustToContents(5, 100);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportPdfAsync(string key, ReportQuery query, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Reports.Export);
        var table = await RunAsync(key, query, cancellationToken);
        var totals = string.Join("   |   ", table.Totals.Select(item => $"{item.Key}: {item.Value}"));

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontFamily("Tahoma").FontSize(9));
                page.ContentFromRightToLeft();

                page.Header().Column(column =>
                {
                    column.Item().AlignRight().Text(table.Title).FontSize(15).Bold().FontColor("#0F766E");
                    if (!string.IsNullOrWhiteSpace(table.Subtitle))
                    {
                        column.Item().AlignRight().Text(table.Subtitle).FontSize(9).FontColor("#475569");
                    }

                    column.Item().PaddingTop(4).LineHorizontal(1).LineColor("#C8A44A");
                });

                page.Content().PaddingVertical(8).Table(tableBuilder =>
                {
                    tableBuilder.ColumnsDefinition(columns =>
                    {
                        foreach (var column in table.Columns)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    tableBuilder.Header(header =>
                    {
                        foreach (var column in table.Columns)
                        {
                            header.Cell().Background("#0F766E").Padding(4)
                                .Text(column.Title).FontColor("#FFFFFF").Bold();
                        }
                    });

                    var index = 0;
                    foreach (var row in table.Rows)
                    {
                        var background = index % 2 == 0 ? "#FFFFFF" : "#F6F8F7";
                        for (var column = 0; column < table.Columns.Count; column++)
                        {
                            var text = row[column]?.ToString() ?? string.Empty;
                            var cell = tableBuilder.Cell().Background(background).Padding(3);
                            if (table.Columns[column].IsNumeric)
                            {
                                cell.AlignLeft().Text(text);
                            }
                            else
                            {
                                cell.Text(text);
                            }
                        }

                        index++;
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().AlignRight().Text(totals).FontSize(9);
                    row.RelativeItem().AlignLeft().Text($"تاریخ تولید: {JalaliDate.FormatDateTime(DateTimeOffset.UtcNow)}").FontSize(8).FontColor("#475569");
                });
            });
        });

        return document.GeneratePdf();
    }

    private async Task<ReportTableDto> MissionsReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.View, PermissionCatalog.Missions.ViewOwn);
        var missions = await LoadRowsAsync(query, cancellationToken);
        var columns = new ReportColumnDto[]
        {
            new("MissionNumber", "شماره"),
            new("Title", "عنوان"),
            new("Status", "وضعیت"),
            new("Type", "نوع"),
            new("Driver", "راننده"),
            new("Vehicle", "خودرو"),
            new("Start", "شروع"),
            new("End", "پایان"),
            new("Distance", "مسافت (کیلومتر)", true)
        };
        var rows = missions.Select(item => new object?[]
        {
            item.MissionNumber,
            item.Title,
            MissionStatusTitles.Of(item.Status),
            item.Type == MissionType.Freight ? "باربری" : "سرنشین",
            item.DriverName,
            item.VehiclePlate,
            JalaliDate.FormatDateTime(item.PlannedStartAtUtc),
            JalaliDate.FormatDateTime(item.PlannedEndAtUtc),
            (object?)item.DistanceKm
        }).ToList();
        var totals = new Dictionary<string, object?>
        {
            ["تعداد"] = missions.Count,
            ["مجموع مسافت"] = missions.Sum(item => item.DistanceKm ?? 0)
        };
        return new ReportTableDto("missions", "گزارش مأموریت‌ها", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> DistanceReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.View, PermissionCatalog.Missions.ViewOwn);
        var missions = await LoadRowsAsync(query, cancellationToken);
        var rows = missions
            .Where(item => item.VehiclePlate is not null)
            .GroupBy(item => item.VehiclePlate!)
            .Select(group => new object?[]
            {
                group.Key,
                group.Count(),
                group.Sum(item => item.DistanceKm ?? 0)
            })
            .OrderByDescending(row => (long)row[2]!)
            .ToList();
        var columns = new ReportColumnDto[]
        {
            new("Vehicle", "خودرو"),
            new("Count", "تعداد مأموریت", true),
            new("Distance", "مجموع مسافت (کیلومتر)", true)
        };
        var totals = new Dictionary<string, object?>
        {
            ["تعداد خودرو"] = rows.Count,
            ["مجموع مسافت"] = rows.Sum(row => (long)row[2]!)
        };
        return new ReportTableDto("missions-distance", "کارکرد کیلومتری", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> DriverActivityReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.Require(PermissionCatalog.Missions.View);
        var missions = await LoadRowsAsync(query, cancellationToken);
        var rows = missions
            .Where(item => item.DriverName is not null)
            .GroupBy(item => item.DriverName!)
            .Select(group => new object?[]
            {
                group.Key,
                group.Count(),
                group.Sum(item => item.DistanceKm ?? 0),
                group.Count(item => item.Status == MissionStatus.Completed)
            })
            .OrderByDescending(row => (int)row[1]!)
            .ToList();
        var columns = new ReportColumnDto[]
        {
            new("Driver", "راننده"),
            new("Count", "تعداد مأموریت", true),
            new("Distance", "مسافت (کیلومتر)", true),
            new("Completed", "کامل‌شده", true)
        };
        var totals = new Dictionary<string, object?>
        {
            ["تعداد راننده"] = rows.Count,
            ["مجموع مأموریت"] = rows.Sum(row => (int)row[1]!)
        };
        return new ReportTableDto("driver-activity", "فعالیت رانندگان", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> VehicleUsageReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.Require(PermissionCatalog.Missions.View);
        var missions = await LoadRowsAsync(query, cancellationToken);
        var rangeDays = Math.Max(1, (int)Math.Ceiling(((query.ToUtc ?? DateTimeOffset.UtcNow) - (query.FromUtc ?? DateTimeOffset.UtcNow.AddMonths(-1))).TotalDays));
        var rows = missions
            .Where(item => item.VehiclePlate is not null)
            .GroupBy(item => item.VehiclePlate!)
            .Select(group => new object?[]
            {
                group.Key,
                group.Count(),
                Math.Round((double)group.Sum(item => (item.PlannedEndAtUtc - item.PlannedStartAtUtc).TotalHours), 1),
                Math.Round(100.0 * group.Sum(item => (item.PlannedEndAtUtc - item.PlannedStartAtUtc).TotalDays) / rangeDays, 1)
            })
            .OrderByDescending(row => (double)row[3]!)
            .ToList();
        var columns = new ReportColumnDto[]
        {
            new("Vehicle", "خودرو"),
            new("Count", "تعداد مأموریت", true),
            new("Hours", "ساعت بهره‌برداری", true),
            new("UsagePercent", "درصد بهره‌برداری", true)
        };
        var totals = new Dictionary<string, object?>
        {
            ["تعداد خودرو"] = rows.Count,
            ["مجموع مأموریت"] = rows.Sum(row => (int)row[1]!)
        };
        return new ReportTableDto("vehicle-usage", "بهره‌برداری خودروها", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> OrgUnitReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.Require(PermissionCatalog.Missions.View);
        var missions = await LoadRowsAsync(query, cancellationToken);
        var rows = missions
            .GroupBy(item => string.IsNullOrWhiteSpace(item.OrganizationalUnit) ? "بدون واحد" : item.OrganizationalUnit)
            .Select(group => new object?[]
            {
                group.Key,
                group.Count(),
                group.Count(item => item.Status == MissionStatus.Rejected),
                group.Count(item => item.Status == MissionStatus.Cancelled),
                group.Sum(item => item.DistanceKm ?? 0)
            })
            .OrderByDescending(row => (int)row[1]!)
            .ToList();
        var columns = new ReportColumnDto[]
        {
            new("Unit", "واحد سازمانی"),
            new("Count", "تعداد مأموریت", true),
            new("Rejected", "ردشده", true),
            new("Cancelled", "لغوشده", true),
            new("Distance", "مسافت (کیلومتر)", true)
        };
        var totals = new Dictionary<string, object?> { ["مجموع مأموریت"] = missions.Count };
        return new ReportTableDto("org-unit", "مأموریت واحدهای سازمانی", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> StatusReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.View, PermissionCatalog.Missions.ViewOwn);
        var missions = await LoadRowsAsync(query, cancellationToken);
        var rows = missions
            .GroupBy(item => item.Status)
            .OrderBy(item => (int)item.Key)
            .Select(group => new object?[] { MissionStatusTitles.Of(group.Key), group.Count() })
            .ToList();
        var columns = new ReportColumnDto[]
        {
            new("Status", "وضعیت"),
            new("Count", "تعداد", true)
        };
        var totals = new Dictionary<string, object?> { ["مجموع"] = missions.Count };
        return new ReportTableDto("mission-status", "توزیع وضعیت مأموریت‌ها", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> SmsLogReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.Require(PermissionCatalog.Notifications.SmsView);
        var source = db.SmsQueueItems.AsNoTracking();
        if (query.FromUtc.HasValue)
        {
            source = source.Where(item => item.CreatedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(item => item.CreatedAtUtc <= query.ToUtc.Value);
        }

        var items = await source.OrderByDescending(item => item.CreatedAtUtc).Take(1000).ToListAsync(cancellationToken);
        var columns = new ReportColumnDto[]
        {
            new("Event", "رویداد"),
            new("Recipient", "گیرنده"),
            new("Mobile", "شماره"),
            new("Status", "وضعیت"),
            new("Attempts", "تلاش", true),
            new("CreatedAt", "زمان ثبت"),
            new("DeliveredAt", "زمان تحویل")
        };
        var rows = items.Select(item => new object?[]
        {
            item.EventKey,
            item.RecipientName,
            item.RecipientMobile,
            item.Status.ToString(),
            item.AttemptCount,
            JalaliDate.FormatDateTime(item.CreatedAtUtc),
            item.DeliveredAtUtc.HasValue ? JalaliDate.FormatDateTime(item.DeliveredAtUtc.Value) : null
        }).ToList();
        var totals = new Dictionary<string, object?>
        {
            ["تعداد"] = items.Count,
            ["تحویل‌شده"] = items.Count(item => item.Status == SmsQueueStatus.Delivered)
        };
        return new ReportTableDto("sms-log", "گزارش پیامک‌ها", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private async Task<ReportTableDto> SessionsReport(ReportQuery query, CancellationToken cancellationToken)
    {
        currentUser.Require(PermissionCatalog.Security.SessionsView);
        var source = db.UserSessions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.OrganizationalUnit))
        {
            source = source.Where(item => item.Username == query.OrganizationalUnit);
        }

        if (query.FromUtc.HasValue)
        {
            source = source.Where(item => item.LoginAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(item => item.LoginAtUtc <= query.ToUtc.Value);
        }

        var items = await source.OrderByDescending(item => item.LoginAtUtc).Take(1000).ToListAsync(cancellationToken);
        var columns = new ReportColumnDto[]
        {
            new("Username", "کاربر"),
            new("Method", "روش ورود"),
            new("Status", "وضعیت"),
            new("LoginAt", "زمان ورود"),
            new("EndedAt", "زمان پایان"),
            new("Ip", "نشانی IP"),
            new("Reason", "علت")
        };
        var rows = items.Select(item => new object?[]
        {
            item.Username,
            item.LoginMethod.ToString(),
            item.Status.ToString(),
            JalaliDate.FormatDateTime(item.LoginAtUtc),
            item.EndedAtUtc.HasValue ? JalaliDate.FormatDateTime(item.EndedAtUtc.Value) : null,
            item.ClientIpAddress,
            item.Reason
        }).ToList();
        var totals = new Dictionary<string, object?>
        {
            ["تعداد"] = items.Count,
            ["موفق"] = items.Count(item => item.Status != UserSessionStatus.Failed)
        };
        return new ReportTableDto("sessions", "گزارش نشست‌ها", columns, rows, totals, DateTimeOffset.UtcNow, SubtitleOf(query));
    }

    private IQueryable<Mission> Filtered(ReportQuery query)
    {
        var source = db.Missions.AsNoTracking();

        if (query.FromUtc.HasValue)
        {
            source = source.Where(item => item.PlannedStartAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(item => item.PlannedStartAtUtc <= query.ToUtc.Value);
        }

        if (query.DriverId.HasValue)
        {
            source = source.Where(item => item.DriverId == query.DriverId.Value);
        }

        if (query.VehicleId.HasValue)
        {
            source = source.Where(item => item.VehicleId == query.VehicleId.Value);
        }

        if (query.Type.HasValue)
        {
            source = source.Where(item => item.Type == query.Type.Value);
        }

        if (query.Status.HasValue)
        {
            source = source.Where(item => item.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.OrganizationalUnit))
        {
            var unit = query.OrganizationalUnit.Trim();
            source = source.Where(item => item.RequesterOrganizationalUnit == unit);
        }

        return source;
    }

    /// <summary>فیلتر روی خود موجودیت ترجمه می‌شود؛ پرتاب به رکورد گزارش باید آخرین مرحله باشد، وگرنه LINQ قابل ترجمه نیست.</summary>
    private async Task<List<MissionReportRow>> LoadRowsAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var source = from mission in Filtered(query)
                     join driver in db.Drivers.AsNoTracking() on mission.DriverId equals driver.Id into drivers
                     from driver in drivers.DefaultIfEmpty()
                     join vehicle in db.Vehicles.AsNoTracking() on mission.VehicleId equals vehicle.Id into vehicles
                     from vehicle in vehicles.DefaultIfEmpty()
                     select new MissionReportRow(
                         mission.Id,
                         mission.DriverId,
                         mission.VehicleId,
                         mission.MissionNumber,
                         mission.Title,
                         mission.Status,
                         mission.Type,
                         mission.RequesterOrganizationalUnit,
                         driver == null ? null : driver.FirstName + " " + driver.LastName,
                         vehicle == null ? null : vehicle.PlateDisplay,
                         mission.PlannedStartAtUtc,
                         mission.PlannedEndAtUtc,
                         mission.OdometerStartKm,
                         mission.OdometerEndKm);

        return await source.ToListAsync(cancellationToken);
    }

    private static string? SubtitleOf(ReportQuery query)
    {
        var parts = new List<string>();
        if (query.FromUtc.HasValue || query.ToUtc.HasValue)
        {
            var from = query.FromUtc.HasValue ? JalaliDate.FormatDate(query.FromUtc.Value) : "ابتدا";
            var to = query.ToUtc.HasValue ? JalaliDate.FormatDate(query.ToUtc.Value) : "اکنون";
            parts.Add($"بازه: {from} تا {to}");
        }

        return parts.Count > 0 ? string.Join(" — ", parts) : null;
    }

    private sealed record MissionReportRow(
        Guid Id,
        Guid? DriverId,
        Guid? VehicleId,
        string MissionNumber,
        string Title,
        MissionStatus Status,
        MissionType Type,
        string OrganizationalUnit,
        string? DriverName,
        string? VehiclePlate,
        DateTimeOffset PlannedStartAtUtc,
        DateTimeOffset PlannedEndAtUtc,
        long? OdometerStartKm,
        long? OdometerEndKm)
    {
        public long? DistanceKm => OdometerStartKm.HasValue && OdometerEndKm.HasValue
            ? OdometerEndKm.Value - OdometerStartKm.Value
            : null;
    }
}
