using Microsoft.Playwright;
using AutomationAPI.Models;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Text;
using System.Globalization;

namespace AutomationAPI.Services;

public class AutomationService : IAutomationService
{
    private readonly IConfiguration _configuration;

    // Inyectamos la configuración para no tener contraseñas en el código
    public AutomationService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task ExecuteWebAutomation(Registro registro)
    {
        // Extraemos los datos del appsettings.json
        string baseUrl = _configuration["SicConfig:BaseUrl"] ?? "";
        string user = _configuration["SicConfig:User"] ?? "";
        string password = _configuration["SicConfig:Password"] ?? "";

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false, 
            SlowMo = 800 
        });

        var page = await browser.NewPageAsync();

        try 
        {
            // 1. LOGIN (Usando variables de configuración)
            Console.WriteLine("[BOT] Iniciando sesión en el sistema...");
            await page.GotoAsync($"{baseUrl}/index");
            await page.ClickAsync("text=Portal Colaboradores");
            
            await page.FillAsync("#name", user); 
            await page.FillAsync("#password", password);
            await page.ClickAsync("#ingresar");

            // 2. NAVEGACIÓN DIRECTA AL FORMULARIO
            Console.WriteLine("[BOT] Navegando directamente al formulario de creación...");
            await page.GotoAsync($"{baseUrl}/SolicitudGestor/create");
            
            // Esperamos que el campo NIT esté listo
            await page.WaitForSelectorAsync("#nit", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });

            // 3. LLENADO DE DATOS BÁSICOS
            await page.FillAsync("#nit", registro.Nit);
            await page.FillAsync("#empresa", registro.Empresa);
            
            // --- LÓGICA DE CIUDAD SEGURA (Fuzzy Matching) ---
            Console.WriteLine("[BOT] Procesando selección de ciudad...");
            
            var rawOptions = await page.EvaluateAsync<List<OptionData>>(@"() => {
                const select = document.querySelector('#ciudad');
                if (!select) return [];
                return Array.from(select.options)
                            .filter(o => o.value !== '') 
                            .map(o => ({ Text: o.text, Value: o.value }));
            }");

            if (rawOptions != null && rawOptions.Any() && !string.IsNullOrEmpty(registro.Ciudad)) 
            {
                var listaParaComparar = rawOptions.Select(o => (o.Text, o.Value)).ToList();
                string? valorCiudad = EncontrarMejorCiudad(registro.Ciudad, listaParaComparar);
                
                if (!string.IsNullOrEmpty(valorCiudad)) 
                {
                    await page.SelectOptionAsync("#ciudad", valorCiudad);
                    Console.WriteLine($"[BOT] Ciudad seleccionada: {valorCiudad}");
                }
            }

            // 4. RESTO DE CAMPOS
            await page.FillAsync("#contacto", registro.Cliente);
            await page.FillAsync("#celular", registro.Celular);
            await page.FillAsync("#correo", registro.Correo);
            await page.FillAsync("#concepto", registro.Concepto);

            // 5. TIPO DE CLIENTE (Mapeo de IDs)
            string tipoValue = registro.TipoCliente switch
            {
                "Nuevo" => "1",
                "Antiguo" => "2",
                "Fidelizado" => "3",
                "Recuperado" => "4",
                _ => "1"
            };
            await page.SelectOptionAsync("#id_tipo_cliente", tipoValue);

            Console.WriteLine("[BOT] Formulario completado. Esperando revisión manual antes de cerrar.");
            
            // Pausa de 30 segundos para validar que todo esté bien
            await Task.Delay(30000); 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT ERROR FATAL]: {ex.Message}");
            await Task.Delay(10000);
        }
    }

    // --- MÉTODOS DE APOYO PARA TEXTO ---

    private string? EncontrarMejorCiudad(string ciudadCapturada, List<(string Text, string Value)> opciones)
    {
        if (string.IsNullOrEmpty(ciudadCapturada)) return null;

        string entrada = NormalizarTexto(ciudadCapturada);
        
        var coincidencia = opciones
            .Select(o => new { o.Value, Normalizado = NormalizarTexto(o.Text) })
            .OrderByDescending(o => CalcularSimilitud(entrada, o.Normalizado))
            .FirstOrDefault();

        return coincidencia?.Value;
    }

    private string NormalizarTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        
        var normalizedString = texto.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().ToUpper().Trim();
    }

    private double CalcularSimilitud(string source, string target)
    {
        if (source == target) return 1.0;
        if (target.Contains(source) || source.Contains(target)) return 0.8;
        return 0;
    }
}

// Clase para recibir los datos del dropdown desde el navegador
public class OptionData 
{ 
    public string Text { get; set; } = ""; 
    public string Value { get; set; } = ""; 
}