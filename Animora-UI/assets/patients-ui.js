/* =========================================================
   Vetplus — Patients module : shared UI helpers
   ========================================================= */

/* ---------- extra icons (merged into the global set) ---------- */
Object.assign(window.Icons, {
  phone:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.9.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>',
  mobile:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="5" y="2" width="14" height="20" rx="2"/><path d="M12 18h.01"/></svg>',
  mail:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-10 6L2 7"/></svg>',
  pin:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>',
  idcard:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="20" height="16" rx="2"/><circle cx="8.5" cy="11" r="2.5"/><path d="M4.5 17c.8-1.6 2.3-2.5 4-2.5s3.2.9 4 2.5"/><path d="M15 9h4M15 13h4"/></svg>',
  cake:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-8a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8"/><path d="M4 16s1.5-1 4-1 4 2 8 2 4-1 4-1"/><path d="M2 21h20"/><path d="M7 8v-.5a1.5 1.5 0 1 1 3 0V8M14 8v-.5a1.5 1.5 0 1 1 3 0V8"/></svg>',
  palette: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="13.5" cy="6.5" r=".5" fill="currentColor"/><circle cx="17.5" cy="10.5" r=".5" fill="currentColor"/><circle cx="8.5" cy="7.5" r=".5" fill="currentColor"/><circle cx="6.5" cy="12.5" r=".5" fill="currentColor"/><path d="M12 2a10 10 0 0 0 0 20 2 2 0 0 0 2-2v-1a2 2 0 0 1 2-2h1a4 4 0 0 0 4-4 10 10 0 0 0-9-11z"/></svg>',
  heart:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1.1L12 21.2l7.8-7.7 1-1.1a5.5 5.5 0 0 0 0-7.8z"/></svg>',
  smile:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M8 14s1.5 2 4 2 4-2 4-2"/><path d="M9 9h.01M15 9h.01"/></svg>',
  cpu:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M9 2v2M15 2v2M9 20v2M15 20v2M2 9h2M2 15h2M20 9h2M20 15h2"/></svg>',
  barcode: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 5v14M7 5v14M11 5v10M15 5v14M19 5v14"/></svg>',
  scale:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v18"/><path d="M5 21h14"/><path d="m3 9 4-6 4 6a4 4 0 0 1-8 0z"/><path d="m13 9 4-6 4 6a4 4 0 0 1-8 0z"/></svg>',
  ruler:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.3 8.7 8.7 21.3a1 1 0 0 1-1.4 0l-4.6-4.6a1 1 0 0 1 0-1.4L15.3 2.7a1 1 0 0 1 1.4 0l4.6 4.6a1 1 0 0 1 0 1.4z"/><path d="m7.5 10.5 2 2M10.5 7.5l2 2M13.5 4.5l2 2M4.5 13.5l2 2"/></svg>',
  thermo:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 14.76V3.5a2.5 2.5 0 0 0-5 0v11.26a4.5 4.5 0 1 0 5 0z"/></svg>',
  pulse:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 12h-4l-3 9L9 3l-3 9H2"/></svg>',
  lungs:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v10"/><path d="M9 13c0 3-1 6-3.5 6C3 19 2 17 2 14c0-3 2-6 4-8"/><path d="M15 13c0 3 1 6 3.5 6C21 19 22 17 22 14c0-3-2-6-4-8"/></svg>',
  stetho:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 3v6a5 5 0 0 0 10 0V3"/><path d="M4 3H2M14 3h2"/><path d="M9 14v2a5 5 0 0 0 10 0v-2"/><circle cx="19" cy="11" r="2.5"/></svg>',
  scalpel: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 4 20 10 10 20H4v-6z"/><path d="m14 4 6 6"/></svg>',
  shield:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="m9 12 2 2 4-4"/></svg>',
  folder:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4h5l2 3h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"/></svg>',
  upload:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>',
  printer: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>',
  copy:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>',
  arrowR:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="19" y1="12" x2="5" y2="12"/><polyline points="12 19 5 12 12 5"/></svg>',
  user:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
  userPlus:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><line x1="19" y1="8" x2="19" y2="14"/><line x1="22" y1="11" x2="16" y2="11"/></svg>',
  grid:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>',
  list:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
  home2:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M9 22V12h6v10"/></svg>',
  bowl:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 11h18a9 9 0 0 1-9 9 9 9 0 0 1-9-9z"/><path d="M8 7c0-1.5 1-2 1-3M12 6c0-1.5 1-2 1-3M16 7c0-1.5 1-2 1-3"/></svg>',
  info:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>',
  history: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v5h5"/><path d="M3.05 13A9 9 0 1 0 6 5.3L3 8"/><path d="M12 7v5l4 2"/></svg>',
  trend:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>',
  star:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>',
  sms:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"/></svg>',
  microscope:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 18h8"/><path d="M3 22h18"/><path d="M14 22a7 7 0 1 0 0-14h-1"/><path d="M9 14h2"/><path d="M9 12a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h1a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2z"/></svg>',
  sliders: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="4" y1="21" x2="4" y2="14"/><line x1="4" y1="10" x2="4" y2="3"/><line x1="12" y1="21" x2="12" y2="12"/><line x1="12" y1="8" x2="12" y2="3"/><line x1="20" y1="21" x2="20" y2="16"/><line x1="20" y1="12" x2="20" y2="3"/><line x1="1" y1="14" x2="7" y2="14"/><line x1="9" y1="8" x2="15" y2="8"/><line x1="17" y1="16" x2="23" y2="16"/></svg>',
});

