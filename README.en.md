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
- [Suggested improvements](#suggested-improvements)

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

## Suggested improvements

- Don’t store secrets in `appsettings.json` (use env vars/User Secrets).
- Add validation (`DataAnnotations`) to `Registro`.
- Add `GET` endpoints to list records.
- Persist bot execution status (success/failure) in the database.
- Fill additional SIC form fields if required (for example `#telefono`, `#asignado_a`, `#linea_venta`, etc.).
