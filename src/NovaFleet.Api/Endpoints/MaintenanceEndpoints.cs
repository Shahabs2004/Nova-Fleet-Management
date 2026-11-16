using Microsoft.AspNetCore.Http.Features;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;

namespace NovaFleet.Api.Endpoints;

public static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/maintenance");
        group.MapGet("/templates", (IMaintenanceService s, CancellationToken ct) => s.TemplatesAsync(ct));
        group.MapPost("/templates", (TemplateInput input, IMaintenanceService s, CancellationToken ct) => s.SaveTemplateAsync(null, input, ct));
        group.MapPut("/templates/{id:guid}", (Guid id, TemplateInput input, IMaintenanceService s, CancellationToken ct) => s.SaveTemplateAsync(id, input, ct));
        group.MapGet("/vehicles", (string? search, string? state, int? page, int? pageSize, IMaintenanceService s, CancellationToken ct) => s.FleetAsync(search, state, page ?? 1, pageSize ?? 20, ct));
        var vehicle = group.MapGroup("/vehicles/{vehicleId:guid}");
        vehicle.MapGet("/", (Guid vehicleId, IMaintenanceService s, CancellationToken ct) => s.VehicleAsync(vehicleId, ct));
        vehicle.MapGet("/records", (Guid vehicleId, int? page, int? pageSize, bool? includeVoided, IMaintenanceService s, CancellationToken ct) => s.RecordsAsync(vehicleId, page ?? 1, pageSize ?? 20, includeVoided ?? false, ct));
        vehicle.MapGet("/records/{id:guid}", (Guid vehicleId, Guid id, IMaintenanceService s, CancellationToken ct) => s.RecordAsync(vehicleId, id, ct));
        vehicle.MapPost("/records", (Guid vehicleId, RecordInput input, IMaintenanceService s, CancellationToken ct) => s.SaveRecordAsync(vehicleId, null, input, ct));
        vehicle.MapPut("/records/{id:guid}", (Guid vehicleId, Guid id, RecordInput input, IMaintenanceService s, CancellationToken ct) => s.SaveRecordAsync(vehicleId, id, input, ct));
        vehicle.MapPost("/records/{id:guid}/void", async (Guid vehicleId, Guid id, RevisionInput input, IMaintenanceService s, CancellationToken ct) =>
        {
            await s.VoidRecordAsync(vehicleId, id, input, ct);
            return Results.NoContent();
        });
        vehicle.MapGet("/revisions/{id:guid}", (Guid vehicleId, Guid id, IMaintenanceService s, CancellationToken ct) => s.RevisionsAsync(vehicleId, id, ct));
        vehicle.MapPost("/schedules", (Guid vehicleId, ScheduleInput input, IMaintenanceService s, CancellationToken ct) => s.SaveScheduleAsync(vehicleId, null, input, ct));
        vehicle.MapPut("/schedules/{id:guid}", (Guid vehicleId, Guid id, ScheduleInput input, IMaintenanceService s, CancellationToken ct) => s.SaveScheduleAsync(vehicleId, id, input, ct));
        vehicle.MapGet("/odometer", (Guid vehicleId, IMaintenanceService s, CancellationToken ct) => s.OdometerAsync(vehicleId, ct));
        vehicle.MapPost("/odometer", (Guid vehicleId, OdometerInput input, IMaintenanceService s, CancellationToken ct) => s.SaveOdometerAsync(vehicleId, null, input, ct));
        vehicle.MapPut("/odometer/{id:guid}", (Guid vehicleId, Guid id, OdometerInput input, IMaintenanceService s, CancellationToken ct) => s.SaveOdometerAsync(vehicleId, id, input, ct));
        vehicle.MapGet("/report", (Guid vehicleId, DateTimeOffset from, DateTimeOffset to, IMaintenanceService s, CancellationToken ct) => s.ReportAsync(vehicleId, from, to, ct));
        vehicle.MapPost("/records/{recordId:guid}/attachments", async (Guid vehicleId, Guid recordId, HttpContext context, IMaintenanceService s, CancellationToken ct) =>
        {
            if (!context.Request.HasFormContentType) throw new ValidationAppException("فایل را به‌صورت فرم ارسال کنید.");
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = 6*1024*1024;
            var form = await context.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = 6*1024*1024 }, ct);
            if (form.Files.Count != 1 || form.Files[0].Length > 5*1024*1024)
                throw new ValidationAppException("یک فایل با حجم حداکثر ۵ مگابایت انتخاب کنید.");
            var file = form.Files[0];
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            return Results.Ok(await s.AddAttachmentAsync(vehicleId, recordId, file.FileName, stream.ToArray(), ct));
        });
        vehicle.MapGet("/records/{recordId:guid}/attachments/{id:guid}", async (Guid vehicleId, Guid recordId, Guid id, HttpContext context, IMaintenanceService s, CancellationToken ct) =>
        {
            var file = await s.DownloadAsync(vehicleId, recordId, id, ct);
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Content, "application/octet-stream", file.FileName);
        });
    }
}
