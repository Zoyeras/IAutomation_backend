using AutomationAPI.Data;
using Microsoft.EntityFrameworkCore;
using AutomationAPI.Services;
using Microsoft.Playwright;

// Verificar si se está pidiendo ejecutar solo el test de WhatsApp con dos mensajes
if (args.Contains("--test-whatsapp-dos-mensajes"))
{
    Console.WriteLine("🧪 EJECUTANDO TEST DE WHATSAPP CON DOS MENSAJES (GRUPO + CLIENTE)...\n");
    
    var testTask = Task.Run(async () =>
    {
        try
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var waBaseUrl = config["WhatsAppConfig:BaseUrl"] ?? "https://web.whatsapp.com";
            var groupName = config["WhatsAppConfig:GroupName"] ?? "Tickets Soluciones";
            var storageStatePathCfg = config["WhatsAppConfig:StorageStatePath"] ?? "whatsapp.storage.json";

            var contentRoot = Directory.GetCurrentDirectory();
            var storageStatePath = Path.IsPathRooted(storageStatePathCfg)
                ? storageStatePathCfg
                : Path.Combine(contentRoot, storageStatePathCfg);

            Console.WriteLine($"📋 CONFIGURACIÓN:");
            Console.WriteLine($"   Base URL: {waBaseUrl}");
            Console.WriteLine($"   Grupo: {groupName}");
            Console.WriteLine($"   Storage: {storageStatePath}");
            Console.WriteLine($"   Archivo existe: {File.Exists(storageStatePath)}");

            // Datos de prueba del cliente
            var nombreCliente = "Juan Pérez";
            var celularCliente = "3105003030";

            // Mensaje 1: Al grupo (con info de ticket)
            var mensajeGrupo = @"Buen día, asignación de
TICKET N° 999999
NIT: 900000000
RAZÓN SOCIAL: TEST PRUEBA
NOMBRE DE CONTACTO: Juan Pérez
TELÉFONO DE CONTACTO: 3105003030
CIUDAD: Bogota
OBSERVACIÓN: MENSAJE DE PRUEBA DOS ENVÍOS";

            // Mensaje 2: Al cliente (personalizado)
            var mensajeCliente = $"Muchas gracias por la información sr Juan Pérez, la solicitud acaba de ser compartida con un asesor el cual le contactara pronto, tenga excelente dia, cualquier duda estoy atento";

            Console.WriteLine($"\n📝 MENSAJE 1 - AL GRUPO '{groupName}':");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine(mensajeGrupo);
            Console.WriteLine(new string('─', 60));

            Console.WriteLine($"\n📝 MENSAJE 2 - AL CLIENTE ({celularCliente}):");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine(mensajeCliente);
            Console.WriteLine(new string('─', 60));

            await EnviarWhatsAppDosMensajes(waBaseUrl, groupName, celularCliente, nombreCliente, storageStatePath, mensajeGrupo, mensajeCliente);
            Console.WriteLine("\n✅ PRUEBA COMPLETADA EXITOSAMENTE");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ ERROR EN PRUEBA: {ex.GetType().Name}");
            Console.WriteLine($"   Mensaje: {ex.Message}");
            if (ex.StackTrace != null)
            {
                Console.WriteLine($"\n📌 StackTrace:");
                Console.WriteLine(ex.StackTrace);
            }
        }

        Console.WriteLine("\n" + new string('=', 60));
        Environment.Exit(0);
    });

    testTask.Wait();
    return;
}

