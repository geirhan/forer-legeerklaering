# Kravspesifikasjon: SMART on FHIR-integrasjon med Altinn Studio
## Legeerklæring for førerrett — v0.6

**Dato:** 2026-06-15  
**Status:** PoC gjennomført — grunnleggende prefill fungerer  
**Forfatter:** Digitaliseringsdirektoratet

---

## 1. Bakgrunn og formål

Legeerklæring for førerrett er et offentlig skjema som legen fyller ut og sender inn til Helsedirektoratet/Statens vegvesen. I dag gjøres dette manuelt, ofte ved å kopiere informasjon fra EPJ-systemet (elektronisk pasientjournal) inn i et separat skjema.

**Formålet med dette initiativet** er å vise at Altinn Studio kan brukes som skjema-plattform for helsefaglig dokumentasjon der:

1. Legen er allerede innlogget i EPJ-systemet (f.eks. DIPS Arena)
2. Kontekstuell data (pasient, konsultasjon, diagnose) hentes automatisk fra EPJ via FHIR
3. Legen kontrollerer, supplerer og sender inn via Altinn
4. PDF-kvittering kan skrives tilbake til EPJ-journalen

### 1.1 Strategisk relevans

- Reduserer dobbeltregistrering for helsepersonell
- Gjenbruker Altinns eksisterende infrastruktur for signering, arkivering og distribusjon
- Etablerer mønster som kan brukes for andre helseskjemaer (sykmelding, henvisninger, attester)
- Følger internasjonale standarder: SMART App Launch IG v2.2.0, FHIR R4

---

## 2. Avgrensninger

### 2.1 MVP-scope

Dette er eksplisitt innenfor scope for PoC og v1:

| I scope | Ikke i scope |
|---|---|
| SMART App Launch 2.0 (EHR Launch) | SMART Backend Services |
| Read-only FHIR (prefill) | FHIR Write / FHIR-innsending |
| Konfidensielt klient (server-side) | Dynamic Client Registration |
| EHR Launch-flyt | Standalone Launch |
| Én pasient, én konsultasjon | Multi-pasient-arbeidsflyt |
| Norske FHIR-basisprofiler (no-basis) | CDS Hooks |
| Altinn-innlogging via ID-porten | PKI-signering av lege (kvalifisert) |
| PDF-generering og arkivering i Altinn | DocumentReference writeback til EPJ |
| — | Samtykke-/tilgangsmodell |
| — | Multifaktor-autentisering i EPJ |

**Uten denne listen** vil prosjektet typisk bli dratt mot SMART Backend Services, CDS Hooks og FHIR Write — som alle er separate integrasjonsprosjekter.

---

## 3. Aktører

| Aktør | Rolle | System |
|---|---|---|
| Lege | Fyller ut og sender inn skjema | EPJ + Altinn |
| EPJ-system | Starter SMART-launch, tilbyr FHIR API | DIPS Arena / tilsvarende |
| Altinn Platform | Infrastruktur for skjema, signering, arkiv | Altinn 3 |
| Altinn-appen | Applikasjonslaget — SMART-integrasjon + skjema | .NET 8 / Altinn Studio |
| Helsedirektoratet | Tjenesteeier, mottar innsending | — |

---

## 4. Arkitektur

### 4.1 Overordnet prinsipp

```
EPJ → SMART Launch → Altinn App → FHIR API → Prefill → Skjema → Innsending
```

**Nøkkelprinsipp:** FHIR brukes **utelukkende til forhåndsutfylling**. Altinns datamodell og innsendingsmekanisme er uendret. Access token lagres **aldri** i nettleseren — kun server-side i ASP.NET Core session.

**BFF-mønster (Backend For Frontend):** Altinn-appen fungerer som konfidensielt klient og mellomlag. All token-utveksling skjer server-side; nettleseren ser aldri access token. FHIR-kall gjøres server-side med Bearer token. Dette er et krav for sikker EPJ-integrasjon.

Se: [Arkitekturoversikt](./arkitektur-oversikt.svg) | [Sekvensdiagram](./smart-launch-sekvens.svg) | [Nettverksruting](./nettverksruting.svg)

