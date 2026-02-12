using AutomationAPI.Data;
using Microsoft.EntityFrameworkCore;
using AutomationAPI.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IAutomationService, AutomationService>();

// DbContextFactory para AutomationService y controladores
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS para React (aunque ahora se sirve desde el mismo origen)
builder.Services.AddCors(options => {
    options.AddPolicy("AllowReact", policy => {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddControllers();

// Swagger (Documentación)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configuración del pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Servir archivos estáticos del frontend
app.UseDefaultFiles();
app.UseStaticFiles();

// Fallback para SPA routing (React Router)
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseFileServer(new FileServerOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath),
        EnableDefaultFiles = true
    });
}

app.UseCors("AllowReact");
app.UseAuthorization();
app.MapControllers();

// Fallback para todas las rutas no API -> servir index.html (SPA)
app.MapFallbackToFile("index.html");

Console.WriteLine("====================================");
Console.WriteLine("AutoHJR360 - Sistema de Automatización");
Console.WriteLine("====================================");
Console.WriteLine($"API disponible en: http://localhost:5016");
Console.WriteLine($"Frontend disponible en: http://localhost:5016");
Console.WriteLine("Presiona Ctrl+C para detener el servidor");
Console.WriteLine("====================================");

app.Run();

