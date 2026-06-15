# Implementeringsdetaljer og beste praksis
## SMART on FHIR + Altinn Studio — Legeerklæring førerrett

**Dato:** 2026-06-15  
**Basert på:** PoC med Altinn App API 8.6.4, HAPI FHIR R4, SMART App Launch IG v2.2.0

---

## Innhold

1. [Komponentoversikt](#1-komponentoversikt)
2. [Komponent 1: HAPI FHIR R4 (testserver)](#2-komponent-1-hapi-fhir-r4)
3. [Komponent 2: SMART Auth Mock](#3-komponent-2-smart-auth-mock)
4. [Komponent 3: Altinn Local Test](#4-komponent-3-altinn-local-test)
5. [Komponent 4: Altinn .NET App](#5-komponent-4-altinn-net-app)
6. [Nettverksruting](#6-nettverksruting)
7. [Beste praksis](#7-beste-praksis)
8. [Kjente fallgruver](#8-kjente-fallgruver)

---

## 1. Komponentoversikt

Løsningen består av fire separate komponenter som kommuniserer over nettverk. I lokalt utviklingsmiljø kjøres tre av dem i Podman-containere (via docker-compose), og to kjøres direkte på Windows.

```
┌─────────────────────────────────────────────────────┐
│ WINDOWS HOST                                        │
│  ┌──────────────────┐   ┌──────────────────────┐   │
│  │ Altinn .NET App  │   │  SMART Auth Mock      │   │
│  │ localhost:5005   │   │  localhost:9090        │   │
│  └──────────────────┘   └──────────────────────┘   │
└────────────────────────────────── 172.30.80.1 ──────┘
         ↑ port-forward :8000, :8080, :5101
┌─────────────────────────────────────────────────────┐
│ WSL2 / PODMAN (podman-machine-default)              │
│  ┌──────────┐ ┌──────────┐ ┌──────┐ ┌───────────┐  │
│  │  nginx   │ │  Local   │ │ PDF  │ │   HAPI    │  │
│  │  :80→    │ │  Test    │ │  3   │ │   FHIR    │  │
│  │  host    │ │  :5101   │ │:5031 │ │   :8080   │  │
│  │  :8000   │ │          │ │      │ │           │  │
│  └──────────┘ └──────────┘ └──────┘ └───────────┘  │
└─────────────────────────────────────────────────────┘
```

**Diagram:** Se [nettverksruting.svg](./nettverksruting.svg)

---

## 2. Komponent 1: HAPI FHIR R4

### Hva det er
[HAPI FHIR](https://hapifhir.io/) er en åpen kildekode Java-implementasjon av HL7 FHIR-standarden. Den fungerer som en fullstendig FHIR R4-server med RESTful API, søk, validering og persistens.

**I dette prosjektet** brukes HAPI som en enkel testserver som simulerer EPJ-systemets FHIR API. I produksjon erstattes denne av det ekte FHIR-endepunktet i EPJ (f.eks. DIPS Arena FHIR API).

### Hvor det kjøres
```yaml
# app-localtest/docker-compose.yml
hapi_fhir:
  image: hapiproject/hapi:latest
  ports:
    - "8080:8080"
  environment:
    - hapi.fhir.fhir_version=R4
    - hapi.fhir.allow_multiple_delete=true
```

### Testdata
Testdata lastes inn via `fhir-testdata/seed.ps1`. Scriptet bruker HTTP PUT for å opprette ressurser med kjente ID-er:

| Ressurs | ID | Innhold |
|---|---|---|
| `Patient` | `sophie-salt` | Fnr 01039012345, navn Sophie Salt |
| `Practitioner` | `lege-ola` | HPR 1234567, navn Ola Nordmann |
| `Organization` | `sandvika-legesenter` | Orgnr 987654321, HER-id 8141253 |
| `Encounter` | `enc-sophie-001` | Kobler pasient, lege og org |
| `Condition` | `cond-sophie-001` | ICD-10: R55 Synkope |

Kjør seedingen:
```powershell
cd C:\Users\jsf\source\app-localtest\fhir-testdata
.\seed.ps1
```

### Nås fra
- **.NET-appen** (Windows): `http://localhost:8080/fhir`
- **SMART Mock** (Windows): `http://localhost:8080`
- **Nettleser** (diagnostikk): `http://localhost:8080/fhir/Patient/sophie-salt`

> **Merk:** `172.30.80.1:8080` brukes når containere skal nå HAPI. `.NET`-appen på Windows bruker `localhost:8080`.

---

## 3. Komponent 2: SMART Auth Mock

### Hva det er
En Node.js/Express-server som simulerer EPJ-systemets SMART autorisasjonsserver. Den implementerer de delene av [SMART App Launch IG v2.2.0](https://hl7.org/fhir/smart-app-launch/) som trengs for EHR Launch-flyten.

**I produksjon** erstattes denne av EPJ-leverandørens ekte SMART-server (f.eks. DIPS sin SMART-implementasjon). Mocken fjernes helt.

### Hvor det kjøres
```
Sti:  app-localtest/fhir-testdata/smart-mock/server.js
Port: 9090 (Windows)
Start: node server.js
```

### Endepunkter

#### `GET /.well-known/smart-configuration`
SMART discovery-endepunkt. Returnerer metadata om auth-serveren:
```json
{
  "issuer": "http://localhost:9090",
  "authorization_endpoint": "http://localhost:9090/auth",
  "token_endpoint": "http://localhost:9090/token",
  "capabilities": ["launch-ehr", "client-confidential-symmetric", ...]
}
```
Altinn-appen kaller dette automatisk under `/smart/launch` for å finne auth- og token-URL-ene.

#### `GET /auth`
Simulerer EPJ-innloggingssiden. I en ekte EPJ vil legen måtte autentisere seg her. Mocken utsteder autorisasjonskode umiddelbart uten brukerinteraksjon.

Mottar: `redirect_uri`, `state`, `launch`, `code_challenge`  
Returnerer: Redirect til `redirect_uri?code=<kode>&state=<state>`

#### `POST /token`
Veksler autorisasjonskode mot access token med SMART-spesifikke claims:
```json
{
  "access_token": "mock-token-abc123",
  "token_type": "Bearer",
  "expires_in": 3600,
  "patient": "sophie-salt",
  "encounter": "enc-sophie-001",
  "fhirUser": "http://172.30.80.1:8080/fhir/Practitioner/lege-ola"
}
```
`patient` og `encounter` er SMART-spesifikke felt som forteller appen hvilken kontekst som gjelder.

`fhirUser` er definert i SMART App Launch IG v2.2.0 som et eget toppnivåfelt i tokenresponsen — og det er slik denne mocken returnerer det. **Merk:** Noen produksjons-EPJ-systemer returnerer `fhirUser` som en claim inne i `access_token` JWT-en (ikke som eget toppnivåfelt). Koden bør da dekode JWT-en server-side og lese ut `fhirUser`-claimet derfra. Se `SmartLaunchController.cs` → `TokenResponse.FhirUser`.

#### `GET /fhir/*`
Proxyer alle FHIR-kall videre til HAPI FHIR på `localhost:8080`. Brukes ikke av .NET-appen direkte (den går til HAPI direkte via `FhirBaseUrlOverride`), men nyttig for testing via nettleser.

### Konfigurasjon
```javascript
const HAPI_FHIR = "http://localhost:8080";   // Lokal HAPI
const HOST_IP = "172.30.80.1";              // Windows-host sett fra containere
// fhirUser-URL bruker HOST_IP slik at .NET-appen kan nå ressursen
// .NET-appen bruker FhirBaseUrlOverride (localhost:8080), ikke fhirUser-URL direkte
```

### Avhengigheter
```bash
cd app-localtest/fhir-testdata/smart-mock
npm install    # express, http-proxy-middleware
node server.js
```

---

## 4. Komponent 3: Altinn Local Test

### Hva det er
[app-localtest](https://github.com/Altinn/app-localtest) er Altinns offisielle lokale testmiljø. Det simulerer Altinn-plattformen lokalt med docker-compose og gir et komplett miljø for app-utvikling uten tilgang til Altinn-skyen.

**I produksjon** erstattes dette av Altinn-plattformens tjenester i skyen. Appen deployes til Kubernetes i Altinn-miljøet.

### Containere

#### `localtest-loadbalancer` (nginx)
Fungerer som inngangspunkt for all trafikk på port 8000. Ruter basert på hostname og path:

| Request | Rutes til |
|---|---|
| `local.altinn.cloud:8000/{org}/{app}/api/...` | `172.30.80.1:5005` (.NET app) |
| `local.altinn.cloud:8000/{org}/{app}/` | `172.30.80.1:5005` (.NET app) |
| `local.altinn.cloud:8000/localtestresources/...` | `host.docker.internal` (localtest) |
| `local.altinn.cloud:8000/authentication/...` | `host.docker.internal` (localtest) |

Viktig konfigurasjon i `docker-compose.yml`:
```yaml
environment:
  - HOST_DOMAIN=172.30.80.1           # Windows-host IP (app kjører her)
  - INTERNAL_DOMAIN=host.docker.internal  # nginx→localtest (container-intern)
  - ALTINN3LOCAL_PORT=8000            # Eksponert port (rootless Podman ≥ 1024)
```

> **Fallgruve:** `HOST_DOMAIN` og `INTERNAL_DOMAIN` er forskjellige. nginx ruter appen til Windows-host-IP, og plattform-API-et til container-internt nettverk.

#### `localtest` (Altinn Platform)
Simulerer Altinn-plattformens API-er:
- **Storage** (`:5101/storage`): Instanser, data-elementer, prosess
- **Authentication** (`:5101/authentication`): JWT-validering, OpenID Connect
- **Authorization** (`:5101/authorization`): PDP-beslutninger (tillat/avslå)
- **Register** (`:5101/register`): Organisasjoner og personer
- **Profile** (`:5101/profile`): Brukerprofiler

Konfigurasjon som brukes av .NET-appen (`appsettings.Development.json`):
```json
"PlatformSettings": {
  "ApiStorageEndpoint": "http://localhost:5101/storage/api/v1/",
  "ApiAuthenticationEndpoint": "http://localhost:5101/authentication/api/v1/",
  "ApiAuthorizationEndpoint": "http://localhost:5101/authorization/api/v1/"
}
```

#### `localtest-pdf3`
Genererer PDF fra Altinn-skjemaer ved innsending. Bruker Chromium headless. Eksponeres på port 5300 på Windows-host.

#### `hapi-fhir`
HAPI FHIR R4-server (se komponent 1 over).

### Start/stopp
```powershell
# Starte (fra app-localtest-mappen)
# Brukes via Podman Desktop GUI, eller:
$env:ALTINN3LOCAL_PORT = "8000"
# Podman Desktop håndterer compose automatisk
```

---

## 5. Komponent 4: Altinn .NET App

### Hva det er
Selve Altinn-applikasjonen. Bygget med Altinn App API 8.6.4 på .NET 8. Denne er den eneste komponenten som vil eksistere i produksjon (de andre er enten plattform eller testes-erstattet).

**Sti:** `forer-legeerklaering/src/App/`

### Nøkkelfiler

| Fil | Formål |
|---|---|
| `Program.cs` | DI-registrering, middleware-oppsett |
| `controllers/SmartLaunchController.cs` | SMART EHR Launch-flyt |
| `services/FhirPrefillService.cs` | `IDataProcessor` — henter og mapper FHIR-data |
| `models/ForerLegeerklaeringModel.cs` | Datamodell (XML/JSON) |
| `ui/form/layouts/Side1.json` | Skjema-layout (Altinn UI) |
| `config/applicationmetadata.json` | App-metadata og datatype-konfig |
| `options/kjoretoygrupper.json` | Kodeverk for kjøretøygrupper |
| `appsettings.Development.json` | Lokal konfigurasjon |

### SmartLaunchController

**Rute:** `[Route("{org}/{app}/smart")]`  
**Auth:** `[AllowAnonymous]` — *kritisk*, Altinn JWT-middleware vil ellers blokkere

Endepunkter:

| Endepunkt | Formål |
|---|---|
| `GET /smart/launch` | EHR Launch entry point. Leser `iss`+`launch`, oppdager SMART-config, genererer PKCE, sender til auth |
| `GET /smart/callback` | OAuth callback. Veksler kode → token server-side, lagrer i session |
| `GET /smart/test-prefill` | **Kun lokal testing.** Bypasser OAuth, seeder session direkte med testdata |

### FhirPrefillService

Implementerer `IDataProcessor`. Kalles av Altinn når skjemadata leses (`ProcessDataRead`).

**Registrering i Program.cs:**
```csharp
services.AddTransient<IDataProcessor, FhirPrefillService>();
```

**Hva den gjør:**
1. Leser `smart_token` og `smart_fhir_context` fra server-session
2. Hvis session er tom: sjekker `IMemoryCache` (fallback)
3. Kaller HAPI FHIR for Patient, Practitioner, Encounter, Organization, Condition
4. Mapper FHIR-JSON til `ForerLegeerklaeringModel`

**Viktig:** `await session.LoadAsync()` må kalles eksplisitt før `session.GetString()`.

### Program.cs — middleware-rekkefølge

```csharp
// Registrering (rekkefølge er likegyldig)
services.AddMemoryCache();
services.AddDistributedMemoryCache();
services.AddSession(options => {
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // ikke Always!
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
services.AddTransient<IDataProcessor, FhirPrefillService>();

// Pipeline (rekkefølge er KRITISK)
app.UseSession();                      // MÅ komme FØR
app.UseAltinnAppCommonConfiguration(); // Altinn-middleware
```

### Start
```powershell
cd C:\Users\jsf\source\forer-legeerklaering\src\App
dotnet run
# Lytter på http://localhost:5005 og https://localhost:5006
```

---

## 6. Nettverksruting

Se [nettverksruting.svg](./nettverksruting.svg) for fullstendig diagram.

### Port-oversikt

| Port | Tjeneste | Tilgjengelig fra |
|---|---|---|
| 8000 | nginx (via Podman port-forward) | Nettleser, Windows |
| 5005 | .NET App (Windows) | nginx (via 172.30.80.1), localhost |
| 5101 | Altinn Local Test (container) | .NET App (via localhost) |
| 5300 | PDF Generator (via Podman port-forward) | .NET App |
| 8080 | HAPI FHIR (via Podman port-forward) | .NET App, SMART Mock |
| 9090 | SMART Auth Mock (Windows) | .NET App, nettleser |

### IP-adresse-logikk

| Fra → Til | IP som brukes |
|---|---|
| .NET App → HAPI FHIR | `localhost:8080` |
| .NET App → Altinn Local Test | `localhost:5101` |
| nginx-container → .NET App | `172.30.80.1:5005` |
| Altinn Local Test → .NET App | `172.30.80.1:5005` |
| Nettleser → Alt | `local.altinn.cloud:8000` (→ hosts-fil → localhost) |

### hosts-fil (Windows)
```
# C:\Windows\System32\drivers\etc\hosts
127.0.0.1  local.altinn.cloud
```

---

## 7. Beste praksis

### 7.1 Session og token-håndtering

```csharp
// Alltid last session eksplisitt før bruk
await session.LoadAsync();
var token = session.GetString("smart_token");

// Bruk IMemoryCache som fallback
if (string.IsNullOrEmpty(token) && session != null) {
    _memoryCache.TryGetValue("smart_fhir_" + session.Id, out CachedFhirData cached);
}
```

**Aldri** lagre access token i:
- URL-parametere
- localStorage / sessionStorage
- Response-body til nettleser

### 7.2 AllowAnonymous på SMART-endepunkter

Altinn registrerer JWT-cookie-autentisering som default for alle endepunkter. SMART-endepunkter må eksplisitt unntas:

```csharp
[AllowAnonymous]
[ApiController]
[Route("{org}/{app}/smart")]
public class SmartLaunchController : ControllerBase { ... }
```

Uten dette får du redirect-loop: Altinn sender uautentiserte requests til innlogging, som redirecter til SMART-launch, som redirecter til Altinn, osv.

### 7.3 CookieSecurePolicy i lokalt miljø

```csharp
// Lokalt (HTTP): SameAsRequest
// Produksjon (HTTPS): Always
options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
```

### 7.4 Nginx og URL-parametere med `://`

Nginx i Altinn Local Test stripper query-parametere som inneholder `://` i verdien. Dette betyr at `?iss=http://localhost:9090` vil miste `iss`-parameteren.

**Løsning:** Lagre defaults i konfig og les fra dem hvis params er tomme:
```csharp
iss ??= _config["SmartOnFhir:DefaultIss"];
launch ??= _config["SmartOnFhir:DefaultLaunch"];
```

### 7.5 IDataProcessor.ProcessDataRead

- Kalles av `DataController.Get` ved hver GET til data-endepunktet
- Kjøres i kontekst av HTTP-requesten (session er tilgjengelig)
- Legg til logging på toppen for å bekrefte at metoden kalles:

```csharp
_logger.LogInformation("ProcessDataRead called for instance {Id}", instance?.Id);
```

### 7.6 FHIR URL-konstruksjon

Practitioner-URL fra SMART token (`fhirUser`) er en absolutt URL. Bruk den direkte:
```csharp
// fhirUser = "http://localhost:8080/fhir/Practitioner/lege-ola"
var json = await client.GetStringAsync(ctx.FhirUser);
```

Organization-referanse i Encounter er relativ. Bygg absolutt URL:
```csharp
var orgUrl = orgRef.GetString();
if (!orgUrl.StartsWith("http"))
    orgUrl = $"{ctx.FhirBaseUrl}/{orgUrl}";
```

---

## 8. Kjente fallgruver

| Symptom | Årsak | Løsning |
|---|---|---|
| Alle FHIR-felt tomme, ingen feil | `ProcessDataRead` ikke kalt | Sjekk at `IDataProcessor` er registrert i DI |
| "No SMART session found" i logg | Session-cookie ikke sendt | Sjekk `SameSite`, `SecurePolicy`, `HttpOnly` |
| Connection refused til port 8080 | Feil IP — bruker `172.30.80.1` fra .NET-app | Bruk `localhost:8080` fra Windows-prosesser |
| ERR_TOO_MANY_REDIRECTS | Mangler `[AllowAnonymous]` | Legg til attributt på controller |
| "no schema with key" | JSON Schema draft/2020-12 brukes | Bruk draft-07, fjern `$id`-feltet |
| "Could not find data type 'model'" | `layout-sets.json` peker på feil dataType | Sett `dataType` til eksakt ID fra `applicationmetadata.json` |
| Nginx fjerner `iss`-parameter | `://` i query-param-verdi | Bruk `DefaultIss` i konfig som fallback |
| `TimeSpan` not found i Program.cs | Mangler `using System;` | Legg til using øverst i filen |
| Session tom etter `await LoadAsync()` | `UseSession()` kalt etter Altinn-middleware | Flytt `app.UseSession()` til FØR `UseAltinnAppCommonConfiguration()` |

---

## 9. Test-endepunkt for lokal utvikling

For å teste FHIR-prefill uten full OAuth-flyt:

```
GET http://local.altinn.cloud:8000/digdir/forer-legeerklaering/smart/test-prefill
```

Dette endepunktet:
1. Skriver mock-token og FHIR-kontekst direkte til server-session
2. Lagrer også i `IMemoryCache` (fallback)
3. Videresender til skjemaet

**Viktig:** Fjern eller beskytt dette endepunktet i produksjon!

```csharp
// Eksempel: deaktiver i produksjon
[HttpGet("test-prefill")]
public async Task<IActionResult> TestPrefill()
{
    if (!_env.IsDevelopment())
        return NotFound();
    // ...
}
```
