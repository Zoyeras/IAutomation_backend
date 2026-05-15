using Microsoft.Playwright;
using AutomationAPI.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace AutomationAPI.Services;

public class SeguimientoService : ISeguimientoService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly SeguimientoStore _store;

    public SeguimientoService(IConfiguration configuration, IWebHostEnvironment environment, SeguimientoStore store)
    {
        _configuration = configuration;
        _environment = environment;
        _store = store;
    }

    public async Task EjecutarSeguimientoLote(SeguimientoJob job)
    {
        _store.ActualizarEstadoGeneral(job.BatchId, "EN_PROCESO");

        string baseUrl = _configuration["SicConfig:BaseUrl"] ?? "";
        string user = _configuration["SicConfig:User"] ?? "";
        string password = _configuration["SicConfig:Password"] ?? "";

        var waBaseUrl = _configuration["WhatsAppConfig:BaseUrl"] ?? "https://web.whatsapp.com";
        var storageStatePathCfg = _configuration["WhatsAppConfig:StorageStatePath"] ?? "whatsapp.storage.json";
        var ensureLoginTimeoutSeconds = int.TryParse(_configuration["WhatsAppConfig:EnsureLoginTimeoutSeconds"], out var t) ? t : 90;

        var contentRoot = _environment.ContentRootPath;
        var storageStatePath = Path.IsPathRooted(storageStatePathCfg)
            ? storageStatePathCfg
            : Path.Combine(contentRoot, storageStatePathCfg);

        Directory.CreateDirectory(Path.GetDirectoryName(storageStatePath) ?? contentRoot);

        var artifactsDir = Path.Combine(AppContext.BaseDirectory, "bot-artifacts");
        Directory.CreateDirectory(artifactsDir);

        using var playwright = await Playwright.CreateAsync();

        // ── SIC: un solo browser para todos los tickets ──────────────────────
        await using var sicBrowser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 600
        });
        var sicPage = await sicBrowser.NewPageAsync();

        // Login SIC (una sola vez)
        try
        {
            Console.WriteLine("[SEG] Login SIC...");
            await sicPage.GotoAsync($"{baseUrl}/index", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await sicPage.ClickAsync("text=Portal Colaboradores");
            await sicPage.WaitForSelectorAsync("#name", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await sicPage.FillAsync("#name", user);
            await sicPage.FillAsync("#password", password);
            await sicPage.ClickAsync("#ingresar");
            await Task.Delay(2000);
            Console.WriteLine("[SEG] Login SIC OK.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEG] Error login SIC: {ex.Message}");
            foreach (var ticket in job.Tickets)
                _store.ActualizarTicket(job.BatchId, ticket, "ERROR", $"Error login SIC: {ex.Message}");
            _store.ActualizarEstadoGeneral(job.BatchId, "ERROR");
            return;
        }

        // ── WhatsApp: perfil persistente ─────────────────────────────────────
        ValidarStorageState(storageStatePath);
        var waProfileDir = Path.Combine(contentRoot, "wa-profile");
        Directory.CreateDirectory(waProfileDir);

        await using var waContext = await playwright.Chromium.LaunchPersistentContextAsync(
            waProfileDir,
            new BrowserTypeLaunchPersistentContextOptions { Headless = false, SlowMo = 200 }
        );
        var waPage = waContext.Pages.Count > 0 ? waContext.Pages[0] : await waContext.NewPageAsync();

        await waPage.GotoAsync(waBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        try { await waPage.BringToFrontAsync(); } catch { /* no-op */ }
        await AsegurarLoginWhatsAppAsync(waPage, waContext, storageStatePath, TimeSpan.FromSeconds(ensureLoginTimeoutSeconds));

        // ── Procesar cada ticket en orden ─────────────────────────────────────
        foreach (var ticket in job.Tickets)
        {
            _store.ActualizarTicket(job.BatchId, ticket, "EN_PROCESO", "Procesando...");
            Console.WriteLine($"[SEG] Procesando ticket {ticket}...");

            try
            {
                await ProcesarTicketAsync(sicPage, waPage, waContext, storageStatePath, baseUrl, ticket, job.TipoMensaje, artifactsDir);
                _store.ActualizarTicket(job.BatchId, ticket, "COMPLETADO", "Seguimiento enviado correctamente.");
                Console.WriteLine($"[SEG] Ticket {ticket} completado.");
            }
            catch (SeguimientoException ex)
            {
                Console.WriteLine($"[SEG] Ticket {ticket} error controlado: {ex.Message}");
                _store.ActualizarTicket(job.BatchId, ticket, "ERROR", ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEG] Ticket {ticket} error: {ex.Message}");
                _store.ActualizarTicket(job.BatchId, ticket, "ERROR", $"Error inesperado: {ex.Message}");

                // Capturar evidencia
                try
                {
                    var ts = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    await sicPage.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifactsDir, $"seg-{ticket}-{ts}.png"), FullPage = true });
                }
                catch { /* no-op */ }
            }

            // Pequeña pausa entre tickets
            await Task.Delay(1500);
        }

        _store.ActualizarEstadoGeneral(job.BatchId, "COMPLETADO");
        Console.WriteLine("[SEG] Lote completado.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Procesa un solo ticket
    // ─────────────────────────────────────────────────────────────────────────
    private async Task ProcesarTicketAsync(
        IPage sicPage,
        IPage waPage,
        IBrowserContext waContext,
        string storageStatePath,
        string baseUrl,
        string ticket,
        string tipoMensaje,
        string artifactsDir)
    {
        // 1. Buscar el ticket en el listado para obtener la URL real del botón "Ver"
        //    (el número de ticket visible NO es el ID interno de la URL)
        Console.WriteLine($"[SEG] Buscando ticket {ticket} en el listado SIC...");
        var verUrl = await BuscarUrlTicketEnListadoAsync(sicPage, baseUrl, ticket);

        if (string.IsNullOrWhiteSpace(verUrl))
            throw new SeguimientoException($"Ticket {ticket} no encontrado en el listado del SIC.");

        Console.WriteLine($"[SEG] URL del ticket {ticket}: {verUrl}");

        // 2. Navegar a la vista del ticket
        await sicPage.GotoAsync(verUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await Task.Delay(3000);

        var currentUrl = sicPage.Url;
        if (!currentUrl.Contains("SolicitudGestor"))
            throw new SeguimientoException($"No se pudo abrir la vista del ticket {ticket}. URL actual: {currentUrl}");

        // 3. Extraer datos del ticket
        var (contacto, celular, descripcion) = await ExtraerDatosTicketAsync(sicPage, ticket);
        Console.WriteLine($"[SEG] Datos extraídos - Contacto: '{contacto}', Celular: '{celular}', Descripcion: '{descripcion}'");

        // 4. Validar celular
        if (string.IsNullOrWhiteSpace(celular))
            throw new SeguimientoException("SIN_TELEFONO: El ticket no tiene número de celular/teléfono.");

        // 5. Enviar WhatsApp
        var mensaje = ConstruirMensajeSeguimiento(contacto, descripcion);
        Console.WriteLine($"[SEG] Enviando WhatsApp a {celular}...");
        await EnviarWhatsAppContactoAsync(waPage, waContext, storageStatePath, celular, mensaje);

        // 6. Volver al SIC y agregar seguimiento
        Console.WriteLine($"[SEG] Volviendo al SIC para agregar seguimiento en ticket {ticket}...");
        try { await sicPage.BringToFrontAsync(); } catch { /* no-op */ }

        await sicPage.GotoAsync(verUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await Task.Delay(3000);

        // 7. Agregar seguimiento en el SIC
        await AgregarSeguimientoEnSicAsync(sicPage, tipoMensaje, descripcion);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Buscar la URL real del ticket en el listado (igual que el flujo existente)
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task<string> BuscarUrlTicketEnListadoAsync(IPage page, string baseUrl, string ticket)
    {
        // Ir al listado
        await page.GotoAsync($"{baseUrl}/SolicitudGestor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await Task.Delay(1500);

        // Aplicar filtro de búsqueda con el número de ticket
        var searchInput = page.Locator("input#nombre[name='buscar']");
        try
        {
            await searchInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await searchInput.FillAsync(ticket);
            await searchInput.PressAsync("Enter");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 60000 });
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEG] Falló el filtro de búsqueda en listado: {ex.Message}");
        }

        // Recorrer filas buscando el ticket
        var rows = page.Locator("table.responsive-table tbody tr");
        var rowCount = await rows.CountAsync();

        for (int i = 0; i < rowCount; i++)
        {
            var cells = rows.Nth(i).Locator("td");
            var cellCount = await cells.CountAsync();
            if (cellCount < 3) continue;

            // El número de ticket está en la columna índice 1 (después de la celda control)
            var ticketCell = (await cells.Nth(1).InnerTextAsync()).Trim();
            if (ticketCell != ticket) continue;

            // Encontrado: extraer href del botón "Ver"
            try
            {
                var actionCell = cells.Nth(cellCount - 1);
                var verLink = actionCell.Locator("a[data-original-title='Ver'], a[title='Ver'], a[href*='/SolicitudGestor/']").First;
                if (await verLink.CountAsync() > 0)
                {
                    var href = (await verLink.GetAttributeAsync("href") ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(href))
                        return href;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEG] Error extrayendo href del botón Ver en fila {i}: {ex.Message}");
            }
        }

        return string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extraer datos del ticket desde la vista SIC
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task<(string contacto, string celular, string descripcion)> ExtraerDatosTicketAsync(IPage page, string ticket)
    {
        // Los datos están en <section> → <label> → <br> → texto siguiente
        // Usamos JS para extraer el texto del nodo de texto que sigue al <br>
        var contacto = await ExtraerTextoSeccionAsync(page, "contacto");
        var celular = await ExtraerTextoSeccionAsync(page, "celular");
        var descripcion = await ExtraerTextoSeccionAsync(page, "descripcion");
        var empresa = await ExtraerTextoSeccionAsync(page, "empresa");

        // Fallback: si no hay contacto, usar empresa/razón social
        if (string.IsNullOrWhiteSpace(contacto))
        {
            Console.WriteLine($"[SEG] Contacto vacío en ticket {ticket}. Usando empresa como fallback: '{empresa}'");
            contacto = empresa;
        }

        return (contacto.Trim(), celular.Trim(), descripcion.Trim());
    }

    private static async Task<string> ExtraerTextoSeccionAsync(IPage page, string labelFor)
    {
        try
        {
            // Busca la sección que contiene el label con el for indicado, y extrae el texto después del <br>
            var result = await page.EvaluateAsync<string>($@"() => {{
                const label = document.querySelector('label[for=""{labelFor}""]');
                if (!label) return '';
                const section = label.closest('section');
                if (!section) return '';
                // El texto del campo está en nodos de texto después del <br>
                let text = '';
                let foundBr = false;
                for (const node of section.childNodes) {{
                    if (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'BR') {{
                        foundBr = true;
                        continue;
                    }}
                    if (foundBr && node.nodeType === Node.TEXT_NODE) {{
                        text += node.textContent;
                    }}
                    // Para spans (como nombre+apellido juntos)
                    if (foundBr && node.nodeType === Node.ELEMENT_NODE) {{
                        text += node.textContent;
                    }}
                }}
                return text.trim();
            }}");
            return result ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEG] Error extrayendo campo '{labelFor}': {ex.Message}");
            return string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construir mensaje del template
    // ─────────────────────────────────────────────────────────────────────────
    private string ConstruirMensajeSeguimiento(string contacto, string descripcion)
    {
        var descripcionLimpia = LimpiarNotasInternas(descripcion);
        if (!string.Equals(descripcionLimpia, descripcion, StringComparison.Ordinal))
            Console.WriteLine($"[SEG] Descripción filtrada: '{descripcion}' -> '{descripcionLimpia}'");

        // Template de message.md:
        return $"¡Hola, Sr/Sra. {contacto}! Soy Andrés Bautista 😀 de Hidráulicos JR. Espero esté muy bien el día de hoy.\n\n" +
               $"Le contacto por este medio para validar la cotización solicitada sobre {descripcionLimpia} y si tiene dudas adicionales sobre ella.";
    }

    // Quita notas internas de la descripción que NO deben verse en el mensaje al cliente.
    // Lista configurable en appsettings.json: "SeguimientoConfig:NotasInternas": ["REVENDEDOR", "ES REVENDEDOR", ...]
    private string LimpiarNotasInternas(string descripcion)
    {
        if (string.IsNullOrWhiteSpace(descripcion)) return descripcion;

        var notas = _configuration.GetSection("SeguimientoConfig:NotasInternas").Get<string[]>()
            ?? new[] { "REVENDEDOR", "ES REVENDEDOR" };

        // La descripción suele venir separada por comas. Filtramos los segmentos que
        // coinciden (case-insensitive, sin acentos) con alguna nota interna.
        var partes = descripcion.Split(',');
        var partesFiltradas = partes
            .Where(p => !EsNotaInterna(p, notas))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (partesFiltradas.Count == 0) return descripcion.Trim();
        return string.Join(", ", partesFiltradas);
    }

    private static bool EsNotaInterna(string parte, string[] notas)
    {
        var norm = NormalizarTextoParaComparar(parte);
        foreach (var n in notas)
        {
            if (string.Equals(norm, NormalizarTextoParaComparar(n), StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string NormalizarTextoParaComparar(string s) =>
        new string((s ?? string.Empty)
            .Trim()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray())
            .ToUpperInvariant();

    // ─────────────────────────────────────────────────────────────────────────
    // Enviar WhatsApp al número del cliente — vía CHAT PROPIO (Tú)
    // Estrategia:
    //   1. Abrir chat propio (3105003030 = Tú)
    //   2. Enviar el número del cliente como mensaje (ej. "+573008616586")
    //   3. WhatsApp lo renderiza como link → clic
    //   4. Menú emergente → clic en "Chatear"
    //   5. Se abre nuevo chat con el cliente → escribir y enviar mensaje
    // ─────────────────────────────────────────────────────────────────────────
    private async Task EnviarWhatsAppContactoAsync(IPage page, IBrowserContext context, string storageStatePath, string celular, string message)
    {
        var celularNorm = NormalizarTelefonoE164(celular);
        var celularConPlus = "+" + celularNorm;
        var selfChat = _configuration["WhatsAppConfig:SelfChat"] ?? "3105003030";

        Console.WriteLine($"[SEG-WA] Estrategia: enviar {celularConPlus} al chat propio ({selfChat}) y hacer clic en 'Chatear'.");

        // Asegurar que estamos en la página principal de WhatsApp Web
        if (!page.Url.Contains("web.whatsapp.com") || page.Url.Contains("/send"))
        {
            await page.GotoAsync("https://web.whatsapp.com/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await Task.Delay(3000);
        }

        try { await page.Keyboard.PressAsync("Escape"); await Task.Delay(300); } catch { /* no-op */ }

        // ── PASO 1: Abrir chat propio (buscando por el número configurado) ──
        await AbrirChatPorBusquedaAsync(page, selfChat, "chat propio");

        // ── PASO 2: Enviar el número del cliente como mensaje en el chat propio ──
        var composer = await EncontrarCompositorAsync(page, "chat propio");
        await composer.ClickAsync();
        await Task.Delay(500);

        Console.WriteLine($"[SEG-WA] Escribiendo número '{celularConPlus}' en chat propio...");
        await composer.PressSequentiallyAsync(celularConPlus, new LocatorPressSequentiallyOptions { Delay = 30 });
        await Task.Delay(800);
        await composer.PressAsync("Enter");
        Console.WriteLine($"[SEG-WA] Número enviado. Esperando que se renderice como link...");
        await Task.Delay(3000);

        // ── PASO 3: Hacer clic en el link del número en el último mensaje enviado ──
        var phoneLink = await EncontrarUltimoLinkNumeroAsync(page, celularNorm);
        if (phoneLink == null)
            throw new SeguimientoException($"No se encontró el link del número {celularConPlus} en el chat propio. WhatsApp no lo renderizó como teléfono.");

        Console.WriteLine($"[SEG-WA] Click en link del número...");
        await phoneLink.ClickAsync();
        await Task.Delay(1500);

        // ── PASO 4: Clic en opción "Chatear" del menú emergente ──
        var chatearClicked = await ClickearOpcionChatearAsync(page);
        if (!chatearClicked)
            throw new SeguimientoException($"SIN_WHATSAPP: No apareció la opción 'Chatear' para {celular}. El número posiblemente no tiene WhatsApp.");

        Console.WriteLine($"[SEG-WA] 'Chatear' seleccionado. Esperando que abra el chat con {celularNorm}...");
        await Task.Delay(4000);

        // ── PASO 5: Escribir y enviar el mensaje en el chat del cliente ──
        var composerCliente = await EncontrarCompositorAsync(page, $"chat de {celularNorm}");
        await composerCliente.ClickAsync();
        await Task.Delay(500);

        var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!string.IsNullOrEmpty(line))
                await composerCliente.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 10 });
            if (i < lines.Length - 1)
                await composerCliente.PressAsync("Shift+Enter");
        }

        await composerCliente.PressAsync("Enter");
        await Task.Delay(3000);
        Console.WriteLine($"[SEG-WA] Mensaje enviado a {celularNorm}.");

        await GuardarSesionWhatsAppAsync(context, storageStatePath);
    }

    // ─── Helper: abrir un chat buscándolo en la barra de búsqueda ────────────
    private static async Task AbrirChatPorBusquedaAsync(IPage page, string query, string descripcion)
    {
        var searchBoxSelectors = new[]
        {
            "input[role='textbox'][data-tab='3']",
            "input[aria-label*='Buscar']",
            "div[contenteditable='true'][role='textbox']",
            "div[contenteditable='true'][data-tab]",
            "div[aria-label='Buscar']",
            "div[aria-label='Search']"
        };

        ILocator? searchBox = null;
        foreach (var sel in searchBoxSelectors)
        {
            var loc = page.Locator(sel).First;
            if (await loc.CountAsync() > 0)
            {
                searchBox = loc;
                break;
            }
        }

        if (searchBox == null)
            throw new SeguimientoException($"No se encontró el buscador de chats al abrir {descripcion}.");

        await searchBox.ClickAsync();
        await Task.Delay(300);
        try { await searchBox.PressAsync("Control+A"); await searchBox.PressAsync("Delete"); } catch { /* no-op */ }
        await Task.Delay(200);

        await searchBox.PressSequentiallyAsync(query, new LocatorPressSequentiallyOptions { Delay = 40 });
        Console.WriteLine($"[SEG-WA] Buscando {descripcion} ('{query}')...");
        await Task.Delay(2000);

        await searchBox.PressAsync("ArrowDown");
        await Task.Delay(400);
        await searchBox.PressAsync("Enter");
        await Task.Delay(2500);
    }

    // ─── Helper: encontrar el compositor (input de mensaje) en el chat actual ──
    private static async Task<ILocator> EncontrarCompositorAsync(IPage page, string contexto)
    {
        var composerSelectors = new[]
        {
            "div[contenteditable='true'][role='textbox'][aria-label='Mensaje']",
            "div[contenteditable='true'][role='textbox'][aria-label='Message']",
            "footer div[contenteditable='true'][role='textbox']",
            "footer div[contenteditable='true']",
            "div[contenteditable='true'][role='textbox']",
            "div[contenteditable='true'][data-tab]",
            "[contenteditable='true']"
        };

        ILocator? composer = null;
        foreach (var sel in composerSelectors)
        {
            var loc = page.Locator(sel);
            var count = await loc.CountAsync();
            if (count > 0)
            {
                composer = loc.Last;
                Console.WriteLine($"[SEG-WA] Compositor encontrado en {contexto}: {sel}");
                break;
            }
        }

        if (composer == null)
            throw new SeguimientoException($"No se encontró el compositor en {contexto}.");

        try
        {
            await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        }
        catch (TimeoutException)
        {
            throw new SeguimientoException($"El compositor no se hizo visible en {contexto}.");
        }

        return composer;
    }

    // ─── Helper: encontrar el último link de número en el chat propio ────────
    private static async Task<ILocator?> EncontrarUltimoLinkNumeroAsync(IPage page, string celularNorm)
    {
        // WhatsApp renderiza números como <a href="tel:..."> o como spans clickables.
        // Probamos varios selectores y nos quedamos con el último.
        var linkSelectors = new[]
        {
            $"a[href*='tel:'][href*='{celularNorm}']",
            $"a[href*='tel:']",
            $"span[role='button'][title*='{celularNorm}']",
            $"div[role='button'][title*='{celularNorm}']",
            $"span:has-text('+{celularNorm}')",
        };

        foreach (var sel in linkSelectors)
        {
            try
            {
                var loc = page.Locator(sel);
                var count = await loc.CountAsync();
                Console.WriteLine($"[SEG-WA] Link selector '{sel}': {count} elemento(s)");
                if (count > 0)
                    return loc.Last;
            }
            catch { /* continuar */ }
        }

        return null;
    }

    // ─── Helper: clic en opción "Chatear" del menú emergente ─────────────────
    private static async Task<bool> ClickearOpcionChatearAsync(IPage page)
    {
        // El menú aparece tras hacer clic en un número. Tiene items tipo:
        //   - mi-chat-via-whatsapp / "Chatear"
        //   - mi-call / "Llamar"
        //   - mi-copy-phone-number / "Copiar número"
        var chatearSelectors = new[]
        {
            "li[data-testid*='chat']",
            "li[data-testid='mi-chat-via-whatsapp']",
            "div[role='application'] li:has-text('Chatear')",
            "li:has-text('Chatear')",
            "div[role='button']:has-text('Chatear')",
        };

        foreach (var sel in chatearSelectors)
        {
            try
            {
                var loc = page.Locator(sel).First;
                if (await loc.CountAsync() > 0)
                {
                    var text = await loc.InnerTextAsync();
                    Console.WriteLine($"[SEG-WA] Opción 'Chatear' detectada con selector '{sel}': '{text}'");
                    await loc.ClickAsync();
                    return true;
                }
            }
            catch { /* continuar */ }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Agregar seguimiento en el modal del SIC
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task AgregarSeguimientoEnSicAsync(IPage page, string tipoMensaje, string descripcionTicket)
    {
        var esSegundo = tipoMensaje.Equals("segundo", StringComparison.OrdinalIgnoreCase);
        var textoObservacion = esSegundo
            ? "SE REALIZO ENVIO DE SEGUNDO MENSAJE DE SEGUIMIENTO VIA WHATSAPP"
            : "SE REALIZO ENVIO DE PRIMER MENSAJE DE SEGUIMIENTO VIA WHATSAPP";

        Console.WriteLine($"[SEG-SIC] Abriendo modal de seguimiento...");

        // Esperar que el botón esté visible
        await page.WaitForSelectorAsync("#btnAbrirmodalAgregar", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await page.ClickAsync("#btnAbrirmodalAgregar");
        await Task.Delay(1500);

        // Esperar que el modal esté visible
        await page.WaitForSelectorAsync("#modalAgregarSeguimiento", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Llenar descripción (textarea dentro del modal)
        var descSelector = "#modalAgregarSeguimiento textarea[name='descripcion']";
        await page.WaitForSelectorAsync(descSelector, new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.FillAsync(descSelector, textoObservacion);
        Console.WriteLine($"[SEG-SIC] Descripción: '{textoObservacion}'");

        // Seleccionar motivo: opción 6 = "Pendiente respuesta del cliente"
        await page.SelectOptionAsync("#motivo_id", "6");
        Console.WriteLine($"[SEG-SIC] Motivo: 6 (Pendiente respuesta del cliente)");

        // Llenar Equipo/Servicio con la descripción del ticket
        var equipoVal = descripcionTicket.Length > 200 ? descripcionTicket[..200] : descripcionTicket;
        await page.FillAsync("#equipo", equipoVal);
        Console.WriteLine($"[SEG-SIC] Equipo/Servicio: '{equipoVal}'");

        // Cotización se deja vacía (required en HTML pero el servidor lo acepta vacío)
        // Limpiar por si tiene algo
        try { await page.FillAsync("#cotizacion", ""); } catch { /* no-op */ }

        // Guardar
        Console.WriteLine($"[SEG-SIC] Guardando seguimiento...");
        await page.ClickAsync("#btnGuardarSegumiento");
        await Task.Delay(3000);

        Console.WriteLine($"[SEG-SIC] Seguimiento guardado.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WhatsApp helpers
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task AsegurarLoginWhatsAppAsync(IPage page, IBrowserContext context, string storageStatePath, TimeSpan timeout)
    {
        var searchBoxSelectors = new[]
        {
            "input[role='textbox'][data-tab='3']",
            "input[aria-label*='Buscar']",
            "div[contenteditable='true'][role='textbox']",
            "[role='textbox'][contenteditable='true']",
            "div[aria-label='Buscar']",
            "div[aria-label='Search']"
        };

        var waitOptions = new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = (float)timeout.TotalMilliseconds };
        ILocator? found = null;

        foreach (var selector in searchBoxSelectors)
        {
            try
            {
                var loc = page.Locator(selector);
                await loc.WaitForAsync(waitOptions);
                found = loc;
                Console.WriteLine($"[SEG-WA] Sesión detectada: {selector}");
                break;
            }
            catch (TimeoutException)
            {
                // continuar con siguiente selector
            }
        }

        if (found != null)
        {
            Console.WriteLine("[SEG-WA] Sesión WhatsApp OK.");
            await GuardarSesionWhatsAppAsync(context, storageStatePath);
            return;
        }

        Console.WriteLine($"[SEG-WA] No hay sesión. Escanea el QR en {timeout.TotalSeconds} segundos...");
        foreach (var selector in searchBoxSelectors)
        {
            try
            {
                var loc = page.Locator(selector);
                await loc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = (float)timeout.TotalMilliseconds });
                Console.WriteLine($"[SEG-WA] QR escaneado. Selector: {selector}");
                break;
            }
            catch (TimeoutException) { /* continuar */ }
        }

        await GuardarSesionWhatsAppAsync(context, storageStatePath);
    }

    private static async Task GuardarSesionWhatsAppAsync(IBrowserContext context, string storageStatePath)
    {
        try
        {
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = storageStatePath });
            await Task.Delay(1000);
            Console.WriteLine($"[SEG-WA] Sesión guardada: {storageStatePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEG-WA] Error guardando sesión: {ex.Message}");
        }
    }

    private void ValidarStorageState(string storageStatePath)
    {
        if (!File.Exists(storageStatePath)) return;
        try
        {
            var content = File.ReadAllText(storageStatePath);
            if (string.IsNullOrWhiteSpace(content) || content.Length < 50)
            {
                File.Delete(storageStatePath);
                return;
            }
            JsonDocument.Parse(content);
        }
        catch
        {
            try { File.Delete(storageStatePath); } catch { /* no-op */ }
        }
    }

    private static string NormalizarTelefonoE164(string raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        // Si es número colombiano de 10 dígitos que empieza con 3, agregar prefijo 57
        if (digits.Length == 10 && digits.StartsWith("3"))
            digits = "57" + digits;
        return digits;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Excepción para errores controlados del flujo
// ─────────────────────────────────────────────────────────────────────────────
public class SeguimientoException : Exception
{
    public SeguimientoException(string message) : base(message) { }
}

// ─────────────────────────────────────────────────────────────────────────────
// Store en memoria para jobs de seguimiento
// ─────────────────────────────────────────────────────────────────────────────
public class SeguimientoStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SeguimientoJob> _jobs = new();

    public SeguimientoJob CrearJob(List<string> tickets, string tipoMensaje)
    {
        var job = new SeguimientoJob
        {
            BatchId = Guid.NewGuid().ToString("N")[..12],
            Tickets = tickets,
            TipoMensaje = tipoMensaje,
            EstadoGeneral = "PENDIENTE",
            Resultados = tickets.Select(t => new TicketSeguimientoResult { Ticket = t, Estado = "PENDIENTE" }).ToList()
        };
        _jobs[job.BatchId] = job;
        return job;
    }

    public SeguimientoJob? ObtenerJob(string batchId) =>
        _jobs.TryGetValue(batchId, out var job) ? job : null;

    public void ActualizarEstadoGeneral(string batchId, string estado)
    {
        if (_jobs.TryGetValue(batchId, out var job))
            job.EstadoGeneral = estado;
    }

    public void ActualizarTicket(string batchId, string ticket, string estado, string mensaje)
    {
        if (!_jobs.TryGetValue(batchId, out var job)) return;
        var resultado = job.Resultados.FirstOrDefault(r => r.Ticket == ticket);
        if (resultado == null) return;
        resultado.Estado = estado;
        resultado.Mensaje = mensaje;
    }
}
