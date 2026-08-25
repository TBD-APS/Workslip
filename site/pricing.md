---
title: Priser
description: Byg jeres Workslip-løsning og betal kun for de moduler, I vælger.
pricing: true
---

<section class="pricing-hero">
  <div class="pricing-hero-glow pricing-hero-glow-one" aria-hidden="true"></div>
  <div class="pricing-hero-glow pricing-hero-glow-two" aria-hidden="true"></div>
  <div class="container pricing-hero-grid">
    <div>
      <p class="pricing-kicker">Outcome builder</p>
      <h1>Betal kun for det, I bruger nu.</h1>
      <p class="pricing-lead">Byg jeres løsning ud fra de resultater, der giver mest værdi lige nu. Slå moduler til og fra, når behovet ændrer sig.</p>
    </div>
    <ul class="pricing-promises" aria-label="Fordele ved modulpriser">
      <li><span aria-hidden="true">✓</span> Betal kun for de moduler, I vælger</li>
      <li><span aria-hidden="true">✓</span> Skru op eller ned, når behovet ændrer sig</li>
      <li><span aria-hidden="true">✓</span> Ingen binding — opsig når som helst</li>
    </ul>
  </div>
</section>

<section class="pricing-builder-section" aria-labelledby="pricing-builder-title">
  <div class="container pricing-builder-grid">
    <div class="pricing-builder-card">
      <div class="pricing-builder-heading">
        <div>
          <h2 id="pricing-builder-title">Byg jeres løsning</h2>
          <p>Vælg de moduler, I vil aktivere nu.</p>
        </div>
        <a class="pricing-help-link" href="#pricing-how-it-works">Sådan virker det</a>
      </div>

      <div class="pricing-module-list" id="pricing-module-list">
        <article class="pricing-module" data-module="job-flow" data-price="1000" data-label="Job flow">
          <div class="pricing-module-main">
            <div class="pricing-module-icon pricing-module-icon-blue" aria-hidden="true">▣</div>
            <div class="pricing-module-copy">
              <span class="pricing-module-badge">Kerneflow</span>
              <h3>Job flow</h3>
              <p>Opret, planlæg og udfør jobs. Overblik over status, opgaver og kommunikation i marken.</p>
            </div>
            <div class="pricing-module-action">
              <span class="pricing-module-price">1.000 kr./md.</span>
              <label class="pricing-switch">
                <span class="sr-only">Aktivér Job flow</span>
                <input id="pricing-module-job-flow" type="checkbox" data-pricing-toggle>
                <span class="pricing-switch-track" aria-hidden="true"></span>
              </label>
            </div>
          </div>
          <details class="pricing-module-details">
            <summary>Se hvad modulet indeholder</summary>
            <ul>
              <li>Joboprettelse og planlægning</li>
              <li>Status og medarbejderflow</li>
              <li>Noter og grundlæggende dokumentation</li>
            </ul>
          </details>
        </article>

        <article class="pricing-module" data-module="time-economy" data-price="1235" data-label="Tid & jobøkonomi">
          <div class="pricing-module-main">
            <div class="pricing-module-icon pricing-module-icon-cyan" aria-hidden="true">◷</div>
            <div class="pricing-module-copy">
              <span class="pricing-module-badge">Tilvalg</span>
              <h3>Tid &amp; jobøkonomi</h3>
              <p>Registrér tid, kørsel og materialer. Få overblik over dækningsbidrag og lønsomhed pr. job.</p>
            </div>
            <div class="pricing-module-action">
              <span class="pricing-module-price">1.235 kr./md.</span>
              <label class="pricing-switch">
                <span class="sr-only">Aktivér Tid &amp; jobøkonomi</span>
                <input id="pricing-module-time-economy" type="checkbox" data-pricing-toggle>
                <span class="pricing-switch-track" aria-hidden="true"></span>
              </label>
            </div>
          </div>
          <details class="pricing-module-details">
            <summary>Se hvad modulet indeholder</summary>
            <ul>
              <li>Tidsregistrering på jobs</li>
              <li>Kørsel og materialeforbrug</li>
              <li>Jobøkonomi og lønsomhedsoverblik</li>
            </ul>
          </details>
        </article>

        <article class="pricing-module pricing-module-pilot" data-module="quality-delivery" data-label="Kvalitet & aflevering">
          <div class="pricing-module-main">
            <div class="pricing-module-icon pricing-module-icon-violet" aria-hidden="true">◇</div>
            <div class="pricing-module-copy">
              <span class="pricing-module-badge pricing-module-badge-violet">Pilot</span>
              <h3>Kvalitet &amp; aflevering</h3>
              <p>Tjeklister, fotos og dokumentation sikrer kvalitet og en professionel aflevering.</p>
            </div>
            <div class="pricing-module-action">
              <a class="pricing-secondary-action" href="{{ '/demo/' | relative_url }}?module=quality-delivery">Pilot — tal med os</a>
            </div>
          </div>
        </article>

        <article class="pricing-module pricing-module-upcoming" data-module="insights" data-label="Indsigt">
          <div class="pricing-module-main">
            <div class="pricing-module-icon pricing-module-icon-slate" aria-hidden="true">▥</div>
            <div class="pricing-module-copy">
              <span class="pricing-module-badge pricing-module-badge-muted">Kommer snart</span>
              <h3>Indsigt</h3>
              <p>Rapporter og nøgletal giver jer indblik i drift, performance og kundetilfredshed.</p>
            </div>
            <div class="pricing-module-action">
              <a class="pricing-secondary-action pricing-secondary-action-muted" href="{{ '/demo/' | relative_url }}?module=insights">Vis interesse</a>
            </div>
          </div>
        </article>
      </div>

      <p class="pricing-builder-footnote">Priser er pr. virksomhed. Alle beløb er ekskl. moms.</p>
    </div>

    <aside class="pricing-summary" aria-live="polite" aria-labelledby="pricing-summary-title">
      <p class="pricing-summary-label" id="pricing-summary-title">Jeres månedlige total</p>
      <div class="pricing-total"><strong id="pricing-total-value">0</strong><span>kr./md.</span></div>
      <p class="pricing-vat">ekskl. moms</p>

      <div class="pricing-summary-lines" id="pricing-summary-lines">
        <div class="pricing-summary-empty" id="pricing-summary-empty">Ingen moduler valgt endnu.</div>
      </div>

      <div class="pricing-summary-total-row">
        <strong>I alt ekskl. moms</strong>
        <span id="pricing-total-row">0 kr./md.</span>
      </div>

      <div class="pricing-not-included">
        <strong>Ikke inkluderet</strong>
        <p>Følgende er ikke en del af den viste modulpris.</p>
        <ul>
          <li>Kvalitet &amp; aflevering, før pilotpris er aftalt</li>
          <li>Indsigt, før modulet lanceres</li>
          <li>Eventuelle tredjepartslicenser og hardware</li>
          <li>Særlige engangsydelser eller datamigrering</li>
        </ul>
      </div>

      <a class="pricing-primary-cta" id="pricing-primary-cta" href="{{ '/demo/' | relative_url }}">Start jeres opsætning <span aria-hidden="true">→</span></a>
      <a class="pricing-outline-cta" href="#pricing-how-it-works">Se hvad der er inkluderet</a>
      <p class="pricing-no-binding">Ingen binding — opsig når som helst</p>
    </aside>
  </div>
</section>

<section class="pricing-how-section" id="pricing-how-it-works">
  <div class="container pricing-how-grid">
    <div>
      <p class="pricing-kicker pricing-kicker-light">Sådan virker det</p>
      <h2>Start småt. Udvid, når behovet opstår.</h2>
    </div>
    <div class="pricing-how-steps">
      <article><span>01</span><h3>Vælg moduler</h3><p>Slå kun de moduler til, der giver værdi for jer nu. Intet modul er påkrævet.</p></article>
      <article><span>02</span><h3>Se prisen med det samme</h3><p>Månedstotalen følger jeres valg live — også helt ned til 0 kr./md.</p></article>
      <article><span>03</span><h3>Tilpas senere</h3><p>Tilføj eller fjern moduler, når jeres arbejdsgange og behov ændrer sig.</p></article>
    </div>
  </div>
</section>
