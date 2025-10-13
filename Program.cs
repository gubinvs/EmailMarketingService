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
///

var builder = WebApplication.CreateBuilder(args);

// Настройки конфигурации
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

// Регистрация зависимостей
builder.Services.AddSingleton<IPendingStore, JsonPendingStore>();
builder.Services.AddSingleton<IEmailQueue, InMemoryEmailQueue>();
builder.Services.AddHostedService<BatchEmailSender>();
builder.Services.AddHttpClient();
builder.Services.AddControllers();

// ✅ Добавляем Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true));
});

// --- CORS ---
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("AllowOnlySupply", policy =>
//         policy.WithOrigins("https://supply.encomponent.ru") // Только этот источник
//               .AllowAnyHeader()
//               .AllowAnyMethod());
// });

var app = builder.Build();

// ✅ Включаем Swagger UI (в Dev-режиме)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Email Batch Sender API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    });
}

// Маршрутизация контроллеров
app.MapControllers();

app.UseCors("AllowAll"); // Если все кому не лень запрашивают
// app.UseCors("AllowOnlySupply"); // только конкретному сайту

app.Run();
