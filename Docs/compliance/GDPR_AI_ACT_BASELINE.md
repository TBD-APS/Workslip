# Workslip GDPR and EU AI Act compliance baseline

**Status:** Active  
**Owner:** Workslip product owner, with engineering ownership for technical controls  
**Source of truth:** Applicable EU and Danish law, regulator guidance, signed contracts, the current repository, deployed configuration, data inventories, and executable compliance evidence  
**Review cadence:** Quarterly, before every material data-processing or AI change, and when applicable law or official guidance changes  
**Linear:** WOR-294

## Purpose

This document makes GDPR and the EU AI Act permanent product, architecture, engineering, vendor, validation, and release requirements for Workslip.

It is an engineering and governance baseline. It is not a legal opinion, certification, privacy notice, data processing agreement, record of processing activities, or proof that Workslip is compliant. Compliance may only be claimed when the relevant legal roles, contracts, processing records, technical controls, operational procedures, and evidence have been verified by the accountable owner and, where necessary, qualified legal or data-protection counsel.

## Mandatory use

Read this document before changing any of the following:

- personal-data collection, storage, display, search, export, deletion, retention, logging, telemetry, cache, backup, or transfer;
- authentication, users, invitations, organizations, customers, contacts, addresses, jobs, worksheets, documents, audit history, or notifications;
- external services, subprocessors, hosting regions, analytics, monitoring, email, identity, storage, or support tooling;
- automated recommendations, scoring, classification, prioritization, content generation, decision support, biometric processing, emotion recognition, or any other AI-system capability;
- employee or contractor use of AI tools with Workslip source code, customer data, production data, credentials, logs, support material, or internal documentation.

A change with compliance impact must not be merged until the applicable gate below is completed or an explicit, time-limited waiver is approved by the accountable owner and tracked in Linear.

## Verified repository baseline as of 2026-08-01

### Personal data observed

The current implementation contains or processes data that can identify or relate to natural persons, including:

- user names, display names, email addresses, telephone numbers, roles, organization membership, Entra identifiers, authentication/session material, and invitation records;
- customer and contact-person names, email addresses, telephone numbers, postal and destination addresses;
- job assignments, job history, observations, status changes, timestamps, worksheets, work dates, hours, and links between users and work performed;
- technical telemetry such as routes, actions, dependency calls, error fingerprints, correlation identifiers, IP-related service metadata, and operational logs;
- uploaded documents or generated reports where enabled, whose contents may include personal or confidential business information.

This list is a code-derived starting point, not a complete record of processing activities.

### Services and integrations requiring classification

The repository contains integrations or infrastructure for Microsoft Entra ID and Microsoft Graph, Azure App Service, Azure SQL, Azure Storage, Key Vault, App Configuration, Application Insights, Azure Communication Services, Vercel, and the Danish Address Web API (DAWA). Development and delivery also use GitHub and test tooling.

For each service, the accountable owner must verify and record:

- whether it processes personal data in the relevant environment;
- controller, joint-controller, processor, or subprocessor roles;
- purpose, data categories, data subjects, legal basis, instructions, and retention;
- hosting and support locations, international transfers, transfer mechanism, and supplementary measures;
- data processing agreement, subprocessor terms, deletion/return terms, audit rights, breach duties, and security commitments.

Repository presence alone does not prove that the contract, legal role, region, or deployed setting is compliant.

### Existing positive technical controls observed

The frontend Application Insights integration disables cookie use and automatic request/exception tracking and applies redaction to tokens, credentials, email addresses, telephone numbers, query values, and identifiers before custom telemetry is sent. This is a useful control, but it does not replace a documented purpose, legal basis, retention period, access policy, processor agreement, or verification of all backend and infrastructure telemetry.

### AI baseline

Repository searches found no active LLM, generative-AI, machine-learning model, or AI decision feature in the current application code. Workslip must therefore maintain an AI inventory with the current product state recorded as **no product AI system identified** until evidence changes that classification.

AI tools used by developers, administrators, support staff, or other personnel are still subject to the AI literacy, confidentiality, data-protection, security, and vendor-governance rules below.

### Compliance status

