import { useEffect, useRef, useState } from 'react';

type Enemy = {
  id: number;
  x: number;
  y: number;
  hp: number;
  max: number;
  vx: number;
  vy: number;
  hit: number;
  kind: 'hound' | 'wretch' | 'brute';
};

type Particle = { x: number; y: number; vx: number; vy: number; life: number; size: number };

type Game = {
  px: number;
  py: number;
  faceX: number;
  faceY: number;
  hp: number;
  stam: number;
  attackCd: number;
  attackFlash: number;
  dodge: number;
  hurtCd: number;
  kills: number;
  embers: number;
  dead: boolean;
  won: boolean;
  enemies: Enemy[];
  particles: Particle[];
  keys: Set<string>;
};

const WORLD = 1600;

const spawn = (): Enemy[] => [
  { id: 1, x: 890, y: 790, hp: 45, max: 45, vx: 0, vy: 0, hit: 0, kind: 'hound' },
  { id: 2, x: 620, y: 540, hp: 58, max: 58, vx: 0, vy: 0, hit: 0, kind: 'wretch' },
  { id: 3, x: 1110, y: 560, hp: 86, max: 86, vx: 0, vy: 0, hit: 0, kind: 'brute' },
  { id: 4, x: 1180, y: 1030, hp: 58, max: 58, vx: 0, vy: 0, hit: 0, kind: 'wretch' },
  { id: 5, x: 500, y: 1060, hp: 45, max: 45, vx: 0, vy: 0, hit: 0, kind: 'hound' }
];

const initial = (): Game => ({
  px: 800,
  py: 820,
  faceX: 1,
  faceY: 0,
  hp: 100,
  stam: 100,
  attackCd: 0,
  attackFlash: 0,
  dodge: 0,
  hurtCd: 0,
  kills: 0,
  embers: 0,
  dead: false,
  won: false,
  enemies: spawn(),
  particles: [],
  keys: new Set()
});

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

