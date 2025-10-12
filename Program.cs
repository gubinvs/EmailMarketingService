using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using EmailMarketingService;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimeKit;
using Newtonsoft.Json;


///
/// реализация сервиса на ASP.NET Core (C#), который:
/// - Принимает Excel-файл по POST-запросу.
/// - Сохраняет файл на диск.
/// - Считывает адреса email из первого столбца.
/// - Отправляет письма по 500 штук с задержкой 24 часа между партиями.
/// - Тело письма берёт из HTML-файла по ссылке.
/// - После отправки всех писем — уведомляет на gmail.com.


var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));


builder.Services.AddSingleton<IPendingStore, JsonPendingStore>();
builder.Services.AddSingleton<IEmailQueue, InMemoryEmailQueue>();
builder.Services.AddHostedService<BatchEmailSender>();
builder.Services.AddHttpClient();
builder.Services.AddControllers();


var app = builder.Build();


app.MapControllers();


app.Run();
