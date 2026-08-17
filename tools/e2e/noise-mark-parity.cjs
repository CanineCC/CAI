// FLUEKNEPPER (the CAI-measured mark) — /noise/mark/{period} against the approved mock, in BOTH colour
// schemes, with contrast computed.
//
//   NODE_PATH=/home/jimmy/kennel-localtest/node_modules \
//     MOCK=file:///tmp/mock-noise-mark.html IMPL=http://localhost:8199/noise/mark/2026-09 \
//     node tools/e2e/noise-record-parity.cjs
//
// ★★ CAI THEMES ON prefers-color-scheme AND HAS NO TOGGLE. There is no data-theme attribute to set, so the
//    scheme is driven through the browser CONTEXT — setting an attribute here would have "passed" both runs
//    against the light palette and told us nothing about the dark one, which is the majority of readers.
const { chromium } = require('playwright');
const MOCK = process.env.MOCK || 'file:///tmp/mock-noise-mark.html';
const IMPL = process.env.IMPL || 'http://localhost:8199/noise/mark/2026-09';

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
    // ★★ THE TWO MARK STATES, probed BY STATE — granted and withheld must be distinguishable at a glance and
    //    legible in both schemes, because a withheld mark is what a competitor reads about itself in public.
    pillGranted:  g('[data-region="marks"] .pill.upheld', TXT.concat(BOX)),
    pillWithheld: g('[data-region="marks"] .pill.open', TXT.concat(BOX)),

    tableHead:  g('[data-region="marks"] table.lenses thead th', TXT),
    whyCell:    g('[data-region="marks"] tbody td.muted', TXT),
    condName:   g('[data-region="conditions"] tbody td.mono', TXT),
    condReads:  g('[data-region="conditions"] tbody td.muted', TXT),
    callout:    g('[data-region="conditions"] .callout', BOX),

    // ── the claims, as rendered text ────────────────────────────────────────────────────────────
    // ★★ Upheld AND overturned both visible: a contest that only appeared when the challenger won would be a
    //    complaints box, and the upheld ones are the evidence that it is not.
    showsGranted:   /CAI-measured/.test(body),
    showsWithheld:  /Withheld/.test(body),
    // ★★ A withheld mark must NAME the condition — "did not qualify" is a verdict nobody can contest.
    namesTheCondition: /the-run-reproduces|ran-against-the-published-holdout|numbers-published-in-full/.test(body),
    listsAllFourConditions:
        (body.match(/ran-against-the-published-holdout|submitted-before-the-deadline|the-run-reproduces|numbers-published-in-full/g) || []).length >= 4,
    saysItIsFree:   /free/i.test(body),
    saysNotAQualityBadge: /not a quality badge/i.test(body),
    // ★★ The word that leaks into everyone's marketing if it appears once.
    neverSaysCertified: !/certif/i.test(body),

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
    await p.screenshot({ path: `/tmp/noise-mark-${scheme}-${tag}.png`, fullPage: true });
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
    // ★★ A run where MOST probes were absent compared almost nothing and reported zero. That is exactly what a
    //    script pointed at the wrong page looks like, and it happened — so it is now a failure, not a footnote.
    if (absent.length > 3) {
      console.log(`  ★★ ${absent.length} probes matched on NEITHER side — is IMPL the right page?`);
      total += absent.length;
    }
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