// Verificar si se está pidiendo ejecutar solo el test de WhatsApp
if (args.Contains("--test-whatsapp"))
{
    Console.WriteLine("🧪 EJECUTANDO TEST DE WHATSAPP SOLAMENTE...\n");
    
    // Ejecutar test
    var testTask = Task.Run(async () =>
    {
        try
        {
            // Cargar configuración
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var waBaseUrl = config["WhatsAppConfig:BaseUrl"] ?? "https://web.whatsapp.com";
            var groupName = config["WhatsAppConfig:GroupName"] ?? "Tickets Soluciones";
            var storageStatePathCfg = config["WhatsAppConfig:StorageStatePath"] ?? "whatsapp.storage.json";

            var contentRoot = Directory.GetCurrentDirectory();
            var storageStatePath = Path.IsPathRooted(storageStatePathCfg)
                ? storageStatePathCfg
                : Path.Combine(contentRoot, storageStatePathCfg);

            Console.WriteLine($"📋 CONFIGURACIÓN:");
            Console.WriteLine($"   Base URL: {waBaseUrl}");
            Console.WriteLine($"   Grupo: {groupName}");
            Console.WriteLine($"   Storage: {storageStatePath}");
            Console.WriteLine($"   Archivo existe: {File.Exists(storageStatePath)}");

            // Mensaje de prueba
            var mensaje = @"Buen día, asignación de
TICKET N° 999999
NIT: 900000000
RAZÓN SOCIAL: TEST PRUEBA
NOMBRE DE CONTACTO: PRUEBA
TELÉFONO DE CONTACTO: 3105003030
CIUDAD: Bogota
OBSERVACIÓN: MENSAJE DE PRUEBA SOLO WHATSAPP";

            Console.WriteLine($"\n📝 MENSAJE A ENVIAR:");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine(mensaje);
            Console.WriteLine(new string('─', 60));

            await EnviarWhatsAppTest(waBaseUrl, groupName, storageStatePath, mensaje);
            Console.WriteLine("\n✅ PRUEBA COMPLETADA EXITOSAMENTE");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ ERROR EN PRUEBA: {ex.GetType().Name}");
            Console.WriteLine($"   Mensaje: {ex.Message}");
            if (ex.StackTrace != null)
            {
                Console.WriteLine($"\n📌 StackTrace:");
                Console.WriteLine(ex.StackTrace);
            }
        }

        Console.WriteLine("\n" + new string('=', 60));
        Environment.Exit(0);
    });

    testTask.Wait();
    return;
}

async Task EnviarWhatsAppTest(string baseUrl, string groupName, string storageStatePath, string message)
{
    Console.WriteLine("\n🚀 INICIANDO PLAYWRIGHT...");
    using var playwright = await Playwright.CreateAsync();
    
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Headless = false,
        SlowMo = 500
    });

    Console.WriteLine("✓ Navegador abierto");

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

        Console.WriteLine("\n🔑 Verificando autenticación...");
        var loginProbe = page.Locator("div[contenteditable='true'][data-tab='3']");
        try
        {
            await loginProbe.WaitForAsync(new LocatorWaitForOptions 
            { 
                State = WaitForSelectorState.Visible, 
                Timeout = 5000 
            });
            Console.WriteLine("✓ YA ESTÁ LOGUEADO en WhatsApp");

            await context.StorageStateAsync(new BrowserContextStorageStateOptions 
            { 
                Path = storageStatePath 
            });
            Console.WriteLine($"✓ Sesión guardada/actualizada");
        }
        catch
        {
            Console.WriteLine("⚠️  NO ESTÁ LOGUEADO. Escanea el QR en los próximos 90 segundos...");
            await loginProbe.WaitForAsync(new LocatorWaitForOptions 
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

        // Buscar el grupo por la barra de búsqueda
        Console.WriteLine($"\n💬 Buscando grupo/chat: {groupName}");
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

        Console.WriteLine("   Haciendo click en buscador...");
        await searchBox.ClickAsync();
        await Task.Delay(500);
        
        // Limpiar el buscador
        try { await searchBox.PressAsync("Control+A"); await searchBox.PressAsync("Delete"); } catch { /* no-op */ }
        await Task.Delay(300);
        
        // Escribir el nombre del grupo
        Console.WriteLine($"   Escribiendo: {groupName}");
        await searchBox.PressSequentiallyAsync(groupName, new LocatorPressSequentiallyOptions { Delay = 50 });
        
        // Esperar a que los resultados aparezcan
        Console.WriteLine("   Esperando resultados de búsqueda...");
        await Task.Delay(2000);

        // Presionar ArrowDown y Enter para seleccionar el primer resultado
        Console.WriteLine("   Presionando ArrowDown para seleccionar primer resultado...");
        await searchBox.PressAsync("ArrowDown");
        await Task.Delay(500);
        
        Console.WriteLine("   Presionando Enter para abrir el chat...");
        await searchBox.PressAsync("Enter");
        
        // Esperar a que el chat se cargue completamente
        Console.WriteLine("   ⏳ Esperando a que el chat se cargue...");
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
                Console.WriteLine($"✓ Compositor encontrado: {selector}");
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
                await composer.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 20 });
            if (i < lines.Length - 1)
                await composer.PressAsync("Shift+Enter");
        }

        Console.WriteLine("📤 Enviando mensaje...");
        await composer.PressAsync("Enter");
        Console.WriteLine("   ✓ MENSAJE ENVIADO (grupo)");

        Console.WriteLine("\n💾 Guardando sesión...");
        await context.StorageStateAsync(new BrowserContextStorageStateOptions 
        { 
            Path = storageStatePath 
        });
        Console.WriteLine($"✓ Sesión guardada: {storageStatePath}");

        await Task.Delay(3000);
    }
    finally
    {
        await context.CloseAsync();
        Console.WriteLine("✓ Contexto cerrado");
    }
}

