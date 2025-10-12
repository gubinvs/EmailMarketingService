using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace EmailMarketingService;

[ApiController]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
     private readonly StorageOptions _storage;
     private readonly ILogger<UploadController> _log;
     private readonly IEmailQueue _queue;

     public UploadController(
         IOptions<StorageOptions> storage,
         ILogger<UploadController> log,
         IEmailQueue queue)
     {
          _storage = storage.Value;
          _log = log;
          _queue = queue;
     }

     /// <summary>
     /// Загружает Excel-файл с адресами email (первый столбец).
     /// </summary>
     [HttpPost("upload-excel")]
     [Consumes("multipart/form-data")]
     [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
     [ProducesResponseType(StatusCodes.Status400BadRequest)]
     public async Task<IActionResult> UploadExcel([FromForm] UploadExcelRequest request)
     {
          var file = request.File;
          if (file == null || file.Length == 0)
               return BadRequest("Файл не передан.");

          var safeName = Path.GetRandomFileName() + ".xlsx";
          var path = Path.Combine(_storage.UploadFolder, safeName);

          try
          {
               // 1️⃣ Сохраняем файл на диск
               await using (var fs = System.IO.File.Create(path))
                    await file.CopyToAsync(fs);

               // 2️⃣ Извлекаем адреса email
               var emails = ExtractEmailsFromExcel(path);
               if (!emails.Any())
                    return BadRequest("Не найдено email-адресов в первом столбце.");

               // 3️⃣ Добавляем в очередь рассылки
               await _queue.EnqueueMany(emails);

               // 4️⃣ Возвращаем результат
               return Ok(new { count = emails.Count(), message = "Файл обработан и удалён" });
          }
          catch (Exception ex)
          {
               _log.LogError(ex, "Ошибка при обработке Excel-файла");
               return StatusCode(500, "Ошибка при обработке файла");
          }
          finally
          {
               // 5️⃣ Удаляем файл независимо от результата
               try
               {
                    if (System.IO.File.Exists(path))
                         System.IO.File.Delete(path);
               }
               catch (Exception cleanupEx)
               {
                    _log.LogWarning(cleanupEx, "Не удалось удалить временный файл {Path}", path);
               }
          }
     }

     private IEnumerable<string> ExtractEmailsFromExcel(string path)
     {
          using var wb = new XLWorkbook(path);
          var ws = wb.Worksheets.First();
          var result = new List<string>();

          foreach (var row in ws.RowsUsed())
          {
               var value = row.Cell(1).GetString().Trim();
               if (IsValidEmail(value)) result.Add(value);
          }

          return result.Distinct(StringComparer.OrdinalIgnoreCase);
     }

     private static bool IsValidEmail(string email)
     {
          try
          {
               _ = new System.Net.Mail.MailAddress(email);
               return true;
          }
          catch
          {
               return false;
          }
     }
}
