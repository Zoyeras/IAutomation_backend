using AutomationAPI.Data;
using Microsoft.EntityFrameworkCore;
using AutomationAPI.Services;
using Microsoft.Extensions.FileProviders;
using System.Reflection;

if (args.Any(IsPlaywrightInstallArg))
{
    var exitCode = InstallPlaywrightBrowsers();
    Environment.Exit(exitCode);
}

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

// Aplicar migraciones automaticamente al iniciar (crea tablas si no existen)
try
{
    using var scope = app.Services.CreateScope();
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.Migrate();
    Console.WriteLine("[DB] Migraciones aplicadas correctamente.");
}
catch (Exception ex)
{
    Console.WriteLine($"[DB] Error aplicando migraciones: {ex.Message}");
}

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

static bool IsPlaywrightInstallArg(string arg)
{
    return string.Equals(arg, "--install-playwright", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "--install-browsers", StringComparison.OrdinalIgnoreCase);
}

static int InstallPlaywrightBrowsers()
{
    Console.WriteLine("====================================");
    Console.WriteLine("AutoHJR360 - Instalacion Playwright");
    Console.WriteLine("====================================");

    try
    {
        var programType = Type.GetType("Microsoft.Playwright.Program, Microsoft.Playwright");
        if (programType == null)
        {
            Console.WriteLine("No se encontro Microsoft.Playwright.Program.");
            Console.WriteLine("Verifica que Microsoft.Playwright este incluido en el ejecutable.");
            return 1;
        }

        var mainMethod = programType.GetMethod(
            "Main",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string[]) },
            modifiers: null);

        if (mainMethod == null)
        {
            Console.WriteLine("No se encontro el metodo Main de Playwright.");
            return 1;
        }

        var result = mainMethod.Invoke(null, new object[] { new[] { "install", "chromium" } });
        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
            return result is Task<int> taskInt ? taskInt.Result : 0;
        }

        return result is int exitCode ? exitCode : 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error instalando Playwright: {ex.Message}");
        return 1;
    }
}

