# Åpne beslutninger og uavklarte designvalg

Dette dokumentet parkerer beslutninger som krever menneskelig avklaring — juridisk, organisatorisk eller arkitektonisk — og som ikke kan løses med kode alene. Hvert punkt inneholder bakgrunn, alternativer og hvem som beslutter.

---

## C-1: Hvem er «parten» i Altinn-instansen?

**Problemstilling:**  
En Altinn-instans har alltid én «part» (party) som eier instansen. I dag starter legen instansen på vegne av seg selv. Spørsmålet er om parten skal være:

| Alternativ | Beskrivelse | Konsekvens |
|---|---|---|
| **A — Legen** | Legen er part og signatar | Enklest teknisk. Men instansen lever i legens «innboks» i Altinn, ikke pasientens. |
| **B — Pasienten** | Legen fyller ut på vegne av pasienten | Krever delegering eller samtykke. Mer korrekt juridisk (erklæringen gjelder pasienten). |
| **C — Legekontoret (org)** | Instansen tilhører virksomheten | Mulig ved bruk av `partyTypesAllowed.organisation`. Krever systemtilgang-autorisasjon. |

**Avhengigheter:** Valget påvirker autorisasjonsmodellen i `policy.xml`, rollekrav, og hvem som kan se instansen i Altinn-meldingsboksen.

**Beslutter:** Tjenesteeier (Digdir) i samråd med Statens vegvesen og Helsedirektoratet.

**Status:** Uavklart — PoC bruker alternativ A (legen som part).

---

## C-2: HelseID — når skal BFF-siden validere tokenet?

**Problemstilling:**  <!-- KOMMENTAR: Mulig jeg missforstår, men jeg forvanter at DelseID-tokenet må valideres før det mappes til et Altinn-internt token.   -->
I dag stoler BFF-en (ASP.NET Core) på access token fra SMART-mock uten å validere signaturen. I produksjon med HelseID må tokenet valideres. Spørsmålet er *når* dette skal innføres og *hva* som kreves:

- JWT Bearer-validering mot HelseID sitt JWKS-endepunkt
- Krav til claims: `pid` (fnr), `helseid://claims/hpr/hpr_number`, `assurance_level`
- NHN Tillitsrammeverk-claims for behandlingsformål (`CareRelationshipPurposeOfUseCode`, etc.)
- Klientregistrering på selvbetjening.test.nhn.no (HelseID test)

**Konkrete oppgaver som er utsatt:**
1. Registrer klient på `selvbetjening.test.nhn.no`
2. Legg til `AddJwtBearer` i `Program.cs` med HelseID JWKS-URL
3. Trekk ut `pid`-claim og verifiser mot FHIR `Practitioner.identifier` (fnr)
4. Valider at `assurance_level >= high` (kreves for helseopplysninger)
5. Logg `hpr_number` + `pid` i audit trail

**Referanse:** Se IMPLEMENTERING.md §14 (HelseID kom-i-gang) for detaljert veiledning.

**Beslutter:** Teknisk team + NHN avtaleprosess.

**Status:** Dokumentert, ikke implementert. Blokkert av klientregistrering.

---

## C-3: Mottaksarkitektur — hvem er tjenesteeier?

**Problemstilling:**  
Når legen sender inn erklæringen, hvor skal den ende opp?

| Alternativ | Beskrivelse | Avhengigheter |
|---|---|---|
| **A — Digdir (nåværende)** | Digdir eier tjenesten, tar imot via Altinn storage | Ingen nye avtaler. Men Digdir er ikke naturlig mottaker av helseerklæringer. |
| **B — Statens vegvesen** | SVV som tjenesteeier og mottaker | Krever avtale med SVV, integrasjon mot SVVs systemer. Riktig juridisk mottaker. <!-- KOMMENTAR: For Førerrets tjenesten, er det Statens vegvesen som må være tjenesteeier. Det er de som har ansvaret for å utstede førrerett --> |
| **C — Helsedirektoratet** | Hdir som nasjonal koordinator | Krever avtale og API-integrasjon. |
| **D — EPJ (DocumentReference)** | Erklæringen skrives tilbake til pasientjournalen | Krever FHIR `DocumentReference` writeback til EPJ etter innsending. |

**Avhengigheter:** Valget påvirker `applicationmetadata.json` (`org`-felt), `policy.xml` (tjenesteeier-regel), og evt. Maskinporten-scope for system-til-system-integrasjon.

