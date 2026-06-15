# Helseattest førerett — Skjemastruktur og feltanalyse
## Blankett IS-2569 (Helsedirektoratet, 22.05.2017)

**Kilde:** [Helseattest førerett (PDF)](https://www.helsedirektoratet.no/veiledere/forerkortveileder/dokumenter-forerkortveileder/Helseattest%20f%C3%B8rerett.pdf/_/attachment/inline/661710e2-a13e-4591-8a29-3c6dc2fe9fd2:051bc49cd8af2d7aba25f9c2c13d6ea601328d36/Helseattest%20f%C3%B8rerett.pdf)  
**Juridisk hjemmel:** Førerkortforskriften vedlegg 1 — Helsekrav, helsepersonelloven § 4 og § 15  
**Merk:** Helseattesten skrives ut til søker, som tar den med til trafikkstasjonen. Den må ikke være eldre enn 3 måneder.

---

## Feltdekning i PoC

| Status | Antall felt |
|---|---|
| Dekket av FHIR-prefill | ~12 felt |
| Delvis dekket | 3 felt |
| Mangler i datamodellen | ~30 felt |

PoC-en dekker FHIR-prefill-delen (pasient, lege, virksomhet, diagnose) fullt ut. Selve legeattestdelen — helsekategoriene 1–16 og konklusjonen — er ikke implementert i datamodellen. Se [implementeringsstatus](#implementeringsstatus) nederst.

---

## Side 1–2: Helseattest (utfylles av lege)

### Søkers personopplysninger

| Felt | Type | FHIR-kilde | Status i PoC |
|---|---|---|---|
| Etternavn, fornavn og mellomnavn | Tekst | `Patient.name` | Delvis — mellomnavn mangler eget felt |
| Fødselsnummer | Tekst | `Patient.identifier` (OID `2.16.578.1.12.4.1.4.1`) | Dekket |

### Legens tilknytning

| Felt | Type | Utfylles av | Status i PoC |
|---|---|---|---|
| Er søkers fastlege | Avkrysning | Lege | Mangler |
| Eventuell annen tilknytning (vikar, behandlende spesialist o.l.) | Fritekst | Lege | Mangler |

### Identitetsbekreftelse

| Felt | Type | Utfylles av | Status i PoC |
|---|---|---|---|
| Søkers identitet er kjent fra tidligere | Avkrysning | Lege | Mangler |
| Det er forevist akseptabel legitimasjon med navn, fødselsnummer/D-nummer og bilde | Avkrysning | Lege | Mangler |
| Jeg har lest søkers egenerklæring om helse | Avkrysning | Lege | Mangler |

### Helseattesten gjelder (formål)

Legen krysser av for hva attesten skal brukes til. Én eller flere kan velges:

| Alternativ | Type | Status i PoC |
|---|---|---|
| Førerkort første gang | Avkrysning | Mangler |
| Utvidelse | Avkrysning | Mangler |
| Fornyelse | Avkrysning | Mangler |
| Innbytte av utenlandsk førerkort | Avkrysning | Mangler |
| Tilbakelevering | Avkrysning | Mangler |
| Utrykningskompetanse | Avkrysning | Mangler |
| Kjøreseddel for drosje inntil 8 passasjerer | Avkrysning | Mangler |
| Kjøreseddel for buss | Avkrysning | Mangler |
| Godkjenning som trafikklærer | Avkrysning | Mangler |
| Godkjenning som førerprøvesensor | Avkrysning | Mangler |

### Førerkortgruppe

| Alternativ | Beskrivelse | Status i PoC |
|---|---|---|
| Gruppe 1 | Personbil, motorsykkel, moped | Delvis — PoC bruker kjøretøykoder (A, B osv.), ikke gruppe 1/2/3 |
| Gruppe 2 | Lastebil, buss (tung) | Delvis |
| Gruppe 3 | Drosje, buss (lett), utrykningskjøretøy | Delvis |

---

## Helsekategorier 1–16

For hver kategori krysser legen av Ja eller Nei. Dersom én eller flere av spørsmål 2–15 besvares med Ja, må spørsmål 16 også besvares. Alle konklusjoner og begrunnelser dokumenteres i søkers journal (journalforskriften § 8 bokstav p).

### 1. Enkel synstest
*(Forskriften og Veilederen lenket i original blankett)*

#### A. Synsstyrke

| Felt | Høyre øye | Venstre øye | Begge øyne | Status i PoC |
|---|---|---|---|---|
| Uten korreksjon | Tall | Tall | — | Mangler |
| Med korreksjon | Tall | Tall | — | Mangler |
| Korreksjonens styrke | Tall | Tall | — | Mangler |

#### B. Synsfelt

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker normalt synsfelt vurdert ved Donders metode når begge øyne er i bruk? | Ja/Nei | Mangler |

#### C. Synsfunksjon

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker en svekkelse av synsfunksjon som gjør vurdering av optiker eller øyelege nødvendig? | Ja/Nei | Mangler |

> **Merknad:** Dersom søker har dobbeltsyn, tap/reduksjon av synet på ett øye, problemer med mørke/vekslende lys, mistanke om nedsatt sidesyn eller progressiv øyesykdom, skal synsfunksjoner vurderes av optiker/øyelege (Helseattest førerett – syn, blankett IS-2571) før denne attesten skrives ut, eller attesten gis med forbehold om godkjent synsattest.

---

### 2. Hørsel *(gjelder bare førerkortgruppe 3)*

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker en hørselssvekelse som medfører at talestemme ikke oppfattes på 4 meters avstand? | Ja/Nei | Mangler |

> Dersom hørselshjelpemiddel er nødvendig for førerett i gruppe 3, angis dette under vilkår i konklusjonen.

---

### 3. Kognitiv svekkelse

| Felt | Type | Status i PoC |
|---|---|---|
| Foreligger det en tilstand med kognitiv svekkelse som kan gi økt trafikksikkerhetsrisiko? | Ja/Nei | Mangler |

---

### 4. Nevrologiske sykdommer

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker svekket balanse, koordinasjon eller psykomotoriske funksjoner som medfører økt trafikksikkerhetsrisiko? | Ja/Nei | Mangler |

---

### 5. Epilepsi eller epilepsilignende anfall

| Felt | Type | Status i PoC |
|---|---|---|
| a) Har søker eller har søker hatt epilepsi eller epilepsilignende anfall? | Ja/Nei | Mangler |
| b) Bruker eller har søker brukt anfallsforebyggende legemidler mot epilepsi innenfor siste 10 år? | Ja/Nei | Mangler |

