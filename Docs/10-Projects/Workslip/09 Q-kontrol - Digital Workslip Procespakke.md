---
type: report
project: Workslip
status: draft
---
# Q-kontrol - Digital Workslip Procespakke

## Formål

Dette dokument beskriver, hvordan Workslip kan anvendes som digital dokumentationsmetode for VVS-virksomheders 4V05-lignende arbejdssedler, proceskontrol, slutkontrol og afvigelser.

Formålet er at få en principiel afklaring fra Q-kontrol/Byggeriets Kvalitetskontrol om, hvorvidt en VVS-virksomhed kan erstatte papirbaserede arbejdssedler med Workslip, hvis virksomhedens KLS-procedure opdateres til at beskrive den digitale proces.

Dokumentet er ikke en anmodning om, at Workslip som produkt certificeres selvstændigt. Det er en anmodning om en vurdering af, om Workslip-processen kan indgå som dokumentationsmetode i en autoriseret VVS-virksomheds KLS.

## Baggrund

Mange mindre VVS-virksomheder bruger i dag papirbaserede arbejdssedler og kontrolskemaer som dokumentation for udført arbejde, proceskontrol, slutkontrol og eventuelle afvigelser.

Papirflowet giver typisk disse problemer:

- manglende felter
- manglende signatur eller ansvarlig person
- ulæselige noter
- kontrolpunkter uden tydelig status
- tabte eller forsinkede sedler
- vanskeligt overblik på kontoret
- tidsforbrug ved manuel kontrol før fakturering
- svær fremfinding ved audit

Workslip er tænkt som en digital erstatning for denne dokumentation. Produktets kerne er ikke bare digital indtastning, men en compliance-styret arbejdsseddel, hvor montøren ikke kan sende en ufuldstændig rapport videre til kontoret.

## Forståelse af KLS og Q-kontrol

Vores forståelse er, at Q-kontrol vurderer, om den enkelte VVS-virksomheds kvalitetsledelsessystem er beskrevet, implementeret og anvendt korrekt.

Det betyder:

- Det er VVS-virksomhedens KLS, der skal godkendes og efterprøves.
- Workslip er et værktøj og en dokumentationsmetode i virksomhedens KLS.
- Den fagligt ansvarlige og virksomheden har fortsat ansvaret for, at KLS følges.
- Den digitale proces skal beskrives i KLS, før den kan erstatte papirprocessen.

Q-kontrol skal derfor ikke nødvendigvis godkende Workslip som et selvstændigt produkt. Det relevante spørgsmål er, om en virksomhed kan beskrive og bruge Workslip som sin dokumentationsmetode i KLS.

## Nuværende papirflow

Et typisk papirbaseret flow ser sådan ud:

1. Virksomheden har et godkendt KLS.
2. KLS beskriver, hvordan arbejde, kontrol og afvigelser dokumenteres.
3. Montøren udfører arbejdet hos kunden.
4. Montøren udfylder papirbaseret arbejdsseddel/kontrolskema.
5. Relevante kontrolpunkter afkrydses.
6. Slutkontrol udføres og dokumenteres.
7. Eventuelle afvigelser noteres.
8. Seddel afleveres eller sendes til kontoret.
9. Kontoret gennemgår sedlen for mangler.
10. Dokumentationen bruges til fakturering, kundehistorik og KLS-dokumentation.
11. Dokumentationen opbevares og kan fremfindes ved audit.

Q-kontrol er dermed ikke nødvendigvis et løbende godkendelsestrin for hver enkelt arbejdsseddel. Q-kontrol efterprøver, om virksomhedens KLS fungerer, og om virksomheden kan dokumentere, at proceduren følges.

## Foreslået Workslip-flow

Workslip erstatter papirflowet med følgende digitale proces:

1. Montøren opretter en ny digital arbejdsseddel i Workslip.
2. Montøren indtaster kunde, adresse, opgave og relevante installationsoplysninger.
3. Montøren vælger opgavetype og relevante kontrolområder.
4. Workslip viser relevante proces- og slutkontrolpunkter.
5. Montøren udfylder kontrolpunkter, noter, arbejdstid, materialer og eventuelle fotos.
6. Workslip blokerer indsendelse, hvis obligatoriske felter mangler.
7. Montøren registrerer slutkontrol og ansvarlig person.
8. Kunden, montøren eller intern ansvarlig signerer/godkender efter virksomhedens procedure.
9. Rapporten indsendes digitalt til kontoret.
10. Kontoret gennemgår rapporten i Backoffice.
11. Hvis noget mangler, returneres rapporten digitalt til montøren med begrundelse.
12. Når rapporten er komplet, godkendes den.
13. Rapporten kan eksporteres som PDF.
14. Rapport, revisionsspor og bilag opbevares digitalt i mindst 5 år.
15. Ved audit kan virksomheden søge, fremfinde og eksportere dokumentation.

