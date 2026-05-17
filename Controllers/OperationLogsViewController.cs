using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using System.Security.Claims;
using ClosedXML.Excel;

namespace MovieApi.Controllers
{
    [Authorize(Roles = "admin")]
    public class OperationLogsViewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public OperationLogsViewController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? status, string? logAction)
        {
            var query = _context.OperationLogs
                .AsNoTracking()
                .Include(l => l.Profile)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(l => l.Status == status);

            if (!string.IsNullOrEmpty(logAction))
                query = query.Where(l => l.Action == logAction);

            var logs = await query.OrderByDescending(l => l.CreatedAt)
                                .Take(1000)
                                .ToListAsync();

            var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            foreach (var log in logs)
            {
                log.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, ecuadorZone);
            }

            // Fetch distinct actions and statuses for the dropdowns
            ViewBag.Actions = await _context.OperationLogs
                .AsNoTracking()
                .Where(l => l.Action != null)
                .Select(l => l.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            ViewBag.Statuses = await _context.OperationLogs
                .AsNoTracking()
                .Where(l => l.Status != null)
                .Select(l => l.Status)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            ViewBag.SelectedStatus = status;
            ViewBag.SelectedAction = logAction;

            return View(logs);
        }

        [HttpGet]
        public async Task<IActionResult> Export(string? status, string? logAction)
        {
            var query = _context.OperationLogs
                .AsNoTracking()
                .Include(l => l.Profile)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(l => l.Status == status);

            if (!string.IsNullOrEmpty(logAction))
                query = query.Where(l => l.Action == logAction);

            var logs = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
            var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Logs de Operaciones");

                // Headers
                var headers = new[] { "Fecha", "Acción", "Estado", "Usuario", "Mensaje", "Latencia (ms)", "Detalles (JSON)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0078D4");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                // Data
                for (int i = 0; i < logs.Count; i++)
                {
                    var log = logs[i];
                    int rowIdx = i + 2;
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, ecuadorZone);

                    worksheet.Cell(rowIdx, 1).Value = localTime;
                    worksheet.Cell(rowIdx, 2).Value = log.Action;
                    worksheet.Cell(rowIdx, 3).Value = log.Status;
                    worksheet.Cell(rowIdx, 4).Value = log.Profile?.Email ?? "Sistema";
                    worksheet.Cell(rowIdx, 5).Value = log.ErrorMessage ?? "";
                    worksheet.Cell(rowIdx, 6).Value = log.LatencyMs ?? 0;
                    worksheet.Cell(rowIdx, 7).Value = log.Metadata ?? "";

                    // Conditional formatting for status
                    if (log.Status == "error")
                        worksheet.Cell(rowIdx, 3).Style.Font.FontColor = XLColor.Red;
                    else if (log.Status == "success")
                        worksheet.Cell(rowIdx, 3).Style.Font.FontColor = XLColor.Green;
                }

                // Formatting
                worksheet.Columns().AdjustToContents();
                worksheet.Column(5).Width = 50; // Message column wider
                worksheet.Column(7).Width = 60; // Metadata column wider
                worksheet.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"logs_cine_{localNow:yyyyMMdd_HHmm}.xlsx");
                }
            }
        }
    }
}