export default function App() {
  const canvas = useRef<HTMLCanvasElement>(null);
  const game = useRef<Game>(initial());
  const raf = useRef(0);
  const last = useRef(performance.now());
  const stick = useRef({ x: 0, y: 0 });

  const [ui, setUi] = useState({
    hp: 100,
    stam: 100,
    kills: 0,
    embers: 0,
    dead: false,
    won: false,
    message: 'Hunt the corrupted. Survive the grove.'
  });

  const sync = (message?: string) => {
    const g = game.current;
    setUi((value) => ({
      hp: Math.ceil(g.hp),
      stam: Math.ceil(g.stam),
      kills: g.kills,
      embers: g.embers,
      dead: g.dead,
      won: g.won,
      message: message ?? value.message
    }));
  };

  const burst = (x: number, y: number, n = 9) => {
    const g = game.current;
    for (let i = 0; i < n; i += 1) {
      const angle = Math.random() * Math.PI * 2;
      const speed = 40 + Math.random() * 95;
      g.particles.push({
        x,
        y,
        vx: Math.cos(angle) * speed,
        vy: Math.sin(angle) * speed,
        life: 0.45 + Math.random() * 0.25,
        size: 2 + Math.random() * 3
      });
    }
  };

  const attack = () => {
    const g = game.current;
    if (g.dead || g.won || g.attackCd > 0 || g.stam < 18) return;

    g.stam -= 18;
    g.attackCd = 0.42;
    g.attackFlash = 0.16;
    let hit = false;

    for (const enemy of g.enemies) {
      if (enemy.hp <= 0) continue;
      const dx = enemy.x - g.px;
      const dy = enemy.y - g.py;
      const distance = Math.hypot(dx, dy);
      const dot = distance ? (dx / distance) * g.faceX + (dy / distance) * g.faceY : 1;
      if (distance < 105 && dot > 0.1) {
        enemy.hp -= 34;
        enemy.hit = 0.16;
        hit = true;
        burst(enemy.x, enemy.y, 12);
        if (enemy.hp <= 0) {
          g.kills += 1;
          g.embers += 12 + (enemy.kind === 'brute' ? 12 : 0);
          sync('Enemy slain. The grove grows quieter.');
        }
      }
    }

    if (!hit) sync('Your blade cuts empty air.');
    else sync();
  };

  const dodge = () => {
    const g = game.current;
    if (g.dead || g.won || g.dodge > 0 || g.stam < 28) return;
    g.stam -= 28;
    g.dodge = 0.42;
    g.hurtCd = 0.5;
    sync('Dodge');
  };

  const restart = () => {
    game.current = initial();
    stick.current = { x: 0, y: 0 };
    sync('Back to the hunt.');
  };

  const setStick = (x: number, y: number) => {
    stick.current = { x, y };
  };

  useEffect(() => {
    const canvasNode = canvas.current;
    if (!canvasNode) return;
    const ctx = canvasNode.getContext('2d');
    if (!ctx) return;

    const resize = () => {
      const density = Math.min(devicePixelRatio || 1, 2);
      canvasNode.width = Math.floor(innerWidth * density);
      canvasNode.height = Math.floor(innerHeight * density);
      canvasNode.style.width = innerWidth + 'px';
      canvasNode.style.height = innerHeight + 'px';
      ctx.setTransform(density, 0, 0, density, 0, 0);
    };

    const keyDown = (event: KeyboardEvent) => {
      game.current.keys.add(event.key.toLowerCase());
      if (event.code === 'Space') {
        event.preventDefault();
        attack();
      }
      if (event.key === 'Shift') dodge();
    };

    const keyUp = (event: KeyboardEvent) => game.current.keys.delete(event.key.toLowerCase());

    resize();
    addEventListener('resize', resize);
    addEventListener('keydown', keyDown);
    addEventListener('keyup', keyUp);

    const update = (g: Game, dt: number) => {
      if (g.dead || g.won) return;

      g.attackCd = Math.max(0, g.attackCd - dt);
      g.attackFlash = Math.max(0, g.attackFlash - dt);
      g.dodge = Math.max(0, g.dodge - dt);
      g.hurtCd = Math.max(0, g.hurtCd - dt);
      g.stam = Math.min(100, g.stam + 22 * dt);

      let mx = stick.current.x;
      let my = stick.current.y;
      const keys = g.keys;
      if (keys.has('w') || keys.has('arrowup')) my -= 1;
      if (keys.has('s') || keys.has('arrowdown')) my += 1;
      if (keys.has('a') || keys.has('arrowleft')) mx -= 1;
      if (keys.has('d') || keys.has('arrowright')) mx += 1;

      const moveLength = Math.hypot(mx, my);
      if (moveLength) {
        mx /= moveLength;
        my /= moveLength;
        g.faceX = mx;
        g.faceY = my;
        const speed = g.dodge > 0 ? 470 : 205;
        g.px = clamp(g.px + mx * speed * dt, 80, WORLD - 80);
        g.py = clamp(g.py + my * speed * dt, 80, WORLD - 80);
      }

      for (const enemy of g.enemies) {
        if (enemy.hp <= 0) continue;
        enemy.hit = Math.max(0, enemy.hit - dt);
        const dx = g.px - enemy.x;
        const dy = g.py - enemy.y;
        const distance = Math.hypot(dx, dy);

        if (distance < 430 && distance > 52) {
          const speed = enemy.kind === 'hound' ? 118 : enemy.kind === 'brute' ? 62 : 82;
          enemy.vx = (dx / distance) * speed;
          enemy.vy = (dy / distance) * speed;
          enemy.x += enemy.vx * dt;
          enemy.y += enemy.vy * dt;
        }

        if (distance < 58 && g.hurtCd <= 0) {
          g.hp -= enemy.kind === 'brute' ? 22 : 13;
          g.hurtCd = 0.7;
          burst(g.px, g.py, 8);
          sync('Hit! Dodge through attacks.');
          if (g.hp <= 0) {
            g.hp = 0;
            g.dead = true;
            sync('YOU DIED');
          }
        }
      }

      for (const particle of g.particles) {
        particle.x += particle.vx * dt;
        particle.y += particle.vy * dt;
        particle.vx *= 0.94;
        particle.vy *= 0.94;
        particle.life -= dt;
      }
      g.particles = g.particles.filter((particle) => particle.life > 0);

      if (g.kills === g.enemies.length && !g.won) {
        g.won = true;
        sync('GROVE CLEARED');
      }
    };

    const draw = (context: CanvasRenderingContext2D, g: Game, width: number, height: number) => {
      context.clearRect(0, 0, width, height);
      context.fillStyle = '#070908';
      context.fillRect(0, 0, width, height);

      const camX = g.px - width / 2;
      const camY = g.py - height / 2;
      context.save();
      context.translate(-camX, -camY);

      const grad = context.createRadialGradient(g.px, g.py, 70, g.px, g.py, 700);
      grad.addColorStop(0, '#263126');
      grad.addColorStop(0.45, '#182019');
      grad.addColorStop(1, '#0b0e0c');
      context.fillStyle = grad;
      context.fillRect(camX - 100, camY - 100, width + 200, height + 200);

      context.strokeStyle = '#283128';
      context.lineWidth = 1;
      for (let x = 0; x < WORLD; x += 80) {
        for (let y = 0; y < WORLD; y += 80) {
          const noise = ((x * 17 + y * 31) % 97) / 97;
          if (noise > 0.45) {
            context.beginPath();
            context.moveTo(x + 12, y + 8);
            context.lineTo(x + 18, y - 4);
            context.moveTo(x + 18, y + 2);
            context.lineTo(x + 26, y - 7);
            context.stroke();
          }
        }
      }

      context.fillStyle = '#111510';
      context.strokeStyle = '#3a3f35';
      context.lineWidth = 5;
      for (const [rx, ry, rw, rh] of [
        [280, 250, 190, 70],
        [1120, 270, 210, 90],
        [250, 1240, 250, 80],
        [1110, 1280, 230, 70]
      ]) {
        context.fillRect(rx, ry, rw, rh);
        context.strokeRect(rx, ry, rw, rh);
      }

      for (const enemy of g.enemies) {
        if (enemy.hp <= 0) {
          context.globalAlpha = 0.22;
          context.fillStyle = '#090a09';
          context.beginPath();
          context.ellipse(enemy.x, enemy.y, 30, 12, 0, 0, Math.PI * 2);
          context.fill();
          context.globalAlpha = 1;
          continue;
        }

        context.save();
        context.translate(enemy.x, enemy.y);
        const angle = Math.atan2(g.py - enemy.y, g.px - enemy.x);
        context.rotate(angle);
        context.shadowBlur = enemy.hit ? 22 : 10;
        context.shadowColor = enemy.hit ? '#d9c2a1' : '#000';
        context.fillStyle = enemy.hit ? '#b9a58a' : enemy.kind === 'brute' ? '#473f37' : '#272b25';
        context.beginPath();

        if (enemy.kind === 'hound') {
          context.ellipse(0, 0, 30, 18, 0, 0, Math.PI * 2);
          context.fill();
          context.beginPath();
          context.moveTo(23, -12);
          context.lineTo(44, 0);
          context.lineTo(23, 12);
          context.fill();
        } else {
          context.arc(0, 0, enemy.kind === 'brute' ? 31 : 23, 0, Math.PI * 2);
          context.fill();
          context.strokeStyle = '#777061';
          context.lineWidth = 4;
          context.beginPath();
          context.moveTo(10, -18);
          context.lineTo(25, -36);
          context.moveTo(10, 18);
          context.lineTo(25, 36);
          context.stroke();
        }
        context.restore();

        context.fillStyle = '#140f0e';
        context.fillRect(enemy.x - 28, enemy.y - 43, 56, 4);
        context.fillStyle = '#8e3832';
        context.fillRect(enemy.x - 28, enemy.y - 43, 56 * (enemy.hp / enemy.max), 4);
      }

      for (const particle of g.particles) {
        context.globalAlpha = Math.max(0, particle.life * 1.6);
        context.fillStyle = '#c76a52';
        context.beginPath();
        context.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
        context.fill();
      }
      context.globalAlpha = 1;

      context.save();
      context.translate(g.px, g.py);
      context.rotate(Math.atan2(g.faceY, g.faceX));
      context.shadowBlur = 20;
      context.shadowColor = '#000';
      context.fillStyle = g.dodge > 0 ? '#d9d6c9' : '#aaa99f';
      context.beginPath();
      context.arc(0, 0, 18, 0, Math.PI * 2);
      context.fill();
      context.fillStyle = '#252a25';
      context.beginPath();
      context.moveTo(-22, -18);
      context.lineTo(-39, 0);
      context.lineTo(-22, 18);
      context.closePath();
      context.fill();
      context.strokeStyle = g.attackFlash > 0 ? '#f0d5a2' : '#b9b29f';
      context.lineWidth = 5;
      context.beginPath();
      context.moveTo(14, 0);
      context.lineTo(47, 0);
      context.stroke();
      if (g.attackFlash > 0) {
        context.strokeStyle = '#d8b47b88';
        context.lineWidth = 14;
        context.beginPath();
        context.arc(0, 0, 72, -1.1, 1.1);
        context.stroke();
      }
      context.restore();
      context.restore();

      const vignette = context.createRadialGradient(
        width / 2,
        height / 2,
        Math.min(width, height) * 0.25,
        width / 2,
        height / 2,
        Math.max(width, height) * 0.72
      );
      vignette.addColorStop(0, '#0000');
      vignette.addColorStop(1, '#000c');
      context.fillStyle = vignette;
      context.fillRect(0, 0, width, height);
    };

    const loop = (now: number) => {
      const dt = Math.min((now - last.current) / 1000, 0.034);
      last.current = now;
      const g = game.current;
      update(g, dt);
      draw(ctx, g, innerWidth, innerHeight);
      if (Math.floor(now / 100) !== Math.floor((now - dt * 1000) / 100)) sync();
      raf.current = requestAnimationFrame(loop);
    };

    raf.current = requestAnimationFrame(loop);

    return () => {
      cancelAnimationFrame(raf.current);
      removeEventListener('resize', resize);
      removeEventListener('keydown', keyDown);
      removeEventListener('keyup', keyUp);
    };
  }, []);

  const stopStick = () => setStick(0, 0);

  return (
    <div className="game">
      <canvas ref={canvas} />
      <div className="topHud">
        <div className="title">ASHFALL <span>THE HOLLOW GROVE</span></div>
        <div className="stats">
          <div className="bar hp"><i style={{ width: ui.hp + '%' }} /><b>{ui.hp}</b></div>
          <div className="bar stam"><i style={{ width: ui.stam + '%' }} /><b>{ui.stam}</b></div>
        </div>
        <div className="objective">{ui.kills}/5 SLAIN <em>{ui.embers} EMBERS</em></div>
      </div>

      <div className="message">{ui.message}</div>

      <div className="mobileControls">
        <div className="pad" onPointerLeave={stopStick} onPointerCancel={stopStick}>
          <button aria-label="move up" onPointerDown={() => setStick(0, -1)} onPointerUp={stopStick}>▲</button>
          <button aria-label="move left" onPointerDown={() => setStick(-1, 0)} onPointerUp={stopStick}>◀</button>
          <button aria-label="move right" onPointerDown={() => setStick(1, 0)} onPointerUp={stopStick}>▶</button>
          <button aria-label="move down" onPointerDown={() => setStick(0, 1)} onPointerUp={stopStick}>▼</button>
        </div>
        <div className="combat">
          <button className="dodge" onPointerDown={dodge}>DODGE</button>
          <button className="attack" onPointerDown={attack}>ATTACK</button>
        </div>
      </div>

      {(ui.dead || ui.won) && (
        <div className="overlay">
          <small>{ui.won ? 'AREA PURGED' : 'THE HUNT ENDS'}</small>
          <h1>{ui.won ? 'GROVE CLEARED' : 'YOU DIED'}</h1>
          <p>{ui.won ? ui.embers + ' embers recovered' : 'The grove takes what you carried.'}</p>
          <button onClick={restart}>{ui.won ? 'DESCEND AGAIN' : 'RISE AGAIN'}</button>
        </div>
      )}

      <div className="desktopHint">WASD · SPACE ATTACK · SHIFT DODGE</div>
    </div>
  );
}
