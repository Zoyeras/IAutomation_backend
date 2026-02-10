using Microsoft.AspNetCore.Mvc;
using AutomationAPI.Data;
using AutomationAPI.Models;
using AutomationAPI.Services; // <-- IMPORTANTE: Este puente es necesario

namespace AutomationAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAutomationService _automationService; // <-- Solo una vez

    public RegistrosController(AppDbContext context, IAutomationService automationService)
    {
        _context = context;
        _automationService = automationService;
    }

    [HttpPost]
    public async Task<ActionResult<Registro>> Post(Registro registro)
    {
        // 1. Guardamos en la base de datos de Arch Linux
        _context.Registros.Add(registro);
        await _context.SaveChangesAsync();

        // 2. Disparamos Playwright en segundo plano
        // El "_" significa que no esperamos a que el bot termine para responderle a React
        _ = _automationService.ExecuteWebAutomation(registro);

        return Ok(new { message = "Guardado y Automatización iniciada", id = registro.Id });
    }
}