---

### 6. Bevissthetstap og bevissthetsforstyrrelser av annen årsak

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker hatt bevissthetstap eller bevissthetsforstyrrelse av annen årsak enn epilepsi, hjerte-/karsykdom eller diabetes? | Ja/Nei | Mangler |

---

### 7. Søvnsykdommer

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker, eller har søker hatt, påtrengende søvnighet eller ukontrollerbar søvn som kan utgjøre en trafikksikkerhetsrisiko? | Ja/Nei | Mangler |

---

### 8. Hjerte- og karsykdommer

| Felt | Type | Status i PoC |
|---|---|---|
| Har eller har søker hatt hjerte- og karsykdom med fare for plutselig innsettende bevissthetspåvirkning? | Ja/Nei | Mangler |

---

### 9. Diabetes

| Felt | Type | Status i PoC |
|---|---|---|
| a) Har søker diabetes? | Ja/Nei | Mangler |
| b) Har søker følgetilstander av diabetes som kan gi økt trafikksikkerhetsrisiko? | Ja/Nei | Mangler |
| c) Bruker søker insulin eller andre legemidler som kan gi hypoglykemi? | Ja/Nei | Mangler |

---

### 10. Psykiske lidelser eller svekkelser

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker psykisk lidelse eller svekkelse som medfører trafikksikkerhetsrisiko? | Ja/Nei | Mangler |

