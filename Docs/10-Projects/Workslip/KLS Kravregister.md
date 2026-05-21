---
type: requirements-register
project: [[Workslip]]
status: active
---

# KLS Kravregister

## Hurtige produktnoter fra kunde/demo

Disse punkter skal bevares som konkrete produktbehov:

- Underkategorier skal kunne markeres “irrelevant”, fordi montøren stadig skal tage aktivt stilling.
- Backoffice skal have mobilvenlig visning, i hvert fald til opslag/status.
- Jobs/sager skal kunne linkes sammen.
- Niels/kontor skal kunne assigne jobs til folk.
- Backoffice skal kunne søge på kunde, navn, adresse og telefonnummer.
- Email og telefonnummer skal være synligt i Backoffice, hvor det er relevant.
- “Tegninger og færdigmelding” skal ikke fylde som selvstændigt hovedspor, hvis det ikke matcher MVP-demoens værdi.
- Udlæg/overnatning kan være et separat arbejdsseddel-/faktureringsfelt senere, ikke KLS-kerne.

## Formål

Dette register omsætter KLS-, auditør- og produktkrav til konkrete Workslip-krav.

Det er skrevet for den nuværende MVP-retning: digital arbejdsseddel, jobs, Backoffice og 4V05-dokumentation. AI/OCR/scanning er ikke MVP-scope.

## Statusforklaring

| Status | Betydning |
|---|---|
| Antaget | Vi mener kravet gælder, men det skal bekræftes af auditør |
| Bekræftet | Bekræftet skriftligt eller klart af auditør/kilde |
| Produktgap | Workslip mangler funktion eller visning |
| Dokumentationsgap | Produkt kan understøtte det, men vi mangler procedure/beskrivelse |
| MVP | Bør være med i første brugbare version |
| Senere | Vigtigt, men kan vente hvis MVP bliver for stor |

## Kravregister