### 4.2 Autentisering — to separate lag

Løsningen har to uavhengige autentiseringssystemer som kjører parallelt:

| Lag | System | Formål |
|---|---|---|
| Altinn-autentisering | ID-porten / JWT | Legen som Altinn-bruker (person) |
| SMART-autentisering | EPJ OAuth2 / SMART | Tilgang til FHIR-ressurser i EPJ |

Disse to er **ikke** koblet — Altinn vet ikke om SMART-tokenet, og EPJ vet ikke om Altinn-innloggingen.

### 4.3 Dataflyt

1. EPJ omdirigerer legens nettleser til `/smart/launch?iss=...&launch=...`
2. Appen oppdager EPJs SMART-konfigurasjon (`/.well-known/smart-configuration` eller CapabilityStatement fra `GET /fhir/metadata`)
3. PKCE-utfordring genereres; nettleser sendes til EPJs `/auth`-endepunkt
4. EPJ utsteder autorisasjonskode; nettleser sendes til `/smart/callback`
5. Appen veksler kode mot token **server-side** (POST til EPJs `/token`)
6. Token + FHIR-kontekst (patientId, encounterId, fhirUser) lagres i server-session. `fhirUser` leveres per SMART-spec som eget felt i tokenresponsen; noen EPJ-systemer returnerer det i stedet som JWT-claim i access_token
7. Nettleser videresendes til Altinn-skjemaet
8. Altinn-frontend henter datamodell → `IDataProcessor.ProcessDataRead` kalles
9. `FhirPrefillService` leser session, kaller FHIR API, fyller ut datamodellen
10. Lege kontrollerer prefylt skjema, fyller ut gjenværende felt, sender inn

---

## 5. Datamodell

### 5.1 Feltgrupper og FHIR-kilde

| Feltgruppe | Felt | FHIR-ressurs | Norsk OID / element |
|---|---|---|---|
| **Pasient** | Fnr | Patient.identifier | `urn:oid:2.16.578.1.12.4.1.4.1` |
| | Fornavn | Patient.name[0].given[0] | — |
| | Etternavn | Patient.name[0].family | — |
| | Fødselsdato | Patient.birthDate | — |
| | Kjønn | Patient.gender | — |
| **Lege** | HPR-nummer | Practitioner.identifier | `urn:oid:2.16.578.1.12.4.1.4.4` |
| | Fornavn | Practitioner.name[0].given[0] | — |
| | Etternavn | Practitioner.name[0].family | — |
| **Lege (foretrukket)** | HPR + organisasjon + rolle | PractitionerRole → Practitioner + Organization | — |
| **Virksomhet** | Organisasjonsnummer | Organization.identifier | `urn:oid:2.16.578.1.12.4.1.4.101` |
| | HER-id | Organization.identifier | `urn:oid:2.16.578.1.12.4.1.2` |
| | Navn | Organization.name | — |
| **Konsultasjon** | Dato | Encounter.period.start | — |
| | Organisasjon | Encounter.serviceProvider.reference → Organization | — |
| **Diagnose** | Kode | Condition.code.coding[0].code | ICD-10 |
| | Tekst | Condition.code.coding[0].display | — |
| **Erklæring** | Kjøretøygruppe | (lege velger) | Altinn options |
| | Er skikket | (lege velger) | Boolean |
| | Vilkår | (lege fyller ut) | Fritekst |
| | Merknad | (lege fyller ut) | Fritekst |

**Merknad om PractitionerRole:** NAV krever `no-basis-PractitionerRole` som obligatorisk ressurs for sin sykmeldingsapplikasjon (syk-inn). PractitionerRole kobler legen direkte til sin rolle og organisasjon, og er mer robust enn å hente organisasjon via `Encounter.serviceProvider`. PoC-en bruker Practitioner + Encounter-kjeden; for produksjon anbefales PractitionerRole som primærkilde.

### 5.2 Klassereferanse

```
Altinn.App.Models.ForerLegeerklaeringModel
Namespace: Altinn.App.Models
XmlRoot: ForerLegeerklaering
```

