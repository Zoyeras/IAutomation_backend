using AutomationAPI.Models;

namespace AutomationAPI.Services;

public interface ISeguimientoService
{
    Task EjecutarSeguimientoLote(SeguimientoJob job);
}
