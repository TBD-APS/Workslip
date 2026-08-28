---
title: Roadmap
description: Se hvad der er tilgængeligt, under udvikling og på vej i Workslip.
permalink: /roadmap/
roadmap: true
---

{% assign roadmap = site.data.public_roadmap %}

<section class="roadmap-hero" aria-labelledby="roadmap-title">
  <div class="roadmap-hero-grid" aria-hidden="true"></div>
  <div class="roadmap-hero-orbit roadmap-hero-orbit-one" aria-hidden="true"></div>
  <div class="roadmap-hero-orbit roadmap-hero-orbit-two" aria-hidden="true"></div>
  <div class="container roadmap-hero-content">
    <p class="roadmap-kicker"><span aria-hidden="true"></span> Produktretning fra Linear</p>
    <h1 id="roadmap-title">Det vi bygger<br><em>videre på.</em></h1>
    <p class="roadmap-lead">Her er den kundevendte retning for Workslip — fra det I kan bruge i dag til de områder, vi arbejder videre på sammen med virksomheder i branchen.</p>
    <div class="roadmap-hero-meta">
      <p><span class="roadmap-live-dot" aria-hidden="true"></span> {{ roadmap.source_label }}</p>
      <time datetime="{{ roadmap.updated }}">Senest opdateret {{ roadmap.updated_label }}</time>
    </div>
  </div>
</section>

<section class="roadmap-board-section" aria-labelledby="roadmap-board-title">
  <div class="container">
    <div class="roadmap-board-intro">
      <div>
        <p class="roadmap-overline">Produktroadmap</p>
        <h2 id="roadmap-board-title">Fra drift i dag til næste skridt.</h2>
      </div>
      <p>Roadmapet er kurateret fra det aktive produktarbejde i Linear. Vi viser kun det, vi kan stå offentligt inde for — ikke interne opgaver eller faste lanceringsdatoer.</p>
    </div>

    <ol class="roadmap-lanes" aria-label="Workslips produktroadmap">
      {% for lane in roadmap.lanes %}
      <li class="roadmap-lane roadmap-lane--{{ lane.key }}">
        <section aria-labelledby="roadmap-lane-{{ lane.key }}">
          <header class="roadmap-lane-header">
            <div class="roadmap-lane-title">
              <span class="roadmap-lane-index" aria-hidden="true">0{{ forloop.index }}</span>
              <div>
                <p class="roadmap-status">{{ lane.status }}</p>
                <h2 id="roadmap-lane-{{ lane.key }}">{{ lane.title }}</h2>
              </div>
            </div>
            <p>{{ lane.description }}</p>
          </header>

          <ul class="roadmap-items">
            {% for item in lane.items %}
            <li>
              <article class="roadmap-card">
                <div class="roadmap-card-topline">
                  <span class="roadmap-card-marker" aria-hidden="true"></span>
                  <p>{{ item.label }}</p>
                </div>
                <h3>{{ item.title }}</h3>
                <p>{{ item.description }}</p>
              </article>
            </li>
            {% endfor %}
          </ul>
        </section>
      </li>
      {% endfor %}
    </ol>
  </div>
</section>

<section class="roadmap-contribute-section" aria-labelledby="roadmap-contribute-title">
  <div class="container roadmap-contribute">
    <div>
      <p class="roadmap-overline">Vær med til at forme det næste</p>
      <h2 id="roadmap-contribute-title">Jeres arbejdsgang<br>prioriterer roadmapet.</h2>
      <p>Fortæl os, hvor I mister tid eller overblik i hverdagen. Det er den viden, der hjælper os med at vælge, hvad der skal bygges videre på.</p>
    </div>
    <a class="roadmap-contribute-cta" href="{{ '/demo/' | relative_url }}">Prøv den guidede demo <span aria-hidden="true">→</span></a>
  </div>
</section>
