---
title: Workslip
description: Saml planlægning, registrering og godkendelse af arbejde ét sted.
module_builder: true
---

<section class="hero hero-fullscreen">
  <div class="hero-video-layer" aria-hidden="true">
    <video class="hero-video" autoplay muted loop playsinline preload="metadata" poster="{{ '/assets/video/workslip-hero-poster.jpg' | relative_url }}">
      <source src="{{ '/assets/video/workslip-hero-video.mp4' | relative_url }}" type="video/mp4">
    </video>
  </div>
  <div class="container hero-fullscreen-content">
    <div class="hero-copy">
      <p class="eyebrow"><span class="pulse-dot" aria-hidden="true"></span> Workslip for virksomheder i marken</p>
      <h1>Få styr på arbejdet. <span>Uden at miste tempoet.</span></h1>
      <p class="lead">Planlæg, registrér og godkend arbejdet, mens det sker. Workslip samler teamet i marken og kontoret i ét klart flow.</p>
      <div class="actions">
        <a class="button" href="{{ '/demo/' | relative_url }}">Prøv demoen <span aria-hidden="true">→</span></a>
        <a class="button hero-secondary-button" href="{{ '/features/' | relative_url }}">Se funktionerne</a>
      </div>
    </div>
  </div>
</section>

<section class="signal-strip" aria-label="Hvad Workslip samler">
  <div class="container signal-grid">
    <p>Ét fælles flow for teamet</p>
    <span>Opgaver</span><span>Timer</span><span>Noter</span><span>Godkendelser</span>
  </div>
</section>

{% include module-builder.html %}

<section class="section workflow-section">
  <div class="container">
    <div class="section-heading split-heading">
      <div>
        <p class="eyebrow">Skabt til hverdagen</p>
        <h2>Få hvert job til at glide videre.</h2>
      </div>
      <p>Workslip giver hver rolle et klart næste skridt – fra første plan til det færdige grundlag for administration og fakturering.</p>
    </div>
    <div class="workflow-grid">
      <article class="workflow-card workflow-card-featured">
        <span class="card-number">01</span>
        <div class="workflow-icon"><span></span><span></span><span></span></div>
        <h3>Planlæg med ro</h3>
        <p>Fordel opgaver og giv medarbejderen de oplysninger, der skal bruges, lige dér hvor arbejdet udføres.</p>
        <a href="{{ '/features/' | relative_url }}">Se planlægning <span aria-hidden="true">→</span></a>
      </article>
      <article class="workflow-card">
        <span class="card-number">02</span>
        <div class="workflow-icon register-icon"><span></span><span></span><span></span></div>
        <h3>Registrér i flowet</h3>
        <p>Timer og noter registreres tæt på det udførte arbejde, så intet skal genskabes senere.</p>
        <a href="{{ '/features/' | relative_url }}">Se registrering <span aria-hidden="true">→</span></a>
      </article>
      <article class="workflow-card">
        <span class="card-number">03</span>
        <div class="workflow-icon approval-icon"><span>✓</span></div>
        <h3>Godkend med overblik</h3>
        <p>Gennemgå indsendte arbejdssedler, håndtér rettelser og behold en tydelig status gennem hele processen.</p>
        <a href="{{ '/features/' | relative_url }}">Se godkendelse <span aria-hidden="true">→</span></a>
      </article>
    </div>
  </div>
</section>

<section class="section clarity-section">
  <div class="container clarity-grid">
    <div class="clarity-visual" aria-hidden="true">
      <div class="clarity-panel">
        <div class="clarity-panel-head"><span></span><span></span><span></span></div>
        <div class="clarity-lines"><i></i><i></i><i></i><i></i><i></i></div>
        <div class="clarity-chart"><i></i><i></i><i></i><i></i><i></i><i></i></div>
        <div class="clarity-badge"><span>✓</span> Klar til godkendelse</div>
      </div>
      <div class="clarity-ring"></div>
    </div>
    <div class="clarity-copy">
      <p class="eyebrow">Mindre friktion. Mere fremdrift.</p>
      <h2>Det fulde overblik, uden at skifte mellem systemer.</h2>
      <p>Workslip samler den vigtigste dokumentation omkring arbejdet i én tydelig arbejdsgang. Teamet i marken får retning, og administrationen får et bedre udgangspunkt for næste skridt.</p>
      <ul class="check-list">
        <li><span aria-hidden="true">✓</span> Én kilde til opgaver, timer og noter</li>
        <li><span aria-hidden="true">✓</span> Tydelig status fra opgave til godkendelse</li>
        <li><span aria-hidden="true">✓</span> Klar dokumentation tæt på det udførte arbejde</li>
      </ul>
    </div>
  </div>
</section>

<section class="section final-cta-section">
  <div class="container final-cta">
    <div>
      <p class="eyebrow">Klar til bedre overblik?</p>
      <h2>Giv arbejdet en stærkere rytme.</h2>
    </div>
    <div class="final-cta-actions">
      <a class="button" href="{{ '/demo/' | relative_url }}">Prøv demoen nu <span aria-hidden="true">→</span></a>
      <a class="text-link on-dark" href="https://app.mrsoftware.dk/app">Åbn Workslip <span aria-hidden="true">↗</span></a>
    </div>
  </div>
</section>
