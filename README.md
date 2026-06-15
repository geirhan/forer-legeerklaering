# Legeerklæring for førerrett — SMART on FHIR + Altinn Studio

**Status:** PoC gjennomført — FHIR-prefill fungerer i lokalt testmiljø  
**Eier:** Digitaliseringsdirektoratet  
**Kontakt:** johann.finnur.sigurvinsson.olafsson@digdir.no

---

## Hva er dette?

Et proof of concept som viser at Altinn Studio kan brukes som skjema-plattform for helsefaglig dokumentasjon med automatisk prefill fra EPJ (elektronisk pasientjournal) via FHIR.

Legen er innlogget i sitt EPJ-system (f.eks. DIPS Arena). EPJ-et starter en **SMART EHR Launch** som åpner Altinn-appen med pasient- og konsultasjonskontekst. Altinn-appen henter relevante data fra FHIR-APIet og forhåndsutfyller legeerklæringen. Legen kontrollerer, supplerer og sender inn.

```
EPJ-system ──SMART EHR Launch──► Altinn App (BFF)
                                      │
                                      ├─► FHIR API (Patient, Practitioner, Encounter...)
                                      │        │
                                      │        └─► Prefiller skjema
                                      │
                                      └─► Altinn Platform (signering, arkiv, PDF)
```

Etablerer et mønster som kan gjenbrukes for andre helseskjemaer: sykmelding, henvisninger, attester.

---

## Arkitektur

| Komponent | Teknologi | Rolle |
|---|---|---|
| Altinn-app | ASP.NET Core / .NET 8 | BFF — SMART-flyt, FHIR-prefill, skjema |
| Altinn Platform | Altinn Studio App API 8.6.4 | Infrastruktur: auth, storage, PDF, signering |
| EPJ FHIR API | FHIR R4 | Kilde for pasient- og konsultasjonsdata |
| EPJ SMART Auth | OAuth2 / SMART App Launch IG v2.2.0 | Autorisasjon for FHIR-tilgang |

**Nøkkelprinsipp — BFF-mønster:** Access token forlater aldri nettleseren. All token-utveksling og FHIR-kall skjer server-side i ASP.NET Core.

### Diagrammer

- [Arkitekturoversikt](docs/arkitektur-oversikt.svg)
- [SMART Launch-sekvens v0.6.1](docs/smart-launch-sekvens.svg)
- [Nettverksruting (lokalt utviklingsmiljø)](docs/nettverksruting.svg)

---

## Dokumentasjon

| Dokument | Beskrivelse |
|---|---|
| [KRAVSPESIFIKASJON-v0.6.md](docs/KRAVSPESIFIKASJON-v0.6.md) | Krav, arkitektur, datamodell, SMART-krav, kodeverk, referanser |
| [IMPLEMENTERING.md](docs/IMPLEMENTERING.md) | Komponentguide, beste praksis, fallgruver, referanser |
| [SKJEMA-IS2569.md](docs/SKJEMA-IS2569.md) | Fullstendig feltstruktur for blankett IS-2569 (Helseattest førerett) med implementeringsstatus |
| [PASIENTFLYT.md](docs/PASIENTFLYT.md) | Arkitekturforslag for digital egenerklæring (NA-0201) med Dialogporten og helsenorge.no — pasientens del av flyten |

---

## Kom i gang (lokalt utviklingsmiljø)

### Forutsetninger

- Windows 11 med Podman Desktop
- .NET 8 SDK
- Node.js 18+
- PowerShell 7+

### 1. Start containere

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

### 2. Last inn testdata

```powershell
cd app-localtest\fhir-testdata
.\seed.ps1
```

Oppretter: Patient `sophie-salt`, Practitioner `lege-ola`, Organization `sandvika-legesenter`, Encounter og Condition (ICD-10: R55 Synkope).

### 3. Start SMART Auth Mock

```powershell
cd app-localtest\fhir-testdata\smart-mock
npm install
node server.js
# Lytter på http://localhost:9090
```

### 4. Start Altinn-appen

```powershell
cd src\App
dotnet run
# Lytter på http://localhost:5005
```

### 5. Test FHIR-prefill

Åpne i nettleser:
```
http://local.altinn.cloud:8000/digdir/forer-legeerklaering/smart/test-prefill
```

Dette bypasser full OAuth-flyt og seeder session med testdata direkte. Skjemaet åpnes med forhåndsutfylte pasient- og legeopplysninger.

> **Hosts-fil:** Legg til `127.0.0.1  local.altinn.cloud` i `C:\Windows\System32\drivers\etc\hosts`

---

## Repostruktur

```
forer-legeerklaering/
├── src/App/                          # Altinn .NET-app
│   ├── controllers/
│   │   └── SmartLaunchController.cs  # SMART EHR Launch-flyt
│   ├── services/
│   │   └── FhirPrefillService.cs     # IDataProcessor — FHIR → datamodell
│   ├── models/
│   │   └── ForerLegeerklaeringModel.cs
│   ├── ui/form/layouts/              # Altinn UI-layout
│   ├── options/kjoretoygrupper.json  # Kodeverk
│   └── appsettings.Development.json # Lokal konfig
├── docs/
│   ├── KRAVSPESIFIKASJON-v0.6.md
│   ├── IMPLEMENTERING.md
│   ├── arkitektur-oversikt.svg
│   ├── smart-launch-sekvens.svg      # v0.6.1 — oppdatert etter ekspertgjennomgang
│   └── nettverksruting.svg
└── local-dev/                        # Lokal testinfrastruktur
    └── smart-mock/
        └── server.js                 # SMART Auth Mock (Node.js/Express)
```

---

## Kjente begrensninger

| Begrensning | Status |
|---|---|
| Full OAuth-redirect-flyt (ERR_TOO_MANY_REDIRECTS) | Uløst — workaround: `/test-prefill` |
| DocumentReference writeback til EPJ etter innsending | Planlagt, ikke implementert |
| FHIR-token validering mot EPJ | Ikke implementert (kreves i prod) |
| Issuer allowlist er tom | Konfig-gap (kreves i prod) |

---

## Standarder og referanser

- [SMART App Launch IG v2.2.0](https://hl7.org/fhir/smart-app-launch/)
- [HL7 FHIR R4](https://hl7.org/fhir/R4/)
- [Norske FHIR-basisprofiler (no-basis)](https://hl7norway.github.io/basisprofil-no-R4/)
- [Norsk OID-register (Volven)](https://www.ehelse.no/kodeverk-og-terminologi/OID)
- [Altinn Studio dokumentasjon](https://docs.altinn.studio/)
- [app-localtest](https://github.com/Altinn/app-localtest)
- [NAV syk-dig — norsk FHIR-sykmelding](https://github.com/navikt/syk-dig-backend)

---

## Lisens

Kode og dokumentasjon er utviklet som åpen kildekode av Digitaliseringsdirektoratet.  
Bruk og videreutvikling er tillatt med kildeangivelse.