## Dokumentation Workslip Skal Indeholde

For hver rapport bør Workslip gemme:

- rapportnummer eller entydigt ID
- kunde
- adresse/installationssted
- opgavetype
- installationskategori
- montør/udfører
- fagligt eller internt ansvarlig person, hvor relevant
- dato og tidspunkt for oprettelse
- dato og tidspunkt for udført arbejde
- dato og tidspunkt for slutkontrol
- valgte kontrolpunkter
- resultat af kontrolpunkter
- afvigelser og bemærkninger
- eventuelle fotos eller bilag
- signatur eller godkendelseshandling
- status i arbejdsgangen
- PDF-eksport af færdig rapport

## Audit Trail

Workslip bør registrere væsentlige hændelser:

- rapport oprettet
- rapport redigeret
- kontrolpunkt ændret
- foto/bilag tilføjet
- rapport indsendt
- rapport returneret til montør
- rapport korrigeret
- rapport godkendt
- rapport eksporteret
- rapport markeret som faktureret

For hver hændelse bør systemet gemme:

- tidspunkt
- bruger
- handlingstype
- relevant status før og efter handlingen

Rettelser efter indsendelse bør ikke fremstå som usynlig overskrivning af den oprindelige dokumentation.

## Valideringsregler

Workslip bør blokere indsendelse, hvis:

- kunde eller installationssted mangler
- opgavetype mangler
- relevante kontrolpunkter ikke er udfyldt
- slutkontrol ikke er registreret
- ansvarlig person mangler
- signatur/godkendelse mangler, hvis virksomhedens procedure kræver det
- afvigelser er markeret uden beskrivelse eller opfølgning

Dette skal sikre, at en rapport ikke kan sendes til kontoret, hvis den ikke ville kunne bestå virksomhedens egen interne kontrol.

## Afvigelser

Afvigelser skal håndteres eksplicit.

Hvis der konstateres en afvigelse, bør Workslip kunne registrere:

- type af afvigelse
- beskrivelse
- hvem der har registreret afvigelsen
- dato
- korrigerende handling
- ansvarlig for opfølgning
- status
- dokumentation for afslutning

Afvigelser skal kunne findes igen og kobles til den relevante rapport.

## PDF og Eksport

Hver færdig rapport skal kunne eksporteres som PDF.

PDF'en bør være læsbar uden adgang til selve appen og bør indeholde:

- rapportens stamdata
- kontrolpunkter og resultater
- noter og afvigelser
- signatur/godkendelsesoplysninger
- audit-relevante tidsstempler
- eventuelle billedbilag eller reference til bilag

PDF'en bør ligge tæt på den nuværende 4V05-lignende papirrapport, så kontorpersonale, kunder og auditorer hurtigt kan forstå dokumentationen.

## Opbevaring, Backup og Adgang

Workslip-processen forudsætter:

- opbevaring af rapporter og tilhørende dokumentation i mindst 5 år
- løbende backup
- mulighed for eksport ved ophør af kundeforhold
- adgangsstyring for montører, kontorpersonale og ansvarlige personer
- mulighed for at tilgå dokumentation, selv hvis en telefon mistes
- procedure for adgang, hvis en medarbejder stopper

Hvis Workslip driftes som ekstern cloudløsning, skal virksomheden have en klar beskrivelse af backup, eksport og adgang.

## Fallback Hvis Workslip Ikke Er Tilgængelig

Virksomhedens KLS bør beskrive en fallback-procedure.

Forslag:

1. Hvis Workslip ikke er tilgængelig på arbejdsstedet, udfyldes midlertidig papirrapport.
2. Den midlertidige rapport markeres med dato, montør og årsag.
3. Rapporten registreres i Workslip, når systemet igen er tilgængeligt.
4. Den oprindelige papirrapport scannes/fotograferes og vedhæftes den digitale rapport.
5. Kontoret godkender først rapporten, når digital registrering og bilag er komplette.

## Roller og Ansvar

Workslip ændrer ikke det formelle ansvar i KLS.

VVS-virksomheden er ansvarlig for:

