# AutomationAPI (Backend)

**Language / Idioma:** English (this file) | [Español](./README.md)

REST API built with **ASP.NET Core (.NET 10)** that:

1. Receives a record (ticket) from a frontend (for example React).
2. Stores the record in **PostgreSQL** using **Entity Framework Core**.
3. Runs a web automation flow with **Microsoft Playwright** to create a ticket in **SIC** (external web portal).

> Project location: `Backend/AutomationAPI`

---

## Table of contents

- [Architecture](#architecture)
- [Endpoints](#endpoints)
- [Data model](#data-model)
- [Configuration](#configuration)
- [Dependencies](#dependencies)
- [Install and run](#install-and-run)
- [Migrations / Database](#migrations--database)
- [Playwright (browser installation)](#playwright-browser-installation)
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

## Short changelog (recent changes)

### Fix: DI error (scoped DbContextOptions vs singleton pool)

If running `dotnet run` failed with an error like:

- `Cannot consume scoped service 'DbContextOptions<AppDbContext>' from singleton ... (DbContextPool/DbContextFactory)`

The fix was to **avoid mixing** `AddDbContext(...)` with `AddDbContextFactory/AddPooledDbContextFactory` in a way that ends up registering `DbContextOptions<T>` as *scoped* while the pool/factory is *singleton*.

**Current recommended setup:**

- Use `AddPooledDbContextFactory<AppDbContext>(...)` (safe for background jobs / singletons).
- Register `AppDbContext` as *scoped* for controllers, created from the factory.

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

1) Restore and build

```bash
cd Backend/AutomationAPI
dotnet restore
dotnet build
```

2) Configure `appsettings.json` (or use `appsettings.Development.json`)

Suggested: copy from the example file:

```bash
cp appsettings.Example.json appsettings.Development.json
```

3) Run

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

1) Build first so Playwright scripts are generated:

```bash
cd Backend/AutomationAPI
dotnet build
```

2) Install system dependencies (recommended on Linux) and then install browsers:

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
- >4 → fallback: first 2 as name, rest as last names

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