| ID | Krav | Hvorfor | Status | Produktstatus | Dokumentationsstatus | Næste handling |
|---|---|---|---|---|---|---|
| KLS-001 | Kundens KLS skal beskrive Workslip-processen | Kontrolinstansen vurderer virksomhedens KLS og praksis | Antaget | Ikke produktfunktion | Procesbeskrivelse findes | Få auditør til at bekræfte og skriv kundetekst |
| KLS-002 | Job/rapport skal identificere installation/sag | Krav om dokumentation for udført slutkontrol med installationens identifikation | Bekræftet via BEK 725 | Delvist | Feltmapping findes | Tilføj/afklar sagsnr., rapportnr. og job-id |
| KLS-003 | Rapport skal vise hvem der udførte arbejdet | Minimumsdokumentation for slutkontrol | Bekræftet via BEK 725 | Delvist | Feltmapping findes | Bind montør til job/PWA/API |
| KLS-004 | Rapport skal vise hvem der udførte slutkontrol | Minimumsdokumentation for slutkontrol | Bekræftet via BEK 725 | Produktgap | Dokumenteret som manglende | Tilføj særskilt slutkontrol-felt |
| KLS-005 | Rapport skal vise dato for udførelse/slutkontrol | Minimumsdokumentation for slutkontrol | Bekræftet via BEK 725 | Delvist | Dokumenteret som manglende | Tilføj finalControlDate/reportDate hvor nødvendigt |
| KLS-006 | Rapport skal vise resultat af slutkontrol | Minimumsdokumentation for slutkontrol | Bekræftet via BEK 725 | Delvist | Dokumenteret som manglende | Tilføj samlet resultat/status |
| KLS-007 | Montør skal aktivt tage stilling til relevante kontrolpunkter | Reducerer manglende dokumentation | Antaget | Delvist | Procesbeskrivelse findes | Gør irrelevante underkategorier til eksplicit valg |
| KLS-008 | Indsendelse skal blokeres ved kritiske mangler | Digital proces skal være bedre end papir | Antaget | Delvist | Valideringsregler beskrevet | Definer præcise required fields |
| KLS-009 | Afvigelser skal kunne registreres og følges op | KLS skal beskrive forbedringsaktiviteter ved afvigelser | Bekræftet via BEK 725 | Produktgap | Procesbeskrivelse findes | Start simpelt i MVP, udvid senere |
| KLS-010 | Digital attestering skal gemme bruger, rolle og tidspunkt | Dokumenterer ansvar | Antaget | Produktgap | Login/attestering dokument findes | Afklar om MitID er nødvendigt eller om almindeligt login er nok |
| KLS-011 | Revisionsspor skal vise væsentlige hændelser | Sporbarhed ved digital proces | Antaget | Delvist | Beskrevet | Gem job/status/control events og vis dem i Backoffice/PDF |
| KLS-012 | Dokumentation skal opbevares i 5 år | BEK 725 | Bekræftet via BEK 725 | Produktgap | Mangler policy | Skriv opbevaring/backup-policy |
| KLS-013 | Cloudløsning kræver effektiv backup | Sikkerhedsstyrelsens præcisering om cloud/backup | Bekræftet via SIK vejledning | Produktgap | Dokumentationsgap | Beskriv backup, restore og eksport |
| KLS-014 | Dokumentation skal kunne fremfindes ved audit | KLS skal være tilgængeligt for kontrolinstans/SIK | Bekræftet via BEK 725 | Delvist | Beskrevet | Definer søgefelter og eksportpakke |
| KLS-015 | Færdige rapporter bør ikke kunne slettes uden spor | Revisionsspor og opbevaring | Antaget | Produktgap | Mangler policy | Definer slette-/arkiveringspolitik |
| KLS-016 | Backoffice skal kunne søge på kunde/adresse/telefon | Kontoret skal kunne finde og følge op | Produktkrav | Delvist | Ikke KLS-kerne | Bind søgning til API |
| KLS-017 | Jobs skal kunne tildeles montør/ansvarlig | Praktisk drift og ansvar | Produktkrav | Produktgap | Ikke KLS-kerne | Tilføj assignee-felter og UI |
| KLS-018 | Jobs skal kunne linkes sammen | Samme kunde/sag kan have flere relaterede jobs | Produktkrav | Produktgap | Ikke KLS-kerne | Tilføj relatedJobs senere eller i MVP hvis kunden kræver det |
| KLS-019 | Mobilvenlig Backoffice-visning | Kontor/leder kan have brug for opslag på farten | Produktkrav | Delvist | Ikke KLS-kerne | Gør vigtigste jobliste responsive |

## Åbne auditørspørgsmål

1. Kan en VVS-virksomhed bruge Workslip som digital dokumentationsmetode, hvis processen beskrives i kundens KLS?
2. Er digital attestering med bruger, rolle og timestamp tilstrækkelig, eller forventes underskrift/MitID?
3. Hvilke felter skal absolut fremgå af rapport/PDF for VVS/vand/afløb?
4. Er det nok, at afvigelser ligger på jobbet i MVP, eller skal der være separat afvigelsesregister?
5. Skal revisionsspor være synligt i PDF, Backoffice eller begge dele?
6. Er cloudbackup og eksportmulighed tilstrækkeligt, eller forventes lokal kopi?
7. Skal fallback ved nedetid beskrives i kundens KLS?
8. Vil kontrolinstansen vurdere processen principielt eller kun i en konkret kundes KLS?

## Produktbacklog afledt af registeret

Prioritet før seriøs pilot:

1. Ensret backend/API til jobs.
2. Bind PWA-indsendelse til Jobs API.
3. Bind Backoffice til Jobs API.
4. Tilføj montør/assignee og ansvarlig person.
5. Tilføj slutkontrol udført af, slutkontroldato og samlet resultat.
6. Gør underkategorier eksplicit relevante/irrelevante.
7. Tilføj digital attestering med bruger, rolle og tidspunkt.
8. Gør revisionsspor synligt for kontor/audit.
9. Tilføj simpel afvigelsesregistrering.
10. Lav auditvenlig rapportvisning/PDF.
11. Beskriv backup, opbevaring, eksport og fallback.
12. Tilføj mobilvenlig Backoffice-jobliste.
13. Tilføj job-linking, hvis kunden prioriterer det højt.
