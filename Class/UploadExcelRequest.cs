using Microsoft.AspNetCore.Mvc;

namespace EmailMarketingService
{
    public class UploadExcelRequest
    {
        /// <summary>
        /// Excel-файл с адресами email.
        /// </summary>
        [FromForm(Name = "file")]
        public required IFormFile File { get; set; }
    }

}