/* ---------- render helpers ---------- */
window.speciesIcon = (key) => window.Icons[(window.SPECIES[key] || window.SPECIES.other).icon] || window.Icons.paw;
window.speciesLabel = (key) => (window.SPECIES[key] || window.SPECIES.other).label;

window.initials = (o) => `${(o.first || '؟')[0]}${(o.last || '')[0] || ''}`;

window.statusPill = (status) => {
  const s = window.STATUS_MAP[status] || window.STATUS_MAP.healthy;
  return `<span class="pill ${s.cls}">${s.label}</span>`;
};

window.sexDot = (g) => {
  const m = { male: '♂', female: '♀', unknown: '؟' };
  return `<span class="sex-dot ${g}" title="${(window.GENDER_MAP[g] || {}).label || ''}">${m[g] || '؟'}</span>`;
};

/* ---------- toast ---------- */
window.toast = function (msg, icon = 'check') {
  let host = document.querySelector('.toast-host');
  if (!host) { host = document.createElement('div'); host.className = 'toast-host'; document.body.appendChild(host); }
  const el = document.createElement('div');
  el.className = 'toast';
  el.innerHTML = `${window.Icons[icon] || ''}<span>${msg}</span>`;
  host.appendChild(el);
  setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 320); }, 2600);
};

/* ---------- reveal on scroll ---------- */
window.mountReveal = function (sel = '.rv') {
  const els = document.querySelectorAll(sel);
  if (!('IntersectionObserver' in window)) { els.forEach(e => e.classList.add('in')); return; }
  const io = new IntersectionObserver((ents) => {
    ents.forEach((e, i) => {
      if (e.isIntersecting) {
        setTimeout(() => e.target.classList.add('in'), Math.min(i * 45, 220));
        io.unobserve(e.target);
      }
    });
  }, { rootMargin: '0px 0px -40px 0px', threshold: .04 });
  els.forEach(e => io.observe(e));
};

