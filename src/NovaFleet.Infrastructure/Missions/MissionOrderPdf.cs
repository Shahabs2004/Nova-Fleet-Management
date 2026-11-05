using NovaFleet.Application.Missions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Missions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NovaFleet.Infrastructure.Missions;

/// <summary>سند حکم مأموریت (PDF راست‌به‌چپ) با سرصفحه و پانویس قالب ذخیره‌شده در تنظیمات.</summary>
public static class MissionOrderPdf
{
    private const string Emerald = "#0F766E";
    private const string Gold = "#C8A44A";
    private const string Muted = "#475569";
    private const string Surface = "#F6F8F7";

    public static byte[] Build(MissionDetailsDto mission, SettingsBundleDto settings, string printedBy, DateTimeOffset printedAtUtc)
    {
        var template = settings.MissionOrderTemplate;
        var logo = template.ShowLogo ? DecodeLogo(settings.Organization.LogoDataUrl) : null;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(style => style.FontFamily("Tahoma").FontSize(9.5f));
                page.ContentFromRightToLeft();

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        if (logo is not null)
                        {
                            row.ConstantItem(64).Height(48).Image(logo);
                            row.ConstantItem(12);
                        }

                        row.RelativeItem().Column(identity =>
                        {
                            identity.Item().Text(settings.Organization.Name).FontSize(15).Bold().FontColor(Emerald);
                            if (!string.IsNullOrWhiteSpace(template.HeaderText))
                            {
                                identity.Item().AlignRight().Text(template.HeaderText.Trim()).FontSize(8.5f).FontColor(Muted);
                            }
                        });

                        row.ConstantItem(150).Column(number =>
                        {
                            number.Item().AlignRight().Text($"حکم مأموریت {mission.MissionNumber}").Bold().FontColor(Emerald);
                            number.Item().AlignRight().Text(MissionStatusTitles.Of(mission.Status)).FontSize(8.5f).FontColor(Muted);
                        });
                    });

                    header.Item().PaddingTop(6).LineHorizontal(1.4f).LineColor(Gold);
                });

                page.Content().PaddingVertical(10).Column(content =>
                {
                    Section(content.Item(), "مشخصات درخواست", new[]
                    {
                        Field("عنوان", mission.Title),
                        Field("نوع", mission.Type == MissionType.Freight ? "باربری" : "سرنشین‌دار"),
                        Field("درخواست‌کننده", $"{mission.RequesterName} ({mission.RequesterUsername})"),
                        Field("گروه درخواست‌کننده", mission.RequestingGroup),
                        Field("واحد سازمانی", mission.RequesterOrganizationalUnit),
                        Field("موبایل", mission.RequesterPhoneSnapshot),
                        Field("هدف مأموریت", mission.Purpose),
                        Field("توضیحات", mission.Description),
                    });

                    Section(content.Item(), "مسیر و زمان", new[]
                    {
                        Field("مبدأ", mission.Origin),
                        Field("مقصدهای میانی", mission.IntermediateStops.Count > 0 ? string.Join(" ← ", mission.IntermediateStops) : null),
                        Field("مقصد نهایی", mission.Destination),
                        Field("شروع برنامه‌ریزی‌شده", JalaliDate.FormatDateTime(mission.PlannedStartAtUtc)),
                        Field("پایان برنامه‌ریزی‌شده", JalaliDate.FormatDateTime(mission.PlannedEndAtUtc)),
                        Field("شروع واقعی", mission.ActualStartAtUtc is { } actualStart ? JalaliDate.FormatDateTime(actualStart) : null),
                        Field("پایان واقعی", mission.ActualEndAtUtc is { } actualEnd ? JalaliDate.FormatDateTime(actualEnd) : null),
                    });

                    Section(content.Item(), "منابع تخصیص‌یافته", new[]
                    {
                        Field("راننده", mission.DriverName),
                        Field("موبایل راننده", mission.DriverMobile),
                        Field("خودرو", mission.VehicleDisplayName),
                        Field("کیلومتر شروع", mission.OdometerStartKm?.ToString()),
                        Field("کیلومتر پایان", mission.OdometerEndKm?.ToString()),
                        Field("مسافت (کیلومتر)", mission.DistanceKm?.ToString()),
                    });

                    var payload = mission.Type == MissionType.Freight
                        ? new[] { Field("شرح بار", mission.CargoDescription), Field("تحویل‌گیرنده", mission.ConsigneeName) }
                        : new[]
                        {
                            Field("تعداد همراهان", mission.PassengerCount?.ToString()),
                            Field("نام همراهان", mission.PassengerNames),
                        };
                    Section(content.Item(), mission.Type == MissionType.Freight ? "محموله" : "مسافران", payload);

                    Section(content.Item(), "حکم", new[]
                    {
                        Field("شماره حکم", mission.MissionOrderNumber),
                        Field("تاریخ حکم", mission.MissionOrderDate?.ToString("yyyy/MM/dd")),
                        Field("تأییدکننده", mission.ApprovedBy),
                        Field("زمان تأیید", mission.ApprovedAtUtc is { } approvedAt ? JalaliDate.FormatDateTime(approvedAt) : null),
                        Field("مأموریت مرتبط", mission.RelatedMissionNumber is null ? null : $"{mission.RelatedMissionNumber} — {mission.SplitReason}"),
                    });

                    content.Item().PaddingTop(18).Row(signatures =>
                    {
                        foreach (var title in new[] { "امضای درخواست‌کننده", "امضای راننده", "امضای تحویل‌گیرنده", "مهر و امضای اپراتور" })
                        {
                            SignatureBlock(signatures.RelativeItem(), title);
                        }
                    });
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().PaddingBottom(4).LineHorizontal(0.7f).LineColor("#E2E8F0");
                    footer.Item().Row(row =>
                    {
                        if (!string.IsNullOrWhiteSpace(template.FooterText))
                        {
                            row.RelativeItem().AlignRight().Text(template.FooterText.Trim()).FontSize(8).FontColor(Muted);
                        }
                        else
                        {
                            row.RelativeItem();
                        }

                        row.ConstantItem(230).AlignLeft().Text(
                            $"چاپ توسط {printedBy} در {JalaliDate.FormatDateTime(printedAtUtc)}")
                            .FontSize(7.5f).FontColor(Muted);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void SignatureBlock(IContainer container, string title) =>
        container.Column(column =>
        {
            column.Item().Height(44);
            column.Item().PaddingTop(3).LineHorizontal(0.7f).LineColor("#CBD5E1");
            column.Item().AlignCenter().Text(title).FontSize(8).FontColor(Muted);
        });

    private static void Section(IContainer container, string title, IReadOnlyList<(string Label, string Value)> fields) =>
        container.Column(section =>
        {
            section.Item().PaddingTop(8).PaddingBottom(4).Text(title).Bold().FontSize(10.5f).FontColor(Emerald);
            section.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(104);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(104);
                    columns.RelativeColumn(3);
                });

                for (var i = 0; i < fields.Count; i += 2)
                {
                    var background = (i / 2) % 2 == 0 ? Surface : "#FFFFFF";
                    for (var slot = 0; slot < 2; slot++)
                    {
                        if (i + slot >= fields.Count)
                        {
                            table.Cell();
                            table.Cell();
                            continue;
                        }

                        var (label, value) = fields[i + slot];
                        table.Cell().Padding(4).Background(background).Text(label).FontSize(8.5f).FontColor(Muted);
                        table.Cell().Padding(4).Background(background).Text(value).Bold();
                    }
                }
            });
        });

    private static (string Label, string Value) Field(string label, string? value) =>
        (label, string.IsNullOrWhiteSpace(value) ? "—" : value.Trim());

    private static byte[]? DecodeLogo(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return null;
        }

        var comma = dataUrl.IndexOf(',');
        var payload = comma < 0 ? dataUrl : dataUrl[(comma + 1)..];
        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
