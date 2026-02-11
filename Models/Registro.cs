using System.ComponentModel.DataAnnotations.Schema;

namespace AutomationAPI.Models;

public class Registro
{
    public int Id { get; set; }
    public string Nit { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Ciudad { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string TipoCliente { get; set; } = string.Empty; // Antiguo, Recuperado, Nuevo

    // Nuevo: Medio de contacto solicitado por frontend: "WhatsApp" | "Correo"
    public string MedioContacto { get; set; } = string.Empty;

    // Nuevo: Nombre de asignación (se mapea a value del select #asignado_a en el SIC)
    public string AsignadoA { get; set; } = string.Empty;

    // Nuevo: Línea de venta (texto: "Venta", "Mantenimiento", "Servicio montacargas", "Alquiler montacargas", etc.)
    // El bot la convierte a SOLU|SERV|MONT.
    public string LineaVenta { get; set; } = string.Empty;

    // Nuevo: Ticket generado en SIC (se asigna después de validar en el listado)
    public string Ticket { get; set; } = string.Empty;

    public string Concepto { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // Auditoría / estado para evitar registros "huérfanos" silenciosos
    // Valores sugeridos: PENDIENTE, EN_PROCESO, COMPLETADO, ERROR
    public string EstadoAutomatizacion { get; set; } = "PENDIENTE";

    public string? UltimoErrorAutomatizacion { get; set; }

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}