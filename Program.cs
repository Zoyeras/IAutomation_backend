using AutomationAPI.Data;
using Microsoft.EntityFrameworkCore;
using AutomationAPI.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IAutomationService, AutomationService>();
// 1. Base de Datos
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. CORS para React
builder.Services.AddCors(options => {
    options.AddPolicy("AllowReact", policy => {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddControllers();

// 3. Swagger (Documentación)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configuración del pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowReact");
app.UseAuthorization();
app.MapControllers();

app.Run();