**Merk:** Alternativ D (DocumentReference writeback) er en viktig funksjon for helhetlig arbeidsflyt — legen får erklæringen dokumentert i journalen. Dette er teknisk mulig via FHIR `PUT /DocumentReference` med SMART access token, men er ikke implementert i PoC.

**Beslutter:** Programleder + juridisk avdeling + Statens vegvesen.

**Status:** Uavklart — PoC bruker Digdir som placeholder.

---

## C-4: Rettslig grunnlag og DPIA

**Problemstilling:**  
Behandling av helseopplysninger i dette systemet krever avklaring av rettslig grunnlag og en formell personvernkonsekvensvurdering (DPIA/DPIA).

**Relevante hjemler (foreløpig vurdering):**
- Helsepersonelloven § 45 — plikt til å utstede attest/erklæring på anmodning
- Pasientjournalloven § 6 — behandling av helseopplysninger i forbindelse med helsehjelp
- GDPR art. 9 nr. 2 bokstav h — behandling nødvendig for medisinske formål av helsepersonell

**Åpne spørsmål:**
1. Er Digdir behandlingsansvarlig eller databehandler? Hvem er behandlingsansvarlig i siste instans (SVV? Hdir? Legekontoret?)?
2. Trenger Altinn-appen en egen behandlingsprotokoll (art. 30)?
3. Er det krav om DPIA (art. 35) gitt systematisk behandling av helseopplysninger i stor skala?
4. Hva er minimumsloggingskravet (art. 5 nr. 2 — ansvarlighetsprinsippet)?
5. Krav til databehandleravtale mellom Digdir og EPJ-leverandøren?

**Beslutter:** Personvernombud + juridisk rådgiver + evt. Datatilsynet (forhåndskonsultasjon).

**Status:** Uavklart. Se KRAVSPESIFIKASJON-v0.6.md §7 for utdyping.

---

## C-5: Fullstendig IS-2569 og pasientens egenerklæring (NA-0201)

**Problemstilling:**  
Nåværende datamodell (`ForerLegeerklaeringModel`) dekker kun en liten del av blankett IS-2569 (Helseattest for førerkort). Full implementering krever:

**Del A — Manglende felt i IS-2569:**
- Alle 17 helsekategorier med klinisk vurdering (synsfunksjon, hørsel, bevegelsesapparat, hjerte/kar, nevrologi, psykisk helse, kognitiv funksjon, søvn, diabetes, nyre, lever, kreft, øre/nese/hals, muskel/skjelett, rusmidler, medikamenter)
- Vilkår og begrensninger per kategori (kodeverk fra Statens vegvesen)
- Legens underskrift og dato
- Erklæring om at pasienten er informert

**Del B — Pasientens egenerklæring (NA-0201):**
- 17 ja/nei-spørsmål om helsetilstand
- Digital flyt via Dialogporten (se PASIENTFLYT.md for arkitekturforslag)
- Mapping mellom NA-0201-svar og IS-2569 helsekategorier
- FHIR QuestionnaireResponse for å overføre svar til legen

**Prioritering:**
- PoC: Kun grunndata (navn, fnr, HPR, org, vurdering) — ferdig
- v1.0: Full IS-2569 dekningsgrad — krever feltmapping-arbeid med Helsedirektoratet
- v2.0: Digital NA-0201 via Dialogporten — krever ny avtale og tjenesteutvikling

**Beslutter:** Produkteier + Helsedirektoratet (IS-2569 eier) + Statens vegvesen (NA-0201 eier).

**Status:** Datamodell og feltstruktur dokumentert i SKJEMA-IS2569.md. Implementering utsatt.

---

## Beslutningslogg

| Dato | Beslutning | Besluttet av | Status |
|---|---|---|---|
| 2026-06-16 | PoC bruker legen som part (C-1 alt. A) | JSF | Midlertidig |
| 2026-06-16 | HelseID-validering utsettes til post-PoC (C-2) | JSF | Utsatt |
| 2026-06-16 | Digdir som tjenesteeier-placeholder (C-3 alt. A) | JSF | Midlertidig |
| 2026-06-16 | DPIA-avklaring krever juridisk ressurs (C-4) | JSF | Åpen |
| 2026-06-16 | Full IS-2569 utsettes til v1.0 (C-5) | JSF | Planlagt |
