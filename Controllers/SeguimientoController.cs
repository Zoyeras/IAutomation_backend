using Microsoft.AspNetCore.Mvc;
using AutomationAPI.Services;

namespace AutomationAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SeguimientoController : ControllerBase
{
    private readonly ISeguimientoService _seguimientoService;
    private readonly SeguimientoStore _store;

    public SeguimientoController(ISeguimientoService seguimientoService, SeguimientoStore store)
    {
        _seguimientoService = seguimientoService;
        _store = store;
    }

    // POST /api/seguimiento
    // Body: { "tickets": ["16904", "16905"], "tipoMensaje": "primer" }
    [HttpPost]
    public ActionResult Post([FromBody] SeguimientoRequest request)
    {
        if (request.Tickets == null || request.Tickets.Count == 0)
            return BadRequest(new { message = "Debe incluir al menos un ticket." });

        if (string.IsNullOrWhiteSpace(request.TipoMensaje) ||
            (!request.TipoMensaje.Equals("primer", StringComparison.OrdinalIgnoreCase) &&
             !request.TipoMensaje.Equals("segundo", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "TipoMensaje debe ser 'primer' o 'segundo'." });

        // Normalizar tickets
        var tickets = request.Tickets
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();

        if (tickets.Count == 0)
            return BadRequest(new { message = "Tickets inválidos." });

        var job = _store.CrearJob(tickets, request.TipoMensaje.ToLower());

        Console.WriteLine($"[API-SEG] POST batch={job.BatchId} tickets=[{string.Join(",", tickets)}] tipo={job.TipoMensaje}");

        // Ejecutar en background
        _ = _seguimientoService.EjecutarSeguimientoLote(job);

        return Ok(new { batchId = job.BatchId, tickets = tickets.Count, estado = job.EstadoGeneral });
    }

    // GET /api/seguimiento/{batchId}
    [HttpGet("{batchId}")]
    public ActionResult GetById(string batchId)
    {
        var job = _store.ObtenerJob(batchId);
        if (job == null)
            return NotFound(new { message = "Batch no encontrado." });

        return Ok(new
        {
            batchId = job.BatchId,
            tipoMensaje = job.TipoMensaje,
            estadoGeneral = job.EstadoGeneral,
            resultados = job.Resultados.Select(r => new
            {
                ticket = r.Ticket,
                estado = r.Estado,
                mensaje = r.Mensaje
            })
        });
    }
}

public class SeguimientoRequest
{
    public List<string> Tickets { get; set; } = new();
    public string TipoMensaje { get; set; } = string.Empty;
}