/* ---------- simple line chart (SVG) ---------- */
/* series: [{ points:[{label, value}], color, name, dash }] */
window.lineChart = function (host, series, opts = {}) {
  if (!host) return;
  const W = opts.width || host.clientWidth || 640;
  const H = opts.height || 220;
  const pad = { t: 14, r: 10, b: 26, l: 40 };
  const all = series.flatMap(s => s.points.map(p => p.value));
  if (!all.length) { host.innerHTML = '<div style="padding:30px;text-align:center;color:var(--muted);font-size:12px">داده‌ای ثبت نشده است</div>'; return; }
  let min = Math.min(...all), max = Math.max(...all);
  const span = (max - min) || 1;
  min = Math.max(0, min - span * 0.22); max = max + span * 0.22;
  const n = Math.max(...series.map(s => s.points.length));
  const iw = W - pad.l - pad.r, ih = H - pad.t - pad.b;
  const X = (i, len) => pad.l + (len <= 1 ? iw / 2 : (iw * i) / (len - 1));
  const Y = (v) => pad.t + ih - ((v - min) / (max - min)) * ih;

  const ticks = 4;
  let grid = '';
  for (let i = 0; i <= ticks; i++) {
    const v = min + ((max - min) * i) / ticks;
    const y = Y(v);
    grid += `<line class="grid-l" x1="${pad.l}" y1="${y}" x2="${W - pad.r}" y2="${y}" ${i ? 'stroke-dasharray="3 4"' : ''}/>`;
    grid += `<text class="axis-t" x="${pad.l - 6}" y="${y + 3}" text-anchor="end">${window.fa(v.toFixed(v < 10 ? 1 : 0))}</text>`;
  }

  let paths = '', dots = '', labels = '';
  series.forEach((s, si) => {
    const pts = s.points.map((p, i) => [X(i, s.points.length), Y(p.value)]);
    const d = pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join(' ');
    if (si === 0 && opts.area !== false) {
      const areaD = `${d} L${pts[pts.length - 1][0].toFixed(1)},${pad.t + ih} L${pts[0][0].toFixed(1)},${pad.t + ih} Z`;
      paths += `<path class="area" d="${areaD}"/>`;
    }
    paths += `<path class="line ${si ? 'alt' : ''}" d="${d}" style="stroke:${s.color || 'var(--brand-600)'}" ${s.dash ? 'stroke-dasharray="5 5"' : ''}/>`;
    pts.forEach((p, i) => {
      dots += `<circle class="dot ${si ? 'alt' : ''}" style="stroke:${s.color || 'var(--brand-600)'}" cx="${p[0].toFixed(1)}" cy="${p[1].toFixed(1)}" r="4"
        data-l="${s.points[i].label}" data-v="${s.points[i].value}" data-n="${s.name || ''}" data-u="${s.unit || ''}"/>`;
    });
  });

  const base = series[0].points;
  base.forEach((p, i) => {
    if (n > 6 && i % Math.ceil(n / 6) !== 0 && i !== n - 1) return;
    labels += `<text class="axis-t" x="${X(i, base.length)}" y="${H - 6}" text-anchor="middle">${p.short || p.label}</text>`;
  });

  host.innerHTML = `
    <svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" style="height:${H}px">
      <defs>
        <linearGradient id="wgrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="${series[0].color || '#0d9488'}" stop-opacity=".20"/>
          <stop offset="100%" stop-color="${series[0].color || '#0d9488'}" stop-opacity="0"/>
        </linearGradient>
      </defs>
      ${grid}${paths}${dots}${labels}
    </svg>
    <div class="chart-tip"></div>`;

  const tip = host.querySelector('.chart-tip');
  host.querySelectorAll('.dot').forEach(dot => {
    dot.addEventListener('mouseenter', () => {
      const r = dot.getBoundingClientRect(), hr = host.getBoundingClientRect();
      tip.innerHTML = `<b>${window.fa(dot.dataset.v)} ${dot.dataset.u}</b> <span class="t2">${dot.dataset.n}</span><div class="t2">${window.fa(dot.dataset.l)}</div>`;
      tip.style.insetInlineStart = '';
      tip.style.left = (r.left - hr.left + r.width / 2) + 'px';
      tip.style.top = (r.top - hr.top) + 'px';
      tip.classList.add('on');
    });
    dot.addEventListener('mouseleave', () => tip.classList.remove('on'));
  });
};

/* ---------- pseudo barcode (visual only) ---------- */
window.barcodeSvg = function (code) {
  if (!code) return '';
  let bars = '', x = 0;
  const digits = String(code).replace(/\D/g, '');
  const seq = (digits + digits).split('').map(Number);
  seq.forEach((d, i) => {
    const w = 1 + (d % 3);
    if (i % 2 === 0) { bars += `<rect x="${x}" y="0" width="${w}" height="46" fill="#172a2a"/>`; }
    x += w + 1 + (d % 2);
  });
  return `<svg viewBox="0 0 ${x} 46" preserveAspectRatio="none">${bars}</svg>`;
};

/* ---------- copy ---------- */
window.copyText = function (t, label = 'کپی شد') {
  navigator.clipboard?.writeText(t).then(() => window.toast(label)).catch(() => window.toast(label));
};
