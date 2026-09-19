(() => {
  const root=document.querySelector('[data-flow-demo]'); if(!root)return;
  const flows={
    employee:[
      ['Forside','Dagens arbejde samlet','3 aktive sager','1 til gennemsyn','12 godkendte'],
      ['Opret kunde','Opret kunden på få felter','Fjordblik VVS ApS','Havnevej 24, 8000 Aarhus C','kunde@eksempel.test'],
      ['Sagsdetaljer','Alt det nødvendige på sagen','Udskiftning af blandingsbatteri','Fiktiv kunde · Aarhus','Planlagt i dag'],
      ['Anlægstyper','Vælg det relevante arbejde','Vand','Armatur','Service'],
      ['Kontrolpunkter','Dokumentér mens arbejdet udføres','✓ Tæthed kontrolleret','✓ Funktion testet','2 fiktive billeder'],
      ['Timer & afslutning','Færdiggør uden efterarbejde','2,5 timer registreret','Dokumentation komplet','Send til gennemsyn']
    ],
    admin:[
      ['Forside','Få overblik før du klikker videre','18 aktive sager','4 til gennemsyn','31 godkendte'],
      ['Arbejdsseddel','Åbn sagen med ét klik','Fjordblik VVS ApS','Udskiftning af blandingsbatteri','Medarbejder: Mikkel Demo'],
      ['KLS / kontrol','Se dokumentationen samlet','✓ 6/6 kontrolpunkter','✓ 2 billeder','Ingen mangler'],
      ['Timer','Kontrollér tidsregistreringen','2,5 timer','08:00 – 10:30','Klar til kontrol'],
      ['Kommentar / rettelse','Send en præcis rettelse tilbage','Kommentar til medarbejder','Ret dokumentation','Send tilbage'],
      ['Godkendelse','Godkend med få klik','KLS komplet','Timer kontrolleret','Godkend arbejdsseddel']
    ]
  };
  let role='employee',index=0,startX=null;
  const title=root.querySelector('[data-flow-title]'),desc=root.querySelector('[data-flow-description]'),badge=root.querySelector('[data-flow-badge]'),screen=root.querySelector('[data-flow-screen]'),steps=root.querySelector('[data-flow-steps]'),idx=root.querySelector('[data-flow-index]'),total=root.querySelector('[data-flow-total]');
  const descriptions={employee:'Medarbejderen får ét tydeligt næste skridt ad gangen på mobilen. Det giver mindre papirarbejde og færre oplysninger, der skal genskabes senere.',admin:'Administrationen starter med overblikket og går direkte til det, der kræver handling. Kontrol, rettelser og godkendelse samles i samme flow.'};
  function render(){
    const flow=flows[role],s=flow[index]; idx.textContent=index+1; total.textContent=flow.length; badge.textContent=role==='employee'?'MEDARBEJDERFLOW':'ADMINISTRATORFLOW'; title.textContent=role==='employee'?'Udfyld arbejdet, mens du står på opgaven.':'Godkend arbejdet uden at lede efter dokumentationen.'; desc.textContent=descriptions[role];
    screen.innerHTML='<p class="flow-screen-kicker">'+s[0]+'</p><h4>'+s[1]+'</h4><div class="flow-screen-cards">'+s.slice(2).map((x,i)=>'<div class="flow-screen-card '+(i===s.length-3?'is-accent':'')+'"><span>'+x+'</span></div>').join('')+'</div><button type="button" class="flow-screen-action">'+(index===flow.length-1?(role==='admin'?'Godkend':'Send til gennemsyn'):'Næste')+' <span>→</span></button>';
    steps.innerHTML=flow.map((x,i)=>'<button type="button" data-step="'+i+'" class="'+(i===index?'is-active':'')+'"><span>'+String(i+1).padStart(2,'0')+'</span>'+x[0]+'</button>').join('');
    root.querySelectorAll('[data-flow-role]').forEach(b=>{const on=b.dataset.flowRole===role;b.classList.toggle('is-active',on);b.setAttribute('aria-selected',String(on));});
  }
  root.addEventListener('click',e=>{const r=e.target.closest('[data-flow-role]');if(r){role=r.dataset.flowRole;index=0;render();return;} const s=e.target.closest('[data-step]');if(s){index=Number(s.dataset.step);render();return;} if(e.target.closest('[data-flow-next],.flow-screen-action')){index=(index+1)%flows[role].length;render();} if(e.target.closest('[data-flow-prev]')){index=(index-1+flows[role].length)%flows[role].length;render();}});
  const phone=root.querySelector('[data-flow-phone]'); phone.addEventListener('touchstart',e=>startX=e.changedTouches[0].clientX,{passive:true}); phone.addEventListener('touchend',e=>{if(startX===null)return;const d=e.changedTouches[0].clientX-startX;if(Math.abs(d)>40){index=(index+(d<0?1:-1)+flows[role].length)%flows[role].length;render();}startX=null;},{passive:true});
  phone.addEventListener('keydown',e=>{if(e.key==='ArrowRight'||e.key==='ArrowLeft'){index=(index+(e.key==='ArrowRight'?1:-1)+flows[role].length)%flows[role].length;render();}});
  render();
})();