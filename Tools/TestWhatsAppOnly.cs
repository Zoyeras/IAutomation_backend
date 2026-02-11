using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

public class TestWhatsAppHelper
{
    public static async Task EnviarWhatsAppTestAsync(string baseUrl, string sendToE164, string storageStatePath, string message)
    {
        Console.WriteLine("\n🚀 INICIANDO PLAYWRIGHT...");
        using var playwright = await Playwright.CreateAsync();
        
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 500
        });

        Console.WriteLine("✓ Navegador abierto");

        // Contexto con storage persistido
        var contextOptions = new BrowserNewContextOptions();
        if (File.Exists(storageStatePath))
        {
            contextOptions.StorageStatePath = storageStatePath;
            Console.WriteLine($"✓ Cargando sesión guardada: {storageStatePath}");
        }
        else
        {
            Console.WriteLine($"⚠️  No hay sesión guardada. Tendrás que escanear QR.");
        }

        var context = await browser.NewContextAsync(contextOptions);
        var page = await context.NewPageAsync();

        try
        {
            Console.WriteLine($"\n📱 Abriendo WhatsApp Web: {baseUrl}");
            await page.GotoAsync(baseUrl, new PageGotoOptions 
            { 
                WaitUntil = WaitUntilState.NetworkIdle, 
                Timeout = 60000 
            });

            Console.WriteLine("✓ Página cargada");

            // Verificar si hay sesión
            Console.WriteLine("\n🔑 Verificando autenticación...");
            var searchBox = page.Locator("div[contenteditable='true'][data-tab='3']");
            try
            {
                await searchBox.WaitForAsync(new LocatorWaitForOptions 
                { 
                    State = WaitForSelectorState.Visible, 
                    Timeout = 5000 
                });
                Console.WriteLine("✓ YA ESTÁ LOGUEADO en WhatsApp");

                // Guardar sesión aunque ya exista
                await context.StorageStateAsync(new BrowserContextStorageStateOptions 
                { 
                    Path = storageStatePath 
                });
                Console.WriteLine($"✓ Sesión guardada/actualizada");
            }
            catch
            {
                Console.WriteLine("⚠️  NO ESTÁ LOGUEADO. Escanea el QR en los próximos 90 segundos...");
                await searchBox.WaitForAsync(new LocatorWaitForOptions 
                { 
                    State = WaitForSelectorState.Visible, 
                    Timeout = 90000 
                });
                Console.WriteLine("✓ QR escaneado correctamente");

                await context.StorageStateAsync(new BrowserContextStorageStateOptions 
                { 
                    Path = storageStatePath 
                });
                Console.WriteLine($"✓ Sesión guardada: {storageStatePath}");
            }

            // Ahora abrir el chat
            Console.WriteLine($"\n💬 Abriendo chat con: {sendToE164}");
            var encoded = Uri.EscapeDataString(message);
            var waWebSendUrl = $"https://web.whatsapp.com/send?phone={sendToE164}&text={encoded}";
            
            Console.WriteLine($"[URL] {waWebSendUrl.Substring(0, 80)}...");
            await page.GotoAsync(waWebSendUrl, new PageGotoOptions 
            { 
                WaitUntil = WaitUntilState.NetworkIdle, 
                Timeout = 60000 
            });

            Console.WriteLine("✓ Chat abierto");
            Console.WriteLine("⏳ Esperando 5 segundos para que cargue completamente...");
            await Task.Delay(5000);

            // Buscar el compositor
            Console.WriteLine("\n🔍 Buscando input de mensaje...");
            var composerSelectors = new[]
            {
                "[contenteditable='true']",
                "div[contenteditable='true']",
                "div[contenteditable='true'][data-tab]",
                "div[contenteditable='true'][role='textbox']",
                "[role='textbox']",
                "input[type='text'][placeholder*='message' i]",
                ".selectable-text.copyable-text"
            };

            ILocator? composer = null;
            foreach (var selector in composerSelectors)
            {
                try
                {
                    var loc = page.Locator(selector);
                    var count = await loc.CountAsync();
                    Console.WriteLine($"   [{selector}] → {count} elemento(s)");

                    if (count > 0)
                    {
                        composer = loc.Last;
                        Console.WriteLine($"   ✓ ENCONTRADO con selector: {selector}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   [{selector}] → ERROR: {ex.Message}");
                }
            }

            if (composer == null)
            {
                Console.WriteLine("\n❌ NO SE ENCONTRÓ EL INPUT DE MENSAJE");
                
                // Capturar screenshot
                var screenshotPath = Path.Combine(Directory.GetCurrentDirectory(), $"wa-test-error-{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                Console.WriteLine($"   Screenshot guardado: {screenshotPath}");

                throw new InvalidOperationException("No se encontró el input de mensaje");
            }

            // Hacer click
            Console.WriteLine("\n✍️  Escribiendo mensaje...");
            Console.WriteLine("   - Haciendo click en el input");
            await composer.ClickAsync();
            await Task.Delay(500);

            // Focus JavaScript
            try
            {
                await page.EvaluateAsync("() => { document.activeElement?.focus(); }");
                Console.WriteLine("   - Focus JavaScript aplicado");
            }
            catch { /* no-op */ }

            // Escribir línea por línea
            var lines = message.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!string.IsNullOrEmpty(line))
                {
                    var preview = line.Length > 40 ? line.Substring(0, 40) + "..." : line;
                    Console.WriteLine($"   - Línea {i + 1}/{lines.Length}: {preview}");
                    await composer.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 20 });
                }

                if (i < lines.Length - 1)
                {
                    Console.WriteLine($"   - Salto de línea (Shift+Enter)");
                    await composer.PressAsync("Shift+Enter");
                    await Task.Delay(150);
                }
            }

            Console.WriteLine("   ✓ Mensaje escrito completamente");
            await Task.Delay(2000);

            // Enviar
            Console.WriteLine("\n📤 Enviando mensaje...");
            var messageSent = false;

            try
            {
                Console.WriteLine("   - Intentando con Enter...");
                await composer.PressAsync("Enter");
                Console.WriteLine("   ✓ Enter presionado");
                messageSent = true;
                await Task.Delay(3000);
            }
            catch (Exception exEnter)
            {
                Console.WriteLine($"   ❌ Enter falló: {exEnter.Message}");

                // Buscar botón
                try
                {
                    Console.WriteLine("   - Buscando botón de envío...");
                    var sendButton = page.Locator("button[aria-label*='Send' i], button[aria-label*='Enviar' i]");
                    if (await sendButton.CountAsync() > 0)
                    {
                        Console.WriteLine("   ✓ Botón encontrado, haciendo click...");
                        await sendButton.First.ClickAsync();
                        messageSent = true;
                        await Task.Delay(3000);
                    }
                    else
                    {
                        Console.WriteLine("   ❌ Botón no encontrado");
                    }
                }
                catch (Exception exBtn)
                {
                    Console.WriteLine($"   ❌ Error en botón: {exBtn.Message}");
                }
            }

            if (messageSent)
            {
                Console.WriteLine("   ✓ MENSAJE ENVIADO EXITOSAMENTE");
            }
            else
            {
                Console.WriteLine("   ⚠️  MENSAJE POSIBLEMENTE NO ENVIADO");
            }

            // Guardar sesión final
            Console.WriteLine("\n💾 Guardando sesión...");
            await context.StorageStateAsync(new BrowserContextStorageStateOptions 
            { 
                Path = storageStatePath 
            });
            Console.WriteLine($"✓ Sesión guardada: {storageStatePath}");

            await Task.Delay(3000);
            Console.WriteLine("\n⏳ Cerrando navegador en 3 segundos...");
            await Task.Delay(3000);
        }
        finally
        {
            await context.CloseAsync();
            Console.WriteLine("✓ Contexto cerrado");
        }
    }
}



