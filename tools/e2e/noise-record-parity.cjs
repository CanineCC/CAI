// FLUEKNEPPER (the noise record page) — /noise/record/{period} against the approved mock, in BOTH colour
// schemes, with contrast computed.
//
//   NODE_PATH=/home/jimmy/kennel-localtest/node_modules \
//     MOCK=file:///tmp/mock-noise-record.html IMPL=http://localhost:8199/noise/record/2026-09 \
//     node tools/e2e/noise-record-parity.cjs
//
// ★★ CAI THEMES ON prefers-color-scheme AND HAS NO TOGGLE. There is no data-theme attribute to set, so the
//    scheme is driven through the browser CONTEXT — setting an attribute here would have "passed" both runs
//    against the light palette and told us nothing about the dark one, which is the majority of readers.
const { chromium } = require('playwright');
const MOCK = process.env.MOCK || 'file:///tmp/mock-noise-record.html';
const IMPL = process.env.IMPL || 'http://localhost:8199/noise/record/2026-09';

const PROBE = `(() => {
  const TXT = ['font-size','font-weight','color','text-transform','letter-spacing'];
  const BOX = ['background-color','border-radius','padding-top'];
  const g=(sel,props)=>{const el=document.querySelector(sel); if(!el) return {missing:sel};
    const cs=getComputedStyle(el), o={}; for(const p of props) o[p]=cs.getPropertyValue(p); return o;};

  const lum = (c) => {
    const m = c.match(/[\\d.]+/g).map(Number);
    const f = (v) => { v/=255; return v<=0.03928 ? v/12.92 : Math.pow((v+0.055)/1.055, 2.4); };
    return 0.2126*f(m[0]) + 0.7152*f(m[1]) + 0.0722*f(m[2]);
  };
  // ★★ ALPHA IS COMPOSITED, not ignored. A first version returned the first background that was not fully
  //    transparent and treated it as opaque — so a pill whose background is color-mix(--ok 16%, transparent) was
  //    measured as if it were solid dark green. That reported 4.17:1 for text sitting on a PALE wash, and the
  //    "fix" was a token change this page never needed. A probe that measures the wrong thing is worse than no
  //    probe: it produces work.
  const rgba = (c) => {
    const m = (c || '').match(/[\\d.]+/g);
    return m ? { r: +m[0], g: +m[1], b: +m[2], a: m.length > 3 ? +m[3] : 1 } : null;
  };
  const over = (top, bottom) => ({
    r: top.r * top.a + bottom.r * (1 - top.a),
    g: top.g * top.a + bottom.g * (1 - top.a),
    b: top.b * top.a + bottom.b * (1 - top.a),
    a: 1,
  });
  const behindColor = (el) => {
    const layers = [];
    for (let n = el; n; n = n.parentElement) {
      const c = rgba(getComputedStyle(n).backgroundColor);
      if (!c || c.a === 0) continue;
      layers.push(c);
      if (c.a === 1) break;
    }
    const root = rgba(getComputedStyle(document.documentElement).backgroundColor);
    let acc = layers.length && layers[layers.length - 1].a === 1
      ? layers.pop()
      : (root && root.a === 1 ? root : { r: 255, g: 255, b: 255, a: 1 });
    for (let i = layers.length - 1; i >= 0; i--) { acc = over(layers[i], acc); }
    return acc;
  };
  const behind = (el) => {
    const c = behindColor(el);
    return 'rgb(' + c.r + ', ' + c.g + ', ' + c.b + ')';
  };
  const low = [];
  for (const el of document.querySelectorAll('[data-region] *')) {
    const t = (el.textContent||'').trim();
    if (!t || el.children.length) continue;
    const cs = getComputedStyle(el);
    const px = parseFloat(cs.fontSize), bold = parseInt(cs.fontWeight,10) >= 700;
    const need = (px >= 24 || (px >= 18.66 && bold)) ? 3.0 : 4.5;
    // ★ The TEXT colour can carry alpha too, over the composited background behind it.
    const fg = rgba(cs.color);
    const bg = behindColor(el);
    const text = fg.a < 1 ? over(fg, bg) : fg;
    const a = lum('rgb(' + text.r + ', ' + text.g + ', ' + text.b + ')');
    const b = lum('rgb(' + bg.r + ', ' + bg.g + ', ' + bg.b + ')');
    const r = (Math.max(a,b)+0.05)/(Math.min(a,b)+0.05);
    if (r < need) low.push({ text: t.slice(0,44), ratio: +r.toFixed(2), need, px });
  }

  const body = document.body.innerText;
  return {
    countKey:   g('[data-region="record-counts"] .count .k', TXT),
    countVal:   g('[data-region="record-counts"] .count .v', TXT),
    countRow:   g('[data-region="record-counts"] .counts', ['display','flex-wrap','gap']),

    // ★★ THE THREE DISPUTE STATES, probed BY STATE. An open dispute, an upheld one and an overturned one must be
    //    distinguishable at a glance and legible in both schemes — the pills are the only thing carrying that.
    pillOpen:       g('[data-region="disputes"] .pill.open', TXT.concat(BOX)),
    pillUpheld:     g('[data-region="disputes"] .pill.upheld', TXT.concat(BOX)),
    pillOverturned: g('[data-region="disputes"] .pill.overturned', TXT.concat(BOX)),

    tableHead:  g('[data-region="disputes"] table.lenses thead th', TXT),
    reasonCell: g('[data-region="disputes"] tbody td .muted', TXT),
    callout:    g('[data-region="disputes"] .callout', BOX),

    // ── the claims, as rendered text ────────────────────────────────────────────────────────────
    // ★★ Upheld AND overturned both visible: a contest that only appeared when the challenger won would be a
    //    complaints box, and the upheld ones are the evidence that it is not.
    showsUpheld:      /Upheld/.test(body),
    showsOverturned:  /Overturned/.test(body),
    showsOpen:        /Open/.test(body),
    saysNotYetAnswered: /Not yet answered/.test(body),
    saysAppendOnly:   /append-only/.test(body),
    saysReasoningBothWays: /either way/i.test(body),
    saysNotOurArgument: /not an argument the Code Assurance Index\\s+gets to make/.test(
        body.replace(/\\s+/g, ' ')) || /not an argument the Code Assurance Index gets to make/.test(body),

    lowContrast: low,
  };
})()`;

