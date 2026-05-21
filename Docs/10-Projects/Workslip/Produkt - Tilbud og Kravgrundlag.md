---
type: product-compliance-brief
project: [[Workslip]]
status: active
---

# Workslip - Tilbud og Kravgrundlag

## Kort positionering

Workslip er en digital arbejdsseddel og jobdokumentation for VVS-virksomheder.

Produktet hjælper virksomheden med at:

- oprette og tildele jobs
- få montøren til at udfylde en 4V05-lignende digital arbejdsseddel
- sikre at relevante kontrolpunkter bliver behandlet
- give kontoret overblik over status, mangler og fakturaklarhed
- gemme dokumentation og revisionsspor, så den kan fremfindes ved KLS-audit

Workslip erstatter ikke virksomhedens KLS, den fagligt ansvarlige eller kontrolinstansen. Workslip er et værktøj, som kunden kan beskrive i sin KLS-procedure.

## Den rigtige påstand

> Workslip digitaliserer VVS-virksomhedens arbejdsseddel og gør det lettere at dokumentere jobs, kontrolpunkter, ansvar, status og opfølgning på en måde, der kan beskrives i kundens KLS.

## Den forkerte påstand

> Workslip er godkendt af Q-kontrol og gør virksomheden automatisk KLS-compliant.

Den påstand må ikke bruges, medmindre en relevant kontrolinstans konkret og skriftligt har accepteret formuleringen.

## Hvad vi tilbyder virksomheder

Workslip tilbyder et digitalt flow:

1. Kontoret opretter eller modtager et job.
2. Jobbet tildeles eventuelt til en montør.
3. Montøren åbner jobbet i PWA’en.
4. Montøren registrerer kunde, adresse, kontaktperson og telefon.
5. Montøren beskriver opgaven.
6. Montøren vælger anlægstype og arbejdstype.
7. Workslip viser relevante 4V05-kontrolpunkter.
8. Montøren tager stilling til relevante/irrelevante punkter.
9. Montøren skriver bemærkninger og registrerer eventuelle afvigelser.
10. Montøren attesterer og indsender rapporten.
11. Backoffice viser jobbet til kontor/fagligt ansvarlig.
12. Kontoret godkender, returnerer, retter kontoroplysninger eller markerer fakturaklar.
13. Færdige jobs arkiveres og kan findes ved søgning, audit eller kundedialog.

## Kernebudskab til markedet

> Få VVS-arbejdssedlen væk fra papir og ind i et digitalt jobflow, hvor montøren dokumenterer arbejdet, og kontoret kan følge op uden at jagte manglende oplysninger.

## Kernebudskab til auditør

Workslip skal ikke bede auditøren om at godkende et softwareprodukt som selvstændig KLS-erstatning.

Vi skal bede auditøren vurdere, om en kunde kan beskrive en proces hvor:

- jobs dokumenteres digitalt
- montøren tager stilling til relevante 4V05-kontrolpunkter
- obligatoriske felter valideres før indsendelse
- ansvarlige handlinger attesteres med bruger og tidspunkt
- kontoret kan returnere eller godkende rapporter
- væsentlige hændelser logges
- dokumentation opbevares, sikkerhedskopieres og kan fremfindes

## Hvad Workslip ikke må love endnu

Workslip må ikke love:

- at alle kontrolinstanser automatisk accepterer processen
- at kunden ikke skal opdatere sin KLS-procedure
- at appen alene gør kunden compliant
- at digital attestering altid kan erstatte alle former for underskrift uden auditørens accept
- at cloudopbevaring alene er nok uden backup- og eksportprocedure
- at PDF-eksport, bilag og fuldt afvigelsesregister er færdigt, før det faktisk er implementeret

## Ikke MVP-scope

Følgende er ikke en del af MVP-retningen:

- scanning som primær arbejdsgang
- OCR
- AI-gennemgang
- gennemgangsscore
- Document Intelligence
- generisk dokumenttypeplatform

De kan senere vurderes som tillægsmoduler, men må ikke forvirre kundens eller udviklernes forståelse af MVP’en.

## Produktkrav afledt af processen

### 1. Jobs skal være det centrale objekt

Et job skal mindst kunne indeholde:

- job-id / rapportnummer
- kunde
- installationsadresse
- kontaktperson
- telefon
- opgavebeskrivelse
- anlægstype
- arbejdstype
- montør/ansvarlig
- status
- oprettet/indsendt/godkendt-tidspunkter

### 2. Montøren skal tvinges til stillingtagen

PWA’en skal ikke bare være en notesblok. Den skal aktivt sikre, at montøren tager stilling til relevante kontrolområder.

Særligt vigtigt:

