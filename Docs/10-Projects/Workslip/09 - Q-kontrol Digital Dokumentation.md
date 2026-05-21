---
type: research-note
project: Workslip
status: active
---

# Q-kontrol - Digital Dokumentation

## Formål

Dette dokument forklarer Workslip som digital dokumentationsmetode for VVS-virksomheders arbejdssedler og KLS-relevante kontrolpunkter.

Fokus er den nuværende MVP:

- digital arbejdsseddel
- jobs/sager
- 4V05-lignende kontrolpunkter
- Backoffice-gennemgang
- digital attestering
- revisionsspor
- opbevaring og fremfinding

Scanning, OCR, AI og Document Intelligence er ikke en del af MVP-forklaringen.

## Grundantagelse

Kontrolinstansen godkender og efterprøver normalt virksomhedens KLS, ikke et softwareprodukt isoleret.

Derfor er det relevante spørgsmål:

> Kan en VVS-virksomhed opdatere sin KLS-procedure, så Workslip beskrives som virksomhedens digitale metode til arbejdssedler, kontrolpunkter, attestering, opbevaring og fremfinding?

## Papirproblemet

Det nuværende papirflow giver ofte problemer:

- arbejdssedler bliver væk eller afleveres sent
- felter mangler
- skrift er svær at læse
- kontrolpunkter bliver ikke dokumenteret ensartet
- kontoret bruger tid på at ringe efter manglende oplysninger
- dokumentation er svær at finde ved audit
- ansvar og tidsstempler er uklare

Workslip skal ikke bare digitalisere papir som billede. Workslip skal forbedre selve arbejdsgangen, så mangler fanges tidligere.

## Foreslået digital proces

1. Job oprettes i Workslip eller startes af montør.
2. Montør udfylder kunde, adresse, kontakt, telefon og opgavebeskrivelse.
3. Montør vælger anlægstype og arbejdstype.
4. Workslip viser relevante kontrolpunkter.
5. Montør markerer relevante/irrelevante underkategorier og udfylder kontrolpunkter.
6. Montør registrerer bemærkninger og eventuelle afvigelser.
7. Workslip validerer obligatoriske felter.
8. Montør attesterer og indsender rapporten.
9. Backoffice viser jobbet til kontor/fagligt ansvarlig.
10. Kontoret returnerer jobbet ved mangler eller godkender det.
11. Færdige jobs arkiveres og kan fremfindes ved audit.

## Hvad Workslip skal dokumentere

For hvert job bør systemet gemme:

- job-id / rapportnummer
- kunde og installationsadresse
- kontaktperson og telefon
- opgavebeskrivelse
- anlægstype
- arbejdstype
- montør/udfører
- hvem der udførte slutkontrol
- dato for arbejde/slutkontrol
- kontrolpunkter og resultat
- bemærkninger
- afvigelser og opfølgning
- attestering/godkendelse
- status
- revisionsspor

## Digital attestering

Digital attestering bør vise:

- hvem der attesterede
- hvilken rolle personen havde
- hvad der blev attesteret
- tidspunkt
- om handlingen var indsendelse, godkendelse, returnering eller arkivering

Spørgsmålet til auditør er, om almindeligt login med rolle og timestamp er tilstrækkeligt, eller om kunden i bestemte situationer skal bruge MitID/MitID Erhverv eller en egentlig signatur.

## Revisionsspor

Workslip bør registrere væsentlige hændelser:

- job oprettet
- job tildelt
- job redigeret
- kontrolpunkt ændret
- job indsendt
- job returneret
- job korrigeret
- job godkendt
- job arkiveret
- job markeret fakturaklar

Rettelser efter indsendelse bør ikke skjule, hvad der oprindeligt blev sendt.

## Opbevaring og fremfinding

KLS-dokumentation skal kunne fremfindes ved audit.

Workslip bør derfor understøtte:

- søgning på kunde, adresse, telefon, rapportnummer og dato
- filtrering på status og montør
- arkiv over færdige jobs
- eksport eller PDF-rapport
- opbevaring i mindst 5 år, hvis dette bekræftes som relevant krav
- backup, restore og eksport ved ophør

## Afvigelser

Afvigelser bør ikke bare være løs fritekst.

MVP kan starte med:

- afvigelse ja/nej
- beskrivelse
- ansvarlig/opfølgning
- status

Senere kan det udvides til fuldt afvigelsesregister med årsag, korrigerende handling, lukning og dokumentation.

## Spørgsmål til Q-kontrol/kontrolinstans

1. Kan en digital arbejdsseddel accepteres som erstatning for papir, hvis kundens KLS beskriver processen?
2. Hvilke minimumsfelter skal fremgå for slutkontrol/verifikation?
3. Er digital attestering med login, rolle og tidspunkt acceptabelt?
4. Skal rapporten kunne eksporteres som PDF, eller er digital fremfinding nok?
5. Skal afvigelser ligge på selve jobbet, i separat register eller begge dele?
6. Hvilke krav bør beskrives omkring backup, opbevaring og eksport?
7. Skal fallback ved nedetid beskrives eksplicit?
8. Er der bestemte formuleringer kunden bør bruge i sin KLS-håndbog?

## Foreløbig konklusion

Workslip bør kunne beskrives som en digital dokumentationsmetode i kundens KLS, hvis:

- den digitale proces er tydeligt beskrevet
- relevante kontrolpunkter udfyldes
- ansvar og attestering registreres
- væsentlige ændringer logges
- dokumentation kan fremfindes og eksporteres
- opbevaring, backup og fallback er beskrevet

Den største risiko er ikke, at processen er digital. Den største risiko er uklar ansvarsplacering, manglende obligatoriske felter eller en KLS-procedure, der ikke beskriver hvordan systemet faktisk bruges.