---

### 11. Bruk av midler som kan påvirke kjøreevnen

| Felt | Type | Status i PoC |
|---|---|---|
| Bruker eller har søker brukt alkohol, rusmidler eller legemidler i et omfang og på en måte som medfører økt trafikksikkerhetsrisiko? | Ja/Nei | Mangler |

---

### 12. Respirasjonssvikt

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker en helsetilstand som gir risiko for pO2 lavere enn 7,3 kPa og/eller pCO2 høyere enn 6,7 kPa? | Ja/Nei | Mangler |

---

### 13. Nyresykdommer

| Felt | Type | Status i PoC |
|---|---|---|
| Har søker alvorlig kronisk nyresvikt, behov for dialyse eller har det vært utført nyretransplantasjon? | Ja/Nei | Mangler |

---

### 14. Svekket førlighet

| Felt | Type | Status i PoC |
|---|---|---|
| a) Mangler søker tilstrekkelig førlighet til trafikksikker føring av motorvogn? | Ja/Nei | Mangler |
| b) Hvis Ja på 14a: Er tilstanden stabil? | Ja/Nei | Mangler |

---

### 15. Andre sykdommer og helsesvekkelser

| Felt | Type | Status i PoC |
|---|---|---|
| Har fører annen eller generell helsesvekkelse, eventuelt flere sykdommer samtidig, der svekket helsetilstand utgjør en risiko for trafikksikkerheten? | Ja/Nei | Mangler |

---

### 16. Oppsummering av spørsmålene 2–15

*Besvares kun hvis ett eller flere av spørsmålene 2–15 er besvart med Ja.*

| Felt | Type | Status i PoC |
|---|---|---|
| Er helsekravene i vedlegg 1 likevel oppfylt, eventuelt med begrenset varighet og/eller særlige vilkår? | Ja/Nei | Mangler |

**Leges underskrift** (midtside) — signatur fra attestutstedende lege.

---

## Side 3: Konklusjon

Legen fyller ut helseattesten som medisinsk sakkyndig for trafikkstasjonen og for førerkortssøkeren. Legens sakkyndige erklæring er **ikke** et forvaltningsvedtak med klagerett — det er trafikkstasjonen som treffer vedtak om førerkortutstedelse.

### Konklusjon per førerkortgruppe

For hver av de fire alternativene krysser legen av for ett av tre utfall:

| Gruppe | Helsekrav ikke oppfylt | Helsekrav oppfylt — vanlig varighet | Helsekrav oppfylt — begrenset varighet (antall år) | Status i PoC |
|---|---|---|---|---|
| Førerkortgruppe 1 | Avkrysning | Avkrysning | Avkrysning + tall | Mangler |
| Førerkortgruppe 2 | Avkrysning | Avkrysning | Avkrysning + tall | Mangler |
| Førerkortgruppe 3 inkl. kjøreseddel for drosje | Avkrysning | Avkrysning | Avkrysning + tall | Mangler |
| Førerkortgruppe 3 inkl. utrykningskompetanse/kjøreseddel for buss | Avkrysning | Avkrysning | Avkrysning + tall | Mangler |

### Progresjonsvurdering

| Felt | Type | Status i PoC |
|---|---|---|
| Er det tatt hensyn til forventet progresjon av eventuelle helsesvekkelser ved anbefaling av varighet? | Ja/Nei | Mangler |

### Vilkår

Faste vilkår legen kan krysse av for:

| Vilkår | Type | Status i PoC |
|---|---|---|
| Optisk korreksjon må brukes under føring av motorvogn i gruppe 1, 2 og 3 | Avkrysning | Mangler |
| Optisk korreksjon må brukes under føring av motorvogn i gruppe 2 og 3 | Avkrysning | Mangler |
| Helseattest gis med forbehold om at det leveres godkjent Helseattest førerett – syn (IS-2571) | Avkrysning | Mangler |
| Hørselshjelpemiddel må brukes under føring av motorvogn (gruppe 3) | Avkrysning | Mangler |
| Protese/ortose (støtteskinne o.l.) må brukes under føring av motorvogn i gruppe 1, 2 og 3 | Avkrysning | Mangler |
| Ved Ja på spørsmål 14b (stabil førlighetssvekelse) vurderer trafikkstasjonen om førerett likevel kan gis (§ 41) | Avkrysning | Mangler |
| Særlige vilkår (fritekst) | Fritekst | Delvis — `Forer_Vilkar` i modellen |

