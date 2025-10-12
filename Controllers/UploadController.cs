using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;


namespace EmailMarketingService
{
     [ApiController]
     [Route("api/[controller]")]
     public class UploadController : ControllerBase
     {
          private readonly StorageOptions _storage;
          private readonly ILogger<UploadController> _log;
          private readonly IEmailQueue _queue;
          private readonly IHttpClientFactory _http;


          public UploadController(IOptions<StorageOptions> storage, ILogger<UploadController> log, IEmailQueue queue, IHttpClientFactory http)
          {
               _storage = storage.Value;
               _log = log;
               _queue = queue;
               _http = http;
          }


          [HttpPost("upload-excel")]
          public async Task<IActionResult> UploadExcel([FromForm] IFormFile file)
          {
               if (file == null || file.Length == 0) return BadRequest("No file");
               var safeName = Path.GetRandomFileName() + ".xlsx";
               var path = Path.Combine(_storage.UploadFolder, safeName);
               await using (var fs = System.IO.File.Create(path))
               {
                    await file.CopyToAsync(fs);
               }


               var emails = ExtractEmailsFromExcel(path);
               if (!emails.Any()) return BadRequest("No emails found in first column");


               await _queue.EnqueueMany(emails);
               return Ok(new { count = emails.Count(), path });
          }


          private IEnumerable<string> ExtractEmailsFromExcel(string path)
          {
               using var wb = new XLWorkbook(path);
               var ws = wb.Worksheets.First();
               var result = new List<string>();
               var row = 1;
               while (true)
               {
                    var cell = ws.Cell(row, 1);
                    if (cell.IsEmpty()) { row++; if (row > 100000) break; else continue; }
                    var txt = cell.GetString().Trim();
                    if (IsValidEmail(txt)) result.Add(txt);
                    row++;
                    if (row > 100000) break;
               }
               return result.Distinct(StringComparer.OrdinalIgnoreCase);
          }


          private static bool IsValidEmail(string email)
          {
               try
               {
                    var m = new System.Net.Mail.MailAddress(email);
                    return true;
               }
               catch { return false; }
          }
     }
}