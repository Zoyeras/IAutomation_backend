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
    public string Concepto { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}