### Signatur

| Felt | Type | Status i PoC |
|---|---|---|
| Leges underskrift og HPR-nummer | Signatur + tekst | Delvis — HPR fra FHIR, signatur ikke digital |

---

## Implementeringsstatus

### Hva PoC-datamodellen dekker

```
ForerLegeerklaeringModel (src/App/models/ForerLegeerklaeringModel.cs)

Pasient_Fnr              ← FHIR Patient.identifier
Pasient_Fornavn          ← FHIR Patient.name
Pasient_Etternavn        ← FHIR Patient.name
Pasient_Fodselsdato      ← FHIR Patient.birthDate
Pasient_Kjonn            ← FHIR Patient.gender
Lege_HPR                 ← FHIR Practitioner.identifier
Lege_Fornavn             ← FHIR Practitioner.name
Lege_Etternavn           ← FHIR Practitioner.name
Virksomhet_Navn          ← FHIR Organization.name
Virksomhet_Orgnr         ← FHIR Organization.identifier (OID 4.101)
Virksomhet_HerId         ← FHIR Organization.identifier (OID 4.2)
Konsultasjon_Dato        ← FHIR Encounter.period.start
Diagnose_Kode            ← FHIR Condition.code.coding[0].code
Diagnose_Tekst           ← FHIR Condition.code.coding[0].display
Forer_Kjoretoygruppe     ← Legen velger (kjøretøykoder A–T, ikke gruppe 1/2/3)
Forer_ErSkikket          ← Legen velger (bool — for enkel)
Forer_Merknad            ← Legen fyller ut (fritekst)
Forer_Vilkar             ← Legen fyller ut (fritekst)
```

### Hva som mangler for et produksjonsklart skjema

En fullstendig implementering krever:

1. **Formål og førerkortgruppe** — 10 formålsalternativer + gruppe 1/2/3 (i stedet for kjøretøykoder A–T)
2. **Legens tilknytning** — 2 avkrysningsfelt + 1 fritekstfelt
3. **Identitetsbekreftelse** — 3 avkrysningsfelt
4. **Helsekategoriene 1–16** — 22 Ja/Nei-felt (inkl. underspørsmål), synstestdata (tall per øye)
5. **Konklusjon per gruppe** — 3 utfall × 4 grupper = 12 felt + 4 varighetstall
6. **Vilkår** — 6 faste avkrysninger + eksisterende fritekstfelt
7. **Progresjonsvurdering** — 1 Ja/Nei-felt

Total estimert utvidelse: fra 18 til ~70 felt i datamodellen.

### Merknader om digitalisering

- **Synstestdata** (spørsmål 1A) er i dag numeriske verdier (visus) — disse er ikke tilgjengelig som strukturerte FHIR-data i norske EPJ-er per 2026
- **Helsekategoriene 2–15** er legens kliniske vurdering og **kan ikke prefylles fra FHIR** — de tilhører legens faglige skjønn
- **Diagnose fra FHIR** (Condition) er en naturlig kilde for relevans til kategori 8 (hjerte/kar), 9 (diabetes) og 15 (andre), men legen bekrefter selv med Ja/Nei
- **Signatur** håndteres av Altinn (signering ved innsending) — ikke et eget skjemafelt

---

## Tilknyttede blanketter

| Blankett | ID | Formål |
|---|---|---|
| Helseattest førerett – syn | IS-2571 (2017) | Optiker/øyelege fyller ut synsvurdering separat |
| Egenerkl. om helse (søker) | — | Søker fyller ut selv, legen leser og bekrefter |