---

## 6. SMART on FHIR — tekniske krav

### 6.1 Støttede flows

- **EHR Launch** (primær): EPJ starter appen med `iss` + `launch`
- **Standalone Launch** (ikke implementert): appen starter selvstendig

### 6.2 Scopes som kreves

```
openid profile fhirUser launch launch/patient launch/encounter
patient/Patient.read patient/Encounter.read patient/Condition.read
user/Practitioner.read user/Organization.read
```

Scopes er konfigurert i `SmartLaunchController.cs` og sendes som `scope`-parameter i authorize-requesten. Appen **skal ikke be om mer enn det som trengs** (prinsippet om minste privilegium). Dersom EPJ-et avviser et scope (returnerer `scope`-felt i tokenresponsen som er smalere enn forespurt), må appen degradere elegant — se seksjon om feilhåndtering i IMPLEMENTERING.md.

**Leverandørvariasjoner:** Ikke alle EPJ-systemer støtter alle scopes. Kjente utfordringer:

| Scope | Status i norske EPJ-er |
|---|---|
| `launch/encounter` | Ikke støttet av alle — kan mangle Encounter i token |
| `user/PractitionerRole.read` | Varierer — noen eksponerer `PractitionerRole`, andre bare `Practitioner` |
| `fhirUser` | Returneres enten som token-felt eller JWT-claim (se implementeringsguiden) |

### 6.3 Sikkerhetskrav

- PKCE påkrevd (S256)
- Konfidensielt klient — `client_secret` brukes ved token-utveksling
- Token lagres **kun** server-side (ASP.NET Core session med `HttpOnly`, `SameSite=Lax`)
- `iss`-validering mot tillatt-liste (`AllowedIssuerList` i konfig)
- State-parameter for CSRF-beskyttelse

### 6.4 EPJ-variasjoner og kompatibilitetsstrategi

SMART App Launch-spesifikasjonen standardiserer launch-flyt og autentisering — **ikke** hvilke FHIR-ressurser som finnes, hvilke profiler som brukes, eller hvilke søk som støttes. Dette er den største praktiske risikoen i et multi-EPJ-scenario.

**Kjente norske EPJ-systemer og forventet SMART-støtte:**

| EPJ | SMART App Launch | FHIR R4 | HelseID-integrert | Encounter | PractitionerRole |
|---|---|---|---|---|---|
| DIPS Arena | Ja (eget SMART-lag) | R4 | Planlagt | Ja | Delvis |
| CGM Allmennlegesystem | Ukjent | Ukjent | Ukjent | Ukjent | Ukjent |
| Infodoc Plenario | Ukjent | Ukjent | Ukjent | Ukjent | Ukjent |
| Pridok | Ukjent | Ukjent | Ukjent | Ukjent | Ukjent |

**Strategi for håndtering av variasjoner:**

1. **CapabilityStatement-sjekk** (se seksjon 4.3, punkt 2): Les `GET /fhir/metadata` ved oppstart og tilpass oppførsel
2. **Ressursfallback**: Hvis `Encounter` mangler i token — forsøk søk via `Patient/$everything` eller `Encounter?patient={id}&status=in-progress`
3. **Profil-toleranse**: Ikke anta `identifier[0]` er fnr — søk etter OID `2.16.578.1.12.4.1.4.1` eksplisitt
4. **Graceful degradation**: Manglende ressurser betyr tomme felt, ikke feil — legen fyller inn manuelt

**NAVs sertifiseringsmodell:** NAV sertifiserer EPJ-er mot sine krav og vedlikeholder en liste over godkjente EPJ-systemer og versjoner. Applikasjonen sjekker ved oppstart om EPJ-et er sertifisert. Dette er et mønster Digdir/Helsedirektoratet bør vurdere for førerretterklæringen — alternativt å gjenbruke NAVs sertifiserte EPJ-liste som utgangspunkt.

**Det viktigste arkitekturspørsmålet:** Hvilken konkret EPJ er første integrasjonsmål? Arkitekturen er moden nok — den største gjenværende risikoen er hva DIPS, CGM, Infodoc eller Pridok faktisk eksponerer av FHIR R4 i praksis.

