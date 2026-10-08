// Optical spacing for the promo pictures (the idea of InDesign's "Optical" kerning). Each glyph's outline is
// measured on a canvas, and every pair in a marked element is moved until the white between the two shapes looks
// the same, instead of trusting the font's metrics. Word spaces are set from the same measure, and each line
// starts on its optical edge.
//
// The page lists what to space before this script, as window.OPTICAL = [{ sel, track, space, strength, depth }]:
//   track     em added to the median pair (0 keeps the overall density)
//   space     em of white for a word space (absent: the font's own space)
//   strength  0..1, how far each pair moves toward the median (default 1; small text wants less)
//   depth     em: how deep into an open side the white still counts (default 0.1)
//   spaceDepth the same for the two sides of a word space (default 0.03: a word gap is read edge to edge)
//   kern      { 'ab': em } added to those pairs after the measure: a hand correction where the shape model is
//             wrong (a Hangul syllable's round ㅇ side pulled closer than the gap inside the syllable)
// When it is done, <html data-optical="done">, and window.opticalReport holds each element's measures.
(async () => {
  const M = 240; // glyphs are measured at 240 px and kept in em
  const UP = Math.round(M * 1.15), H = UP + Math.round(M * 0.45);
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  const shapes = new Map();

  // A glyph's advance and, per pixel row, how far its ink sits from the left and right edges of its advance.
  function shape(ch, font) {
    const key = font + '\u0000' + ch;
    if (shapes.has(key)) return shapes.get(key);
    ctx.font = font;
    const adv = ctx.measureText(ch).width;
    const pad = Math.ceil(M * 0.5);
    canvas.width = Math.ceil(adv) + pad * 2;
    canvas.height = H;
    ctx.font = font; // resizing the canvas resets its state
    ctx.textBaseline = 'alphabetic';
    ctx.fillStyle = '#000';
    ctx.fillText(ch, pad, UP);
    const w = canvas.width, data = ctx.getImageData(0, 0, w, H).data;
    const lb = new Float32Array(H).fill(NaN), rb = new Float32Array(H).fill(NaN);
    let top = -1, bottom = -1;
    for (let y = 0; y < H; y++) {
      let l = -1, r = -1;
      for (let x = 0; x < w; x++) {
        if (data[(y * w + x) * 4 + 3] > 96) { if (l < 0) l = x; r = x; }
      }
      if (l < 0) continue;
      lb[y] = (l - pad) / M;
      rb[y] = (pad + adv - r - 1) / M;
      if (top < 0) top = y;
      bottom = y;
    }
    const s = { ch, adv: adv / M, lb, rb, top, bottom, ink: top >= 0 };
    shapes.set(key, s);
    return s;
  }

  const edge = (side, y0, y1) => {
    let m = Infinity;
    for (let y = y0; y <= y1; y++) if (!isNaN(side[y])) m = Math.min(m, side[y]);
    return m;
  };

  // The white between a and b set side by side with no extra space: the closest approach of the two extreme
  // points plus the open area behind them (only `depth` deep), averaged over the zone's rows. `closest` is the
  // nearest the two inks get on any row, so a pair is never pulled into a touch. After a comma or a full stop
  // only its edge counts: the open air above a small mark is not space the eye gives the next word.
  function white(a, b, y0, y1, depth) {
    const mark = /\p{P}/u.test(a.ch);
    let ea = edge(a.rb, y0, y1), eb = edge(b.lb, y0, y1);
    let z0 = y0, z1 = y1;
    if (!isFinite(ea) || !isFinite(eb)) { // a mark outside the zone (a comma): measure it over its own rows
      z0 = Math.min(a.top, b.top); z1 = Math.max(a.bottom, b.bottom);
      ea = edge(a.rb, z0, z1); eb = edge(b.lb, z0, z1);
    }
    let sum = 0, n = 0, closest = Infinity;
    for (let y = 0; y < H; y++) {
      const r = a.rb[y], l = b.lb[y];
      if (!isNaN(r) && !isNaN(l)) closest = Math.min(closest, r + l);
      if (y < z0 || y > z1) continue;
      sum += (mark ? 0 : Math.min(isNaN(r) ? depth : r - ea, depth)) + Math.min(isNaN(l) ? depth : l - eb, depth);
      n++;
    }
    return { w: ea + eb + sum / n, closest };
  }

  // Where a line's first glyph should start: its outermost ink, eased out by half its open area, so a T or a W
  // hangs a little and a stem sits on the margin.
  function hang(g, y0, y1, depth) {
    const e = edge(g.lb, y0, y1);
    if (!isFinite(e)) return 0;
    let sum = 0, n = 0;
    for (let y = y0; y <= y1; y++) { sum += Math.min(isNaN(g.lb[y]) ? depth : g.lb[y] - e, depth); n++; }
    return e + 0.5 * sum / n;
  }

  const median = (xs) => {
    const s = [...xs].sort((p, q) => p - q);
    return s.length ? (s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2) : 0;
  };

  async function space(el, rule) {
    const cs = getComputedStyle(el);
    const font = `${cs.fontStyle} ${cs.fontWeight} ${M}px ${cs.fontFamily}`;
    const size = parseFloat(cs.fontSize);
    const track = rule.track ?? 0;
    const strength = rule.strength ?? 1;
    const depth = rule.depth ?? 0.1;
    const spaceDepth = rule.spaceDepth ?? 0.03;
    const wordGap = rule.space ?? null;
    const kern = rule.kern ?? {};
    await document.fonts.load(`${cs.fontStyle} ${cs.fontWeight} ${size}px ${cs.fontFamily}`, el.textContent);

    // one span per character; <br> and other elements (the caret) stay and end a line
    const lines = [[]];
    for (const node of [...el.childNodes]) {
      if (node.nodeType !== Node.TEXT_NODE) {
        if (node.nodeName === 'BR') lines.push([]);
        else lines[lines.length - 1].push({ el: node, other: true });
        continue;
      }
      const run = document.createElement('span'); // one box for the run, so a flex parent still sees one item
      for (const ch of node.textContent.replace(/\s+/g, ' ')) {
        const span = document.createElement('span');
        span.textContent = ch;
        run.appendChild(span);
        lines[lines.length - 1].push({ el: span, ch, g: shape(ch, font) });
      }
      node.replaceWith(run);
    }
    el.style.letterSpacing = '0';
    el.style.fontKerning = 'none';

    // the zone pairs are judged over: from the highest ink to the letters' usual bottom
    const letters = lines.flat().filter((c) => c.g && c.g.ink && /[\p{L}\p{N}]/u.test(c.ch));
    if (!letters.length) return;
    const y0 = Math.min(...letters.map((c) => c.g.top));
    const y1 = Math.round(median(letters.map((c) => c.g.bottom)));

    const pairs = []; // {left, right, space, w, closest}
    for (const line of lines) {
      for (let i = 0; i < line.length - 1; i++) {
        const a = line[i];
        if (a.other || a.ch === ' ') continue;
        let j = i + 1, gap = null;
        if (!line[j].other && line[j].ch === ' ') { gap = line[j]; j++; }
        const b = line[j];
        if (!b || b.other || b.ch === ' ' || !a.g.ink || !b.g.ink) continue;
        pairs.push({ left: a, right: b, space: gap, ...white(a.g, b.g, y0, y1, gap ? spaceDepth : depth) });
      }
    }
    const tight = pairs.filter((p) => !p.space);
    const natural = median(tight.map((p) => p.w)), target = natural + track;
    const floor = Math.max(0.025, 1.2 / size); // the inks never come closer than this (em)
    const report = { sel: rule.sel, text: el.textContent.slice(0, 24), size, natural: +natural.toFixed(4), pairs: [] };
    for (const p of pairs) {
      let s;
      if (p.space) {
        if (wordGap === null) continue;
        s = target + wordGap - p.w - p.space.g.adv;
        p.space.el.style.marginRight = `${s.toFixed(4)}em`;
      } else {
        s = track + strength * (natural - p.w);
        s = Math.max(s, floor - p.closest) + (kern[p.left.ch + p.right.ch] ?? 0);
        p.left.el.style.marginRight = `${s.toFixed(4)}em`;
      }
      report.pairs.push(`${p.left.ch}${p.space ? '␣' : ''}${p.right.ch} w${p.w.toFixed(3)} s${s.toFixed(3)}`);
    }
    window.opticalReport.push(report);
    return { el, size, y0, y1, depth, chars: lines.flat().filter((c) => !c.other) };
  }

  // Once laid out, the first glyph of every line (wrapped ones too) moves onto its optical edge; a glyph that is
  // not at the element's left edge (after an icon) stays.
  function edges(job) {
    const box = job.el.getBoundingClientRect();
    const left = box.left + parseFloat(getComputedStyle(job.el).paddingLeft);
    let top = -Infinity;
    for (const c of job.chars) {
      if (c.ch === ' ') continue;
      const r = c.el.getBoundingClientRect();
      if (r.top <= top + 1) continue;
      top = r.top;
      if (r.left - left < job.size * 0.3) c.el.style.marginLeft = `${(-hang(c.g, job.y0, job.y1, job.depth)).toFixed(4)}em`;
    }
  }

  window.opticalReport = [];
  await document.fonts.ready;
  const jobs = [];
  for (const rule of window.OPTICAL || []) {
    for (const el of document.querySelectorAll(rule.sel)) {
      const job = await space(el, rule);
      if (job) jobs.push(job);
    }
  }
  jobs.forEach(edges);
  document.documentElement.dataset.optical = 'done';
})();