async Task EnviarWhatsAppDosMensajes(string baseUrl, string groupName, string celularCliente, string nombreCliente, string storageStatePath, string mensajeGrupo, string mensajeCliente)
{
    Console.WriteLine("\n🚀 INICIANDO PLAYWRIGHT...");
    using var playwright = await Playwright.CreateAsync();
    
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Headless = false,
        SlowMo = 500
    });

    Console.WriteLine("✓ Navegador abierto");

    var contextOptions = new BrowserNewContextOptions();
    if (File.Exists(storageStatePath))
    {
        contextOptions.StorageStatePath = storageStatePath;
        Console.WriteLine($"✓ Cargando sesión guardada");
    }

    var context = await browser.NewContextAsync(contextOptions);
    var page = await context.NewPageAsync();

    try
    {
        Console.WriteLine($"\n📱 Abriendo WhatsApp Web");
        await page.GotoAsync(baseUrl, new PageGotoOptions 
        { 
            WaitUntil = WaitUntilState.NetworkIdle, 
            Timeout = 60000 
        });

        Console.WriteLine("✓ Página cargada");

        // Verificar autenticación
        Console.WriteLine("\n🔑 Verificando autenticación...");
        var loginProbe = page.Locator("div[contenteditable='true'][data-tab='3']");
        try
        {
            await loginProbe.WaitForAsync(new LocatorWaitForOptions 
            { 
                State = WaitForSelectorState.Visible, 
                Timeout = 5000 
            });
            Console.WriteLine("✓ YA ESTÁ LOGUEADO en WhatsApp");

            await context.StorageStateAsync(new BrowserContextStorageStateOptions 
            { 
                Path = storageStatePath 
            });
        }
        catch
        {
            Console.WriteLine("⚠️  NO ESTÁ LOGUEADO. Escanea el QR en los próximos 90 segundos...");
            await loginProbe.WaitForAsync(new LocatorWaitForOptions 
            { 
                State = WaitForSelectorState.Visible, 
                Timeout = 90000 
            });
            Console.WriteLine("✓ QR escaneado correctamente");

            await context.StorageStateAsync(new BrowserContextStorageStateOptions 
            { 
                Path = storageStatePath 
            });
        }

        // ===== MENSAJE 1: AL GRUPO =====
        Console.WriteLine($"\n📤 ENVIANDO MENSAJE 1 AL GRUPO '{groupName}'");
        Console.WriteLine("=" + new string('=', 59));
        
        await EnviarMensajeAlGrupo(page, groupName, mensajeGrupo);
        
        Console.WriteLine("✓ Mensaje 1 enviado al grupo");

        // Pequeña pausa entre envíos
        await Task.Delay(2000);

        // ===== MENSAJE 2: AL CLIENTE =====
        Console.WriteLine($"\n📤 ENVIANDO MENSAJE 2 AL CLIENTE ({celularCliente})");
        Console.WriteLine("=" + new string('=', 59));
        
        await EnviarMensajeAlContacto(page, celularCliente, nombreCliente, mensajeCliente);
        
        Console.WriteLine($"✓ Mensaje 2 enviado al cliente: {nombreCliente}");

        // Guardar sesión final
        Console.WriteLine("\n💾 Guardando sesión...");
        await context.StorageStateAsync(new BrowserContextStorageStateOptions 
        { 
            Path = storageStatePath 
        });
        Console.WriteLine("✓ Sesión guardada");

        await Task.Delay(2000);
    }
    finally
    {
        await context.CloseAsync();
        Console.WriteLine("✓ Contexto cerrado");
    }
}

