/* =========================================================
   Vetplus · Finance — page controller
   Unified "هزینه‌ها و گزارش فروش" workspace.
   Tabs: نمای کلی · درآمد و فروش · هزینه‌ها · سود و تحلیل.
   Interactive SVG charts (combined revenue/expense/profit,
   donuts), timeframe filter, expense modal, drill-down toast.
   Pattern A: IIFE + window.initFinancePage().
   ========================================================= */
(function () {
  'use strict';

  const D = window.FinanceData;
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const fa = window.toFa || (v => String(v));
  const money = window.money || (n => fa(n));
  const short = window.moneyShort || (n => fa(n));

  // module-private state
  const S = { tab: 'overview', range: '30d', agg: null, prodSort: 'revenue' };

  // معیارهای مرتب‌سازی «پرفروش‌ترین کالا و خدمات»
  const PROD_SORTS = [
    { id: 'revenue', label: 'پردرآمدترین', key: p => p.revenue },
    { id: 'qty',     label: 'پرفروش‌ترین', key: p => p.qty },
    { id: 'profit',  label: 'پرسودترین',  key: p => p.profit },
  ];

  /* ---------------- number helpers ---------------- */
  const T = 'تومان';
  function signPct(v) {
    const s = Math.abs(v).toFixed(1).replace('.', '٫');
    return (v >= 0 ? '+' : '−') + fa(s) + '٪';
  }
  // نمایش پول به میلیون با یک رقم اعشار برای KPI ها
  function faDec(n, d) { return fa(Number(n).toFixed(d).replace('.', '٫')); }

  /* =========================================================
     TABS
     ========================================================= */
  const TABS = [
    { id: 'overview', label: 'نمای کلی',       icon: 'dashboard' },
    { id: 'income',   label: 'درآمد و فروش',   icon: 'trendUp'   },
    { id: 'expense',  label: 'هزینه‌ها',        icon: 'receipt'   },
    { id: 'profit',   label: 'سود و تحلیل',     icon: 'piggy'     },
  ];

  function renderTabs() {
    return `<div class="pb-seg lg fin-tabs pb-seg--fill" role="tablist">${TABS.map(t => `
      <button class="fin-tab ${t.id === S.tab ? 'active' : ''}" role="tab"
              data-tab="${t.id}" aria-selected="${t.id === S.tab}">
        ${window.Icons[t.icon] || ''}<span>${t.label}</span>
      </button>`).join('')}</div>`;
  }

  function renderRange() {
    return `<div class="pb-seg fin-range">${D.RANGES.map(r => `
      <button data-range="${r.id}" class="${r.id === S.range ? 'active' : ''}">${r.label}</button>`).join('')}</div>`;
  }

  /* =========================================================
     TREND CHART — modern minimal dual-area + smooth profit line
     Fully redesigned: soft gradient area fills for revenue &
     expense, a crisp profit spline on top, dashed baseline grid,
     hover crosshair + focus dots. RTL-aware (data plotted R→L).
     ========================================================= */
  // Catmull-Rom → cubic Bézier smoothing for organic curves.
  function smoothPath(pts) {
    if (pts.length < 2) return pts.length ? `M${pts[0][0]},${pts[0][1]}` : '';
    let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[i - 1] || pts[i];
      const p1 = pts[i];
      const p2 = pts[i + 1];
      const p3 = pts[i + 2] || p2;
      const t = 0.16;
      const c1x = p1[0] + (p2[0] - p0[0]) * t;
      const c1y = p1[1] + (p2[1] - p0[1]) * t;
      const c2x = p2[0] - (p3[0] - p1[0]) * t;
      const c2y = p2[1] - (p3[1] - p1[1]) * t;
      d += ` C${c1x.toFixed(1)},${c1y.toFixed(1)} ${c2x.toFixed(1)},${c2y.toFixed(1)} ${p2[0].toFixed(1)},${p2[1].toFixed(1)}`;
    }
    return d;
  }

  function trendChart(series) {
    const W = 720, H = 248, padT = 18, padB = 26, padL = 10, padR = 46;
    const innerW = W - padL - padR;
    const innerH = H - padT - padB;
    // RTL: newest at the LEFT. reverse for plotting direction.
    const data = series.slice().reverse();
    const maxV = Math.max(...data.map(d => Math.max(d.revenue, d.expense))) * 1.14 || 1;
    const n = data.length;
    const x = i => padL + (n === 1 ? innerW / 2 : innerW * i / (n - 1));
    const y = v => padT + innerH - (v / maxV) * innerH;

    // horizontal grid + right-side value axis
    let grid = '';
    for (let i = 0; i <= 4; i++) {
      const gy = padT + innerH * i / 4;
      grid += `<line class="fin-grid-line" x1="${padL}" y1="${gy.toFixed(1)}" x2="${W - padR}" y2="${gy.toFixed(1)}"/>`;
      const val = maxV * (1 - i / 4);
      grid += `<text class="fin-axis-y" x="${W - padR + 8}" y="${(gy + 3).toFixed(1)}">${short(Math.round(val))}</text>`;
    }

    const revPts = data.map((d, i) => [x(i), y(d.revenue)]);
    const expPts = data.map((d, i) => [x(i), y(d.expense)]);
    const proPts = data.map((d, i) => [x(i), y(Math.max(0, d.profit))]);

    const revLine = smoothPath(revPts);
    const expLine = smoothPath(expPts);
    const proLine = smoothPath(proPts);
    const base = padT + innerH;
    const revArea = `${revLine} L${revPts[n - 1][0].toFixed(1)},${base} L${revPts[0][0].toFixed(1)},${base} Z`;
    const expArea = `${expLine} L${expPts[n - 1][0].toFixed(1)},${base} L${expPts[0][0].toFixed(1)},${base} Z`;

    // interaction columns (hover surfaces) + focus markers, mapped back
    // to the ORIGINAL (non-reversed) index for tooltip lookup.
    const colW = innerW / n;
    const hit = data.map((d, i) => {
      const origIdx = series.length - 1 - i;
      const cx = x(i);
      return `<g class="fin-col-group" data-i="${origIdx}">
        <rect class="fin-col-hit" x="${(cx - colW / 2).toFixed(1)}" y="${padT}" width="${colW.toFixed(1)}" height="${innerH}"/>
        <line class="fin-crosshair" x1="${cx.toFixed(1)}" y1="${padT}" x2="${cx.toFixed(1)}" y2="${base}"/>
        <circle class="fin-focus-dot rev" cx="${cx.toFixed(1)}" cy="${y(d.revenue).toFixed(1)}" r="4"/>
        <circle class="fin-focus-dot exp" cx="${cx.toFixed(1)}" cy="${y(d.expense).toFixed(1)}" r="4"/>
      </g>`;
    }).join('');

    const dashLen = reduce ? 0 : 1600;
    const svg = `
      <svg class="fin-chart" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" id="fin-main-svg">
        <defs>
          <linearGradient id="finRevGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="var(--fin-income)" stop-opacity=".26"/>
            <stop offset="100%" stop-color="var(--fin-income)" stop-opacity="0"/>
          </linearGradient>
          <linearGradient id="finExpGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="var(--fin-expense)" stop-opacity=".16"/>
            <stop offset="100%" stop-color="var(--fin-expense)" stop-opacity="0"/>
          </linearGradient>
        </defs>
        <g class="fin-grid">${grid}</g>
        <path class="fin-area-exp" d="${expArea}"/>
        <path class="fin-area-rev" d="${revArea}"/>
        <path class="fin-line-exp" d="${expLine}" style="--dash:${dashLen}"/>
        <path class="fin-line-rev" d="${revLine}" style="--dash:${dashLen}"/>
        <path class="fin-line-pro" d="${proLine}" style="--dash:${dashLen}"/>
        ${hit}
      </svg>`;

    // x labels: show first / mid / last to stay minimal on dense ranges
    const keep = new Set([0, Math.floor((n - 1) / 2), n - 1]);
    const labels = `<div class="fin-xlabels num-fa">${data.map((d, i) =>
      `<span class="${keep.has(i) ? '' : 'ghost'}">${keep.has(i) ? d.label : ''}</span>`).join('')}</div>`;
    return svg + labels;
  }

  /* =========================================================
     DONUT — dashboard-matched interactive ring
     Gapped animated segments, hover-to-focus dim, glossy center,
     row legend with share fill + percentage. `opts.fmt` controls
     legend value formatting (money vs. percent).
     ========================================================= */
  let donutUid = 0;
  function donut(items, centerVal, centerLabel, opts) {
    opts = opts || {};
    const fmt = opts.fmt || (v => short(v));
    const uid = 'fd' + (++donutUid);
    const total = items.reduce((a, x) => a + x.value, 0) || 1;
    const R = 42, SW = 14, C = 2 * Math.PI * R, GAP = 3.2;
    let off = 0;
    const segs = items.map((x, i) => {
      const len = (x.value / total) * C;
      const target = Math.max(1, len - GAP);
      const seg = `<circle class="fin-seg" data-i="${i}" cx="50" cy="50" r="${R}" stroke="${x.color}"
        stroke-width="${SW}" stroke-linecap="round"
        stroke-dasharray="0 ${C.toFixed(2)}"
        stroke-dashoffset="${(-(off + GAP / 2)).toFixed(2)}"
        data-target="${target.toFixed(2)}" data-delay="${120 + i * 100}">
        <title>${x.label}: ${fmt(x.value)}</title></circle>`;
      off += len;
      return seg;
    }).join('');

    const legend = items.map((x, i) => {
      const pct = Math.round(x.value / total * 100);
      return `<div class="fin-leg-row" data-i="${i}" style="--c:${x.color}">
        <span class="fin-leg-fill"></span>
        <span class="fin-leg-sw" style="background:${x.color}"></span>
        <span class="fin-leg-name">${x.label}</span>
        <span class="fin-leg-val num-fa">${fmt(x.value)}</span>
        <span class="fin-leg-pct num-fa">${fa(pct)}٪</span>
      </div>`;
    }).join('');

    // store metadata on the wrapper for the focus/blur binder
    const meta = items.map(x => ({
      label: x.label, color: x.color,
      val: fmt(x.value), pct: Math.round(x.value / total * 100),
    }));

    return `
      <div class="fin-donut-body">
        <div class="fin-donut-wrap" id="${uid}" data-center-val="${centerVal}" data-center-label="${centerLabel}"
             data-meta='${JSON.stringify(meta).replace(/'/g, "&#39;")}'>
          <svg viewBox="0 0 100 100" aria-hidden="true">
            <circle class="fin-donut-track" cx="50" cy="50" r="${R}" fill="none" stroke="var(--fin-track)" stroke-width="${SW}"/>
            ${segs}
          </svg>
          <div class="fin-donut-center">
            <div class="v num-fa">${centerVal}</div>
            <div class="l">${centerLabel}</div>
          </div>
        </div>
        <div class="fin-legend">${legend}</div>
      </div>`;
  }

  /* animate + wire hover-focus for every donut just injected */
  function bindDonuts(scope) {
    (scope || document).querySelectorAll('.fin-donut-wrap').forEach(wrap => {
      const svg = wrap.querySelector('svg');
      const C = 2 * Math.PI * 42;
      const segs = [].slice.call(wrap.querySelectorAll('.fin-seg'));
      const rows = [].slice.call(wrap.parentElement.querySelectorAll('.fin-leg-row'));
      const vEl = wrap.querySelector('.fin-donut-center .v');
      const lEl = wrap.querySelector('.fin-donut-center .l');
      const baseVal = wrap.dataset.centerVal, baseLbl = wrap.dataset.centerLabel;
      let meta = [];
      try { meta = JSON.parse(wrap.dataset.meta); } catch (e) { meta = []; }

      // grow-in animation
      segs.forEach(s => {
        const target = parseFloat(s.dataset.target);
        const delay = reduce ? 0 : parseInt(s.dataset.delay, 10) || 0;
        if (reduce) { s.setAttribute('stroke-dasharray', target + ' ' + C); return; }
        setTimeout(() => {
          const t0 = performance.now(), dur = 850;
          (function frame(now) {
            const p = Math.min(1, (now - t0) / dur);
            const e = 1 - Math.pow(1 - p, 3);
            s.setAttribute('stroke-dasharray', (target * e).toFixed(2) + ' ' + C);
            if (p < 1) requestAnimationFrame(frame);
          })(t0);
        }, delay);
      });

      // legend fill grow
      rows.forEach((r, i) => {
        const fill = r.querySelector('.fin-leg-fill');
        if (fill) setTimeout(() => { fill.style.width = Math.max(8, (meta[i] ? meta[i].pct : 0)) + '%'; }, reduce ? 0 : 180 + i * 90);
      });

      const focus = (i) => {
        wrap.classList.add('dim');
        segs.forEach((s, k) => s.classList.toggle('on', k === i));
        rows.forEach((r, k) => r.classList.toggle('on', k === i));
        const m = meta[i]; if (!m) return;
        vEl.textContent = fa(m.val); vEl.style.color = m.color;
        lEl.textContent = m.label + ' · ' + fa(m.pct) + '٪';
      };
      const blur = () => {
        wrap.classList.remove('dim');
        segs.forEach(s => s.classList.remove('on'));
        rows.forEach(r => r.classList.remove('on'));
        vEl.textContent = fa(baseVal); vEl.style.color = '';
        lEl.textContent = baseLbl;
      };
      segs.concat(rows).forEach(el => {
        el.addEventListener('mouseenter', () => focus(+el.dataset.i));
        el.addEventListener('mouseleave', blur);
      });
    });
  }

  /* =========================================================
     KPI STRIP
     ========================================================= */
  function kpiStrip(a) {
    const dRev = a.delta.revenue, dExp = a.delta.expense, dProfit = a.delta.profit, dMargin = a.delta.margin;
    // for expense: increase is "bad" (red), decrease is "good"
    const expClass = dExp > 0 ? 'bad' : 'good';
    const arrow = v => (v >= 0 ? window.Icons.arrowUpRight : window.Icons.trendDown);
    return `
      <div class="fin-kpis">
        <div class="fin-kpi hero fin-rv">
          <div class="fin-kpi-head">
            <div class="fin-kpi-ic">${window.Icons.trendUp}</div>
            <span class="fin-kpi-delta good">${window.Icons.arrowUpRight}${signPct(dRev)}</span>
          </div>
          <div class="fin-kpi-value"><span class="num-fa">${faDec(a.revTotal / 1e6, 1)}</span><span class="fin-kpi-unit">م ${T}</span></div>
          <div class="fin-kpi-label">درآمد کل</div>
          <div class="fin-kpi-foot">نسبت به بازه قبل</div>
        </div>

        <div class="fin-kpi fin-rv">
          <div class="fin-kpi-head">
            <div class="fin-kpi-ic expense">${window.Icons.receipt}</div>
            <span class="fin-kpi-delta ${expClass}">${arrow(dExp)}${signPct(dExp)}</span>
          </div>
          <div class="fin-kpi-value"><span class="num-fa">${faDec(a.expTotal / 1e6, 1)}</span><span class="fin-kpi-unit">م ${T}</span></div>
          <div class="fin-kpi-label">هزینه کل</div>
          <div class="fin-kpi-foot">نسبت به بازه قبل</div>
        </div>

        <div class="fin-kpi fin-rv">
          <div class="fin-kpi-head">
            <div class="fin-kpi-ic profit">${window.Icons.piggy}</div>
            <span class="fin-kpi-delta ${dProfit >= 0 ? 'good' : 'bad'}">${arrow(dProfit)}${signPct(dProfit)}</span>
          </div>
          <div class="fin-kpi-value" style="color:var(--fin-profit)"><span class="num-fa">${faDec(a.profit / 1e6, 1)}</span><span class="fin-kpi-unit">م ${T}</span></div>
          <div class="fin-kpi-label">سود خالص</div>
          <div class="fin-kpi-foot">درآمد منهای هزینه</div>
        </div>

        <div class="fin-kpi fin-rv">
          <div class="fin-kpi-head">
            <div class="fin-kpi-ic income">${window.Icons.percent}</div>
            <span class="fin-kpi-delta ${dMargin >= 0 ? 'good' : 'bad'}">${arrow(dMargin)}${fa(Math.abs(dMargin).toFixed(1).replace('.', '٫'))} واحد</span>
          </div>
          <div class="fin-kpi-value"><span class="num-fa">${fa(a.margin.toFixed(1).replace('.', '٫'))}</span><span class="fin-kpi-unit">٪</span></div>
          <div class="fin-kpi-label">حاشیه سود</div>
          <div class="fin-kpi-foot">سهم سود از درآمد</div>
        </div>
      </div>`;
  }

  /* =========================================================
     CARD BUILDERS
     ========================================================= */
  function mainChartCard(a, opts) {
    opts = opts || {};
    const title = opts.title || 'روند مالی';
    const eyebrow = opts.eyebrow || 'درآمد · هزینه · سود';
    return `
      <div class="card fin-rv fin-chart-card">
        <div class="card-header">
          <div>
            <div class="fin-eyebrow">${eyebrow}</div>
            <h3>${title} — ${a.range.label}</h3>
          </div>
          <div class="chart-legend fin-chart-legend">
            <span><span class="swatch" style="background:var(--fin-income)"></span>درآمد</span>
            <span><span class="swatch" style="background:var(--fin-expense)"></span>هزینه</span>
            <span><span class="swatch fin-sw-line" style="--c:var(--fin-profit)"></span>سود خالص</span>
          </div>
        </div>
        <div class="fin-chart-wrap">${trendChart(a.series)}</div>
      </div>`;
  }

  function channelsCard(a) {
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">ترکیب درآمد</div><h3>کانال‌های فروش</h3></div>
          <span class="fin-head-chip">${window.Icons.pieChart}${fa(a.byChannel.length)} کانال</span></div>
        ${donut(a.byChannel, short(a.revTotal), 'کل درآمد')}
      </div>`;
  }

  function categoriesCard(a) {
    const maxV = Math.max(...a.byCategory.map(c => c.value)) || 1;
    const rows = a.byCategory.map(c => `
      <div class="fin-list-row">
        <div class="fin-list-lead">
          <div class="fin-list-ic ${c.tone}">${window.Icons[c.icon] || window.Icons.tag}</div>
          <div style="min-width:0">
            <div class="fin-list-name">${c.label}</div>
            <div class="fin-list-track"><span style="width:${(c.value / maxV * 100).toFixed(0)}%;background:${c.color}"></span></div>
          </div>
        </div>
        <div class="fin-list-val">
          <div class="amt num-fa" style="color:var(--fin-expense)">${short(c.value)}</div>
          <div class="sub"><span class="fin-chip ${c.delta > 0 ? 'down' : 'up'}">${c.delta > 0 ? window.Icons.arrowUpRight : window.Icons.trendDown}${signPct(c.delta)}</span></div>
        </div>
      </div>`).join('');
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">تفکیک مخارج</div><h3>دسته‌بندی هزینه‌ها</h3></div>
          <span class="fin-head-chip">${window.Icons.receipt}${fa(a.byCategory.length)} دسته</span></div>
        <div class="fin-list">${rows}</div>
      </div>`;
  }

  function productsCard() {
    const sortCfg = PROD_SORTS.find(s => s.id === S.prodSort) || PROD_SORTS[0];
    const key = sortCfg.key;
    const sorted = D.TOP_PRODUCTS.slice().sort((a, b) => key(b) - key(a));
    const maxV = Math.max(...sorted.map(key)) || 1;
    // مقدار معیار فعال را برای هر ردیف به شکل خوانا نمایش بده
    const primary = (p) => {
      if (S.prodSort === 'qty')   return { main: fa(p.qty), sub: 'مورد فروش', color: 'var(--fin-income)' };
      if (S.prodSort === 'profit')return { main: short(p.profit), sub: 'سود ناخالص', color: 'var(--fin-profit)' };
      return { main: short(p.revenue), sub: 'درآمد', color: 'var(--fin-income)' };
    };
    const kindTag = (p) => p.kind === 'service'
      ? `<span class="fin-kind-tag svc">خدمت</span>`
      : `<span class="fin-kind-tag prod">کالا</span>`;
    const rows = sorted.map((p, i) => {
      const pr = primary(p);
      const rankCls = i < 3 ? ' rank-' + (i + 1) : '';
      const rank = i < 3
        ? `<span class="fin-rank${rankCls}">${fa(i + 1)}</span>`
        : `<span class="fin-rank ghost">${fa(i + 1)}</span>`;
      return `
      <div class="fin-list-row prod-row">
        ${rank}
        <div class="fin-list-lead">
          <div class="fin-list-ic ${p.tone}">${window.Icons[p.icon] || window.Icons.tag}</div>
          <div style="min-width:0">
            <div class="fin-list-name">${p.name} ${kindTag(p)}</div>
            <div class="fin-list-meta"><span class="num-fa">${fa(p.qty)}</span> فروش · حاشیه سود <span class="num-fa">${fa(p.margin.toFixed(0))}٪</span></div>
            <div class="fin-list-track"><span style="width:${(key(p) / maxV * 100).toFixed(0)}%;background:${pr.color}"></span></div>
          </div>
        </div>
        <div class="fin-list-val">
          <div class="amt num-fa" style="color:${pr.color}">${pr.main}</div>
          <div class="sub">${pr.sub}</div>
        </div>
      </div>`;
    }).join('');
    const toggle = `<div class="fin-seg fin-prod-sort">${PROD_SORTS.map(s =>
      `<button data-psort="${s.id}" class="${s.id === S.prodSort ? 'active' : ''}">${s.label}</button>`).join('')}</div>`;
    return `
      <div class="card fin-rv">
        <div class="card-header">
          <div><div class="fin-eyebrow">قهرمانان فروش</div><h3>پرفروش‌ترین کالا و خدمات</h3></div>
          ${toggle}
        </div>
        <div class="fin-list">${rows}</div>
      </div>`;
  }

  function speciesCard() {
    const total = D.SPECIES.reduce((a, s) => a + s.value, 0);
    const max = Math.max(...D.SPECIES.map(s => s.value));
    const rows = D.SPECIES.map(s => `
      <div class="fin-hbar">
        <div class="lbl">${s.label}</div>
        <div class="track"><span style="width:${(s.value / max * 100).toFixed(0)}%;background:${s.color}"></span></div>
        <div class="val num-fa">${fa(s.value)}</div>
      </div>`).join('');
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">تحلیل مشتری</div><h3>گونه‌های مراجعه‌کننده</h3></div>
          <span class="fin-head-chip">${window.Icons.paw}${fa(total)} مراجعه</span></div>
        <div style="padding:14px 20px 18px">${rows}</div>
      </div>`;
  }

  function paymentCard() {
    const items = D.PAYMENT_SPLIT.map(p => { const m = D.methodById(p.id); return { label: m.label, value: p.value, color: m.color }; });
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">جریان نقدی</div><h3>روش‌های پرداخت</h3></div>
          <span class="fin-head-chip">${window.Icons.card}${fa(items.length)} روش</span></div>
        ${donut(items, fa('۱۰۰') + '٪', 'کل تراکنش', { fmt: v => fa(v) + '٪' })}
      </div>`;
  }

  function heatmapCard() {
    const days = ['شنبه', 'یک‌شنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنج‌شنبه', 'جمعه'];
    const shades = ['#eaf2ef', '#c8e4dd', '#a5d3c6', '#6ec0ac', '#14b8a6', '#104a43'];
    let cells = '<div class="hlbl"></div>';
    for (let h = 9; h <= 20; h++) cells += `<div class="hlbl num-fa">${fa(h)}</div>`;
    D.HEATMAP.forEach((row, i) => {
      cells += `<div class="hlbl">${days[i]}</div>`;
      row.forEach(v => cells += `<div class="cell" style="background:${shades[v]}" title="${days[i]} — شدت مراجعه ${fa(v)}"></div>`);
    });
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">الگوی مراجعه</div><h3>ساعات طلایی کلینیک</h3></div>
          <span class="fin-head-chip">${window.Icons.clock}۹ تا ۲۰</span></div>
        <div style="padding:16px 20px">
          <div class="fin-heatmap">${cells}</div>
          <div class="fin-heat-legend">
            <span>کم</span>
            <div class="scale">${shades.map(c => `<span style="background:${c}"></span>`).join('')}</div>
            <span>زیاد</span>
          </div>
        </div>
      </div>`;
  }

  // کارت «خدمات vs کالا» — منبع اصلی درآمد کجاست؟
  function serviceVsProductCard() {
    const parts = D.serviceVsProduct();
    const total = parts.reduce((a, p) => a + p.revenue, 0) || 1;
    const bar = parts.map(p =>
      `<span class="svp-seg" style="width:${(p.revenue / total * 100).toFixed(1)}%;background:${p.color}" title="${p.label}"></span>`).join('');
    const cards = parts.map(p => {
      const share = Math.round(p.revenue / total * 100);
      return `
      <div class="svp-item">
        <div class="svp-head">
          <span class="svp-ic" style="background:${p.color}1a;color:${p.color}">${window.Icons[p.icon] || window.Icons.tag}</span>
          <span class="svp-name">${p.label}</span>
          <span class="svp-share num-fa" style="color:${p.color}">${fa(share)}٪</span>
        </div>
        <div class="svp-value num-fa">${short(p.revenue)}</div>
        <div class="svp-metrics">
          <span>${window.Icons.cart}<span class="num-fa">${fa(p.qty)}</span> مورد</span>
          <span>${window.Icons.piggy}حاشیه <span class="num-fa">${fa(p.margin.toFixed(0))}٪</span></span>
        </div>
      </div>`;
    }).join('');
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">منبع درآمد</div><h3>خدمات در برابر کالا</h3></div>
          <span class="fin-head-chip">${window.Icons.scale}تفکیک فروش</span></div>
        <div class="svp-body">
          <div class="svp-bar">${bar}</div>
          <div class="svp-grid">${cards}</div>
        </div>
      </div>`;
  }

  // نوار کشف کلیدی — یک جمله‌ی برجسته و متغیر بر اساس تب فعال
  function keyHighlight(a) {
    const parts = D.serviceVsProduct();
    const topRevProd = D.TOP_PRODUCTS.slice().sort((x, y) => y.revenue - x.revenue)[0];
    const topQtyProd = D.TOP_PRODUCTS.slice().sort((x, y) => y.qty - x.qty)[0];
    const topMarginProd = D.TOP_PRODUCTS.slice().sort((x, y) => y.margin - x.margin)[0];
    const topChannel = a.byChannel.slice().sort((x, y) => y.value - x.value)[0];
    const topCat = a.byCategory[0];
    const svcShare = Math.round(parts[0].revenue / (parts[0].revenue + parts[1].revenue) * 100);
    let icon = 'bulb', tone = 'info', label = 'کشف کلیدی', html = '';
    if (S.tab === 'overview') {
      tone = 'ok'; icon = 'spark';
      html = `بیشترین درآمد از <b>${topChannel.label}</b> (${fa(Math.round(topChannel.value / a.revTotal * 100))}٪ کل) و پردرآمدترین آیتم <b>${topRevProd.name}</b> با <b class="num-fa">${short(topRevProd.revenue)}</b> است.`;
    } else if (S.tab === 'income') {
      tone = 'ok'; icon = 'trendUp';
      html = `<b>${fa(svcShare)}٪</b> درآمد از <b>خدمات و ویزیت</b> می‌آید؛ پرفروش‌ترین آیتم <b>${topQtyProd.name}</b> با <b class="num-fa">${fa(topQtyProd.qty)}</b> فروش.`;
    } else if (S.tab === 'expense') {
      tone = 'warn'; icon = 'alert';
      html = `بزرگ‌ترین سرفصل هزینه <b>${topCat.label}</b> با <b class="num-fa">${short(topCat.value)}</b> است — نسبت هزینه به درآمد <b class="num-fa">${fa((a.expTotal / a.revTotal * 100).toFixed(0))}٪</b>.`;
    } else { // profit
      tone = 'info'; icon = 'piggy';
      html = `سودآورترین آیتم <b>${topMarginProd.name}</b> با حاشیه سود <b class="num-fa">${fa(topMarginProd.margin.toFixed(0))}٪</b> است؛ حاشیه سود کل <b class="num-fa">${fa(a.margin.toFixed(1).replace('.', '٫'))}٪</b>.`;
    }
    return `
      <div class="fin-highlight fin-rv ${tone}">
        <span class="fin-hl-ic">${window.Icons[icon] || window.Icons.bulb}</span>
        <div class="fin-hl-text"><span class="fin-hl-label">${label}</span><p>${html}</p></div>
      </div>`;
  }

  function insightsRow() {
    return `<div class="fin-insights">${D.INSIGHTS.map(x => `
      <div class="fin-insight fin-rv">
        <div class="ic ${x.tone}">${window.Icons[x.icon] || window.Icons.bulb}</div>
        <p>${x.text}</p>
      </div>`).join('')}</div>`;
  }

  function expenseTableCard() {
    const rows = D.expenseLog.map(e => {
      const cat = D.catById(e.cat);
      const m = D.methodById(e.method);
      return `
        <tr data-id="${e.id}">
          <td style="font-weight:700">${e.desc}</td>
          <td><span class="fin-tbl-cat"><span class="swatch" style="background:${cat.color}"></span>${cat.label}</span></td>
          <td class="num-fa">${fa(e.date)}</td>
          <td>${m.label}</td>
          <td class="amt-cell num-fa">${money(e.amount)} ${T}</td>
          <td>${e.by}</td>
        </tr>`;
    }).join('');
    return `
      <div class="card fin-rv">
        <div class="card-header">
          <div><div class="fin-eyebrow">دفتر مخارج</div><h3>آخرین هزینه‌ها</h3></div>
          <button class="btn btn-primary" id="fin-add-btn">
            ${window.Icons.plus}ثبت هزینه
          </button>
        </div>
        <table class="tbl fin-tbl">
          <thead><tr><th>شرح</th><th>دسته</th><th>تاریخ</th><th>روش پرداخت</th><th>مبلغ</th><th>ثبت‌کننده</th></tr></thead>
          <tbody id="fin-exp-rows">${rows}</tbody>
        </table>
      </div>`;
  }

  /* =========================================================
     NEW purpose-built cards
     ========================================================= */

  // compact stat card used to build focused summary strips per tab
  function statCard(cfg) {
    const cls = cfg.tone ? ' ' + cfg.tone : '';
    const delta = cfg.delta != null ? `<span class="fin-mini-delta ${cfg.deltaTone || (cfg.delta >= 0 ? 'good' : 'bad')}">${(cfg.delta >= 0 ? window.Icons.arrowUpRight : window.Icons.trendDown)}${signPct(cfg.delta)}</span>` : '';
    return `
      <div class="fin-mini fin-rv${cls}">
        <div class="fin-mini-ic">${window.Icons[cfg.icon] || window.Icons.coins}</div>
        <div class="fin-mini-body">
          <div class="fin-mini-label">${cfg.label}</div>
          <div class="fin-mini-value"><span class="num-fa">${cfg.value}</span>${cfg.unit ? `<span class="fin-mini-unit">${cfg.unit}</span>` : ''}</div>
        </div>
        ${delta}
      </div>`;
  }

  // INCOME tab — channel performance as horizontal bars (drill of donut)
  function channelBarsCard(a) {
    const max = Math.max(...a.byChannel.map(c => c.value)) || 1;
    const total = a.revTotal || 1;
    const rows = a.byChannel.slice().sort((x, y) => y.value - x.value).map(c => `
      <div class="fin-list-row">
        <div class="fin-list-lead">
          <div class="fin-list-ic" style="background:${c.color}1a;color:${c.color}">${window.Icons[c.icon] || window.Icons.tag}</div>
          <div style="min-width:0">
            <div class="fin-list-name">${c.label}</div>
            <div class="fin-list-meta"><span class="num-fa">${fa(Math.round(c.value / total * 100))}٪</span> از درآمد کل</div>
            <div class="fin-list-track"><span style="width:${(c.value / max * 100).toFixed(0)}%;background:${c.color}"></span></div>
          </div>
        </div>
        <div class="fin-list-val"><div class="amt num-fa" style="color:var(--fin-income)">${short(c.value)}</div></div>
      </div>`).join('');
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">عملکرد کانال‌ها</div><h3>درآمد هر واحد</h3></div>
          <span class="fin-head-chip">${window.Icons.layers}${fa(a.byChannel.length)} واحد</span></div>
        <div class="fin-list">${rows}</div>
      </div>`;
  }

  // PROFIT tab — margin gauge + per-bucket profit mini bars
  function profitGaugeCard(a) {
    const pct = Math.max(0, Math.min(100, a.margin));
    const R = 52, C = Math.PI * R; // half ring
    const dash = (pct / 100) * C;
    const gaugeDash = reduce ? dash : 0;
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">سلامت سودآوری</div><h3>حاشیه سود</h3></div>
          <span class="fin-head-chip ${a.delta.margin >= 0 ? 'good' : 'bad'}">${a.delta.margin >= 0 ? window.Icons.arrowUpRight : window.Icons.trendDown}${fa(Math.abs(a.delta.margin).toFixed(1).replace('.', '٫'))} واحد</span></div>
        <div class="fin-gauge-wrap">
          <svg class="fin-gauge" viewBox="0 0 140 82">
            <path class="fin-gauge-track" d="M14,74 A${R},${R} 0 0 1 126,74"/>
            <path class="fin-gauge-val" d="M14,74 A${R},${R} 0 0 1 126,74"
              style="stroke-dasharray:${C.toFixed(1)};stroke-dashoffset:${(C - gaugeDash).toFixed(1)};--to:${(C - dash).toFixed(1)}"/>
          </svg>
          <div class="fin-gauge-center">
            <div class="v num-fa">${fa(a.margin.toFixed(1).replace('.', '٫'))}<i>٪</i></div>
            <div class="l">سهم سود از درآمد</div>
          </div>
        </div>
        <div class="fin-gauge-legend">
          <div><span class="k">درآمد</span><b class="num-fa" style="color:var(--fin-income)">${short(a.revTotal)}</b></div>
          <div><span class="k">هزینه</span><b class="num-fa" style="color:var(--fin-expense)">${short(a.expTotal)}</b></div>
          <div><span class="k">سود خالص</span><b class="num-fa" style="color:var(--fin-profit)">${short(a.profit)}</b></div>
        </div>
      </div>`;
  }

  // PROFIT tab — most profitable products by gross margin
  function marginLeadersCard() {
    const sorted = D.TOP_PRODUCTS.slice().sort((a, b) => b.margin - a.margin);
    const rows = sorted.map(p => `
      <div class="fin-list-row">
        <div class="fin-list-lead">
          <div class="fin-list-ic ${p.tone}">${window.Icons[p.icon] || window.Icons.tag}</div>
          <div style="min-width:0">
            <div class="fin-list-name">${p.name}</div>
            <div class="fin-list-meta">سود ناخالص <span class="num-fa">${short(p.profit)}</span></div>
            <div class="fin-list-track"><span style="width:${p.margin.toFixed(0)}%;background:var(--fin-profit)"></span></div>
          </div>
        </div>
        <div class="fin-list-val">
          <div class="amt num-fa" style="color:var(--fin-profit)">${fa(p.margin.toFixed(0))}٪</div>
          <div class="sub">حاشیه سود</div>
        </div>
      </div>`).join('');
    return `
      <div class="card fin-rv">
        <div class="card-header"><div><div class="fin-eyebrow">سودآورترین‌ها</div><h3>رتبه‌بندی بر اساس حاشیه سود</h3></div>
          <span class="fin-head-chip">${window.Icons.piggy}${fa(sorted.length)} قلم</span></div>
        <div class="fin-list">${rows}</div>
      </div>`;
  }

  /* =========================================================
     PANELS — each tab is purpose-built around its own question
     ========================================================= */
  // نمای کلی: تصویر لحظه‌ای کسب‌وکار — KPI + روند + ترکیب درآمد و هزینه + بینش
  function panelOverview(a) {
    return `
      ${keyHighlight(a)}
      ${kpiStrip(a)}
      <div class="fin-grid-2">${mainChartCard(a)}${channelsCard(a)}</div>
      <div class="fin-grid-2 even">${productsCard()}${serviceVsProductCard()}</div>
      ${insightsRow()}`;
  }
  // درآمد و فروش: از کجا درآمد می‌آید؟ — کانال‌ها، پرفروش‌ها، روش پرداخت، الگوی مراجعه
  function panelIncome(a) {
    return `
      ${keyHighlight(a)}
      <div class="fin-mini-strip">
        ${statCard({ icon: 'trendUp', tone: 'income', label: 'درآمد کل', value: faDec(a.revTotal / 1e6, 1), unit: 'م ' + T, delta: a.delta.revenue })}
        ${statCard({ icon: 'coins', tone: 'income', label: 'میانگین روزانه', value: faDec(a.revTotal / a.n / 1e6, 1), unit: 'م ' + T })}
        ${statCard({ icon: 'cart', tone: 'income', label: 'تراکنش‌های فروش', value: fa(D.TOP_PRODUCTS.reduce((s, p) => s + p.qty, 0)), unit: 'مورد' })}
        ${statCard({ icon: 'target', tone: 'income', label: 'کانال برتر', value: a.byChannel.slice().sort((x, y) => y.value - x.value)[0].label })}
      </div>
      <div class="fin-grid-2">${mainChartCard(a, { title: 'روند درآمد', eyebrow: 'تحلیل فروش' })}${channelsCard(a)}</div>
      <div class="fin-grid-2 even">${productsCard()}${serviceVsProductCard()}</div>
      <div class="fin-grid-2 even">${channelBarsCard(a)}${paymentCard()}</div>
      ${speciesCard()}
      ${heatmapCard()}`;
  }
  // هزینه‌ها: پول کجا خرج می‌شود؟ — دسته‌ها + دفتر مخارج
  function panelExpense(a) {
    const topCat = a.byCategory[0];
    return `
      ${keyHighlight(a)}
      <div class="fin-mini-strip">
        ${statCard({ icon: 'receipt', tone: 'expense', label: 'هزینه کل', value: faDec(a.expTotal / 1e6, 1), unit: 'م ' + T, delta: a.delta.expense, deltaTone: a.delta.expense > 0 ? 'bad' : 'good' })}
        ${statCard({ icon: 'coins', tone: 'expense', label: 'میانگین روزانه', value: faDec(a.expTotal / a.n / 1e6, 1), unit: 'م ' + T })}
        ${statCard({ icon: 'scale', tone: 'expense', label: 'نسبت هزینه به درآمد', value: fa((a.expTotal / a.revTotal * 100).toFixed(0)), unit: '٪' })}
        ${statCard({ icon: 'tag', tone: 'expense', label: 'بزرگ‌ترین دسته', value: topCat.label })}
      </div>
      <div class="fin-grid-2">${mainChartCard(a, { title: 'روند هزینه', eyebrow: 'کنترل مخارج' })}${categoriesCard(a)}</div>
      ${expenseTableCard()}`;
  }
  // سود و تحلیل: چقدر می‌ماند؟ — گیج حاشیه سود + روند + رهبران حاشیه سود + بینش
  function panelProfit(a) {
    return `
      ${keyHighlight(a)}
      <div class="fin-mini-strip">
        ${statCard({ icon: 'piggy', tone: 'profit', label: 'سود خالص', value: faDec(a.profit / 1e6, 1), unit: 'م ' + T, delta: a.delta.profit })}
        ${statCard({ icon: 'percent', tone: 'profit', label: 'حاشیه سود', value: fa(a.margin.toFixed(1).replace('.', '٫')), unit: '٪', delta: a.delta.margin })}
        ${statCard({ icon: 'coins', tone: 'profit', label: 'سود روزانه', value: faDec(a.avgDailyProfit / 1e6, 1), unit: 'م ' + T })}
        ${statCard({ icon: 'wallet', tone: 'profit', label: 'بازده هر تومان هزینه', value: fa((a.profit / a.expTotal).toFixed(2).replace('.', '٫')), unit: T })}
      </div>
      <div class="fin-grid-2">${mainChartCard(a, { title: 'روند سود', eyebrow: 'تحلیل سودآوری' })}${profitGaugeCard(a)}</div>
      <div class="fin-grid-2 even">${marginLeadersCard()}${categoriesCard(a)}</div>
      ${insightsRow()}`;
  }

  const PANEL_FN = { overview: panelOverview, income: panelIncome, expense: panelExpense, profit: panelProfit };

  /* =========================================================
     RENDER
     ========================================================= */
  function render() {
    S.agg = D.aggregate(S.range);
    const host = document.getElementById('fin-root');
    if (!host) return;
    host.innerHTML = `
      <div class="pagebar fin-toolbar">
        <div class="pagebar-row">
          ${renderTabs()}
          <div class="pb-tools">${renderRange()}</div>
        </div>
      </div>
      <div class="fin-panel active" id="fin-panel">${PANEL_FN[S.tab](S.agg)}</div>`;
    bindDynamic();
    revealPass();
    // Persian-digit pass on freshly injected static nodes
    host.querySelectorAll('.num-fa').forEach(el => { el.textContent = fa(el.textContent); });
  }

  // re-render only the panel body (keeps toolbar, no flash)
  function renderPanel() {
    S.agg = D.aggregate(S.range);
    const p = document.getElementById('fin-panel');
    if (!p) return render();
    p.innerHTML = PANEL_FN[S.tab](S.agg);
    // sync active tab/range chrome
    document.querySelectorAll('.fin-tab').forEach(b => {
      const on = b.dataset.tab === S.tab; b.classList.toggle('active', on); b.setAttribute('aria-selected', on);
    });
    document.querySelectorAll('.fin-range button').forEach(b => b.classList.toggle('active', b.dataset.range === S.range));
    bindDynamic();
    revealPass();
    p.querySelectorAll('.num-fa').forEach(el => { el.textContent = fa(el.textContent); });
  }

  /* ---------------- reveal-on-scroll ---------------- */
  let io = null;
  function revealPass() {
    const els = document.querySelectorAll('.fin-rv:not(.in)');
    if (reduce || !('IntersectionObserver' in window)) { els.forEach(e => e.classList.add('in')); return; }
    if (!io) io = new IntersectionObserver((ents) => {
      ents.forEach((e, i) => { if (e.isIntersecting) { setTimeout(() => e.target.classList.add('in'), (i % 6) * 60); io.unobserve(e.target); } });
    }, { threshold: 0.1 });
    els.forEach(e => io.observe(e));
  }

  /* =========================================================
     EVENT BINDING
     ========================================================= */
  function bindOnce() {
    const host = document.getElementById('fin-root');
    if (!host) return;
    // tab + range switching (delegated, survives re-render)
    host.addEventListener('click', (e) => {
      const tab = e.target.closest('.fin-tab');
      if (tab) { if (tab.dataset.tab !== S.tab) { S.tab = tab.dataset.tab; renderPanel(); window.scrollTo({ top: 0, behavior: reduce ? 'auto' : 'smooth' }); } return; }
      const rb = e.target.closest('.fin-range button');
      if (rb) { if (rb.dataset.range !== S.range) { S.range = rb.dataset.range; renderPanel(); } return; }
      // مرتب‌سازی کارت پرفروش‌ترین‌ها (پردرآمد/پرفروش/پرسود)
      const ps = e.target.closest('.fin-prod-sort button');
      if (ps) { if (ps.dataset.psort !== S.prodSort) { S.prodSort = ps.dataset.psort; renderPanel(); } return; }
    });
  }

  function bindDynamic() {
    // chart hover tooltip + crosshair
    const svg = document.getElementById('fin-main-svg');
    if (svg && S.agg) bindChartTooltip(svg, S.agg.series);
    // interactive donuts (animation + hover focus)
    bindDonuts(document.getElementById('fin-panel'));
    // add-expense button
    const addBtn = document.getElementById('fin-add-btn');
    if (addBtn) addBtn.addEventListener('click', openExpenseModal);
  }

  /* ---------------- chart tooltip ---------------- */
  let tipEl = null;
  function ensureTip() {
    if (tipEl) return tipEl;
    tipEl = document.createElement('div');
    tipEl.className = 'fin-tip';
    document.body.appendChild(tipEl);
    return tipEl;
  }
  function bindChartTooltip(svg, series) {
    const tip = ensureTip();
    svg.querySelectorAll('.fin-col-group').forEach(g => {
      g.addEventListener('mouseenter', () => g.classList.add('on'));
      g.addEventListener('mousemove', (e) => {
        const d = series[+g.dataset.i];
        if (!d) return;
        tip.innerHTML = `
          <div class="t-date">${d.full || d.label}</div>
          <div class="t-row"><span><span class="sw" style="background:var(--fin-income)"></span>درآمد</span><b class="num-fa">${short(d.revenue)}</b></div>
          <div class="t-row"><span><span class="sw" style="background:var(--fin-expense)"></span>هزینه</span><b class="num-fa">${short(d.expense)}</b></div>
          <div class="t-row t-profit"><span><span class="sw" style="background:var(--fin-profit)"></span>سود</span><b class="num-fa">${short(d.profit)}</b></div>`;
        tip.classList.add('show');
        const pad = 14;
        let x = e.clientX + pad, yy = e.clientY - pad;
        const r = tip.getBoundingClientRect();
        if (x + r.width > window.innerWidth) x = e.clientX - r.width - pad;
        if (yy + r.height > window.innerHeight) yy = window.innerHeight - r.height - pad;
        tip.style.left = x + 'px'; tip.style.top = Math.max(pad, yy) + 'px';
      });
      g.addEventListener('mouseleave', () => { g.classList.remove('on'); tip.classList.remove('show'); });
    });
  }

  /* =========================================================
     EXPENSE MODAL
     ========================================================= */
  function ensureModal() {
    if (document.getElementById('fin-modal')) return;
    const cats = D.EXPENSE_CATEGORIES.map(c => `<option value="${c.id}">${c.label}</option>`).join('');
    const methods = D.PAYMENT_METHODS.map(m => `<option value="${m.id}">${m.label}</option>`).join('');
    const quick = [1_000_000, 2_500_000, 5_000_000, 10_000_000]
      .map(v => `<button type="button" data-amt="${v}">${short(v)}</button>`).join('');
    const html = `
      <div class="modal-backdrop" id="fin-modal" style="display:none">
        <div class="modal">
          <div class="modal-header">
            <div class="icon-box">${window.Icons.receipt}</div>
            <div class="titles"><div class="eyebrow">مدیریت هزینه‌ها</div><h3>ثبت هزینه جدید</h3></div>
            <button class="icon-btn close" id="fin-modal-x" title="بستن">${window.Icons.x}</button>
          </div>
          <div class="modal-body">
            <div class="field"><label>شرح هزینه</label><input class="input" id="fx-desc" placeholder="مثلاً خرید سرم و لوازم مصرفی"/></div>
            <div class="grid-2">
              <div class="field"><label>دسته‌بندی</label><select class="selectbox" id="fx-cat">${cats}</select></div>
              <div class="field"><label>روش پرداخت</label><select class="selectbox" id="fx-method">${methods}</select></div>
            </div>
            <div class="field"><label>مبلغ (تومان)</label><input class="input num-fa" id="fx-amount" inputmode="numeric" placeholder="۰"/>
              <div class="fin-quick" id="fx-quick">${quick}</div>
            </div>
            <div class="field"><label>تاریخ</label><input class="input" id="fx-date" value="${fa(D.TODAY)}"/></div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" id="fin-modal-cancel">انصراف</button>
            <button class="btn btn-primary" id="fin-modal-save">${window.Icons.check}ثبت هزینه</button>
          </div>
        </div>
      </div>`;
    const holder = document.createElement('div');
    holder.innerHTML = html;
    document.body.appendChild(holder.firstElementChild);

    const modal = document.getElementById('fin-modal');
    const close = () => { modal.style.display = 'none'; document.body.style.overflow = ''; };
    document.getElementById('fin-modal-x').addEventListener('click', close);
    document.getElementById('fin-modal-cancel').addEventListener('click', close);
    modal.addEventListener('click', (e) => { if (e.target === modal) close(); });

    // amount: format with Persian grouped digits while typing
    const amountEl = document.getElementById('fx-amount');
    amountEl.addEventListener('input', () => {
      const raw = amountEl.value.replace(/[^\d۰-۹]/g, '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
      amountEl.dataset.raw = raw;
      amountEl.value = raw ? money(Number(raw)) : '';
      document.querySelectorAll('#fx-quick button').forEach(b => b.classList.remove('on'));
    });
    document.querySelectorAll('#fx-quick button').forEach(b => b.addEventListener('click', () => {
      const v = +b.dataset.amt;
      amountEl.dataset.raw = String(v);
      amountEl.value = money(v);
      document.querySelectorAll('#fx-quick button').forEach(x => x.classList.remove('on'));
      b.classList.add('on');
    }));

    document.getElementById('fin-modal-save').addEventListener('click', () => {
      const desc = document.getElementById('fx-desc').value.trim();
      const amount = +(amountEl.dataset.raw || 0);
      const rec = D.addExpense({
        desc: desc || 'هزینه بدون شرح',
        cat: document.getElementById('fx-cat').value,
        method: document.getElementById('fx-method').value,
        amount,
        date: D.TODAY,
      });
      close();
      // refresh the panel to reflect the new record + KPIs
      renderPanel();
      // highlight the new row if the expense tab is open
      const tr = document.querySelector(`#fin-exp-rows tr[data-id="${rec.id}"]`);
      if (tr) tr.classList.add('fin-row-new');
      toast('هزینه ثبت شد (نمونه طراحی)', 'check');
    });
  }

  function openExpenseModal() {
    ensureModal();
    const modal = document.getElementById('fin-modal');
    // reset
    document.getElementById('fx-desc').value = '';
    const amt = document.getElementById('fx-amount'); amt.value = ''; amt.dataset.raw = '';
    document.querySelectorAll('#fx-quick button').forEach(b => b.classList.remove('on'));
    modal.style.display = 'grid';
    document.body.style.overflow = 'hidden';
    setTimeout(() => document.getElementById('fx-desc').focus(), 60);
  }

  /* ---------------- toast (local fallback if none) ---------------- */
  function toast(msg, icon) {
    if (window.toast && window.toast !== toast) return window.toast(msg, icon);
    let t = document.getElementById('fin-toast');
    if (!t) { t = document.createElement('div'); t.id = 'fin-toast'; t.className = 'toast'; document.body.appendChild(t); }
    t.innerHTML = `${window.Icons[icon] || window.Icons.check}<span>${msg}</span>`;
    t.classList.add('show');
    clearTimeout(t._to);
    t._to = setTimeout(() => t.classList.remove('show'), 2600);
  }

  /* ---------------- keyboard ---------------- */
  document.addEventListener('keydown', (e) => {
    if (e.target.matches('input, textarea, select')) {
      if (e.key === 'Escape') { const m = document.getElementById('fin-modal'); if (m && m.style.display !== 'none') { m.style.display = 'none'; document.body.style.overflow = ''; } }
      return;
    }
    if (e.key === 'Escape') {
      const m = document.getElementById('fin-modal');
      if (m && m.style.display !== 'none') { m.style.display = 'none'; document.body.style.overflow = ''; }
    }
    // quick tab nav with 1..4
    if (['1', '2', '3', '4'].includes(e.key)) {
      const t = TABS[+e.key - 1]; if (t && t.id !== S.tab) { S.tab = t.id; renderPanel(); }
    }
    // N opens the expense modal
    if ((e.key === 'n' || e.key === 'N') && S.tab === 'expense') { openExpenseModal(); }
  });

  /* =========================================================
     INIT — read the page's requested default tab
     ========================================================= */
  window.initFinancePage = function (opts) {
    opts = opts || {};
    if (opts.tab && TABS.some(t => t.id === opts.tab)) S.tab = opts.tab;
    if (opts.range && D.RANGES.some(r => r.id === opts.range)) S.range = opts.range;
    render();
    bindOnce();
  };
})();