- at KLS er opdateret
- at medarbejdere instrueres i processen
- at dokumentationen udfyldes korrekt
- at afvigelser håndteres
- at dokumentation kan fremfindes ved audit

Den fagligt ansvarlige er ansvarlig for:

- at proceduren er fagligt forsvarlig
- at tilsyn og slutkontrol håndteres efter KLS
- at digitale arbejdsgange faktisk følges

Workslip leverandøren er ansvarlig for:

- at systemet understøtter den beskrevne proces
- at valideringsregler virker
- at rapporter kan gemmes og eksporteres
- at revisionsspor registreres
- at backup/opbevaring kan dokumenteres

## Materiale Til Q-kontrol

Før dialog med Q-kontrol bør vi udarbejde:

- denne procesbeskrivelse
- [[09 Q-kontrol - Feltmapping 4V05 til Workslip|feltmapping fra nuværende 4V05/papirrapport til Workslip]]
- eksempel på udfyldt Workslip-rapport som PDF
- eksempel på rapport med afvigelse og korrigerende handling
- skærmbilleder af montørflow og Backoffice-flow
- teknisk systembeskrivelse for revisionsspor, backup, opbevaring og eksport
- forslag til KLS-tekst, som VVS-virksomheden kan tilpasse

## Spørgsmål Til Q-kontrol

Primært spørgsmål:

> Hvis en VVS-virksomheds KLS-procedure opdateres til at beskrive Workslip som digital dokumentationsmetode for 4V05-lignende arbejdssedler, proceskontrol, slutkontrol, afvigelser, revisionsspor, PDF-eksport og 5 års opbevaring, kan Q-kontrol så acceptere dette som erstatning for papirbaseret dokumentation?

Supplerende spørgsmål:

1. Er PDF-eksport tilstrækkeligt som auditvenligt format, hvis den underliggende data og revisionsspor gemmes digitalt?
2. Er digital signatur/godkendelseshandling acceptabel, hvis bruger, tidspunkt og rolle registreres?
3. Skal kunden fortsat opbevare en lokal kopi, eller er cloudbaseret opbevaring med backup og eksportmulighed tilstrækkeligt?
4. Hvilke oplysninger skal altid fremgå af rapporten for VVS/vand/afløb?
5. Skal afvigelser ligge på selve arbejdssedlen, i et særskilt afvigelsesregister, eller begge dele?
6. Skal Q-kontrol se og acceptere fallback-proceduren for nedetid?
7. Ønsker Q-kontrol en bestemt formulering i kundens KLS-håndbog?
8. Vil Q-kontrol vurdere dette principielt, eller kun i forbindelse med en konkret VVS-virksomheds KLS?

## Foreløbig Konklusion

Den foreløbige vurdering er, at Workslip bør kunne accepteres som digital dokumentationsmetode, hvis:

- den enkelte VVS-virksomheds KLS opdateres
- Workslip fanger de samme eller bedre oplysninger end papirprocessen
- der er revisionsspor, PDF-eksport, backup og 5 års opbevaring
- afvigelser håndteres tydeligt
- den faktiske brug i virksomheden matcher den beskrevne procedure

Den største risiko er ikke, at dokumentationen er digital. Den største risiko er, at den digitale proces ikke er beskrevet tydeligt nok i KLS, eller at virksomheden ikke følger den i praksis.

## Kilder

- Sikkerhedsstyrelsen: Krav om kvalitetsledelsessystem (KLS) - el, vvs, kloak, gas eller nedrivning af asbest  
  https://www.sik.dk/erhverv/ansoeg-og-registrer/vejledninger/el-gas-vvs-kloak-og-asbestautorisationer/krav-om-kvalitetsledelsessystem-kls-el-vvs-kloak-gas-eller-nedrivning-asbest

- Bekendtgørelse nr. 725 af 12/06/2024 om kvalitetsledelsessystemer  
  https://www.retsinformation.dk/eli/lta/2024/725

- Byggeriets Kvalitetskontrol: VVS  
  https://www.byggekvalitet.dk/vvs/

- Byggeriets Kvalitetskontrol: KLS-skabeloner for VVS-området  
  https://www.byggekvalitet.dk/kls-skabeloner-for-vvs-omraadet/

- Byggeriets Kvalitetskontrol: KLS i praksis - Det værdifulde KLS  
  https://byggekvalitet.dk/wp-content/uploads/2024/04/KLS-i-praksis-det-vaerdifulde-KLS.pdf
