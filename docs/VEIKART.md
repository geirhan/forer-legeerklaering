# Veikart — `forer-legeerklaering` og SMART on FHIR på Altinn

**Sist oppdatert:** 2026-06-16  
**Utgangspunkt:** PoC gjennomført og verifisert lokalt. Sammenligningsanalyse mot NAV `syk-inn` (prod) identifiserer fem konkrete gap før løsningen kan betraktes som produksjonsklar referansearkitektur.

---

## Overordnet retning

Målet er ikke å reimplementere sykmelding. Målet er å løfte `forer-legeerklaering` fra «velbeskrevet idé-validering» til **reproduserbar referansearkitektur for SMART on FHIR på Altinn Studio** — slik at andre etater og skjemaeiere kan bruke den som mal.

Strategien følger det NAV har bevist med `syk-inn`:
1. Få én app til å virke skikkelig i produksjon (fase 1–3)
2. Ekstraher det generiske laget til et bibliotek andre kan bruke (fase 4)

---

## Fase 1 — Produksjonsklar SMART-klient

*Forutsetning for ethvert produksjonssett. Kan gjøres uten avhengigheter mot ytre systemer.*

| Tiltak | Beskrivelse | Ref |
|---|---|---|
| **Tokenvalidering** | Valider access token-signatur server-side. I produksjon: sjekk mot EPJ-ens JWKS-endepunkt, verifiser `iss`, `aud`, `exp`. Bruk `Microsoft.IdentityModel.Tokens`. | `BESLUTNINGER.md` C-2 |
| **Refresh-håndtering** | `offline_access` etterspørres allerede — men token byttes ikke ut. Implementer bakgrunns-refresh i `FhirPrefillService` (sjekk `ExpiresAt` før hvert FHIR-kall, exchang refresh token om nødvendig). | `syk-inn`: `autoRefresh` |
| **Issuer-allowlist** | Fyll `SmartOnFhir:AllowedIssuerList` i appsettings og sett opp per-miljø konfigurasjon (dev/test/prod). Legg til kjente norske EPJ-er: DIPS Arena, WebMed, CGM Journey. | README «Konfig-gap» |
| **Distribuert sesjon** | Bytt `AddDistributedMemoryCache()` med Redis/Valkey. Kreves for HA (flere pod-er). FHIR-kontekst og token lagres allerede i sesjon — ingen kodeendring utover DI-konfig. | `syk-inn`: Valkey med 30-dagers TTL |
| **ERR_TOO_MANY_REDIRECTS** | Diagnostiser og løs redirect-loopen i full OAuth-flyt. `/dev-login`-workaround er kun lokalt. | README «Kjente begrensninger» |

**Estimat:** 2–3 uker med tilgang til et ekte EPJ-testmiljø (DIPS/WebMed sandbox).

---

## Fase 2 — Writeback til EPJ

*Lukker den viktigste funksjonelle mangelen. NAVs ADR01 fra `syk-inn` er en direkte oppskrift.*

| Tiltak | Beskrivelse |
|---|---|
| **DocumentReference** | Etter innsending: skriv en `DocumentReference` tilbake til EPJ-ens FHIR-server med PDF-referanse. Bruk SMART access token (BFF). |
| **QuestionnaireResponse** | Skriv strukturert skjemadata som `QuestionnaireResponse` koblet til en kanonisk `Questionnaire`-definisjon (servert fra Altinn-appen, URL `/{org}/{app}/fhir/R4/Questionnaire/V1`). |
| **Transaction Bundle med PUT** | Bruk klient-tildelt id (`DocumentReference.id = altinnInstanceId`) for idempotens. `GET`-sjekk før skriving for å unngå duplikater. |
| **Kanonisk Questionnaire** | Publiser `Questionnaire`-ressursen som beskriver IS-2569-feltene — gjenbrukbar av andre systemer som skal konsumere innsendingen. |

**Referanse:** `syk-inn/src/fhir-write-service.ts` + ADR01.  
**Estimat:** 1–2 uker etter fase 1 (krever gyldig access token med write-scope).

---

## Fase 3 — Testing