The repository did not contain a maintained GDPR processing register, retention schedule, data-subject request procedure, breach-response procedure, subprocessor register, transfer assessment, DPIA register, AI-system inventory, or AI literacy record at the time this baseline was introduced. Their absence must not be interpreted as evidence that no external business document exists; it means the repository cannot currently demonstrate them.

Workslip must not describe itself as fully GDPR- or AI Act-compliant until the missing evidence is completed and approved.

## GDPR change gate

Complete the following for every new or materially changed processing activity.

### 1. Purpose and necessity

Document:

- the specific and legitimate purpose;
- why each data field is necessary and proportionate;
- the data subjects and data categories;
- whether special-category, criminal-offence, child, location, employee, behavioural, financial, or other high-impact data is involved;
- what happens if the data is not collected;
- whether an anonymous, aggregated, pseudonymous, less precise, or local-only alternative can meet the purpose.

Do not collect data merely because it may be useful later.

### 2. Legal role and lawful basis

For each processing activity, identify and approve:

- Workslip's role as controller, joint controller, processor, or subprocessor;
- the customer's and each vendor's role;
- the applicable lawful basis under GDPR Article 6;
- any additional condition required for special-category data under Article 9 or criminal-offence data under Article 10;
- the documented customer instruction where Workslip acts as processor;
- whether legitimate-interest assessment, consent records, statutory authority, or contractual necessity evidence is required.

Engineering must not invent or silently choose a lawful basis. Consent must not be used as a convenient fallback, and a contractual basis must not be stretched beyond what is objectively necessary to provide the service.

### 3. Transparency and rights

Before release, verify that the processing is accurately reflected in the applicable privacy information and that Workslip can support, where applicable:

- information and transparent communication;
- access and a usable copy of personal data;
- rectification;
- erasure, including downstream systems and documented backup treatment;
- restriction of processing;
- portability in a structured, commonly used, machine-readable format;
- objection;
- review of decisions based solely on automated processing with legal or similarly significant effects.

A UI delete action is not proof of GDPR erasure. Deletion semantics must cover primary data, derived data, search indexes, caches, files, telemetry where identifiable, vendor copies, exports, and backup expiry, while preserving only data that has a documented legal retention requirement.

### 4. Retention and lifecycle

Every personal-data category must have:

- a defined retention trigger and period;
- an accountable owner;
- an automated deletion, anonymization, or review mechanism where feasible;
- handling for tenant termination, user removal, cancelled invitations, failed onboarding, inactive accounts, and test data;
- deletion verification and failure alerting;
- documented exceptions for legal claims, accounting, safety, or regulatory obligations.

Indefinite retention and undocumented soft deletion are prohibited.

### 5. Privacy by design and default

Default behaviour must:

- expose data only to the minimum authorized role, tenant, and user scope;
- avoid preselected optional sharing or analytics;
- minimize fields, precision, visibility, audience, and retention;
- keep personal data out of URLs, client logs, public caches, analytics event names, metrics dimensions, exception messages, source control, CI output, screenshots, and test artifacts;
- use tenant-safe cache keys and clear user-scoped state on logout, role change, organization switch, and permission revocation;
- prevent stale data from a prior user or organization from remaining visible;
- avoid using production personal data in development, demos, tests, support reproductions, or AI tools.

### 6. Security of processing

Apply controls appropriate to the risk, including:

- least privilege, strong authentication, role and tenant authorization, and regular access review;
- encryption in transit and at rest, managed identities, secret rotation, and no credentials in source or artifacts;
- secure upload/download authorization, malware/content controls where relevant, and non-public storage;
- log and telemetry redaction, bounded retention, restricted access, and monitoring for leakage;
- idempotency, concurrency, transaction, retry, and partial-failure handling where personal data can be duplicated, lost, or exposed;
- backup, restore, business continuity, vulnerability management, dependency updates, incident detection, and evidence preservation;
- processor and integration failure handling that does not silently broaden disclosure or retain data indefinitely.

### 7. Processors, subprocessors, and transfers

A new vendor or material vendor change requires approval before personal data is sent.

Record:

- service, purpose, data, environments, region, support access, and retention;
- contract and GDPR Article 28 terms where applicable;
- subprocessor list and change-notification process;
- transfer mechanism for access or storage outside the EEA, transfer impact assessment, and supplementary measures where required;
- training use, human review, abuse monitoring, model improvement, and secondary-use terms;
- deletion, export, audit, incident, and termination procedures.