### 6.5 SMART App Lifecycle

Klientregistrering mot EPJ-leverandøren er ikke en engangsoperasjon:

| Hendelse | Konsekvens | Håndtering |
|---|---|---|
| Ny scope kreves | Ny klientregistrering eller re-consent | Planlegg scope-lista konservativt |
| Ny redirect URI | Oppdatering hos EPJ-leverandør (kan ta uker) | Fryse URI-er tidlig i prosjektet |
| Ny EPJ-versjon | SMART-server-URL kan endre seg | Bruk discovery (`/.well-known/smart-configuration`), ikke hardkod |
| Klienthemmelighet roteres | Koordinert deploy av ny `client_secret` | Secrets via Azure Key Vault, ikke konfig-fil |

**Registreringsprosess mot norske EPJ-leverandører** er per i dag manuell og leverandørspesifikk. Det finnes ingen nasjonal klientregistry (per 2026). Tidlig dialog med EPJ-leverandør er kritisk.

### 6.6 Åpne problemer

| Problem | Status | Prioritet |
|---|---|---|
| ERR_TOO_MANY_REDIRECTS ved full OAuth-flyt | Uløst — workaround: `/test-prefill` | Høy |
| DocumentReference writeback til EPJ | Ikke implementert | Medium |
| Validering av FHIR-token mot EPJ | Ikke implementert | Høy (prod) |
| Issuer-allowlist er tom (ingen validering) | Konfig-gap | Høy (prod) |

---

## 7. Kjøretøygrupper (kodeverk)

Altinn options-fil: `options/kjoretoygrupper.json`

| Kode | Beskrivelse |
|---|---|
| A | Motorsykkel |
| B | Personbil |
| BE | Personbil med tilhenger |
| C1 | Lett lastebil |
| C1E | Lett lastebil med tilhenger |
| C | Lastebil |
| CE | Lastebil med tilhenger |
| D1 | Minibuss |
| D | Buss |
| S | Snøscooter |
| T | Traktor |

---

## 8. Nasjonal profilstrategi

SMART App Launch standardiserer launch og autentisering. FHIR-profiler standardiserer data. **Dette er to separate problemer** — og interoperabilitet krever begge.

### 8.1 Norske basisprofiler (no-basis)

HL7 Norge vedlikeholder basisprofiler for FHIR R4 som norske systemer forventes å følge:

| Profil | Brukt i PoC | Beskrivelse |
|---|---|---|
| `NoBasisPatient` | Ja | fnr via OID `2.16.578.1.12.4.1.4.1` |
| `NoBasisPractitioner` | Ja | HPR-nummer via OID `2.16.578.1.12.4.1.4.4` |
| `NoBasisOrganization` | Ja | Orgnr via OID `2.16.578.1.12.4.1.4.101`, HER-id via `.4.2` |
| `NoBasisEncounter` | Delvis | Bruker standard R4-Encounter |
| `NoBasisCondition` | Nei | ICD-10-kodeverk benyttes, men profil ikke validert |

### 8.2 HelseAPI og nasjonal infrastruktur

| Initiativ | Relevans |
|---|---|
| **HelseAPI** (NHN) | Nasjonal FHIR-gateway. Sannsynlig fremtidig integrasjonspunkt i stedet for direktekobling mot EPJ. |
| **HelseID** (NHN) | Nasjonal identitetstjeneste for helsepersonell. Potensielt erstatning/supplement til EPJ-eget SMART-lag. Støtter PKCE og OpenID Connect. |
| **Pasientens legemiddelliste (PLL)** | Eksempel på nasjonal FHIR-tjeneste via HelseAPI — viser mønsteret som er i ferd med å etableres. |

### 8.3 Førerrett-spesifikke profiler

Per 2026 finnes det **ingen nasjonal FHIR-profil for legeerklæring førerrett**. Dette PoC-et bruker generiske no-basis-profiler. Anbefalingen for produksjonssetting er:

1. Definere en `NoFoerTrafikkLegeerklaeringComposition`-profil (basert på Composition eller Bundle)
2. Registrere i Simplifier.net under HL7 Norway
3. Koordinere med Helsedirektoratet (tjenesteeier) og Statens vegvesen (mottaker)

### 8.4 Kodeverk

| Kodeverk | OID / system | Brukt til |
|---|---|---|
| ICD-10 | `urn:oid:2.16.578.1.12.4.1.1.7110` | Diagnosekoder (Condition) |
| Kjøretøygrupper | (lokal) | Altinn options |
| Kjønn (administrativt) | `http://hl7.org/fhir/administrative-gender` | Patient.gender |

---

## 9. Referanser

### Standarder

| Dokument | Versjon | URL |
|---|---|---|
| SMART App Launch Implementation Guide | v2.2.0 | https://hl7.org/fhir/smart-app-launch/ |
| HL7 FHIR R4 | 4.0.1 | https://hl7.org/fhir/R4/ |
| OAuth 2.0 (RFC 6749) | — | https://datatracker.ietf.org/doc/html/rfc6749 |
| PKCE (RFC 7636) | — | https://datatracker.ietf.org/doc/html/rfc7636 |

### Norske referanser

| Dokument | URL |
|---|---|
| Norske FHIR-basisprofiler (no-basis R4) | https://hl7norway.github.io/basisprofil-no-R4/ |
| Volven OID-register (fnr, HPR, orgnr, HER-id) | https://www.ehelse.no/kodeverk-og-terminologi/OID |
| Norm for informasjonssikkerhet i helse og omsorg | https://www.ehelse.no/normen |

### Altinn

| Dokument | URL |
|---|---|
| Altinn Studio dokumentasjon | https://docs.altinn.studio/ |
| app-localtest (lokalt testmiljø) | https://github.com/Altinn/app-localtest |

### Relaterte implementasjoner

- **NAV syk-inn**: NAVs SMART on FHIR-applikasjon for ny digital sykmelding. Autoritært referansedokument for norsk SMART on FHIR mot EPJ-leverandører. Bruker samme arkitektur: BFF, konfidensielt klient, EHR Launch, server-side FHIR-kall.
  - Krav til EPJ-leverandører: https://github.com/navikt/syk-inn/blob/main/docs/fhir/nav_requirements.md
  - FHIR-ressursoversikt: https://github.com/navikt/syk-inn/blob/main/docs/fhir/_oversikt.md
- **SMARTHealthIT**: Referanseimplementasjon og sandbox for SMART App Launch

---

## 10. Endringslogg

| Versjon | Dato | Endring |
|---|---|---|
| v0.1 | 2026-05 | Første utkast — konsept og scope |
| v0.2 | 2026-05 | Datamodell og FHIR-mapping |
| v0.3 | 2026-05 | Arkitekturdiagram v0.3 |
| v0.4 | 2026-05 | SMART-flyt og sekvensdiagram |
| v0.5 | 2026-06 | Sikkerhetskrav og kjøretøygrupper |
| **v0.6** | **2026-06-15** | PoC-resultater innarbeidet. Kjente begrensninger dokumentert. SVG-diagrammer oppdatert. Nettverksruting lagt til. |
| **v0.6.1** | **2026-06-15** | BFF-mønster presisert. CapabilityStatement lagt til i discovery-flyt. fhirUser-forbehold (JWT-claim vs. tokenfelt) innarbeidet. |
| **v0.6.2** | **2026-06-15** | Eksplisitt MVP-avgrensning (seksjon 2.1). Scope governance og EPJ-variasjonstrategi (seksjon 6.2/6.4). SMART App Lifecycle (6.5). Nasjonal profilstrategi og HelseAPI/HelseID (seksjon 8). |
| **v0.6.3** | **2026-06-16** | PractitionerRole lagt til som foretrukket FHIR-ressurs (seksjon 5.1). NAVs sertifiseringsmodell for EPJ dokumentert (seksjon 6.4). Referanser oppdatert med navikt/syk-inn. Kilde: NAV Standardiseringsutvalg møte 2-25. |
