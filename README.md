# Legeerklæring for førerrett — SMART on FHIR + Altinn Studio

**Status:** PoC gjennomført og verifisert — FHIR-prefill, auto-innlogging og signering fungerer i lokalt testmiljø  
**Eier:** Digitaliseringsdirektoratet  
**Kontakt:** johann.finnur.sigurvinsson.olafsson@digdir.no

---

## Hva er dette?

Et proof of concept som viser at Altinn Studio kan brukes som skjema-plattform for helsefaglig dokumentasjon med automatisk prefill fra EPJ (elektronisk pasientjournal) via FHIR.

Legen er innlogget i sitt EPJ-system (f.eks. DIPS Arena). EPJ-et starter en **SMART EHR Launch** som åpner Altinn-appen med pasient- og konsultasjonskontekst. Altinn-appen henter relevante data fra FHIR-APIet og forhåndsutfyller legeerklæringen. Legen kontrollerer, supplerer og signerer/sender inn.

```
EPJ-system ──SMART EHR Launch──► Altinn App (BFF)
                                      │
                                      ├─► FHIR API (Patient, Practitioner, Encounter...)
                                      │        │
                                      │        └─► Prefiller skjema
                                      │
                                      └─► Altinn Platform (signering, arkiv, PDF)
```

Etablerer et mønster som kan gjenbrukes for andre helseskjemaer: sykmelding <!-- KOMMENTAR: Dette er løst av NAV allerede, og de har allerde ettablert et mønster for det. -->, henvisninger, attester.

---

## Arkitektur

| Komponent | Teknologi | Rolle |
|---|---|---|
| Altinn-app | ASP.NET Core / .NET 8 | BFF — SMART-flyt, FHIR-prefill, skjema |
| Altinn Platform | Altinn Studio App API 8.6.4 | Infrastruktur: auth, storage, PDF, signering |
| EPJ FHIR API | FHIR R4 | Kilde for pasient- og konsultasjonsdata |
| EPJ SMART Auth | OAuth2 / SMART App Launch IG v2.2.0 | Autorisasjon for FHIR-tilgang |

**Nøkkelprinsipp — BFF-mønster:** Access token forlater aldri nettleseren. All token-utveksling og FHIR-kall skjer server-side i ASP.NET Core.

**Signeringsmønster — «Signer og send inn»:** Task_1 er en Altinn signing task. Legen signerer og sender inn i én handling. Signaturdata opprettes av `sign`-aksjonen (Altinn.App.Api 8.6.4+).

### Diagrammer

- [Arkitekturoversikt](docs/arkitektur-oversikt.svg)
- [SMART Launch-sekvens](docs/smart-launch-sekvens.svg)
- [Nettverksruting (lokalt utviklingsmiljø)](docs/nettverksruting.svg)

---

## Dokumentasjon

| Dokument | Beskrivelse |
|---|---|
| [KRAVSPESIFIKASJON-v0.6.md](docs/KRAVSPESIFIKASJON-v0.6.md) | Krav, arkitektur, datamodell, SMART-krav, kodeverk, referanser |
| [IMPLEMENTERING.md](docs/IMPLEMENTERING.md) | Komponentguide, beste praksis, fallgruver, referanser |
| [SKJEMA-IS2569.md](docs/SKJEMA-IS2569.md) | Fullstendig feltstruktur for blankett IS-2569 (Helseattest førerett) med implementeringsstatus |
| [PASIENTFLYT.md](docs/PASIENTFLYT.md) | Arkitekturforslag for digital egenerklæring (NA-0201) med Dialogporten og helsenorge.no — pasientens del av flyten |
| [BESLUTNINGER.md](docs/BESLUTNINGER.md) | Åpne beslutninger som krever menneskelig avklaring: autorisasjonsmodell, HelseID-validering, mottaksarkitektur, DPIA, full IS-2569 |
| [VEIKART.md](docs/VEIKART.md) | Prioritert veikart mot produksjonsklar referansearkitektur: fase 1–5 inkl. NuGet-pakke `Digdir.SmartOnFhir` |

---

## Kom i gang (lokalt utviklingsmiljø)

### Forutsetninger

- Windows 11 med Podman Desktop
- .NET 8 SDK
- Node.js 18+
- PowerShell 7+

### 1. Konfigurer hosts-fil

Legg til følgende linje i `C:\Windows\System32\drivers\etc\hosts` (krever administratortilgang):

```
127.0.0.1  local.altinn.cloud
```

### 2. Start containere

Åpne Podman Desktop og start compose-prosjektet i `app-localtest/`. Alternativt:

```powershell
$env:ALTINN3LOCAL_PORT = "8000"
# Podman Desktop → Compose → Start
```

Containere som startes:
- `localtest-loadbalancer` (nginx, port 8000)
- `localtest` (Altinn Platform, port 5101)
- `localtest-pdf3` (PDF-generator, port 5300)
- `hapi-fhir` (FHIR R4-server, port 8080)

### 3. Last inn testdata

```powershell
cd local-dev
.\seed.ps1
```

