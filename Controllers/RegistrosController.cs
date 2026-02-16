using Microsoft.AspNetCore.Mvc;
using AutomationAPI.Data;
using AutomationAPI.Models;
using AutomationAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace AutomationAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrosController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAutomationService _automationService; // <-- Solo una vez

    public RegistrosController(IDbContextFactory<AppDbContext> dbContextFactory, IAutomationService automationService)
    {
        _dbContextFactory = dbContextFactory;
        _automationService = automationService;
    }

    [HttpPost]
    public async Task<ActionResult<Registro>> Post(Registro registro)
    {
        // Normalización básica para evitar datos inconsistentes (espacios, case, etc.)
        registro.Nit = (registro.Nit ?? string.Empty).Trim();
        registro.Empresa = (registro.Empresa ?? string.Empty).Trim().ToUpperInvariant();
        registro.Ciudad = (registro.Ciudad ?? string.Empty).Trim();
        registro.Cliente = (registro.Cliente ?? string.Empty).Trim().ToUpperInvariant();
        registro.Celular = (registro.Celular ?? string.Empty).Trim().Replace(" ", "");
        registro.Correo = (registro.Correo ?? string.Empty).Trim().ToLowerInvariant();
        registro.TipoCliente = (registro.TipoCliente ?? string.Empty).Trim();
        registro.Concepto = (registro.Concepto ?? string.Empty).Trim().ToUpperInvariant();
        registro.MedioContacto = (registro.MedioContacto ?? string.Empty).Trim();
        registro.AsignadoA = (registro.AsignadoA ?? string.Empty).Trim().ToUpperInvariant();
        registro.LineaVenta = (registro.LineaVenta ?? string.Empty).Trim();

        registro.EstadoAutomatizacion = "PENDIENTE";
        registro.UltimoErrorAutomatizacion = null;
        registro.FechaActualizacion = DateTime.UtcNow;

        Console.WriteLine($"[API] POST Registro: Nit={registro.Nit}, Empresa={registro.Empresa}, MedioContacto='{registro.MedioContacto}', AsignadoA='{registro.AsignadoA}', LineaVenta='{registro.LineaVenta}'");

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        context.Registros.Add(registro);
        await context.SaveChangesAsync();

        // Disparamos Playwright en segundo plano
        _ = _automationService.ExecuteWebAutomation(registro);

        return Ok(new { message = "Guardado y Automatización iniciada", id = registro.Id });
    }
}