A vendor's general statement that it is “GDPR compliant” is not sufficient evidence.

### 8. DPIA and high-risk screening

Perform and document a DPIA screening before implementation when processing may create high risk to individuals, including systematic monitoring, new technologies, sensitive or large-scale data, vulnerable people, location or behavioural tracking, employee evaluation, data combination, automated decisions, or processing that may prevent people exercising a right or receiving a service.

If high risk remains after mitigation, do not deploy until the required consultation and approval path has been completed.

### 9. Records, incidents, and accountability

Maintain:

- a record of processing activities appropriate to Workslip's roles;
- current data-flow and data-location documentation;
- data processor and subprocessor registers;
- retention schedule and deletion evidence;
- data-subject request log and response procedure;
- incident and personal-data-breach procedure, including assessment, processor notification, authority notification, communication to individuals where required, and the GDPR 72-hour authority-notification deadline where applicable;
- security, privacy, and AI training records;
- DPIA, legitimate-interest, transfer, and automated-decision assessments;
- evidence of reviews, tests, incidents, corrective actions, and approvals.

Do not place personal data from incidents or rights requests into public GitHub issues, PRs, CI logs, or unrestricted chat systems.

## EU AI Act change gate

No AI system or AI-assisted product capability may be introduced without completing this gate.

### 1. Inventory and role

Register the system and identify:

- provider, deployer, importer, distributor, product manufacturer, or other relevant role;
- model/provider, version, hosting, purpose, users, affected persons, inputs, outputs, integrations, and decision points;
- whether Workslip substantially modifies, rebrands, fine-tunes, or places the system on the market;
- dependencies on general-purpose AI models and the provider documentation relied upon.

“Powered by AI” is not a classification.

### 2. Definition and risk classification

Document whether the capability is an AI system under the current legal definition and classify it against:

- prohibited practices;
- high-risk systems and any applicable Annex I or Annex III use case;
- transparency-risk systems under Article 50;
- general-purpose AI obligations;
- other or minimal-risk systems.

The classification must be reviewed when purpose, model, users, data, autonomy, output, integration, or affected population changes.

### 3. Prohibited practices

Do not build, procure, enable, test on real persons, or deploy prohibited AI practices. Escalate immediately if a proposal involves manipulation, exploitation of vulnerabilities, social scoring, unlawful biometric categorization, untargeted facial-image scraping, prohibited emotion recognition, predictive policing restrictions, or prohibited real-time remote biometric identification.

The exact current legal text and official guidance must be checked at decision time.

### 4. AI literacy

Personnel who procure, design, configure, evaluate, operate, monitor, support, or rely on AI systems must receive proportionate AI literacy covering:

- capabilities, limitations, hallucinations, uncertainty, bias, and appropriate reliance;
- data protection, confidentiality, security, prompt injection, data leakage, and adversarial input;
- human oversight, escalation, incident reporting, and prohibited uses;
- the specific system, context, users, and persons affected.

Training must be recorded. Access to an AI tool is not evidence of sufficient literacy.

### 5. Transparency and human oversight

Where applicable:

- clearly inform people when they interact with AI;
- identify AI-generated or manipulated content using required human- and machine-readable disclosures;
- explain the system's purpose, relevant limitations, data use, and how to obtain human assistance;
- provide effective human oversight with authority, competence, time, information, and ability to disregard, reverse, or stop the system;
- prevent automation bias and deceptive interface design;
- keep a meaningful non-AI or human-review path where required.

No AI output may be presented as verified fact merely because it was generated confidently.

### 6. Significant decisions

Workslip must not make decisions based solely on automated processing that produce legal or similarly significant effects without a separately approved legal basis, GDPR Article 22 analysis, AI Act classification, DPIA, meaningful human intervention, contestability, explanation, and operational safeguards.

AI must not autonomously hire, fire, discipline, rank employees, deny work, determine pay, allocate safety-critical tasks, or approve/reject customers unless the specific system has been legally and technically assessed and approved.

### 7. Data and model governance

Before sending data to an AI system:

