using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace LIMS_AJT_NK_API.Controllers;

[ApiController]
[Route("api")]
public class CallbackController(ApplicationDbContext dbContext) : ControllerBase
{
    [HttpPost("call_back")]
    public async Task<IActionResult> ReceiveOcrResult([FromBody] OcrCallbackRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobTaskId))
        {
            return BadRequest(new
            {
                status = "error",
                message = "ข้อมูลไม่ถูกต้อง",
                data = new { },
                errors = new[] { new { field = "job_task_id", message = "ห้ามเว้นว่าง" } }
            });
        }

        if (request.OcrResult.Count == 0)
        {
            return BadRequest(new
            {
                status = "error",
                message = "ข้อมูลไม่ถูกต้อง",
                data = new { },
                errors = new[] { new { field = "ocr_result", message = "ห้ามเว้นว่าง" } }
            });
        }

        var now = DateTime.Now;
        var normalizedJobTaskId = request.JobTaskId.Trim();

        var callback = new InterfaceLimsOcrCallbackEntity
        {
            CallbackId = Guid.NewGuid(),
            JobTaskId = normalizedJobTaskId,
            IsInterface = false,
            CreateBy = "api",
            CreateDate = now
        };

        dbContext.InterfaceLimsOcrCallbacks.Add(callback);

        var resultRows = new List<InterfaceLimsOcrResultEntity>();

        foreach (var page in request.OcrResult)
        {
            if (page.PageId <= 0)
            {
                return BadRequest(new
                {
                    status = "error",
                    message = "ข้อมูลไม่ถูกต้อง",
                    data = new { },
                    errors = new[] { new { field = "page_id", message = "ต้องมากกว่า 0" } }
                });
            }

            if (string.IsNullOrWhiteSpace(page.TrackingStatus))
            {
                return BadRequest(new
                {
                    status = "error",
                    message = "ข้อมูลไม่ถูกต้อง",
                    data = new { },
                    errors = new[] { new { field = "tracking_status", message = "ห้ามเว้นว่าง" } }
                });
            }

            var bodyJson = page.BodyJson;
            var resultRow = new InterfaceLimsOcrResultEntity
            {
                ResultId = Guid.NewGuid(),
                Callback = callback,
                IsInterface = false,
                PageId = page.PageId,
                TrackingId = page.TrackingId,
                TrackingStatus = page.TrackingStatus.Trim(),
                ProductName = bodyJson?.ProductName,
                DocumentType = bodyJson?.DocumentType,
                SupplierName = bodyJson?.SupplierName,
                LotNumber = bodyJson?.LotNumber,
                OriginSupplierName = bodyJson?.OriginSupplierName,
                OriginProductName = bodyJson?.OriginProductName,
                ExpiryDate = ParseDate(bodyJson?.ExpiryDate),
                MfgDate = ParseDate(bodyJson?.MfgDate),
                InternalLot = bodyJson?.InternalLot,
                Quantity = ParseDecimal(bodyJson?.Quantity),
                CreateBy = "api",
                CreateDate = now
            };

            if (bodyJson?.BodyItem is { Count: > 0 })
            {
                for (var index = 0; index < bodyJson.BodyItem.Count; index++)
                {
                    var item = bodyJson.BodyItem[index];
                    if (string.IsNullOrWhiteSpace(item.ParameterName))
                    {
                        return BadRequest(new
                        {
                            status = "error",
                            message = "ข้อมูลไม่ถูกต้อง",
                            data = new { },
                            errors = new[] { new { field = "parameter_name", message = "ห้ามเว้นว่าง" } }
                        });
                    }

                    resultRow.Items.Add(new InterfaceLimsOcrResultItemEntity
                    {
                        ItemId = Guid.NewGuid(),
                        OcrResult = resultRow,
                        IsInterface = false,
                        Seq = index + 1,
                        ParameterName = item.ParameterName.Trim(),
                        ResultValue = item.Result,
                        Uom = item.Uom,
                        CreateBy = "api",
                        CreateDate = now
                    });
                }
            }

            resultRows.Add(resultRow);
        }

        callback.Results = resultRows;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            status = "success",
            message = "รับข้อมูล callback สำเร็จ",
            data = new
            {
                callback_id = callback.CallbackId,
                job_task_id = normalizedJobTaskId,
                result_count = resultRows.Count,
                item_count = resultRows.Sum(x => x.Items.Count)
            },
            errors = (object?)null
        });
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(value.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            ? quantity
            : null;
    }
}