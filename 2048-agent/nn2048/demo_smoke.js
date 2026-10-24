// Smoke-test the generated engine demo page without a browser.
// Usage: node demo_smoke.js <demo.html>
const fs = require('fs');
const vm = require('vm');

const html = fs.readFileSync(process.argv[2], 'utf8');
const m = html.match(/<script>([\s\S]*)<\/script>/);
if (!m) { console.error('no <script> block'); process.exit(1); }

function ctx2d() {
  const noop = () => {};
  return { clearRect: noop, beginPath: noop, moveTo: noop, lineTo: noop, stroke: noop,
           ellipse: noop, rect: noop, fillRect: noop, arc: noop, closePath: noop,
           set strokeStyle(v) {}, set lineWidth(v) {}, set fillStyle(v) {} };
}

const els = new Map();
function mkEl(id) {
  const el = {
    id, style: {}, className: '', textContent: '', value: 0, max: 0,
    width: 1200, height: 240, children: [], dataset: {}, onclick: null, oninput: null,
    appendChild(c) { this.children.push(c); },
    addEventListener() {},
    classList: { toggle() {}, add() {}, remove() {}, contains() { return false; } },
    getContext() { return ctx2d(); },
  };
  return el;
}

const document = {
  getElementById(id) { if (!els.has(id)) els.set(id, mkEl(id)); return els.get(id); },
  createElement(tag) { return mkEl(tag); },
  querySelectorAll() { return []; },
  addEventListener() {},
};
const sandbox = { document, window: {}, console, Math, JSON, setInterval, clearInterval };
sandbox.globalThis = sandbox;
vm.createContext(sandbox);
vm.runInContext(m[1] + "\n;globalThis.__steps = steps; globalThis.__meta = meta;", sandbox,
                { filename: 'demo.js' });

const errors = [];
function check(name, cond) { if (!cond) errors.push(name); }

const steps = sandbox.__steps;
check('steps present', Array.isArray(steps) && steps.length > 0);
check('show() exported', typeof sandbox.show === 'function');

function probe(idx) {
  sandbox.show(idx);
  const board = els.get('board');
  const tiles = board.children;
  check(`tile count at ${idx}`, tiles.length === 16);
  let maxLevel = 0;
  for (let i = 0; i < 16; i++) {
    const t = tiles[i];
    const txt = String(t.textContent);
    if (txt !== '') {
      const v = parseInt(txt.replace('K', '')) * (txt.includes('K') ? 1024 : 1);
      maxLevel = Math.max(maxLevel, v);
    }
  }
  const shownMax = parseInt(els.get('maxTile').textContent);
  check(`max tile at ${idx} (${shownMax} vs ${maxLevel})`, shownMax === maxLevel);
  check(`score text at ${idx}`, els.get('score').textContent === steps[idx].s.toLocaleString());
  check(`moves text at ${idx}`, els.get('moves').textContent == idx + 1);
}

probe(0);
probe(Math.floor(steps.length / 3));
probe(Math.floor(steps.length / 2));
probe(steps.length - 1);

if (errors.length) {
  console.error('FAILED:', errors.join(' | '));
  process.exit(1);
}
console.log(`OK: ${steps.length} steps, final ${steps[steps.length - 1].s}, ` +
            `max tile ${els.get('maxTile').textContent}`);