- minimize and classify the data;
- remove personal, confidential, tenant, credential, and production information unless explicitly approved and necessary;
- verify purpose, lawful basis, controller/processor roles, retention, deletion, training use, model improvement, human access, region, and transfers;
- use approved enterprise settings and contracts that prohibit unauthorized secondary use;
- separate tenants and prevent retrieval, prompt, cache, vector, fine-tuning, evaluation, and log leakage;
- document dataset provenance, quality, representativeness, known limitations, bias testing, and copyright/licensing where relevant.

Public consumer AI tools must not receive Workslip production data, customer data, credentials, private source code, incident material, contracts, or identifiable support content unless specifically approved under this gate.

### 8. Technical validation and monitoring

AI validation must be risk-based and cover, where relevant:

- accuracy, robustness, repeatability, uncertainty, bias, and subgroup performance;
- prompt injection, indirect prompt injection, data exfiltration, insecure tool use, excessive agency, and output handling;
- authorization, tenant isolation, retrieval boundaries, logging, rate limits, abuse, and denial-of-service behaviour;
- harmful, illegal, discriminatory, misleading, or fabricated output;
- human-oversight effectiveness, fallback, outage, provider/model change, rollback, and manual operation;
- transparency notices and content marking;
- production monitoring, incident reporting, feedback, drift, version changes, and periodic reassessment.

Do not silently change a model or provider where performance, risk classification, data processing, transparency, or contractual terms may change.

## Pull-request compliance decision

Every PR must state one of:

1. **No personal-data or AI impact** — with a short explanation.
2. **Personal-data impact** — identify processing activity, data categories, purpose, role, lawful-basis owner, retention, rights, processors/transfers, DPIA result, security controls, and validation.
3. **AI-system impact** — identify inventory entry, legal role, risk classification, data handling, transparency, human oversight, monitoring, vendor evidence, and validation.
4. **Both** — complete both assessments.

A checkbox without evidence is not an assessment.

## Minimum validation evidence

Changes affecting personal data require evidence proportionate to risk, including as applicable:

- tenant and authorization tests;
- minimization and response-shape review;
- access/export, rectification, deletion, restriction, portability, and objection workflows;
- retention and deletion jobs, including retry, partial failure, files, caches, telemetry, and backup policy;
- log, metric, trace, screenshot, artifact, and error redaction;
- processor/integration failure, retry, idempotency, and data-location verification;
- browser validation of privacy notices, choices, rights, and recovery states.

AI changes require the AI validation and monitoring evidence described above. Passing functional tests alone is insufficient.

## Release blockers

Do not release when any of the following is unresolved:

- unknown controller/processor role or lawful-basis owner;
- unnecessary or undisclosed personal-data collection;
- no retention or deletion behaviour for newly collected personal data;
- missing processor agreement or unresolved international-transfer requirement;
- known cross-tenant, authorization, cache, log, telemetry, artifact, or backup leakage;
- required DPIA not completed or residual high risk not approved;
- AI system not inventoried or classified;
- possible prohibited AI practice;
- required transparency or meaningful human oversight missing;
- personal or confidential data sent to an AI provider without approved contractual and technical safeguards;
- a claim of legal compliance that is not supported by evidence and accountable approval.

## Current official sources

Legal and regulatory interpretation must use the current official text and guidance. At the date of this baseline, primary sources include:

- GDPR, Regulation (EU) 2016/679: <https://eur-lex.europa.eu/eli/reg/2016/679/oj>
- European Data Protection Board SME guidance: <https://www.edpb.europa.eu/sme-data-protection-guide/home_en>
- EU AI Act, Regulation (EU) 2024/1689: <https://eur-lex.europa.eu/eli/reg/2024/1689/oj>
- European Commission AI Act portal and implementation timeline: <https://digital-strategy.ec.europa.eu/en/policies/regulatory-framework-ai>
- European Commission AI Act transparency guidance: <https://digital-strategy.ec.europa.eu/en/library/guidelines-transparency-obligations-providers-and-deployers-ai-systems>
- Danish Data Protection Authority: <https://www.datatilsynet.dk/>

The AI Act implementation timeline has been amended and supplemented since adoption. Re-check the current official regulation, amendments, Commission guidance, standards, and Danish competent-authority guidance before relying on a date or classification.
