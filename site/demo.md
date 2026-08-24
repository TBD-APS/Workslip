---
title: Prøv Workslip
description: Udforsk en interaktiv Workslip-demo med fiktive data – helt uden login.
permalink: /demo/
demo: true
---

<section class="demo-experience" data-demo-app>
  <div class="demo-hero container">
    <div>
      <p class="eyebrow"><span class="pulse-dot" aria-hidden="true"></span> Interaktiv demo</p>
      <h1>Se en arbejdsdag <span>falde på plads.</span></h1>
      <p class="demo-lead">Prøv Workslip med det samme. Demoen bruger kun fiktive data og er helt adskilt fra produktionssystemet.</p>
    </div>
    <div class="demo-assurance" aria-label="Demoens sikkerhed">
      <span class="demo-assurance-icon" aria-hidden="true">✓</span>
      <div><strong>Ingen login nødvendig</strong><small>Ingen produktionsdata. Intet bliver gemt.</small></div>
    </div>
  </div>

  <div class="container demo-stage-wrap">
    <div class="demo-stage-label"><span>LIVE DEMO</span><p>Prøv funktionerne i vinduet herunder</p><button class="demo-reset" type="button" data-demo-action="reset">Nulstil demo <span aria-hidden="true">↺</span></button></div>
    <div class="demo-stage">
      <aside class="demo-sidebar" aria-label="Demo-navigation">
        <a class="demo-logo" href="#demo-overview" data-demo-view="overview" aria-label="Workslip demo, overblik"><span class="brand-mark" aria-hidden="true">M</span><span>Workslip</span></a>
        <nav class="demo-nav" aria-label="Demoens sektioner">
          <button class="is-active" type="button" data-demo-view="overview" aria-pressed="true"><span aria-hidden="true">◫</span> Overblik</button>
          <button type="button" data-demo-view="tasks" aria-pressed="false"><span aria-hidden="true">□</span> Opgaver <b>3</b></button>
          <button type="button" data-demo-view="approvals" aria-pressed="false"><span aria-hidden="true">✓</span> Godkendelser <b data-demo-awaiting>2</b></button>
        </nav>
        <div class="demo-sidebar-bottom"><span class="demo-avatar">AV</span><div><strong>Anders V.</strong><small>Demo-administrator</small></div></div>
      </aside>

      <div class="demo-workspace">
        <header class="demo-workspace-head">
          <div><p data-demo-eyebrow>Overblik</p><h2 data-demo-title>Godmorgen, Anders</h2></div>
          <div class="demo-environment"><span></span> Fiktivt demomiljø</div>
        </header>

        <section class="demo-panel is-active" id="demo-overview" data-demo-panel="overview" aria-labelledby="demo-overview-heading">
          <h3 class="sr-only" id="demo-overview-heading">Overblik</h3>
          <div class="demo-metrics">
            <article><span>Aktive opgaver</span><strong>12</strong><small><i aria-hidden="true">↗</i> 3 i dag</small></article>
            <article><span>Timer i denne uge</span><strong>38,5</strong><small><i aria-hidden="true">↗</i> 12 % fra sidste uge</small></article>
            <article><span>Afventer godkendelse</span><strong data-demo-awaiting>2</strong><small class="attention"><i aria-hidden="true">•</i> Kræver handling</small></article>
          </div>
          <div class="demo-content-grid">
            <article class="demo-card demo-today-card">
              <div class="demo-card-head"><div><p>Dagens plan</p><h3>Holdet er i gang</h3></div><button type="button" data-demo-view="tasks">Se opgaver <span aria-hidden="true">→</span></button></div>
              <div class="demo-timeline">
                <div><time>08:30</time><i class="is-done" aria-hidden="true"></i><div><strong>Serviceeftersyn</strong><span>Hansen &amp; Søn · Frederiksberg</span></div><em>Afsluttet</em></div>
                <div><time>11:15</time><i class="is-now" aria-hidden="true"></i><div><strong>Montering</strong><span>Nordic Byg · Hvidovre</span></div><em>I gang</em></div>
                <div><time>14:00</time><i aria-hidden="true"></i><div><strong>Dokumentation</strong><span>Vestergaard ApS · Valby</span></div><em>Planlagt</em></div>
              </div>
            </article>
            <article class="demo-card demo-approval-card">
              <div class="demo-card-head"><div><p>Godkendelser</p><h3>Klar til dit blik</h3></div><span class="demo-count" data-demo-awaiting>2</span></div>
              <div class="demo-approval-preview"><span class="approval-check" aria-hidden="true">✓</span><div><strong>Arbejdsseddel #1842</strong><small>Hansen &amp; Søn · 4,5 timer</small></div></div>
              <button class="demo-primary-button" type="button" data-demo-view="approvals">Gennemgå nu <span aria-hidden="true">→</span></button>
            </article>
          </div>
        </section>

        <section class="demo-panel" id="demo-tasks" data-demo-panel="tasks" aria-labelledby="demo-tasks-heading" hidden>
          <div class="demo-section-heading"><div><p>Opgaver</p><h3 id="demo-tasks-heading">Dagens opgaver</h3></div><span>3 aktive</span></div>
          <div class="demo-task-list">
            <button type="button" class="demo-task-row is-selected" data-demo-task="Montering"><span class="task-badge cyan">M</span><span><strong>Montering</strong><small>Nordic Byg · Hvidovre</small></span><span class="task-status now">I gang</span><span>11:15 <b aria-hidden="true">→</b></span></button>
            <button type="button" class="demo-task-row" data-demo-task="Dokumentation"><span class="task-badge lilac">D</span><span><strong>Dokumentation</strong><small>Vestergaard ApS · Valby</small></span><span class="task-status">Planlagt</span><span>14:00 <b aria-hidden="true">→</b></span></button>
            <button type="button" class="demo-task-row" data-demo-task="Serviceeftersyn"><span class="task-badge green">S</span><span><strong>Serviceeftersyn</strong><small>Hansen &amp; Søn · Frederiksberg</small></span><span class="task-status done">Afsluttet</span><span>08:30 <b aria-hidden="true">→</b></span></button>
          </div>
          <article class="demo-time-card">
            <div><p data-demo-task-label>Montering · Nordic Byg</p><h3>Registrér tid på opgaven</h3><small>Fiktiv registrering – gemmes ikke.</small></div>
            <label>Timer <input data-demo-time type="number" min="0.5" max="12" step="0.5" value="1.5" inputmode="decimal" required></label>
            <button class="demo-primary-button" type="button" data-demo-action="save-time">Gem tid <span aria-hidden="true">→</span></button>
          </article>
        </section>

        <section class="demo-panel" id="demo-approvals" data-demo-panel="approvals" aria-labelledby="demo-approvals-heading" hidden>
          <div class="demo-section-heading"><div><p>Godkendelser</p><h3 id="demo-approvals-heading">Afventer dit svar</h3></div><span data-demo-awaiting>2 arbejdssedler</span></div>
          <article class="demo-slip-card" data-demo-slip>
            <div class="demo-slip-head"><div><span class="task-badge green">H</span><div><strong>Hansen &amp; Søn</strong><small>Arbejdsseddel #1842 · I går</small></div></div><span class="task-status now" data-demo-slip-status>Afventer</span></div>
            <div class="demo-slip-details"><div><span>Opgave</span><strong>Serviceeftersyn</strong></div><div><span>Registreret tid</span><strong>4,5 timer</strong></div><div><span>Medarbejder</span><strong>Mads Jensen</strong></div></div>
            <div class="demo-slip-note"><span aria-hidden="true">✦</span><p>Notat: Anlæg gennemgået. Filter skiftet og funktion testet.</p></div>
            <div class="demo-slip-actions"><button class="demo-secondary-button" type="button" data-demo-action="revision">Bed om rettelse</button><button class="demo-primary-button" type="button" data-demo-action="approve">Godkend arbejdsseddel <span aria-hidden="true">✓</span></button></div>
          </article>
        </section>
      </div>
    </div>
  </div>
  <div class="demo-toast" data-demo-toast role="status" aria-live="polite"></div>
</section>
