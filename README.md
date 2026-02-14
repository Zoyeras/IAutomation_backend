# AutomationAPI (Backend)

**Idioma / Language:** Español (este archivo) | [English](./README.en.md)

API REST en **ASP.NET Core (.NET 10)** que:

1. Recibe un registro (ticket) desde un frontend (por ejemplo React).
2. Guarda el registro en **PostgreSQL** usando **Entity Framework Core**.
3. Ejecuta una automatización web con **Microsoft Playwright** para crear un ticket en el **SIC** (portal web externo).
4. **[NUEVO v2.1]** Envía dos mensajes por WhatsApp: uno al grupo "Tickets Soluciones" y otro personalizado al cliente.

> Proyecto ubicado en: `Backend/AutomationAPI`

> Proyecto ubicado en: `Backend/AutomationAPI`

---

## Tabla de contenidos

- [Arquitectura](#arquitectura)
- [Endpoints](#endpoints)
- [Modelo de datos](#modelo-de-datos)
- [Configuración](#configuración)
- [Dependencias](#dependencias)
- [Instalación y ejecución](#instalación-y-ejecución)
- [Migraciones / Base de datos](#migraciones--base-de-datos)
- [Playwright (instalación de browsers)](#playwright-instalación-de-browsers)
- [**WhatsApp Automation v2.1 (Cambios)**](#whatsapp-automation-v21-cambios) ⭐ **NUEVO**
- [**Testing / Pruebas**](#testing--pruebas) 📋 (Referencia histórica)
- [Troubleshooting](#troubleshooting)
  - [Error: NullReferenceException al leer opciones del select de Ciudad](#error-nullreferenceexception-al-leer-opciones-del-select-de-ciudad)
  - [Error: column "EstadoAutomatizacion" of relation "Registros" does not exist](#error-column-estadoautomatizacion-of-relation-registros-does-not-exist)
- [Sugerencias de mejora](#sugerencias-de-mejora)
- [Implementaciones y arreglos (historial)](#implementaciones-y-arreglos-historial)

---

## Arquitectura

Flujo general:

1. El frontend hace `POST /api/registros` enviando un JSON.
2. La API persiste el registro en PostgreSQL.
3. Se dispara en segundo plano el bot de Playwright para loguearse al SIC y llenar el formulario.

Componentes principales:

- `Program.cs`: configuración de servicios (EF Core, CORS, Swagger, DI).
- `Controllers/RegistrosController.cs`: endpoint `POST`.
- `Models/Registro.cs`: entidad EF Core.
- `Data/AppDbContext.cs`: DbContext con `DbSet<Registro>`.
- `Services/AutomationService.cs`: bot Playwright.

---

## Changelog corto (cambios recientes)

### Arreglo: error de DI (DbContextOptions scoped vs singleton)

Si al ejecutar `dotnet run` aparecía un error similar a:

- `Cannot consume scoped service 'DbContextOptions<AppDbContext>' from singleton ... (DbContextPool/DbContextFactory)`

El arreglo aplicado fue **evitar mezclar** `AddDbContext(...)` con `AddDbContextFactory/AddPooledDbContextFactory` en una forma que termine registrando `DbContextOptions<T>` como *scoped* para un servicio *singleton*.

**Configuración recomendada (actual):**

- Usar `AddPooledDbContextFactory<AppDbContext>(...)` (seguro para tareas en background / singleton).
- Registrar `AppDbContext` como *scoped* para controllers, creado desde la factory.

Esto queda en `Program.cs` (resumen):

- `AddPooledDbContextFactory<AppDbContext>(...)`
- `AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())`

---

### WhatsApp Web (sesión persistida)

El bot también puede enviar un mensaje vía **WhatsApp Web** usando Playwright.

- La primera vez abrirá `https://web.whatsapp.com` y debes escanear el QR.
- La sesión se guarda en un archivo `storageState` para reutilizarse en próximas ejecuciones.

Configuración en `appsettings.json`:

- `WhatsAppConfig:SendTo` → número destino (fijo) en formato E.164 sin `+` (ej: `573105003030`).
- `WhatsAppConfig:StorageStatePath` → archivo donde se persiste la sesión (ej: `whatsapp.storage.json`).

Notas importantes:

- Para evitar que en Linux el navegador intente abrir el esquema `whatsapp://send` (que puede fallar por falta de handler), el bot abre el chat usando **solo web**:
  - `https://web.whatsapp.com/send?phone=<E164>&text=<mensaje>`
- **[IMPORTANTE - v2.2]** El `storageState` (sesión de WhatsApp) se guarda en el **ContentRootPath** del `IWebHostEnvironment` (siempre relativo a donde esté el ejecutable `.exe` o script de ejecución). Esto asegura que:
  - Si ejecutas desde `C:\publish\AutomationAPI.exe` → se guarda en `C:\publish\whatsapp.storage.json`
  - Si ejecutas desde un `.bat` en `C:\publish\start-autohjr360.bat` → busca en `C:\publish\whatsapp.storage.json`
  - La sesión **persiste correctamente entre ejecuciones** sin importar de dónde abras el cmd/terminal
- **[IMPORTANTE - v2.3]** WhatsApp requiere un **perfil persistente** para mantener la sesión real (IndexedDB/Service Worker). El bot usa `publish/wa-profile` y, si hay problemas de logout, borra `wa-profile` y `whatsapp.storage.json` y vuelve a escanear el QR.
- Si configuras `WhatsAppConfig:GroupName`, el bot **enviará al grupo/chat** buscando por nombre en WhatsApp Web y haciendo click en **el primer resultado**.
  - Ejemplo: `"GroupName": "Tickets Soluciones"`
  - Si `GroupName` está vacío, se usa `SendTo` (número) como fallback.

## WhatsApp: forzar envío temporal a número (deshabilitar grupo)

Si por ahora NO quieres enviar al grupo y prefieres enviar **siempre** al número fijo `3105003030`, deja `GroupName` vacío en `appsettings.json`:

- `WhatsAppConfig:SendTo`: `573105003030` (formato E.164 sin `+`)
- `WhatsAppConfig:GroupName`: `""`

Cuando quieras volver a enviar al grupo, vuelve a poner por ejemplo:

- `WhatsAppConfig:GroupName`: `"Tickets Soluciones"`

> Nota: si `GroupName` tiene un valor, el bot prioriza el envío al grupo/chat. Si está vacío, usa `SendTo`.

---

## Endpoints

### `POST /api/registros`

Guarda el registro y dispara la automatización en segundo plano.

**Request body** (ejemplo):

```json
{
  "nit": "900123456",
  "empresa": "Mi Empresa SAS",
  "ciudad": "Bogota",
  "cliente": "Juan Perez",
  "celular": "3001234567",
  "correo": "juan@empresa.com",
  "tipoCliente": "Nuevo",
  "concepto": "Solicitud de servicio"
}
```

**Respuesta** (ejemplo):

```json
{ "message": "Guardado y Automatización iniciada", "id": 38 }
```

---

## Modelo de datos

Entidad: `Models/Registro.cs`

Campos principales:

- `Nit`
- `Empresa`
- `Ciudad`
- `Cliente`
- `Celular`
- `Correo`
- `TipoCliente` (texto: `Nuevo`, `Antiguo`, `Fidelizado`, `Recuperado`)
- `Concepto`
- `FechaCreacion` (UTC)

Campos adicionales (integridad / auditoría):

- `Ticket`: ticket capturado del SIC (se llena después de validar en el listado)
- `EstadoAutomatizacion`: `PENDIENTE` | `EN_PROCESO` | `COMPLETADO` | `ERROR`
- `UltimoErrorAutomatizacion`: último error si falló el bot
- `FechaActualizacion`: última vez que se actualizó el registro

> Importante: si actualizas el código, aplica migraciones con `dotnet ef database update`.

---

## Configuración

La API consume configuración desde `appsettings.json`.

Claves usadas:

- `ConnectionStrings:DefaultConnection` → conexión PostgreSQL
- `SicConfig:BaseUrl` → URL del SIC
- `SicConfig:User` → usuario
- `SicConfig:Password` → contraseña

Archivos:

- `appsettings.json`: configuración real (⚠️ contiene credenciales si lo dejas así).
- `appsettings.Example.json`: plantilla sin secretos.

> Recomendación: usar `appsettings.Development.json` local + variables de entorno / User Secrets.

---

## Dependencias

Dentro de `AutomationAPI.csproj`:

- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Design`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Swashbuckle.AspNetCore` (Swagger)
- `Microsoft.Playwright`

Requisitos del sistema:

- **.NET SDK 10**
- **PostgreSQL**
- Dependencias de Playwright/Chromium (se instalan con Playwright)

---

## Instalación y ejecución

1) Restaurar y compilar

```bash
cd Backend/AutomationAPI
dotnet restore
dotnet build
```

2) Configurar `appsettings.json` (o usar `appsettings.Development.json`)

Sugerencia: copiar desde el ejemplo:

```bash
cp appsettings.Example.json appsettings.Development.json
```

3) Ejecutar

```bash
dotnet run
```

Swagger (en Development):

- `http://localhost:5016/swagger`

> Puertos por defecto definidos en `Properties/launchSettings.json`.

---

## Migraciones / Base de datos

El proyecto incluye migración inicial en `Migrations/`.

Para crear/actualizar la base de datos:

```bash
dotnet ef database update
```

> Nota: requiere tener instalado el tooling de EF. Si no lo tienes:

```bash
dotnet tool install --global dotnet-ef
```

---

## Playwright (instalación de browsers)

> Importante: Playwright necesita instalar browsers la primera vez.

### Linux (recomendado)

1) Compila para que se generen los scripts de Playwright:

```bash
cd Backend/AutomationAPI
dotnet build
```

2) Instala dependencias del sistema (recomendado en Linux) y luego los browsers:

```bash
./bin/Debug/net10.0/playwright.sh install-deps
./bin/Debug/net10.0/playwright.sh install
```

> Nota: `install-deps` instala librerías que Chromium/WebKit/Firefox necesitan para correr.

### Windows

En PowerShell:

```powershell
cd Backend/AutomationAPI
dotnet build
.\bin\Debug\net10.0\playwright.ps1 install
```

Si Windows bloquea scripts por policy, puedes ejecutar (en la misma sesión):

```powershell
Set-ExecutionPolicy Bypass -Scope Process
```

### Linux usando PowerShell (opcional)

Si tienes `pwsh` instalado en Linux:

```bash
cd Backend/AutomationAPI
dotnet build
pwsh ./bin/Debug/net10.0/playwright.ps1 install
```

---

## WhatsApp Automation v2.1 (Cambios)

### Resumen de cambios principales

**Versión 2.1 (11 de febrero 2026):** Implementación de **doble envío a WhatsApp**

#### Antes (v2.0)
- Enviaba un solo mensaje al **celular del cliente** (número de teléfono)

#### Ahora (v2.1)
- **Primer envío:** Grupo "Tickets Soluciones" → Información del ticket para el equipo
- **Segundo envío:** Celular del cliente → Mensaje personalizado con saludo cortés

**Ejemplo de mensajes:**
```
[GRUPO] Buen día, asignación de
TICKET N° 123456
NIT: 123456789
RAZÓN SOCIAL: Mi Empresa
NOMBRE DE CONTACTO: Juan Pérez
TELÉFONO DE CONTACTO: 3105003030
CIUDAD: Bogota
OBSERVACIÓN: Descripción de la solicitud

[CLIENTE] Muchas gracias por la información Sr Juan Pérez, 
la solicitud acaba de ser compartida con un asesor el cual 
le contactara pronto, tenga excelente dia, cualquier duda 
estoy atento
```

---

### Errores encontrados y soluciones

#### ❌ Error 1: Click en resultados de búsqueda no funcionaba
**Problema:** El bot escribía el nombre del grupo en la barra de búsqueda, pero el click no abría el chat.  
**Solución:** Usar navegación por teclado (`ArrowDown` + `Enter`) en lugar de clicks en el DOM.

```csharp
// ❌ No funcionaba:
await firstResult.ClickAsync();

// ✅ Funciona:
await searchBox.PressAsync("ArrowDown");
await searchBox.PressAsync("Enter");
```

**Por qué funciona:** La navegación por teclado es más confiable contra cambios en la UI de WhatsApp Web.

#### ❌ Error 2: Escribía en barra de búsqueda (no en el chat)
**Problema:** El selector `[contenteditable='true']` coincidía con múltiples elementos (barra de búsqueda e input del chat).  
**Solución:** Usar `.Last` en lugar de `.First` para seleccionar el compositor del chat abierto.

```csharp
// ❌ Tomaba el primero (barra de búsqueda):
composer = locator.First;

// ✅ Toma el último (input del chat):
composer = locator.Last;
```

**Mejora adicional:** Esperar 3 segundos después de abrir el chat para que WhatsApp Web renderice completamente.

#### ❌ Error 3: Conflicto de compilación (top-level statements)
**Problema:** Error `CS8802: Only one compilation unit can have top-level statements`.  
**Causa:** Crear múltiples archivos `.cs` con top-level statements en el mismo proyecto.  
**Solución:** Eliminar archivo conflictivo y consolidar tests en `Program.cs` con argumentos de línea de comandos.

---

### Nuevos métodos implementados

#### 1. `EnviarWhatsAppWebAGrupoAsync()`
Envía un mensaje a un grupo de WhatsApp.
```csharp
await EnviarWhatsAppWebAGrupoAsync("Tickets Soluciones", mensajeGrupo);
```

#### 2. `EnviarWhatsAppWebAContactoAsync()`
Envía un mensaje personalizado al celular de un cliente.
```csharp
await EnviarWhatsAppWebAContactoAsync(celular, nombreCliente, mensajePersonalizado);
```

#### 3. `ConstruirMensajePersonalizadoCliente()`
Construye el mensaje personalizado con saludo automático (Sr./Sra.).
```csharp
var msg = ConstruirMensajePersonalizadoCliente("Juan Pérez");
// Retorna: "Muchas gracias por la información Sr Juan Pérez..."
```

---

### Características nuevas

✅ **Búsqueda por navegación de teclado** - Más robusta que selectores específicos  
✅ **Detección automática de Sr./Sra.** - Basada en análisis del primer nombre  
✅ **Persistencia mejorada de sesión** - Se guarda después de cada envío en `whatsapp.storage.json`  
✅ **Logs descriptivos** - Cada paso imprime información clara en consola  
✅ **Manejo independiente de errores** - Un envío fallido no bloquea al otro  

---

### Testing

```bash
# Test solo grupo
dotnet run -- --test-whatsapp

# Test dos mensajes (recomendado) ⭐
dotnet run -- --test-whatsapp-dos-mensajes

# API completa
dotnet run
```

**Salida esperada del test dos mensajes:**
```
📤 ENVIANDO MENSAJE 1 AL GRUPO 'Tickets Soluciones'
   ✓ Mensaje escrito y enviado al grupo

📤 ENVIANDO MENSAJE 2 AL CLIENTE (3105003030)
   ✓ Mensaje escrito y enviado a Juan Pérez

✅ PRUEBA COMPLETADA EXITOSAMENTE
```

---

### Configuración requerida

Asegúrate de que `appsettings.json` tenga la configuración de WhatsApp:

```json
"WhatsAppConfig": {
  "BaseUrl": "https://web.whatsapp.com",
  "GroupName": "Tickets Soluciones",           // Nombre del grupo
  "StorageStatePath": "whatsapp.storage.json", // Persistencia de sesión
  "EnsureLoginTimeoutSeconds": 90              // Timeout para escanear QR
}
```

---

### Flujo de ejecución

```
[Crear Solicitud en SIC]
        ↓
[Obtener Ticket]
        ↓
[ENVÍO 1] → Grupo "Tickets Soluciones"
        │   (info del ticket)
        ↓
[ENVÍO 2] → Celular del cliente
        │   (mensaje personalizado)
        ↓
[Guardar sesión]
        ↓
[FIN]
```

---

### Archivos modificados

- `Services/AutomationService.cs` - Nuevos métodos de envío a WhatsApp
- `Program.cs` - Test `--test-whatsapp-dos-mensajes`
- `appsettings.json` - Campo `GroupName` en WhatsAppConfig
- `appsettings.Example.json` - Campo `GroupName` en WhatsAppConfig

---

## Testing / Pruebas

> ⚠️ **NOTA:** La API está configurada para **producción** (`dotnet run` inicia el servidor normalmente).
> Las siguientes secciones documenta cómo ejecutar tests, ahora disponibles solo como comandos históricos.

### Datos de prueba (Referencia)

Para referencia, estos eran los datos de prueba utilizados durante el desarrollo:

```csharp
// Cliente de prueba
string nombreCliente = "Juan Pérez";
string celularCliente = "3105003030";

// Ticket de prueba
string ticketPrueba = "999999";
string nitPrueba = "900000000";
string razonSocialPrueba = "TEST PRUEBA";
string ciudadPrueba = "Bogota";

// Mensaje de prueba al grupo
string mensajeGrupo = @"Buen día, asignación de
TICKET N° 999999
NIT: 900000000
RAZÓN SOCIAL: TEST PRUEBA
NOMBRE DE CONTACTO: Juan Pérez
TELÉFONO DE CONTACTO: 3105003030
CIUDAD: Bogota
OBSERVACIÓN: MENSAJE DE PRUEBA DOS ENVÍOS";

// Mensaje de prueba al cliente
string mensajeCliente = "Muchas gracias por la información sr Juan Pérez, " +
    "la solicitud acaba de ser compartida con un asesor el cual le contactara pronto, " +
    "tenga excelente dia, cualquier duda estoy atento";
```

### Comandos de prueba (Histórico)

Durante el desarrollo se utilizaban estos comandos para validar la funcionalidad:

```bash
# Test 1: Solo envío al grupo
dotnet run -- --test-whatsapp

# Test 2: Doble envío (grupo + cliente)
dotnet run -- --test-whatsapp-dos-mensajes

# API en modo producción (actual)
dotnet run
```

### Flujo de testing utilizado

1. **Primero:** Se testeaba la búsqueda y envío al grupo
2. **Luego:** Se testeaba el envío personalizado al cliente
3. **Finally:** Se validaba que ambos mensajes se enviaran correctamente

### En producción

La API ahora funciona en **modo producción completo**:

```bash
# Inicia el servidor REST normalmente
$ dotnet run

# El servidor escuchará en https://localhost:5001
# Endpoints disponibles:
#   GET    /api/registros
#   POST   /api/registros
#   GET    /api/registros/{id}
```

Cuando se crea un registro via `POST /api/registros`, automáticamente:
1. Se guarda en PostgreSQL
2. Se abre el navegador y rellena el formulario del SIC
3. Se obtiene el ticket
4. Se envía mensaje al grupo "Tickets Soluciones"
5. Se envía mensaje personalizado al cliente
6. Se guarda el estado en la BD

---

## Troubleshooting

### Error: NullReferenceException al leer opciones del select de Ciudad

**Síntoma**

En consola aparecía:

```text
System.NullReferenceException
 at Microsoft.Playwright.Transport.Converters.EvaluateArgumentValueConverter...
 at Microsoft.Playwright.Core.Frame.EvaluateAsync[T]
 at AutomationService.SeleccionarCiudadAsync(...)
```

**Causa (real)**

No era un problema del HTML del SIC. El `<select id="ciudad">` existía, pero **Playwright .NET fallaba deserializando** el resultado de `EvaluateAsync<List<OptionData>>` hacia un tipo C#.

Es un fallo interno durante la conversión/deserialización del resultado de `EvaluateAsync<T>`.

**Cómo se solucionó**

Se cambió la estrategia:

1. En lugar de retornar una lista tipada, el script JS retorna `JSON.stringify(arr)`.
2. En C# se hace `EvaluateAsync<string>` y luego `JsonSerializer.Deserialize<List<OptionData>>()`.

Fragmento clave:

```csharp
var optionsJson = await page.EvaluateAsync<string>(@"() => {
  const select = document.querySelector('#ciudad');
  if (!select) return '[]';
  const arr = Array.from(select.options)
    .filter(o => o.value && o.value !== '')
    .map(o => ({ Text: o.text || '', Value: o.value || '' }));
  return JSON.stringify(arr);
}");

var rawOptions = JsonSerializer.Deserialize<List<OptionData>>(optionsJson);
```

Con esto se evitó pasar por el converter interno que estaba causando el `NullReferenceException`.

---

### Error: column "EstadoAutomatizacion" of relation "Registros" does not exist

Si al hacer `POST /api/registros` ves un error como:

- `42703: column "EstadoAutomatizacion" of relation "Registros" does not exist`

Significa que el **modelo** (`Models/Registro.cs`) tiene campos nuevos, pero la **base de datos** no fue actualizada.

**Solución:** aplicar migraciones.

```bash
dotnet ef database update
```

Notas:

- Existe una migración antigua `AddAutomationStatusFields` que quedó vacía (no aplicaba cambios). La migración que realmente agrega las columnas es `FixAutomationStatusFields`.
- Si ya habías creado la tabla antes de esos cambios, es obligatorio correr el `database update` para que se creen las columnas nuevas.

---

## Sugerencias de mejora

- No guardar secretos en `appsettings.json` (usar variables de entorno/User Secrets).
- Agregar validaciones (`DataAnnotations`) al modelo `Registro`.
- Agregar endpoints `GET` para listar registros.
- Guardar estado de ejecución del bot (éxito/fallo) en base de datos.
- Rellenar campos faltantes del formulario SIC (por ejemplo `#telefono`, `#asignado_a`, `#linea_venta`, etc.), si el flujo lo requiere.

---

## Implementaciones y arreglos (historial)

Esta sección resume problemas reales encontrados en pruebas y cómo se solucionaron, para que quede trazabilidad.

### 1) Integración de nuevos campos SIC

**Requerimientos:**

- `#telefono` siempre debe ser `3105003030`.
- Enviar y mapear:
  - `#medio_contacto`
  - `#asignado_a`
  - `#linea_venta`

**Implementación (bot Playwright):**

- Se fuerza `#telefono = 3105003030`.
- `#medio_contacto`: el SIC a veces lo renderiza como `<input>` (no `<select>`). Por eso se implementó lógica que:
  - detecta el `tagName` y usa `FillAsync(...)` si es input/textarea;
  - usa `SelectOptionAsync(...)` solo si realmente es `<select>`.
- `#asignado_a`: se mapea el **nombre** (ej. `OSCAR FERNANDO`) al `value` esperado del `<select>` (`40`, `9`, etc.).
- `#linea_venta`: se traduce desde el texto (`Venta`, `Mantenimiento`, `Servicio montacargas`, etc.) a `SOLU|SERV|MONT`.

### 2) División de nombre/apellido para el contacto

En SIC existen dos campos:

- `#nombre_contacto`
- `#apellido_contacto`

Reglas aplicadas:

- 1 palabra → solo nombre
- 2 palabras → nombre + 1 apellido
- 3 palabras → 1 nombre + 2 apellidos
- 4 palabras → 2 nombres + 2 apellidos
- >4 → fallback: primeras 2 como nombre, resto como apellidos

Si `#apellido_contacto` no existe en el SIC, el bot **no rompe** el flujo (try/catch).

### 3) Captura y persistencia del ticket generado

Después de dar click a `#guardar_solicitudGestor`, el SIC redirige a `/SolicitudGestor`.

- Se implementó validación en el listado para ubicar la fila por **NIT** (si existe) o por **Empresa**.
- Se captura el valor de la columna **Ticket** y se persiste en Postgres en la columna `Ticket`.

> Importante: para reducir fallos en producción, la búsqueda en el listado es resiliente (esperas + filtro + matching tolerante). Si no hay coincidencia exacta pero hay filas, usa la **primera fila** como fallback y registra un `[WARN]`.

### 4) WhatsApp Web: envío de mensaje y sesión persistida

Se implementó envío de mensajes por **WhatsApp Web** (Playwright):

- La primera vez requiere escanear QR
- Se persiste sesión en `storageState` (config `WhatsAppConfig:StorageStatePath`)
- Se usa URL con parámetro `text`: `https://web.whatsapp.com/send?phone=<E164>&text=<mensaje>`
- WhatsApp Web pre-rellena automáticamente el input
- El bot solo presiona Enter para enviar (sin duplicación)

**Ver archivo `SOLUCION_FINAL_WHATSAPP.md`** para detalles técnicos completos.

### 5) Arreglo: error EF/Postgres por columnas faltantes

**Síntoma:**

- `42703: column "EstadoAutomatizacion" of relation "Registros" does not exist`

**Causa:**

- El modelo `Registro` tenía campos nuevos, pero la BD no se actualizó.
- La migración `AddAutomationStatusFields` quedó vacía.

**Solución:**

- Crear y aplicar migración `FixAutomationStatusFields` y correr:

```bash
dotnet ef database update
```

### 6) Arreglo: error de compilación (atributos duplicados) por carpeta `Tools/`

**Síntoma (CS0579):**

- `Duplicate 'TargetFrameworkAttribute'` y varios `Duplicate 'Assembly...'`.

**Causa:**

- El proyecto web estaba incluyendo accidentalmente fuentes/obj de `Tools/` en la compilación.

**Solución:**

- En `AutomationAPI.csproj` se eliminó la inclusión del proyecto/herramientas y se excluyó `Tools/**` de `Compile/Content/None`.
- Recomendado tras ese cambio:

```bash
dotnet clean
rm -rf bin obj
```
