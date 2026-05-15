# AutomationAPI (Backend)

**Language / Idioma:** English (this file) | [Español](./README.md)

REST API built with **ASP.NET Core (.NET 10)** that:

1. Receives a record (ticket) from a frontend (for example React).
2. Stores the record in **PostgreSQL** using **Entity Framework Core**.
3. Runs a web automation flow with **Microsoft Playwright** to create a ticket in **SIC** (external web portal).
4. **[NEW v2.1]** Sends two WhatsApp messages: one to the "Tickets Soluciones" group and one personalized to the client.

> Project location: `Backend/AutomationAPI`

---

## Table of contents

- [Architecture](#architecture)
- [Duplicate search](#duplicate-search)
- [Ticket creation validation](#ticket-creation-validation)
- [Endpoints](#endpoints)
- [Data model](#data-model)
- [Configuration](#configuration)
- [Dependencies](#dependencies)
- [Install and run](#install-and-run)
- [Migrations / Database](#migrations--database)
- [Playwright (browser installation)](#playwright-browser-installation)
- [**WhatsApp Automation v2.1 (Changes)**](#whatsapp-automation-v21-changes) ⭐ **NEW**
- [Troubleshooting](#troubleshooting)
  - [Error: NullReferenceException while reading City select options](#error-nullreferenceexception-while-reading-city-select-options)
  - [Error: column "EstadoAutomatizacion" of relation "Registros" does not exist](#error-column-estadoautomatizacion-of-relation-registros-does-not-exist)
- [Suggested improvements](#suggested-improvements)
- [Implementations & fixes (history)](#implementations--fixes-history)

---

## Architecture

High-level flow:

1. The frontend calls `POST /api/registros` sending a JSON payload.
2. The API persists the record into PostgreSQL.
3. A Playwright bot is triggered in the background to log into SIC and fill out the form.

Main components:

- `Program.cs`: service setup (EF Core, CORS, Swagger, DI).
- `Controllers/RegistrosController.cs`: `POST` endpoint.
- `Models/Registro.cs`: EF Core entity.
- `Data/AppDbContext.cs`: DbContext with `DbSet<Registro>`.
- `Services/AutomationService.cs`: Playwright bot.

---

## Duplicate search

Before creating the ticket, the bot determines `TipoCliente` using an automated search in the requests list:

1. By NIT (exact)
2. By Company (tolerant)
3. By Phone (exact)

Rule:

- If any match has a valid invoice, it sets `TipoCliente = "Antiguo"` and stops.
- If no valid invoice is found, it sets `TipoCliente = "Nuevo"`.

Technical details:

- Uses the list filter (`input#nombre[name='buscar']`) and checks up to 2 pages (current + next).
- Uses the row "Ver" link URL to open the ticket (href in actions column), not a constructed URL.
- Validates the `#factura` field in the ticket view.

---

## Ticket creation validation

After clicking `#guardar_solicitudGestor`, the bot validates the SweetAlert result to confirm ticket creation.

- On success: continues the flow and keeps the message for the frontend.
- On error: stops the flow, saves the message, and does not continue to ticket search or WhatsApp.

Note: WhatsApp logic is unchanged; it only runs after successful ticket creation.

---

## Short changelog (recent changes)

### Fix: DI error (scoped DbContextOptions vs singleton pool)

If running `dotnet run` failed with an error like:

- `Cannot consume scoped service 'DbContextOptions<AppDbContext>' from singleton ... (DbContextPool/DbContextFactory)`

The fix was to **avoid mixing** `AddDbContext(...)` with `AddDbContextFactory/AddPooledDbContextFactory` in a way that ends up registering `DbContextOptions<T>` as _scoped_ while the pool/factory is _singleton_.

**Current recommended setup:**

- Use `AddPooledDbContextFactory<AppDbContext>(...)` (safe for background jobs / singletons).
- Register `AppDbContext` as _scoped_ for controllers, created from the factory.

This is implemented in `Program.cs` (summary):

- `AddPooledDbContextFactory<AppDbContext>(...)`
- `AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())`

---

### WhatsApp Web (persisted session)

The bot can also send a message via **WhatsApp Web** using Playwright.

- The first time it opens `https://web.whatsapp.com` and you must scan the QR.
- The session is stored as a `storageState` file and reused on next runs.

Configuration in `appsettings.json`:

- `WhatsAppConfig:SendTo` → destination phone (fixed) in E.164 digits without `+` (e.g. `573105003030`).
- `WhatsAppConfig:StorageStatePath` → storageState file path (e.g. `whatsapp.storage.json`).

Important notes:

- To avoid the browser trying to open the native deep link `whatsapp://send` on Linux (which may fail if no URI handler is configured), the bot opens the chat using **web only**:
  - `https://web.whatsapp.com/send?phone=<E164>&text=<message>`
- The `storageState` file is saved under the project's **ContentRoot** (not `bin/Debug/...`) so it truly persists across runs.

---

## Endpoints

### `POST /api/registros`

Persists the record and starts the automation in the background.

**Request body** (example):

```json
{
  "nit": "900123456",
  "empresa": "My Company SAS",
  "ciudad": "Bogota",
  "cliente": "John Doe",
  "celular": "3001234567",
  "correo": "john@company.com",
  "tipoCliente": "Nuevo",
  "concepto": "Service request"
}
```

**Response** (example):

```json
{ "message": "Saved and automation started", "id": 38 }
```

### `GET /api/registros/{id}`

Fetch automation status and message for frontend polling.

**Response** (example):

```json
{
  "id": 38,
  "estado": "COMPLETADO",
  "mensaje": "Correcto: Datos actualizados exitosamente",
  "ticket": "16076",
  "tipoCliente": "Antiguo"
}
```

---

## Data model

Entity: `Models/Registro.cs`

Main fields:

- `Nit`
- `Empresa`
- `Ciudad`
- `Cliente`
- `Celular`
- `Correo`
- `TipoCliente` (text: `Nuevo`, `Antiguo`, `Fidelizado`, `Recuperado`)
- `Concepto`
- `FechaCreacion` (UTC)

---

## Configuration

The API reads configuration from `appsettings.json`.

Keys used:

- `ConnectionStrings:DefaultConnection` → PostgreSQL connection
- `SicConfig:BaseUrl` → SIC base URL
- `SicConfig:User` → username
- `SicConfig:Password` → password

Files:

- `appsettings.json`: real configuration (⚠️ may contain credentials if you keep it this way).
- `appsettings.Example.json`: template without secrets.

> Recommendation: use `appsettings.Development.json` locally + environment variables / User Secrets.

---

## Dependencies

From `AutomationAPI.csproj`:

- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Design`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Swashbuckle.AspNetCore` (Swagger)
- `Microsoft.Playwright`

System requirements:

- **.NET SDK 10**
- **PostgreSQL**
- Playwright/Chromium system dependencies (installed by Playwright)

---

## Install and run

1. Restore and build

```bash
cd Backend/AutomationAPI
dotnet restore
dotnet build
```

2. Configure `appsettings.json` (or use `appsettings.Development.json`)

Suggested: copy from the example file:

```bash
cp appsettings.Example.json appsettings.Development.json
```

3. Run

```bash
dotnet run
```

Swagger (in Development):

- `http://localhost:5016/swagger`

> Default ports are defined in `Properties/launchSettings.json`.

---

## Migrations / Database

The project includes an initial migration under `Migrations/`.

To create/update the database schema:

```bash
dotnet ef database update
```

> Note: requires the EF tooling. If you don’t have it:

```bash
dotnet tool install --global dotnet-ef
```

---

## Playwright (browser installation)

> Important: Playwright needs to install browsers the first time.

### Linux (recommended)

1. Build first so Playwright scripts are generated:

```bash
cd Backend/AutomationAPI
dotnet build
```

2. Install system dependencies (recommended on Linux) and then install browsers:

```bash
./bin/Debug/net10.0/playwright.sh install-deps
./bin/Debug/net10.0/playwright.sh install
```

> Note: `install-deps` installs system libraries required by Chromium/WebKit/Firefox.

### Windows

In PowerShell:

```powershell
cd Backend/AutomationAPI
dotnet build
.\bin\Debug\net10.0\playwright.ps1 install
```

If scripts are blocked by execution policy, run (same session):

```powershell
Set-ExecutionPolicy Bypass -Scope Process
```

### Linux using PowerShell (optional)

If you have `pwsh` installed on Linux:

```bash
cd Backend/AutomationAPI
dotnet build
pwsh ./bin/Debug/net10.0/playwright.ps1 install
```

---

## WhatsApp Automation v2.1 (Changes)

### Summary of main changes

**Version 2.1 (February 11, 2026):** Implementation of **dual WhatsApp messaging**

#### Before (v2.0)

- Sent a single message to **client's phone** (phone number)

#### Now (v2.1)

- **First send:** Group "Tickets Soluciones" → Ticket information for the team
- **Second send:** Client's phone → Personalized message with courteous greeting

**Example messages:**

```
[GROUP] Good morning, assignment of
TICKET No. 123456
NIT: 123456789
COMPANY NAME: My Company
CONTACT NAME: Juan Pérez
CONTACT PHONE: 3105003030
CITY: Bogota
OBSERVATION: Request description

[CLIENT] Thank you very much for the information Mr Juan Pérez,
the request has just been shared with an advisor who will contact
you soon, have a great day, if you have any questions I'm here
```

---

### Errors found and solutions

#### ❌ Error 1: Click on search results didn't work

**Problem:** Bot typed the group name in the search bar, but clicking didn't open the chat.  
**Solution:** Use keyboard navigation (`ArrowDown` + `Enter`) instead of DOM clicks.

```csharp
// ❌ Didn't work:
await firstResult.ClickAsync();

// ✅ Works:
await searchBox.PressAsync("ArrowDown");
await searchBox.PressAsync("Enter");
```

**Why it works:** Keyboard navigation is more reliable against WhatsApp Web UI changes.

#### ❌ Error 2: Typed in search bar (not in chat)

**Problem:** Selector `[contenteditable='true']` matched multiple elements (search bar and chat input).  
**Solution:** Use `.Last` instead of `.First` to select the open chat composer.

```csharp
// ❌ Took first (search bar):
composer = locator.First;

// ✅ Takes last (chat input):
composer = locator.Last;
```

**Additional improvement:** Wait 3 seconds after opening chat for WhatsApp Web to fully render.

#### ❌ Error 3: Compilation conflict (top-level statements)

**Problem:** Error `CS8802: Only one compilation unit can have top-level statements`.  
**Cause:** Multiple `.cs` files with top-level statements in same project.  
**Solution:** Remove conflicting file and consolidate tests in `Program.cs` with command-line arguments.

---

### New methods implemented

#### 1. `EnviarWhatsAppWebAGrupoAsync()`

Sends a message to a WhatsApp group.

```csharp
await EnviarWhatsAppWebAGrupoAsync("Tickets Soluciones", groupMessage);
```

#### 2. `EnviarWhatsAppWebAContactoAsync()`

Sends a personalized message to a client's phone.

```csharp
await EnviarWhatsAppWebAContactoAsync(phone, clientName, personalizedMessage);
```

#### 3. `ConstruirMensajePersonalizadoCliente()`

Builds the personalized message with automatic greeting (Mr./Ms.).

```csharp
var msg = ConstruirMensajePersonalizadoCliente("Juan Pérez");
// Returns: "Thank you very much for the information Mr Juan Pérez..."
```

---

### New features

✅ **Keyboard navigation search** - More robust than specific selectors  
✅ **Automatic Mr./Ms. detection** - Based on first name analysis  
✅ **Improved session persistence** - Saved after each send in `whatsapp.storage.json`  
✅ **Descriptive logs** - Each step prints clear console information  
✅ **Independent error handling** - One send failure doesn't block the other

---

### Testing

```bash
# Test group only
dotnet run -- --test-whatsapp

# Test two messages (recommended) ⭐
dotnet run -- --test-whatsapp-dos-mensajes

# Full API
dotnet run
```

**Expected output from two messages test:**

```
📤 SENDING MESSAGE 1 TO GROUP 'Tickets Soluciones'
   ✓ Message written and sent to group

📤 SENDING MESSAGE 2 TO CLIENT (3105003030)
   ✓ Message written and sent to Juan Pérez

✅ TEST COMPLETED SUCCESSFULLY
```

---

### Required configuration

Make sure `appsettings.json` has WhatsApp configuration:

```json
"WhatsAppConfig": {
  "BaseUrl": "https://web.whatsapp.com",
  "GroupName": "Tickets Soluciones",           // Group name
  "StorageStatePath": "whatsapp.storage.json", // Session persistence
  "EnsureLoginTimeoutSeconds": 90              // QR scan timeout
}
```

---

### Execution flow

```
[Create Request in SIC]
        ↓
[Get Ticket]
        ↓
[SEND 1] → Group "Tickets Soluciones"
        │   (ticket information)
        ↓
[SEND 2] → Client's phone
        │   (personalized message)
        ↓
[Save session]
        ↓
[DONE]
```

---

### Modified files

- `Services/AutomationService.cs` - New WhatsApp send methods
- `Program.cs` - Test `--test-whatsapp-dos-mensajes`
- `appsettings.json` - `GroupName` field in WhatsAppConfig
- `appsettings.Example.json` - `GroupName` field in WhatsAppConfig

---

## Troubleshooting

### Error: NullReferenceException while reading City select options

**Symptom**

Console output like:

```text
System.NullReferenceException
 at Microsoft.Playwright.Transport.Converters.EvaluateArgumentValueConverter...
 at Microsoft.Playwright.Core.Frame.EvaluateAsync[T]
 at AutomationService.SeleccionarCiudadAsync(...)
```

**Root cause**

This was not an SIC HTML issue. The `<select id="ciudad">` existed, but **Playwright .NET failed while deserializing** the result of `EvaluateAsync<List<OptionData>>` into a C# type.

This is an internal failure during `EvaluateAsync<T>` result conversion.

**Fix / Workaround**

Strategy was changed:

1. The JS snippet returns `JSON.stringify(arr)`.
2. C# uses `EvaluateAsync<string>` and then `JsonSerializer.Deserialize<List<OptionData>>()`.

Key snippet:

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

This avoids the failing internal converter.

---

### Error: column "EstadoAutomatizacion" of relation "Registros" does not exist

If `POST /api/registros` fails with an error like:

- `42703: column "EstadoAutomatizacion" of relation "Registros" does not exist`

It means the **model** (`Models/Registro.cs`) has new fields but the **database schema** wasn't updated.

**Fix:** apply migrations.

```bash
dotnet ef database update
```

Notes:

- There is an older migration `AddAutomationStatusFields` that ended up empty (no schema changes). The migration that actually adds the columns is `FixAutomationStatusFields`.
- If you created the table before these changes, running `database update` is mandatory.

---

## Suggested improvements

- Don’t store secrets in `appsettings.json` (use env vars/User Secrets).
- Add validation (`DataAnnotations`) to `Registro`.
- Add `GET` endpoints to list records.
- Persist bot execution status (success/failure) in the database.
- Fill additional SIC form fields if required (for example `#telefono`, `#asignado_a`, `#linea_venta`, etc.).

---

## Implementations & fixes (history)

This section documents real issues found during testing and the fixes implemented.

### 1) SIC new fields integration

**Requirements:**

- `#telefono` must always be `3105003030`.
- Send/map:
  - `#medio_contacto`
  - `#asignado_a`
  - `#linea_venta`

**Implementation (Playwright bot):**

- Force `#telefono = 3105003030`.
- `#medio_contacto`: SIC may render it as an `<input>` (not a `<select>`). The bot now:
  - checks `tagName` and uses `FillAsync(...)` for input/textarea;
  - only uses `SelectOptionAsync(...)` when it is actually a `<select>`.
- `#asignado_a`: maps the **name** (e.g. `OSCAR FERNANDO`) to the expected `<select>` value (`40`, `9`, etc.).
- `#linea_venta`: converts human text (`Venta`, `Mantenimiento`, `Servicio montacargas`, etc.) into `SOLU|SERV|MONT`.

### 2) Split contact name / last name

SIC has two fields:

- `#nombre_contacto`
- `#apellido_contacto`

Rules:

- 1 word → first name only
- 2 words → name + 1 last name
- 3 words → 1 name + 2 last names
- 4 words → 2 names + 2 last names
- > 4 → fallback: first 2 as name, rest as last names

If `#apellido_contacto` does not exist in SIC, the bot won’t break the flow (try/catch).

### 3) Capture and persist the generated ticket

After clicking `#guardar_solicitudGestor`, SIC redirects to `/SolicitudGestor`.

- The bot validates the row using **NIT** (if present) or **Empresa**.
- It captures the **Ticket** column and persists it in Postgres in the `Ticket` field.

> Important: to reduce production failures, the list lookup is resilient (waits + filter + tolerant matching). If there is no exact match but there are rows, it uses the **first row** as a fallback and logs a `[WARN]`.

### 4) WhatsApp Web: message sending + persisted session

WhatsApp sending via **WhatsApp Web** (Playwright):

- First run requires scanning the QR.
- Session is persisted using `storageState` (`WhatsAppConfig:StorageStatePath`).
- Forces web navigation (no `whatsapp://send`) using:
  - `https://web.whatsapp.com/send?phone=<E164>&text=<message>`

Multi-line message:

- Writes line by line and inserts a line break with `Shift+Enter`.

Group/chat sending:

- If `WhatsAppConfig:GroupName` is set, it searches that name in WhatsApp Web and clicks the **first result**.

### 5) Fix: EF/Postgres missing columns

**Symptom:**

- `42703: column "EstadoAutomatizacion" of relation "Registros" does not exist`

**Cause:**

- `Registro` model had new fields but the DB schema wasn’t updated.
- The migration `AddAutomationStatusFields` ended up empty.

**Fix:**

- Create/apply `FixAutomationStatusFields` migration and run:

```bash
dotnet ef database update
```

### 6) Fix: build errors (duplicate attributes) caused by `Tools/` folder

**Symptom (CS0579):**

- `Duplicate 'TargetFrameworkAttribute'` and several `Duplicate 'Assembly...'`.

**Cause:**

- The web project was accidentally compiling sources/obj files from `Tools/`.

**Fix:**

- Updated `AutomationAPI.csproj` to remove accidental inclusion and exclude `Tools/**` from `Compile/Content/None`.
- Recommended after the change:

```bash
dotnet clean
rm -rf bin obj
```
