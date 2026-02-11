# AutomationAPI (Backend)

**Idioma / Language:** Español (este archivo) | [English](./README.en.md)

API REST en **ASP.NET Core (.NET 10)** que:

1. Recibe un registro (ticket) desde un frontend (por ejemplo React).
2. Guarda el registro en **PostgreSQL** usando **Entity Framework Core**.
3. Ejecuta una automatización web con **Microsoft Playwright** para crear un ticket en el **SIC** (portal web externo).

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
- El `storageState` se guarda en el **ContentRoot** del proyecto (no en `bin/Debug/...`) para que realmente persista entre ejecuciones.
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
