namespace AutomationAPI.Models;

public class TicketSeguimientoResult
{
    public string Ticket { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDIENTE"; // PENDIENTE | EN_PROCESO | COMPLETADO | ERROR
    public string Mensaje { get; set; } = string.Empty;
}

public class SeguimientoJob
{
    public string BatchId { get; set; } = string.Empty;
    public List<string> Tickets { get; set; } = new();
    public string TipoMensaje { get; set; } = string.Empty; // "primer" | "segundo"
    public string EstadoGeneral { get; set; } = "PENDIENTE"; // PENDIENTE | EN_PROCESO | COMPLETADO | ERROR
    public List<TicketSeguimientoResult> Resultados { get; set; } = new();
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
