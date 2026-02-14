using Microsoft.Playwright;
using AutomationAPI.Models;
using System.Text;
using System.Globalization;
using System.Text.Json;
using AutomationAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace AutomationAPI.Services;

public class AutomationService : IAutomationService
{
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IWebHostEnvironment _environment;

    public AutomationService(IConfiguration configuration, IDbContextFactory<AppDbContext> dbContextFactory, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _dbContextFactory = dbContextFactory;
        _environment = environment;
    }

    public async Task ExecuteWebAutomation(Registro registro)
    {
        // Reintento simple por si el usuario cierra Chromium en medio de la ejecución.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await ExecuteWebAutomationOnce(registro);
                return;
            }
            catch (PlaywrightException ex) when (attempt == 1 &&
                (ex.Message.Contains("Target page", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("TargetClosed", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"[BOT] Playwright target cerrado (probable cierre de ventana). Reintentando 1 vez... Error={ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task ExecuteWebAutomationOnce(Registro registro)
    {
        // Marcamos estado=EN_PROCESO en BD
        await ActualizarEstadoAutomatizacionAsync(registro.Id, "EN_PROCESO", null);

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

            // Teléfono (línea de contacto) - siempre fijo
            await page.FillAsync("#telefono", "3105003030");

            // CIUDAD (dropdown nativo)
            await SeleccionarCiudadAsync(page, registro.Ciudad);

            // Nombre y apellido del contacto (se divide según cantidad de palabras)
            var (nombreContacto, apellidoContacto) = DividirNombreContacto(registro.Cliente);
            await page.FillAsync("#nombre_contacto", nombreContacto);
            if (!string.IsNullOrWhiteSpace(apellidoContacto))
            {
                // Si el campo no existe en el SIC, no rompemos el flujo
                try
                {
                    await page.FillAsync("#apellido_contacto", apellidoContacto);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BOT] No se pudo llenar #apellido_contacto. Error={ex.Message}");
                }
            }

            await page.FillAsync("#celular", registro.Celular ?? string.Empty);
            await page.FillAsync("#email", registro.Correo ?? string.Empty);
            await page.FillAsync("#descripcion", registro.Concepto ?? string.Empty);

            Console.WriteLine($"[BOT] Datos extra: MedioContacto='{registro.MedioContacto}', AsignadoA='{registro.AsignadoA}', LineaVenta='{registro.LineaVenta}'");

            // MEDIO DE CONTACTO (dropdown)
            await SeleccionarMedioContactoAsync(page, registro.MedioContacto);

            // ASIGNADO A (dropdown)
            await SeleccionarAsignadoAAsync(page, registro.AsignadoA);

            // LINEA DE VENTA (dropdown)
            await SeleccionarLineaVentaAsync(page, registro.LineaVenta);

            // TIPO CLIENTE (dropdown nativo)
            await SeleccionarTipoClienteAsync(page, registro.TipoCliente);

            // Guardar y crear ticket
            await GuardarSolicitudAsync(page);

            // Validar en listado y capturar el TICKET real de la fila que corresponde
            var ticketEncontrado = await ValidarEnListadoYObtenerTicketAsync(page, baseUrl, registro.Nit ?? string.Empty, registro.Empresa ?? string.Empty);
            Console.WriteLine($"[BOT] Ticket encontrado en listado: '{ticketEncontrado}'");

            await PersistirTicketAsync(registro.Id, ticketEncontrado);

            await EnviarWhatsAppNotificacionesAsync(registro, ticketEncontrado);

            Console.WriteLine("[BOT] Flujo completado.");

            await ActualizarEstadoAutomatizacionAsync(registro.Id, "COMPLETADO", null);

            await Task.Delay(30000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT ERROR FATAL]: {ex}\n{ex.StackTrace}");

            await ActualizarEstadoAutomatizacionAsync(registro.Id, "ERROR", ex.Message);

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

    private async Task PersistirTicketAsync(int registroId, string ticket)
    {
        if (registroId <= 0) return;
        if (string.IsNullOrWhiteSpace(ticket)) return;

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var entity = await db.Registros.FindAsync(registroId);
            if (entity == null)
            {
                Console.WriteLine($"[BOT] No se encontró Registro Id={registroId} para persistir ticket.");
                return;
            }

            entity.Ticket = ticket.Trim();
            await db.SaveChangesAsync();
            Console.WriteLine($"[BOT] Ticket persistido en BD. Id={registroId}, Ticket={entity.Ticket}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] Error persistiendo ticket en BD. Id={registroId}. Error={ex.Message}");
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

    private static async Task SeleccionarMedioContactoAsync(IPage page, string? medioContacto)
    {
        var medio = (medioContacto ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(medio))
        {
            Console.WriteLine("[BOT] MedioContacto vacío, usando default: WhatsApp");
            medio = "WhatsApp";
        }

        var locator = page.Locator("#medio_contacto");
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        // El SIC puede renderizarlo como <input> (según tus logs) o como <select>.
        var tagName = await locator.EvaluateAsync<string>("el => el.tagName");

        if (string.Equals(tagName, "INPUT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagName, "TEXTAREA", StringComparison.OrdinalIgnoreCase))
        {
            await locator.FillAsync(medio);
            // ayuda extra para disparar listeners del UI
            try { await locator.PressAsync("Enter"); } catch { /* no-op */ }
            try { await locator.EvaluateAsync("el => el.blur()"); } catch { /* no-op */ }
            Console.WriteLine($"[BOT] Medio contacto llenado en input: {medio}");
            return;
        }

        if (string.Equals(tagName, "SELECT", StringComparison.OrdinalIgnoreCase))
        {
            // Preferimos seleccionar por VALUE si el SIC usa valores tipo WHATSAPP/EMAIL.
            var medioNorm = NormalizarTextoStatic(medio);
            var candidateValues = new List<string>();

            if (medioNorm.Contains("CORRE") || medioNorm.Contains("EMAIL"))
            {
                candidateValues.AddRange(new[] { "EMAIL", "CORREO", "email", "correo" });
            }
            else
            {
                candidateValues.AddRange(new[] { "WHATSAPP", "WSP", "whatsapp" });
            }

            foreach (var v in candidateValues)
            {
                try
                {
                    await page.SelectOptionAsync("#medio_contacto", v);
                    Console.WriteLine($"[BOT] Medio contacto seleccionado por value: {v}");
                    return;
                }
                catch
                {
                    // try next
                }
            }

            // Fallback: seleccionar por label
            try
            {
                await page.SelectOptionAsync("#medio_contacto", new[] { new SelectOptionValue { Label = medio } });
                Console.WriteLine($"[BOT] Medio contacto seleccionado por label: {medio}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOT] No se pudo seleccionar medio_contacto ('{medio}'). Error={ex.Message}");
                return;
            }
        }

        // Fallback final: intentar fill por si el tagName no se pudo leer bien.
        try
        {
            await locator.FillAsync(medio);
            Console.WriteLine($"[BOT] Medio contacto llenado (fallback): {medio}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] No se pudo setear medio_contacto ('{medio}') tag={tagName}. Error={ex.Message}");
        }
    }

    private static async Task SeleccionarAsignadoAAsync(IPage page, string? asignadoA)
    {
        var asignado = (asignadoA ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(asignado))
        {
            Console.WriteLine("[BOT] AsignadoA vacío, se omite selección.");
            return;
        }

        var map = new Dictionary<string, string>
        {
            ["LILIANA DEL PILAR"] = "9",
            ["OSCAR FERNANDO"] = "40",
            ["JOSE"] = "34",
            ["JESSICA MARCELA"] = "60",
            ["LAURA ALEJANDRA"] = "78",
            ["GERALDINE"] = "79",
            ["JOSE AUGUSTO"] = "80",
            ["MARIA CAMILA"] = "81",
        };

        await page.WaitForSelectorAsync("#asignado_a", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        var asignadoNorm = NormalizarTextoStatic(asignado);
        if (!map.TryGetValue(asignadoNorm, out var value))
        {
            var best = map.Keys
                .Select(k => new { Key = k, Score = CalcularSimilitudStatic(asignadoNorm, k) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best == null || best.Score <= 0)
            {
                Console.WriteLine($"[BOT] AsignadoA no reconocido: '{asignadoA}'. Se omite selección.");
                return;
            }

            value = map[best.Key];
        }

        try
        {
            await page.SelectOptionAsync("#asignado_a", value);
            Console.WriteLine($"[BOT] AsignadoA seleccionado: {asignado} -> {value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] No se pudo seleccionar asignado_a value={value}. Error={ex.Message}");
        }
    }

    private static async Task SeleccionarLineaVentaAsync(IPage page, string? lineaVenta)
    {
        var linea = (lineaVenta ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(linea))
        {
            Console.WriteLine("[BOT] LineaVenta vacía, se omite selección.");
            return;
        }

        var norm = NormalizarTextoStatic(linea);

        string value = norm switch
        {
            var s when s.Contains("MONTACARG") || s.Contains("ALQUILER") => "MONT",
            var s when s.Contains("MANTEN") => "SERV",
            var s when s.Contains("SERVIC") && s.Contains("MONT") => "MONT",
            var s when s.Contains("VENT") => "SOLU",
            _ => "SOLU"
        };

        await page.WaitForSelectorAsync("#linea_venta", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        try
        {
            await page.SelectOptionAsync("#linea_venta", value);
            Console.WriteLine($"[BOT] Linea venta seleccionada: {linea} -> {value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] No se pudo seleccionar linea_venta value={value}. Error={ex.Message}");
        }
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

    private static (string Nombre, string Apellido) DividirNombreContacto(string? cliente)
    {
        var raw = (cliente ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return (string.Empty, string.Empty);

        // Divide por espacios múltiples y elimina vacíos
        var parts = raw
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length <= 1)
        {
            return (raw, string.Empty);
        }

        // Reglas pedidas:
        // 2 palabras: NOMBRE APELLIDO
        // 3 palabras: 1 nombre + 2 apellidos
        // 4 palabras: 2 nombres + 2 apellidos
        // >4: dejamos primeras 2 como nombre y el resto como apellidos (fallback)
        return parts.Length switch
        {
            2 => (parts[0], parts[1]),
            3 => (parts[0], string.Join(' ', parts.Skip(1))),
            4 => (string.Join(' ', parts.Take(2)), string.Join(' ', parts.Skip(2))),
            _ => (string.Join(' ', parts.Take(2)), string.Join(' ', parts.Skip(2)))
        };
    }

    private static async Task GuardarSolicitudAsync(IPage page)
    {
        // El botón/acción de guardar crea el ticket y luego redirige al listado.
        // Esperamos que exista y esté clickable.
        await page.WaitForSelectorAsync("#guardar_solicitudGestor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });

        Console.WriteLine("[BOT] Guardando solicitud (#guardar_solicitudGestor)...");

        // A veces el click dispara navegación o postback.
        try
        {
            await page.ClickAsync("#guardar_solicitudGestor");
        }
        catch
        {
            // fallback: click vía locator
            await page.Locator("#guardar_solicitudGestor").ClickAsync();
        }

        // Espera corta a que el sistema procese/redirect.
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 60000 });
    }

    private static async Task<string> ValidarEnListadoYObtenerTicketAsync(IPage page, string baseUrl, string nitBackend, string empresaBackend)
    {
        var nit = (nitBackend ?? string.Empty).Trim();
        var empresa = (empresaBackend ?? string.Empty).Trim();

        await page.GotoAsync($"{baseUrl}/SolicitudGestor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

        var table = page.Locator("table.responsive-table");
        await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });

        var rowsLocator = page.Locator("table.responsive-table tbody tr");

        async Task<int> EsperarFilasAsync(int timeoutMs)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                var c = await rowsLocator.CountAsync();
                if (c > 0) return c;
                await page.WaitForTimeoutAsync(250);
            }
            return await rowsLocator.CountAsync();
        }

        // Espera inicial a que aparezca la tabla con filas (cuando el servidor tarda)
        var count = await EsperarFilasAsync(8000);
        if (count == 0)
        {
            // reload 1 vez si llegó vacía
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            count = await EsperarFilasAsync(8000);
        }

        // Helper para normalizar
        static string T(string? s) => (s ?? string.Empty).Trim();
        static string N(string? s) => NormalizarTextoStatic((s ?? string.Empty).Trim());

        async Task<string?> BuscarEnFilasAsync()
        {
            var c = await rowsLocator.CountAsync();
            if (c == 0) return null;

            for (var i = 0; i < c; i++)
            {
                var row = rowsLocator.Nth(i);
                var cells = row.Locator("td");
                var cellCount = await cells.CountAsync();
                if (cellCount < 5) continue;

                var ticket = T(await cells.Nth(0).InnerTextAsync());
                var nitRow = T(await cells.Nth(3).InnerTextAsync());
                var empresaRow = T(await cells.Nth(4).InnerTextAsync());

                // Match 1: NIT exacto
                if (!string.IsNullOrWhiteSpace(nit) && string.Equals(nitRow, nit, StringComparison.OrdinalIgnoreCase))
                    return ticket;

                // Match 2: Empresa exacta normalizada
                if (!string.IsNullOrWhiteSpace(empresa) && N(empresaRow) == N(empresa))
                    return ticket;

                // Match 3 (tolerante): empresa contiene / parcial
                if (!string.IsNullOrWhiteSpace(empresa) && (N(empresaRow).Contains(N(empresa)) || N(empresa).Contains(N(empresaRow))))
                    return ticket;
            }

            return null;
        }

        // 1) Intento directo sin filtro
        var match = await BuscarEnFilasAsync();
        if (!string.IsNullOrWhiteSpace(match))
        {
            Console.WriteLine($"[BOT] Ticket encontrado en listado (sin filtro): {match}");
            return match;
        }

        // 2) Intento por filtro (NIT o Empresa)
        var q = !string.IsNullOrWhiteSpace(nit) ? nit : empresa;
        if (!string.IsNullOrWhiteSpace(q))
        {
            Console.WriteLine($"[BOT] Sin match directo. Intentando búsqueda por filtro: '{q}'");

            // En el HTML real el input es id="nombre" name="buscar"
            var searchInput = page.Locator("input#nombre[name='buscar']");
            try
            {
                await searchInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

                // Limpiamos y escribimos
                await searchInput.FillAsync(string.Empty);
                await searchInput.FillAsync(q);

                // El form es GET, el submit actualiza listado.
                // Preferimos "Enter" sobre click para evitar issues de click intercept.
                await searchInput.PressAsync("Enter");

                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 60000 });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOT] Falló búsqueda por filtro (input#nombre). Error={ex.Message}");
            }

            // Esperamos filas luego del filtro
            await EsperarFilasAsync(12000);

            match = await BuscarEnFilasAsync();
            if (!string.IsNullOrWhiteSpace(match))
            {
                Console.WriteLine($"[BOT] Ticket encontrado en listado (con filtro): {match}");
                return match;
            }
        }

        // 3) Fallback seguro: primera fila (lo más reciente). Evita reventar el flujo con tickets reales.
        // Dejamos evidencia en logs para auditoría.
        count = await rowsLocator.CountAsync();
        if (count > 0)
        {
            var first = rowsLocator.Nth(0).Locator("td").Nth(0);
            var ticketFallback = T(await first.InnerTextAsync());

            Console.WriteLine($"[BOT][WARN] No hubo coincidencia exacta en listado. NIT='{nit}', Empresa='{empresa}'. " +
                              $"Usando ticket de primera fila como fallback: {ticketFallback}");

            return ticketFallback;
        }

        // Si de verdad no hay nada, ahí sí es un error real.
        throw new InvalidOperationException("No se encontraron filas en el listado /SolicitudGestor (después de filtro/reload)");
    }

    private static string ConstruirMensajeWhatsApp(string ticket, Registro r)
    {
        // Teléfono de contacto: celular del cliente
        var telefonoContacto = (r.Celular ?? string.Empty).Trim();

        var nit = (r.Nit ?? string.Empty).Trim();
        var razon = (r.Empresa ?? string.Empty).Trim();
        var nombreContacto = (r.Cliente ?? string.Empty).Trim();
        var ciudad = (r.Ciudad ?? string.Empty).Trim();
        var obs = (r.Concepto ?? string.Empty).Trim();

        // WhatsApp soporta saltos de línea \n
        return "Buen día, asignación de" + "\n" +
               $"TICKET N° {ticket.Trim()}" + "\n" +
               $"NIT: {nit}" + "\n" +
               $"RAZÓN SOCIAL: {razon}" + "\n" +
               $"NOMBRE DE CONTACTO: {nombreContacto}" + "\n" +
               $"TELÉFONO DE CONTACTO: {telefonoContacto}" + "\n" +
               $"CIUDAD: {ciudad}" + "\n" +
               $"OBSERVACIÓN: {obs}";
    }

    private static string ConstruirMensajePersonalizadoCliente(string nombreCliente)
    {
        var nombre = (nombreCliente ?? string.Empty).Trim();
        
        // Determinar si es Sr. o Sra. (análisis muy básico del primer nombre)
        var primerNombre = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var saludo = "sr/sra";
        
        // Heurística simple: nombres típicamente femeninos en español
        var nombresF = new[] { "MARIA", "PAOLA", "CAROLINA", "ALEJANDRA", "JESSICA", "LAURA", "Ana", "ROSA", "DIANA", "SOFIA" };
        var nombreNormalizado = primerNombre.ToUpper();
        
        if (nombresF.Any(n => nombreNormalizado.StartsWith(n)))
        {
            saludo = "Sra";
        }
        else if (nombreNormalizado.Length > 0)
        {
            saludo = "Sr";
        }

        return $"Muchas gracias por la información {saludo} {nombre}, la solicitud acaba de ser compartida con un asesor el cual le contactara pronto, tenga excelente dia, cualquier duda estoy atento";
    }

    private async Task EnviarWhatsAppNotificacionesAsync(Registro registro, string ticket)
    {
        var waGroupName = _configuration["WhatsAppConfig:GroupName"] ?? "";
        var waTo = _configuration["WhatsAppConfig:SendTo"] ?? "";
        var waBaseUrl = _configuration["WhatsAppConfig:BaseUrl"] ?? "https://web.whatsapp.com";
        var storageStatePathCfg = _configuration["WhatsAppConfig:StorageStatePath"] ?? "whatsapp.storage.json";
        var ensureLoginTimeoutSeconds = int.TryParse(_configuration["WhatsAppConfig:EnsureLoginTimeoutSeconds"], out var t) ? t : 90;

        // Usar ContentRootPath de IWebHostEnvironment (donde está el ejecutable)
        var contentRoot = _environment.ContentRootPath;
        var storageStatePath = Path.IsPathRooted(storageStatePathCfg)
            ? storageStatePathCfg
            : Path.Combine(contentRoot, storageStatePathCfg);

        Directory.CreateDirectory(Path.GetDirectoryName(storageStatePath) ?? contentRoot);
        Console.WriteLine($"[WA] Storage path: {storageStatePath}");
        Console.WriteLine($"[WA] Storage exists: {File.Exists(storageStatePath)}");

        using var playwright = await Playwright.CreateAsync();

        if (File.Exists(storageStatePath))
        {
            // Validar que el archivo sea JSON válido
            try
            {
                var fileContent = await File.ReadAllTextAsync(storageStatePath);
                
                // Si está vacío o es muy pequeño (< 50 bytes), probablemente está corrupto
                if (string.IsNullOrWhiteSpace(fileContent) || fileContent.Length < 50)
                {
                    Console.WriteLine($"[WA] ⚠️ Storage corrupto o vacío ({fileContent.Length} bytes). Borrando...");
                    File.Delete(storageStatePath);
                }
                else
                {
                    // Intentar parsear como JSON
                    JsonDocument.Parse(fileContent);
                    Console.WriteLine($"[WA] ✓ Storage válido ({fileContent.Length} bytes)");
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[WA] ⚠️ Storage JSON inválido: {ex.Message}. Borrando...");
                File.Delete(storageStatePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WA] ⚠️ Error validando storage: {ex.Message}. Continuando sin sesión...");
            }
        }

        var waProfileDir = Path.Combine(contentRoot, "wa-profile");
        Directory.CreateDirectory(waProfileDir);
        Console.WriteLine($"[WA] Profile dir: {waProfileDir}");

        await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
            waProfileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                SlowMo = 200
            }
        );

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

        await page.GotoAsync(waBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        try { await page.BringToFrontAsync(); } catch { /* no-op */ }

        await AsegurarLoginWhatsAppAsync(page, context, storageStatePath, TimeSpan.FromSeconds(ensureLoginTimeoutSeconds));
        try { await page.BringToFrontAsync(); } catch { /* no-op */ }

        // Envío principal (grupo o número)
        try
        {
            if (!string.IsNullOrWhiteSpace(waGroupName))
            {
                var mensaje = ConstruirMensajeWhatsApp(ticket, registro);
                await EnviarWhatsAppWebAGrupoEnPaginaAsync(page, context, storageStatePath, waGroupName, mensaje);
            }
            else if (!string.IsNullOrWhiteSpace(waTo))
            {
                var mensaje = ConstruirMensajeWhatsApp(ticket, registro);
                await EnviarWhatsAppWebEnPaginaAsync(page, context, storageStatePath, waTo, mensaje);
            }
            else
            {
                Console.WriteLine("[BOT] WhatsAppConfig sin destino (ni GroupName ni SendTo). Se omite envío WhatsApp.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] Error enviando WhatsApp principal: {ex.Message}");
        }

        // Segundo envío: mensaje personalizado al celular del cliente
        try
        {
            var celularCliente = registro.Celular ?? "";
            var nombreCliente = registro.Cliente ?? "";

            if (!string.IsNullOrWhiteSpace(celularCliente) && !string.IsNullOrWhiteSpace(nombreCliente))
            {
                var mensajePersonalizado = ConstruirMensajePersonalizadoCliente(nombreCliente);
                await EnviarWhatsAppWebAContactoEnPaginaAsync(page, context, storageStatePath, celularCliente, nombreCliente, mensajePersonalizado);
            }
            else
            {
                Console.WriteLine("[BOT] Celular o nombre del cliente vacíos. Se omite envío personalizado al contacto.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] Error enviando WhatsApp personalizado al cliente: {ex.Message}");
        }
    }

    private async Task EnviarWhatsAppWebEnPaginaAsync(IPage page, IBrowserContext context, string storageStatePath, string sendToE164, string message)
    {
        // Forzamos flujo web (evita whatsapp://send en Linux)
        var to = NormalizarTelefonoE164(sendToE164);
        var encoded = Uri.EscapeDataString(message);
        var waWebSendUrl = $"https://web.whatsapp.com/send?phone={to}&text={encoded}";

        Console.WriteLine($"[WA] Abriendo chat (web): {waWebSendUrl}");
        await page.GotoAsync(waWebSendUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        
        // Esperamos extra para que WhatsApp cargue completamente (puede haber redirecciones)
        Console.WriteLine("[WA] Esperando 5 segundos para que WhatsApp cargue completamente (puede recargar)...");
        await Task.Delay(5000);

        // Ya en WhatsApp Web, esperamos el composer (input de mensaje)
        // Intentamos múltiples selectores en orden de preferencia
        var composerSelectors = new[]
        {
            "[contenteditable='true']",                        // Más genérico, funciona mejor
            "div[contenteditable='true']",                     // Div editable
            "div[contenteditable='true'][data-tab]",           // Con data-tab
            "div[contenteditable='true'][role='textbox']",     // Con role
            "[role='textbox']",                                // Solo role
            "input[type='text'][placeholder*='message' i]",    // Input de texto
            ".selectable-text.copyable-text"                   // Clases de WhatsApp
        };

        ILocator? composer = null;
        foreach (var selector in composerSelectors)
        {
            try
            {
                var loc = page.Locator(selector);
                var count = await loc.CountAsync();
                Console.WriteLine($"[WA] Selector '{selector}' encontró {count} elemento(s)");
                
                if (count > 0)
                {
                    // Tomamos el último visible (suele ser el input principal)
                    composer = loc.Last;
                    Console.WriteLine($"[WA] ✓ Usando compositor con selector: {selector}");
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WA] Error al intentar selector '{selector}': {ex.Message}");
            }
        }

        if (composer == null)
        {
            Console.WriteLine("[WA] ❌ ERROR: No se encontró el input de mensaje en WhatsApp Web");
            
            // Capturar evidencia para debugging
            try
            {
                var artifactsDir = Path.Combine(AppContext.BaseDirectory, "bot-artifacts");
                Directory.CreateDirectory(artifactsDir);
                var timestamp = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                
                var screenshotPath = Path.Combine(artifactsDir, $"wa-error-{timestamp}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                Console.WriteLine($"[WA] Screenshot guardado: {screenshotPath}");
                
                var htmlPath = Path.Combine(artifactsDir, $"wa-error-{timestamp}.html");
                var html = await page.ContentAsync();
                await File.WriteAllTextAsync(htmlPath, html);
                Console.WriteLine($"[WA] HTML guardado: {htmlPath}");
            }
            catch (Exception exDebug)
            {
                Console.WriteLine($"[WA] Error capturando evidencia: {exDebug.Message}");
            }

            throw new InvalidOperationException("No se encontró el compositor de mensajes en WhatsApp Web");
        }

        try
        {
            await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            Console.WriteLine("[WA] ✓ Compositor visible");
        }
        catch (Exception exWait)
        {
            Console.WriteLine($"[WA] ⚠️ Compositor encontrado pero no visible: {exWait.Message}");
            // Continuamos de todas formas
        }

        // Escribimos el mensaje conservando saltos de línea:
        Console.WriteLine("[WA] Haciendo click en el compositor...");
        try
        {
            await composer.ClickAsync();
            Console.WriteLine("[WA] ✓ Click ejecutado");
        }
        catch (Exception exClick)
        {
            Console.WriteLine($"[WA] ⚠️ Click falló: {exClick.Message}");
        }
        
        // ⚠️ IMPORTANTE: WhatsApp Web pre-rellena el input cuando usamos la URL con parámetro text
        // Verificamos si ya hay contenido (para evitar duplicación)
        Console.WriteLine("[WA] Verificando si el mensaje ya está pre-rellenado...");
        
        // Para divs contenteditable, no podemos usar InputValueAsync(). Usamos JavaScript.
        var hasContent = await page.EvaluateAsync<bool>(@"() => {
            const composer = document.querySelector('[contenteditable=""true""]');
            if (!composer) return false;
            const text = composer.innerText || composer.textContent || '';
            return text.trim().length > 0;
        }");
        
        if (hasContent)
        {
            Console.WriteLine("[WA] ✓ El mensaje YA ESTÁ PRE-RELLENADO en el input");
            Console.WriteLine("[WA] ⚠️  SALTANDO ESCRITURA (para evitar duplicación)");
        }
        else
        {
            // Si está vacío, escribimos el mensaje - PERO ESTO NO SUCEDE EN REALIDAD
            // WhatsApp siempre pre-rellena con la URL, así que saltamos directamente a enviar
            Console.WriteLine("[WA] SALTANDO ESCRITURA - Solo enviando...");
        }
        
        await Task.Delay(500);

        // Enviar: Enter
        var messageSent = false;
        try
        {
            Console.WriteLine("[WA] Intentando enviar con Enter...");
            await composer.PressAsync("Enter");
            Console.WriteLine("[WA] ✓ Mensaje enviado (Enter presionado). ");
            messageSent = true;
            await Task.Delay(2000); // Espera a que se procese el envío
        }
        catch (Exception exEnter)
        {
            Console.WriteLine($"[WA] ⚠️ Enter falló: {exEnter.GetType().Name}: {exEnter.Message}");
            
            // Fallback: intentar click en botón enviar
            try
            {
                Console.WriteLine("[WA] Buscando botón de enviar como alternativa...");
                var sendButtonSelectors = new[]
                {
                    "button[aria-label='Enviar']",
                    "button[aria-label='Send']",
                    "button[aria-label*='send' i]",
                    "button[aria-label*='enviar' i]",
                    "button span:has-text('Enviar')",
                    "button[data-testid='send']",
                    "div[role='button'][aria-label*='send' i]",
                    "svg[class*='send']"
                };

                ILocator? sendButton = null;
                foreach (var btnSel in sendButtonSelectors)
                {
                    try
                    {
                        var btn = page.Locator(btnSel);
                        var btnCount = await btn.CountAsync();
                        if (btnCount > 0)
                        {
                            sendButton = btn.First;
                            Console.WriteLine($"[WA] ✓ Encontrado botón con selector: {btnSel} ({btnCount} elemento(s))");
                            break;
                        }
                    }
                    catch { /* ignore */ }
                }

                if (sendButton != null)
                {
                    try
                    {
                        await sendButton.ScrollIntoViewIfNeededAsync();
                        await sendButton.ClickAsync();
                        Console.WriteLine("[WA] ✓ Mensaje enviado (click en botón). ");
                        messageSent = true;
                        await Task.Delay(2000);
                    }
                    catch (Exception exBtnClick)
                    {
                        Console.WriteLine($"[WA] ❌ Error al hacer click en botón: {exBtnClick.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[WA] ❌ No se encontró botón de enviar en ningún selector.");
                    
                    // Último intento: buscar cualquier botón cercano al compositor
                    try
                    {
                        var buttons = await page.Locator("button").AllAsync();
                        Console.WriteLine($"[WA] Encontrados {buttons.Count} botones en la página. Buscando visible...");
                        
                        foreach (var btn in buttons)
                        {
                            var isVisible = await btn.IsVisibleAsync();
                            if (isVisible)
                            {
                                var ariaLabel = await btn.GetAttributeAsync("aria-label");
                                Console.WriteLine($"[WA]   Botón visible: {ariaLabel ?? "(sin label)"}");
                            }
                        }
                    }
                    catch { /* no-op */ }
                }
            }
            catch (Exception exBtn)
            {
                Console.WriteLine($"[WA] ❌ Falló búsqueda de botón. Error={exBtn.Message}");
            }
        }

        if (!messageSent)
        {
            Console.WriteLine("[WA] ⚠️ ADVERTENCIA: El mensaje posiblemente no se envió. El bot no pudo confirmar.");
        }
        else
        {
            Console.WriteLine("[WA] ✓ Mensaje enviado exitosamente");
        }

        Console.WriteLine("[WA] Esperando 3 segundos antes de guardar sesión...");
        await Task.Delay(3000);

        // Guardamos sesión nuevamente por si cambió.
        await GuardarSesionWhatsAppAsync(context, storageStatePath);
    }

    private async Task EnviarWhatsAppWebAGrupoEnPaginaAsync(IPage page, IBrowserContext context, string storageStatePath, string groupName, string message)
    {
        var name = (groupName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("WhatsAppConfig:GroupName vacío");

        // Buscar chat por nombre
        var searchBoxCandidates = new[]
        {
            "div[contenteditable='true'][data-tab='3']",
            "div[contenteditable='true'][data-tab='4']",
            "div[contenteditable='true'][data-tab='5']",
            "div[contenteditable='true'][role='textbox']"
        };

        ILocator? searchBox = null;
        foreach (var sel in searchBoxCandidates)
        {
            var loc = page.Locator(sel).First;
            if (await loc.CountAsync() > 0)
            {
                searchBox = loc;
                break;
            }
        }

        if (searchBox == null)
            throw new InvalidOperationException("No se encontró el buscador de chats en WhatsApp Web.");

        await searchBox.ClickAsync();
        // limpiar (best-effort)
        try { await searchBox.PressAsync("Control+A"); await searchBox.PressAsync("Backspace"); } catch { /* no-op */ }

        await searchBox.PressSequentiallyAsync(name, new LocatorPressSequentiallyOptions { Delay = 10 });

        await Task.Delay(1000);

        var exactTitle = page.Locator($"span[title='{name}']").First;
        if (await exactTitle.CountAsync() > 0)
        {
            await exactTitle.ClickAsync();
        }
        else
        {
            var firstResult = page.Locator("div[role='listbox'] [role='option']").First;
            if (await firstResult.CountAsync() > 0)
            {
                await firstResult.ClickAsync();
            }
            else
            {
                await searchBox.PressAsync("ArrowDown");
                await Task.Delay(500);
                await searchBox.PressAsync("Enter");
            }
        }

        await Task.Delay(3000);

        // Composer y envío por renglones
        var composerSelectors = new[]
        {
            "div[contenteditable='true'][data-tab]",
            "div[contenteditable='true'][role='textbox']",
            "[contenteditable='true']"
        };

        ILocator? composer = null;
        foreach (var selector in composerSelectors)
        {
            var loc = page.Locator(selector);
            if (await loc.CountAsync() > 0)
            {
                composer = loc.Last;
                break;
            }
        }

        if (composer == null)
            throw new InvalidOperationException("No se encontró el input de mensaje (composer)");

        await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await composer.ClickAsync();

        var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!string.IsNullOrEmpty(line))
                await composer.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 10 });
            if (i < lines.Length - 1)
                await composer.PressAsync("Shift+Enter");
        }

        await composer.PressAsync("Enter");
        await Task.Delay(5000);
        Console.WriteLine($"[WA] Mensaje enviado al grupo/chat: {name}");

        await GuardarSesionWhatsAppAsync(context, storageStatePath);
    }

    private async Task EnviarWhatsAppWebAContactoEnPaginaAsync(IPage page, IBrowserContext context, string storageStatePath, string celular, string nombreCliente, string message)
    {
        var celularNormalizado = (celular ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(celularNormalizado))
            throw new InvalidOperationException("Celular del cliente vacío");

        // Buscar chat por celular usando la barra de búsqueda
        var searchBoxCandidates = new[]
        {
            "div[contenteditable='true'][data-tab='3']",
            "div[contenteditable='true'][data-tab='4']",
            "div[contenteditable='true'][data-tab='5']",
            "div[contenteditable='true'][role='textbox']"
        };

        ILocator? searchBox = null;
        foreach (var sel in searchBoxCandidates)
        {
            var loc = page.Locator(sel).First;
            if (await loc.CountAsync() > 0)
            {
                searchBox = loc;
                break;
            }
        }

        if (searchBox == null)
            throw new InvalidOperationException("No se encontró el buscador de chats en WhatsApp Web.");

        Console.WriteLine($"[WA] Buscando contacto: {celularNormalizado}");
        
        await searchBox.ClickAsync();
        await Task.Delay(500);
        
        // Limpiar el buscador
        try { await searchBox.PressAsync("Control+A"); await searchBox.PressAsync("Delete"); } catch { /* no-op */ }
        await Task.Delay(300);
        
        // Escribir el celular
        await searchBox.PressSequentiallyAsync(celularNormalizado, new LocatorPressSequentiallyOptions { Delay = 50 });
        
        // Esperar a que los resultados aparezcan
        Console.WriteLine("[WA] Esperando resultados de búsqueda...");
        await Task.Delay(2000);

        // Presionar ArrowDown y Enter para seleccionar el primer resultado
        Console.WriteLine("[WA] Presionando ArrowDown para seleccionar primer resultado...");
        await searchBox.PressAsync("ArrowDown");
        await Task.Delay(500);
        
        Console.WriteLine("[WA] Presionando Enter para abrir el chat...");
        await searchBox.PressAsync("Enter");
        
        // Esperar a que el chat se cargue completamente
        Console.WriteLine("[WA] Esperando a que el chat se cargue...");
        await Task.Delay(3000);

        // Encontrar el compositor y escribir mensaje por líneas
        var composerSelectors = new[]
        {
            "div[contenteditable='true'][data-tab]",
            "div[contenteditable='true'][role='textbox']",
            "[contenteditable='true']"
        };

        ILocator? composer = null;
        foreach (var selector in composerSelectors)
        {
            var loc = page.Locator(selector);
            if (await loc.CountAsync() > 0)
            {
                composer = loc.Last;
                break;
            }
        }

        if (composer == null)
            throw new InvalidOperationException("No se encontró el input de mensaje (composer)");

        await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await composer.ClickAsync();

        var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!string.IsNullOrEmpty(line))
                await composer.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 10 });
            if (i < lines.Length - 1)
                await composer.PressAsync("Shift+Enter");
        }

        await composer.PressAsync("Enter");
        await Task.Delay(5000);
        Console.WriteLine($"[WA] Mensaje enviado al contacto: {nombreCliente} ({celularNormalizado})");

        await GuardarSesionWhatsAppAsync(context, storageStatePath);
    }

    private static async Task AsegurarLoginWhatsAppAsync(IPage page, IBrowserContext context, string storageStatePath, TimeSpan timeout)
    {
        var searchBox = page.Locator("div[contenteditable='true'][data-tab='3']");

        try
        {
            await searchBox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            Console.WriteLine("[WA] Sesión de WhatsApp OK (ya logueado).");

            // Guardar sesión y ESPERAR confirmación
            await GuardarSesionWhatsAppAsync(context, storageStatePath);
            return;
        }
        catch
        {
            // no-op
        }

        Console.WriteLine($"[WA] No hay sesión de WhatsApp. Escanea el QR en los próximos {timeout.TotalSeconds} segundos...");

        await searchBox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = (float)timeout.TotalMilliseconds });

        // Esperar un poco más para que se estabilice la sesión
        await Task.Delay(2000);
        
        // Guardar sesión y ESPERAR confirmación
        await GuardarSesionWhatsAppAsync(context, storageStatePath);
    }

    private static async Task GuardarSesionWhatsAppAsync(IBrowserContext context, string storageStatePath)
    {
        try
        {
            Console.WriteLine($"[WA] Guardando sesión en: {storageStatePath}");
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = storageStatePath });
            
            // Esperar a que se escriba completamente a disco
            await Task.Delay(1000);
            
            // Validar que se escribió correctamente
            if (File.Exists(storageStatePath))
            {
                var fileInfo = new FileInfo(storageStatePath);
                if (fileInfo.Length > 100)
                {
                    Console.WriteLine($"[WA] ✓ Sesión guardada exitosamente ({fileInfo.Length} bytes)");
                    return;
                }
            }
            
            Console.WriteLine($"[WA] ⚠️ Archivo de sesión no se escribió correctamente o está vacío");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WA] ❌ Error guardando sesión: {ex.Message}");
        }
    }

    private static string NormalizarTelefonoE164(string raw)
    {
        // WhatsApp wa.me espera número sin '+' ni espacios.
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits;
    }

    private async Task ActualizarEstadoAutomatizacionAsync(int registroId, string estado, string? error)
    {
        if (registroId <= 0) return;

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var entity = await db.Registros.FindAsync(registroId);
            if (entity == null) return;

            entity.EstadoAutomatizacion = estado;
            entity.UltimoErrorAutomatizacion = string.IsNullOrWhiteSpace(error) ? null : error;
            entity.FechaActualizacion = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] Error actualizando estado de automatización. Id={registroId}. Error={ex.Message}");
        }
    }
}

public class OptionData
{
    public string Text { get; set; } = "";
    public string Value { get; set; } = "";
}