(async () => {
  const b = await chromium.launch();
  const grab = async (url, scheme, tag) => {
    // ★ The SCHEME comes from the context, because CAI has no toggle to click.
    const context = await b.newContext({ colorScheme: scheme, viewport: { width: 1280, height: 1200 } });
    const p = await context.newPage();
    await p.goto(url, { waitUntil: 'domcontentloaded' });
    await p.waitForTimeout(300);
    const r = await p.evaluate(PROBE);
    await p.screenshot({ path: `/tmp/noise-record-${scheme}-${tag}.png`, fullPage: true });
    await context.close();
    return r;
  };

  let total = 0;
  for (const scheme of ['dark', 'light']) {
    const mock = await grab(MOCK, scheme, 'mock'), impl = await grab(IMPL, scheme, 'impl');
    const diffs = [], absent = [];
    const walk = (a, bb, path = '') => {
      for (const k of new Set([...Object.keys(a || {}), ...Object.keys(bb || {})])) {
        const pa = path ? `${path}.${k}` : k, va = a?.[k], vb = bb?.[k];
        if (k === 'lowContrast') continue;
        if (va?.missing || vb?.missing) { absent.push(pa); continue; }
        if (typeof va === 'object' && va !== null && !Array.isArray(va)) { walk(va, vb, pa); continue; }
        if (JSON.stringify(va) !== JSON.stringify(vb)) {
          diffs.push({ prop: pa, mock: JSON.stringify(va), impl: JSON.stringify(vb) });
        }
      }
    };
    walk(mock, impl);

    console.log(`\n── ${scheme.toUpperCase()} ──`);
    for (const d of diffs) console.log(`  ${d.prop}\n      mock: ${d.mock}\n      impl: ${d.impl}`);
    console.log(`  divergences: ${diffs.length}`);
    if (absent.length) console.log(`  not present on one side: ${absent.join(', ')}`);
    if (impl.lowContrast.length) {
      console.log(`  ★ LOW CONTRAST (impl):`);
      for (const l of impl.lowContrast) console.log(`      ${l.ratio}:1 (needs ${l.need}) @${l.px}px — "${l.text}"`);
    } else {
      console.log(`  contrast: every text node in the regions passes WCAG AA`);
    }
    total += diffs.length + impl.lowContrast.length;
  }
  await b.close();
  console.log(`\nTOTAL DIVERGENCES (both schemes): ${total}`);
})();