*Nødvendig regresjonsvern og demo-verdi. Ingen automatiserte tester finnes i dag.*

| Tiltak | Beskrivelse |
|---|---|
| **e2e røyktest** | Playwright-test som kjører full `dev-login` → prefill → utfylling → signering → verifiser at instans er arkivert i Altinn. Dekker det kritiske happy-path-løpet. |
| **Unit-test: FhirPrefillService** | Test med fixture-JSON for Patient/Practitioner/Encounter — verifiser at modellfelter prefylles korrekt, og at manglende ressurser håndteres uten unntak. |
| **Unit-test: SmartLaunchController** | Test state-mismatch-deteksjon, issuer-allowlist-logikk og PKCE-generering. |
| **Integrasjonstest: writeback** | Test mot HAPI FHIR (allerede i docker-compose) at DocumentReference og QuestionnaireResponse skrives korrekt. |

**Estimat:** 1–2 uker.

---

## Fase 4 — NuGet-pakke: `Digdir.SmartOnFhir`

*Gjøres etter fase 1–3, ikke i stedet for dem. NAV ekstraherte `@navikt/smart-on-fhir` etter at `syk-inn` var i produksjon — samme sekvens gjelder her.*

**Hvorfor:**  
Det finnes ingen SMART on FHIR-klient for .NET/Altinn i dag. En NuGet-pakke ville gjøre det trivielt for andre Altinn-apputviklere å legge til SMART-støtte — på samme måte som `@navikt/smart-on-fhir` gjør for Next.js-apper.

**Hva pakken bør inneholde:**

```
Digdir.SmartOnFhir/
├── SmartLaunchHandler        # EHR Launch: discovery, PKCE, state, redirect
├── SmartCallbackHandler      # Authorization code → token exchange
├── SmartTokenStore           # Abstraksjon over sesjon/cache (Redis-støtte)
├── FhirHttpClientFactory     # HttpClient med SMART token injisert automatisk
├── TokenValidator            # JWKS-validering av access token
├── SmartOptions              # Konfigurasjon: ClientId, AllowedIssuers, osv.
└── Extensions/
    └── AddSmartOnFhir()      # IServiceCollection-extension for enkel DI-konfig
```

**Hva som ikke skal inn:**  
Applikasjonslogikk (prefill-mapping, FHIR-ressursmodeller, skjemastruktur). Pakken er protokoll, ikke domene.

**Publisering:** NuGet.org + GitHub Packages. Versjonering via SemVer, changelog-drevet.

**Estimat:** 2–3 uker for v0.1 (etter at fase 1-koden er stabil nok å ekstrahere fra).

---

## Fase 5 — Full IS-2569 og HelseID

*Lengre horisont. Krever menneskelige avklaringer (se `BESLUTNINGER.md`).*

| Tiltak | Avhengighet |
|---|---|
| Implementer alle 17 helsekategorier i IS-2569 | C-5: menneskelig beslutning om omfang |
| HelseID som behandler-autentisering i Altinn-kontekst | C-2: klientregistrering hos NHN, juridisk avklaring |
| Party-modell: legekontor som innsender | C-1: organisasjonsstruktur i Altinn |
| Mottaksarkitektur (SVV/Hdir/EPJ) | C-3: avtaleverk mellom etater |
| DPIA og behandlingsgrunnlag | C-4: juridisk prosess |

---

## Prioritert rekkefølge

```
Fase 1: SMART-klient (tokenvalidering, refresh, allowlist, distribuert sesjon)
    ↓
Fase 2: Writeback (DocumentReference + QuestionnaireResponse)
    ↓
Fase 3: Testing (e2e + unit + integrasjon)
    ↓
Fase 4: NuGet-pakke Digdir.SmartOnFhir
    ↓
Fase 5: Full IS-2569, HelseID, mottaksarkitektur (etter menneskelige avklaringer)
```

Fase 1–3 er uavhengige av menneskelige beslutninger og kan startes nå.  
Fase 4 er avhengig av at fase 1 er stabil — ikke av fase 2–3.  
Fase 5 er avhengig av avklaringene i `BESLUTNINGER.md`.
