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
- [Sugerencias de mejora](#sugerencias-de-mejora)

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

## Sugerencias de mejora

- No guardar secretos en `appsettings.json` (usar variables de entorno/User Secrets).
- Agregar validaciones (`DataAnnotations`) al modelo `Registro`.
- Agregar endpoints `GET` para listar registros.
- Guardar estado de ejecución del bot (éxito/fallo) en base de datos.
- Rellenar campos faltantes del formulario SIC (por ejemplo `#telefono`, `#asignado_a`, `#linea_venta`, etc.), si el flujo lo requiere.
