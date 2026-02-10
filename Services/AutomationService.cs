using Microsoft.Playwright;
using AutomationAPI.Models;
using System.Text;
using System.Globalization;
using System.Text.Json;

namespace AutomationAPI.Services;

public class AutomationService : IAutomationService
{
    private readonly IConfiguration _configuration;

    public AutomationService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task ExecuteWebAutomation(Registro registro)
    {
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

        // Carpeta para evidencias cuando falle un dropdown
        var artifactsDir = Path.Combine(AppContext.BaseDirectory, "bot-artifacts");
        Directory.CreateDirectory(artifactsDir);
        var runId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{registro.Id}";

        try
        {
            Console.WriteLine("[BOT] Iniciando sesión en el sistema...");
            await page.GotoAsync($"{baseUrl}/index", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.ClickAsync("text=Portal Colaboradores");

            await page.WaitForSelectorAsync("#name", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await page.FillAsync("#name", user);
            await page.FillAsync("#password", password);
            await page.ClickAsync("#ingresar");

            Console.WriteLine("[BOT] Navegando al formulario...");
            await page.GotoAsync($"{baseUrl}/SolicitudGestor/create", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

            await page.WaitForSelectorAsync("#nit", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
            await page.WaitForSelectorAsync("#empresa", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

            await page.FillAsync("#nit", registro.Nit ?? string.Empty);
            await page.FillAsync("#empresa", registro.Empresa ?? string.Empty);

            // CIUDAD (dropdown nativo)
            await SeleccionarCiudadAsync(page, registro.Ciudad);

            // Este campo en el HTML es Linea de Contacto: id="telefono"
            // Ajuste: si quieres guardarlo desde tu modelo, hoy no existe propiedad. Por ahora no lo llenamos.
            // await page.FillAsync("#telefono", ...);

            await page.FillAsync("#nombre_contacto", registro.Cliente ?? string.Empty);
            await page.FillAsync("#celular", registro.Celular ?? string.Empty);
            await page.FillAsync("#email", registro.Correo ?? string.Empty);
            await page.FillAsync("#descripcion", registro.Concepto ?? string.Empty);

            // TIPO CLIENTE (dropdown nativo)
            await SeleccionarTipoClienteAsync(page, registro.TipoCliente);

            Console.WriteLine("[BOT] Formulario completado. Esperando revisión manual.");
            await Task.Delay(30000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT ERROR FATAL]: {ex}\n{ex.StackTrace}");

            try
            {
                var screenshotPath = Path.Combine(artifactsDir, $"{runId}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                Console.WriteLine($"[BOT] Screenshot: {screenshotPath}");

                var htmlPath = Path.Combine(artifactsDir, $"{runId}.html");
                await File.WriteAllTextAsync(htmlPath, await page.ContentAsync());
                Console.WriteLine($"[BOT] HTML: {htmlPath}");
            }
            catch
            {
                // no-op
            }

            await Task.Delay(5000);
        }
    }

    private static async Task SeleccionarCiudadAsync(IPage page, string? ciudad)
    {
        if (string.IsNullOrWhiteSpace(ciudad))
        {
            Console.WriteLine("[BOT] Ciudad vacía, se omite selección.");
            return;
        }

        await page.WaitForSelectorAsync("#ciudad", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        // Importante: Playwright .NET a veces falla deserializando directamente a List<OptionData>
        // (NullReference dentro de EvaluateArgumentValueConverter). Lo pedimos como JSON y deserializamos nosotros.
        var optionsJson = await page.EvaluateAsync<string>(@"() => {
            const select = document.querySelector('#ciudad');
            if (!select) return '[]';
            const arr = Array.from(select.options)
                .filter(o => o.value && o.value !== '')
                .map(o => ({ Text: o.text || '', Value: o.value || '' }));
            return JSON.stringify(arr);
        }");

        List<OptionData>? rawOptions = null;
        try
        {
            rawOptions = JsonSerializer.Deserialize<List<OptionData>>(optionsJson);
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"[BOT] Falló parseo de opciones de ciudad. JSON length={optionsJson?.Length}. Error={jsonEx.Message}");
        }

        if (rawOptions is not { Count: > 0 })
        {
            Console.WriteLine("[BOT] No se pudieron leer opciones de ciudad.");
            return;
        }

        var entrada = NormalizarTextoStatic(ciudad);
        var best = rawOptions
            .Select(o => new { o.Value, Norm = NormalizarTextoStatic(o.Text) })
            .OrderByDescending(o => CalcularSimilitudStatic(entrada, o.Norm))
            .FirstOrDefault();

        if (best == null)
        {
            Console.WriteLine($"[BOT] No se encontró coincidencia para ciudad '{ciudad}'");
            return;
        }

        await page.SelectOptionAsync("#ciudad", best.Value);
        Console.WriteLine($"[BOT] Ciudad seleccionada: {best.Value}");
    }

    private static async Task SeleccionarTipoClienteAsync(IPage page, string? tipoCliente)
    {
        var tipo = (tipoCliente ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(tipo))
        {
            Console.WriteLine("[BOT] TipoCliente vacío, usando default: Nuevo");
            tipo = "Nuevo";
        }

        // Según el HTML real:
        // <select id="tipo_cliente"> <option value="A">Antiguo</option> <option value="N">Nuevo</option> <option value="F">Fidelizado</option> <option value="R">Recuperado</option>
        string tipoValue = tipo switch
        {
            "Antiguo" => "A",
            "Nuevo" => "N",
            "Fidelizado" => "F",
            "Recuperado" => "R",
            _ => "N"
        };

        await page.WaitForSelectorAsync("#tipo_cliente", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await page.SelectOptionAsync("#tipo_cliente", tipoValue);
    }

    private static string NormalizarTextoStatic(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";

        var normalizedString = texto.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }

        return stringBuilder.ToString().ToUpper().Trim();
    }

    private static double CalcularSimilitudStatic(string source, string target)
    {
        if (source == target) return 1.0;
        if (target.Contains(source) || source.Contains(target)) return 0.8;
        return 0;
    }
}

public class OptionData
{
    public string Text { get; set; } = "";
    public string Value { get; set; } = "";
}