- underkategorier må gerne markeres irrelevante, men ikke ignoreres skjult
- service/andet kræver forklaring
- afvigelse kræver bemærkning/opfølgning
- indsendelse må blokeres ved kritiske mangler

### 3. Backoffice skal gøre opfølgning praktisk

Backoffice skal kunne svare på:

- Hvilke jobs er nye?
- Hvilke jobs er indsendt men ikke gennemgået?
- Hvilke jobs mangler oplysninger?
- Hvem er montør/ansvarlig?
- Hvilke jobs er fakturaklare?
- Hvilke jobs hører sammen?
- Kan jeg finde kunden på navn, adresse eller telefon?

### 4. Statusflowet skal være simpelt

Anbefalet MVP-statusflow:

```text
Draft -> Assigned -> Submitted -> InReview -> Approved -> Archived
                         |             |
                         v             v
                      Returned       Rejected
```

Statusserne skal ikke være pynt. De skal drive UI, søgning, ansvar og revisionsspor.

### 5. Revisionsspor skal gemme væsentlige handlinger

Workslip bør logge:

- job oprettet
- job tildelt
- rapport redigeret
- kontrolpunkt ændret
- rapport indsendt
- rapport returneret
- rapport godkendt
- rapport arkiveret
- rapport markeret fakturaklar

For hver hændelse:

- bruger
- rolle
- tidspunkt
- handlingstype
- relevant status før/efter

### 6. Afvigelser skal mindst kunne forklares

MVP kan starte simpelt:

- afvigelse ja/nej
- beskrivelse
- ansvarlig/opfølgning
- status

Senere bør det udvides til fuldt afvigelsesregister.

### 7. PDF og eksport

PDF er vigtig for audit og kundedialog, men den bør ikke blandes sammen med AI/OCR.

Minimumskrav til auditvenlig rapportvisning/PDF:

- firma/CVR
- job-/rapportnummer
- kunde og adresse
- kontaktperson og telefon
- opgavebeskrivelse
- anlægs- og arbejdstype
- kontrolpunkter og resultater
- bemærkninger/afvigelser
- attestering/godkendelse
- auditmetadata

## Regelgrundlag - foreløbig forståelse

Sikkerhedsstyrelsen beskriver KLS som virksomhedens egenkontrolsystem. KLS skal blandt andet beskrive organisation, kompetencer, bemanding, instruktion, tilsyn, slutkontrol og dokumentation.

BEK nr. 725 af 12/06/2024 kræver blandt andet:

- at kvalitetsledelsessystemet dokumenteres
- at KLS er tilgængeligt for Sikkerhedsstyrelsen og kontrolinstansen
- at virksomheden opbevarer KLS og dokumentation i 5 år
- at KLS implementeres, opdateres og evalueres
- at KLS efterprøves af en kontrolinstans

For el-, vvs- og kloakinstallationsområdet skal KLS blandt andet kunne dokumentere slutkontrol/verifikation med:

- identifikation af installationen
- hvem der udførte installationen
- hvem der udførte slutkontrollen
- dato for udførelse
- resultatet af slutkontrollen

## Generelle spørgsmål der skal bekræftes af auditør

| Spørgsmål | Foreløbig vurdering | Skal bekræftes |
|---|---|---|
| Kan en digital arbejdsseddel erstatte papir, hvis kundens KLS beskriver processen? | Sandsynligvis ja | Ja |
| Er digital attestering med login, rolle og timestamp acceptabelt? | Sandsynligvis ja | Ja |
| Er PDF/rapportvisning nok som auditformat? | Sandsynligvis, hvis data og revisionsspor gemmes | Ja |
| Skal kunden have lokal kopi ud over cloud? | Ikke afklaret | Ja |
| Skal afvigelser være separat register eller kan de ligge på jobbet i MVP? | Bør afklares | Ja |
| Skal Workslip vurderes principielt eller kun i en konkret kundes KLS? | Sandsynligvis konkret kundes KLS | Ja |

## Kilder

- Sikkerhedsstyrelsen: Krav om kvalitetsledelsessystem (KLS)  
  https://www.sik.dk/erhverv/ansoeg-og-registrer/vejledninger/el-gas-vvs-kloak-og-asbestautorisationer/krav-om-kvalitetsledelsessystem-kls-el-vvs-kloak-gas-eller-nedrivning-asbest

- BEK nr. 725 af 12/06/2024 om kvalitetsledelsessystemer  
  https://www.retsinformation.dk/eli/lta/2024/725

- Internt: [[09 Q-kontrol - Digital Workslip Procespakke]]
- Internt: [[09 Q-kontrol - Feltmapping 4V05 til Workslip]]
- Internt: [[KLS Kravregister]]