async Task EnviarMensajeAlGrupo(IPage page, string groupName, string message)
{
    Console.WriteLine($"   Buscando grupo: {groupName}");
    
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
        throw new InvalidOperationException("No se encontró el buscador de chats");

    await searchBox.ClickAsync();
    await Task.Delay(500);
    
    try { await searchBox.PressAsync("Control+A"); await searchBox.PressAsync("Delete"); } catch { }
    await Task.Delay(300);
    
    await searchBox.PressSequentiallyAsync(groupName, new LocatorPressSequentiallyOptions { Delay = 50 });
    await Task.Delay(2000);

    await searchBox.PressAsync("ArrowDown");
    await Task.Delay(500);
    await searchBox.PressAsync("Enter");
    
    await Task.Delay(3000);

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
        throw new InvalidOperationException("No se encontró el compositor");

    await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
    await composer.ClickAsync();

    var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
    for (var i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        if (!string.IsNullOrEmpty(line))
            await composer.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 20 });
        if (i < lines.Length - 1)
            await composer.PressAsync("Shift+Enter");
    }

    await composer.PressAsync("Enter");
    Console.WriteLine("   ✓ Mensaje escrito y enviado al grupo");
}

async Task EnviarMensajeAlContacto(IPage page, string celular, string nombreCliente, string message)
{
    Console.WriteLine($"   Buscando contacto: {celular}");
    
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
        throw new InvalidOperationException("No se encontró el buscador de chats");

    await searchBox.ClickAsync();
    await Task.Delay(500);
    
    try { await searchBox.PressAsync("Control+A"); await searchBox.PressAsync("Delete"); } catch { }
    await Task.Delay(300);
    
    await searchBox.PressSequentiallyAsync(celular, new LocatorPressSequentiallyOptions { Delay = 50 });
    await Task.Delay(2000);

    await searchBox.PressAsync("ArrowDown");
    await Task.Delay(500);
    await searchBox.PressAsync("Enter");
    
    await Task.Delay(3000);

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
        throw new InvalidOperationException("No se encontró el compositor");

    await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
    await composer.ClickAsync();

    var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
    for (var i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        if (!string.IsNullOrEmpty(line))
            await composer.PressSequentiallyAsync(line, new LocatorPressSequentiallyOptions { Delay = 20 });
        if (i < lines.Length - 1)
            await composer.PressAsync("Shift+Enter");
    }

    await composer.PressAsync("Enter");
    Console.WriteLine($"   ✓ Mensaje escrito y enviado a {nombreCliente}");
}

var builder = WebApplication.CreateBuilder(args);

// ...existing code...