Oppretter FHIR-ressurser for 5 pasienter (alle tilknyttet Dr. Ola Nordmann / Sandvika Legesenter):

| Pasient | FHIR-id | Encounter |
|---|---|---|
| Sophie Salt | `sophie-salt` | `enc-sophie-001` |
| Per Hansen | `per-hansen` | `enc-per-001` |
| Anne Johansen | `anne-johansen` | `enc-anne-001` |
| Kari Larsen | `kari-larsen` | `enc-kari-001` |
| Olav Berg | `olav-berg` | `enc-olav-001` |

Practitioner `lege-ola` (HPR: 1234567) og Organization `sandvika-legesenter` (orgnr: 987654321) opprettes én gang og deles av alle pasienter.

### 4. Start SMART Auth Mock

```powershell
cd local-dev\smart-mock
npm install
node server.js
# Lytter på http://localhost:9090
```

### 5. Start Altinn-appen

```powershell
cd src\App
dotnet run
# Lytter på http://localhost:5005
```

### 6. Åpne EPJ-simulatoren

Åpne i nettleser:
```
http://localhost:9090/epj
```

EPJ-simulatoren åpnes med fullstendig Digdir-design. Velg pasient fra listen og trykk:

- **Hurtigstart** — logger automatisk inn som Dr. Ola Nordmann i Altinn localtest og åpner skjema med FHIR-prefill (anbefalt for demo)
- **Start i Altinn** — full SMART EHR Launch-flyt med OAuth-redirect

**Hurtigstart bruker `/smart/dev-login`:** Altinn-appen henter JWT fra localtest server-to-server (ingen CSRF-problem), setter `AltinnStudioRuntime`- og `AltinnPartyId`-cookies, og redirecter til skjemaet med pasient- og encounter-kontekst seedet i FHIR-sesjonen.

---

## Repostruktur

```
forer-legeerklaering/
├── src/App/                          # Altinn .NET-app
│   ├── controllers/
│   │   └── SmartLaunchController.cs  # SMART EHR Launch-flyt + /smart/dev-login
│   ├── services/
│   │   └── FhirPrefillService.cs     # IDataProcessor — FHIR → datamodell
│   ├── models/
│   │   └── ForerLegeerklaeringModel.cs
│   ├── config/
│   │   ├── applicationmetadata.json  # signing task, allowedContributors
│   │   └── process/process.bpmn     # «Signer og send inn»: én signing task
│   ├── ui/form/layouts/              # Altinn UI-layout
│   ├── options/kjoretoygrupper.json  # Kodeverk
│   └── appsettings.Development.json # Lokal konfig
├── docs/
│   ├── KRAVSPESIFIKASJON-v0.6.md
│   ├── IMPLEMENTERING.md
│   ├── VEIKART.md                    # Prioritert veikart fase 1–5
│   ├── arkitektur-oversikt.svg
│   ├── smart-launch-sekvens.svg
│   └── nettverksruting.svg
└── local-dev/                        # Lokal testinfrastruktur
    ├── seed.ps1                      # Seeder FHIR med 5 pasienter + lege + org
    └── smart-mock/
        ├── server.js                 # SMART Auth Mock (Node.js/Express) + launch-kontekster for 5 pasienter
        └── epj-simulator.html        # EPJ-simulator med Digdir designsystemet
```

---

## Kjente begrensninger

| Begrensning | Status |
|---|---|
| Full OAuth-redirect-flyt (ERR_TOO_MANY_REDIRECTS) | Uløst — workaround: Hurtigstart (`/smart/dev-login`) |
| DocumentReference writeback til EPJ etter innsending | Planlagt — se [VEIKART.md fase 2](docs/VEIKART.md) |
| FHIR-token validering mot EPJ | Ikke implementert (kreves i prod) — se [VEIKART.md fase 1](docs/VEIKART.md) |
| Issuer allowlist er tom | Konfig-gap (kreves i prod) — se [VEIKART.md fase 1](docs/VEIKART.md) |
| `/smart/dev-login` kun tilgjengelig i Development-miljø | By design — `IsDevelopment()`-sjekk i controller |

---

## Standarder og referanser

- [SMART App Launch IG v2.2.0](https://hl7.org/fhir/smart-app-launch/)
- [HL7 FHIR R4](https://hl7.org/fhir/R4/)
- [Norske FHIR-basisprofiler (no-basis)](https://hl7norway.github.io/basisprofil-no-R4/)
- [Norsk OID-register (Volven)](https://www.ehelse.no/kodeverk-og-terminologi/OID)
- [Altinn Studio dokumentasjon](https://docs.altinn.studio/)
- [app-localtest](https://github.com/Altinn/app-localtest)
- [NAV syk-inn — SMART on FHIR sykmelding i produksjon](https://github.com/navikt/syk-inn)

---

## Lisens

Kode og dokumentasjon er utviklet som åpen kildekode av Digitaliseringsdirektoratet.  
Bruk og videreutvikling er tillatt med kildeangivelse.
