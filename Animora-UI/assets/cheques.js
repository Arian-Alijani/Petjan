/* =========================================================
   وت‌پلاس · مدیریت چک — منطق صفحه (بدون بک‌اند، فقط طراحی)
   ========================================================= */

/* ---------- toast (اگر جای دیگری تعریف نشده باشد) ---------- */
if (!window.toast) {
  window.toast = function (msg, icon = 'check') {
    let host = document.querySelector('.toast-host');
    if (!host) { host = document.createElement('div'); host.className = 'toast-host'; document.body.appendChild(host); }
    const el = document.createElement('div');
    el.className = 'toast';
    el.innerHTML = `${window.Icons[icon] || window.Icons.check}<span>${msg}</span>`;
    host.appendChild(el);
    setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 320); }, 2600);
  };
}

/* ---------- وضعیت صفحه ---------- */
const CH = {
  tab: 'all',
  statuses: new Set(),
  q: '',
  bank: '',
  range: 'all',
  sort: { k: 'due', dir: 'asc' },
  view: 'table',
  page: 1,
  per: 8,
  sel: new Set(),
  cal: null,          // {jy, jm}
  today: null,
};

/* ---------- کمکی‌ها ---------- */
const TONE_C = {
  ok: '#027a48', info: '#1570cd', warn: '#b54708',
  danger: '#d92d20', lilac: '#7b5cc4', mute: '#7a8b89', peach: '#c06a29',
  brand: '#0d9488',
};
const digits = (s) => String(s).replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
const bankOf = (k) => window.CH_BANKS[k] || { name: '—', short: '—', c: '#7a8b89', c2: '#9aa8a5' };
const initials = (n) => String(n).trim().split(/\s+/).slice(0, 2).map(w => w[0]).join('');
const AV_COLORS = ['#8f74d1', '#e08a5c', '#4aa591', '#5c8ad6', '#d16060', '#eab749', '#b06fc4', '#027a48'];
const avColor = (s) => AV_COLORS[[...String(s)].reduce((a, c) => a + c.charCodeAt(0), 0) % AV_COLORS.length];

function daysLeft(c) { return window.J.diff(c.due, CH.today); }

/** حالت شمارش معکوس سررسید */
function cdInfo(c) {
  const d = daysLeft(c);
  if (['cleared', 'void'].includes(c.status)) return { cls: 'done', txt: c.status === 'cleared' ? 'تسویه شد' : 'باطل', icon: 'check' };
  if (c.status === 'bounced') return { cls: 'past', txt: 'برگشت خورده', icon: 'alert' };
  if (c.status === 'spent') return { cls: 'done', txt: 'واگذار شد', icon: 'share' };
  if (d < 0) return { cls: 'past', txt: `${fa(Math.abs(d))} روز گذشته`, icon: 'alert' };
  if (d === 0) return { cls: 'now', txt: 'سررسید امروز', icon: 'clock' };
  if (d <= 7) return { cls: 'soon', txt: `${fa(d)} روز مانده`, icon: 'clock' };
  return { cls: 'far', txt: `${fa(d)} روز مانده`, icon: 'calendar' };
}
const isOpen = (c) => ['inhand', 'atbank'].includes(c.status);

/* ---------- فیلتر و مرتب‌سازی ---------- */
function filtered() {
  const q = digits(CH.q.trim().toLowerCase());
  return window.CHEQUES.filter(c => {
    if (CH.tab !== 'all' && c.dir !== CH.tab) return false;
    if (CH.statuses.size && !CH.statuses.has(c.status)) return false;
    if (CH.bank && c.bank !== CH.bank) return false;
    if (CH.range !== 'all') {
      const d = daysLeft(c);
      if (CH.range === 'overdue' && !(d < 0 && isOpen(c))) return false;
      if (CH.range === 'week' && !(d >= 0 && d <= 7)) return false;
      if (CH.range === 'month' && !(d >= 0 && d <= 30)) return false;
      if (CH.range === 'quarter' && !(d >= 0 && d <= 90)) return false;
    }
    if (q) {
      const hay = [c.no, c.sayad, c.party, c.subject, c.ref, c.id, bankOf(c.bank).name, c.branch, String(c.amount)]
        .join(' ').toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  }).sort((a, b) => {
    const s = CH.sort.dir === 'asc' ? 1 : -1;
    switch (CH.sort.k) {
      case 'amount': return (a.amount - b.amount) * s;
      case 'party': return a.party.localeCompare(b.party, 'fa') * s;
      case 'status': return a.status.localeCompare(b.status) * s;
      case 'no': return (Number(a.no) - Number(b.no)) * s;
      default: return (window.J.toDay(a.due) - window.J.toDay(b.due)) * s;
    }
  });
}

const sum = (arr) => arr.reduce((t, c) => t + c.amount, 0);

/* =========================================================
   KPI
   ========================================================= */
function renderKpis() {
  const all = window.CHEQUES;
  const inOpen = all.filter(c => c.dir === 'in' && isOpen(c));
  const outOpen = all.filter(c => c.dir === 'out' && isOpen(c));
  const soon = all.filter(c => isOpen(c) && daysLeft(c) >= 0 && daysLeft(c) <= 7);
  const bounced = all.filter(c => c.status === 'bounced');
  const cleared = all.filter(c => c.status === 'cleared');
  const overdue = all.filter(c => isOpen(c) && daysLeft(c) < 0);

  const items = [
    {
      k: '#027a48', icon: 'arrowDownLeft', label: 'چک‌های دریافتی در جریان',
      cnt: inOpen.length, val: moneyShort(sum(inOpen)),
      foot: `<span class="up">${fa('+12')}٪</span> نسبت به ماه گذشته`,
      spark: [12, 18, 15, 22, 19, 26, 24, 31],
      filter: () => { CH.tab = 'in'; CH.statuses = new Set(['inhand', 'atbank']); },
    },
    {
      k: '#d98032', icon: 'arrowUpRight', label: 'چک‌های پرداختی در جریان',
      cnt: outOpen.length, val: moneyShort(sum(outOpen)),
      foot: `تعهد پیش‌رو تا ${window.J.medium(window.J.fromDay(window.J.toDay(CH.today) + 60))}`,
      spark: [22, 19, 24, 20, 26, 23, 28, 25],
      filter: () => { CH.tab = 'out'; CH.statuses = new Set(['inhand', 'atbank']); },
    },
    {
      k: '#b54708', icon: 'hourglass', label: 'سررسید ۷ روز آینده',
      cnt: soon.length, val: moneyShort(sum(soon)),
      foot: overdue.length
        ? `<span class="dn">${fa(overdue.length)} فقره</span> سررسید گذشته`
        : 'بدون سررسید عقب‌افتاده',
      spark: [8, 12, 10, 16, 14, 20, 18, 22],
      filter: () => { CH.tab = 'all'; CH.statuses = new Set(); CH.range = 'week'; },
    },
    {
      k: '#d92d20', icon: 'alert', label: 'چک‌های برگشتی',
      cnt: bounced.length, val: moneyShort(sum(bounced)),
      foot: `نرخ وصول <span class="up">${fa(Math.round(cleared.length / (cleared.length + bounced.length) * 100))}٪</span>`,
      spark: [6, 5, 7, 4, 6, 3, 5, 2],
      filter: () => { CH.tab = 'all'; CH.statuses = new Set(['bounced']); },
    },
  ];

  document.getElementById('chKpis').innerHTML = items.map((it, i) => `
    <div class="ch-kpi rv" style="--k:${it.k}" data-kpi="${i}" role="button" tabindex="0">
      <div class="ch-kpi-top">
        <div class="ch-kpi-ic">${Icons[it.icon]}</div>
        <div>
          <div class="ch-kpi-lbl">${it.label}</div>
        </div>
        <div class="ch-kpi-cnt">${fa(it.cnt)} فقره</div>
      </div>
      <div class="ch-kpi-val">${it.val}<span class="cur">تومان</span></div>
      <div class="ch-kpi-foot">${it.foot}</div>
    </div>
  `).join('');

  document.querySelectorAll('[data-kpi]').forEach(el => {
    el.addEventListener('click', () => {
      CH.range = 'all';
      items[+el.dataset.kpi].filter();
      CH.page = 1; CH.sel.clear();
      syncControls(); renderAll();
      document.getElementById('chMain').scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  });
}

function sparkSvg(vals, color) {
  const W = 240, H = 40, max = Math.max(...vals), min = Math.min(...vals);
  const pts = vals.map((v, i) => [i * (W / (vals.length - 1)), H - 4 - ((v - min) / ((max - min) || 1)) * (H - 12)]);
  const d = pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join(' ');
  const id = 'sg' + Math.random().toString(36).slice(2, 7);
  return `<svg class="spark" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none">
    <defs><linearGradient id="${id}" x1="0" x2="0" y1="0" y2="1">
      <stop offset="0%" stop-color="${color}" stop-opacity=".22"/>
      <stop offset="100%" stop-color="${color}" stop-opacity="0"/>
    </linearGradient></defs>
    <path d="${d} L${W},${H} L0,${H} Z" fill="url(#${id})"/>
    <path d="${d}" fill="none" stroke="${color}" stroke-width="1.6" stroke-linecap="round" opacity=".7"/>
  </svg>`;
}

/* =========================================================
   نوار ابزار و چیپ‌ها
   ========================================================= */
function renderChips() {
  const base = window.CHEQUES.filter(c => CH.tab === 'all' || c.dir === CH.tab);
  const host = document.getElementById('chChips');
  host.innerHTML = Object.entries(window.CH_STATUS).map(([k, s]) => {
    const n = base.filter(c => c.status === k).length;
    return `<button class="pb-chip ch-chip ${CH.statuses.has(k) ? 'on' : ''}" style="--c:${TONE_C[s.tone]}" data-st="${k}" title="${s.desc}">
      <span class="dot"></span>${s.label}<span class="cnt">${fa(n)}</span>
    </button>`;
  }).join('') + (CH.statuses.size || CH.bank || CH.q || CH.range !== 'all'
      ? `<button class="pb-chip ch-chip ch-reset" id="chReset">${Icons.x}پاک کردن فیلترها</button>` : '');

  host.querySelectorAll('[data-st]').forEach(b => b.addEventListener('click', () => {
    const k = b.dataset.st;
    CH.statuses.has(k) ? CH.statuses.delete(k) : CH.statuses.add(k);
    CH.page = 1; renderAll();
  }));
  const rs = document.getElementById('chReset');
  if (rs) rs.addEventListener('click', () => {
    CH.statuses.clear(); CH.bank = ''; CH.q = ''; CH.range = 'all'; CH.page = 1;
    syncControls(); renderAll();
  });
}

function syncControls() {
  document.querySelectorAll('#chTabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === CH.tab));
  document.querySelectorAll('#chViews button').forEach(b => b.classList.toggle('on', b.dataset.view === CH.view));
  const s = document.getElementById('chQ');
  if (s) { s.value = CH.q; s.parentElement.classList.toggle('has-value', !!CH.q); }
  const bs = document.getElementById('chBank'); if (bs) bs.value = CH.bank;
  const rs = document.getElementById('chRange'); if (rs) rs.value = CH.range;
}

/* =========================================================
   نمای جدول
   ========================================================= */
function bankMark(k, size) {
  const b = bankOf(k);
  return `<span class="bank-mark" style="background:linear-gradient(135deg,${b.c},${b.c2});${size ? `width:${size}px;height:${size}px` : ''}">${b.short.slice(0, 3)}</span>`;
}

function statusPill(st) {
  const s = window.CH_STATUS[st];
  return `<span class="st ${s.tone}">${Icons[s.icon]}${s.label}</span>`;
}

function renderTable(list) {
  const start = (CH.page - 1) * CH.per;
  const rows = list.slice(start, start + CH.per);
  const th = (k, label, cls = '') => `<th class="sortable ${CH.sort.k === k ? 'act' : ''} ${cls}" data-sort="${k}">
      ${label}<span class="sarr">${CH.sort.k === k ? (CH.sort.dir === 'asc' ? '▲' : '▼') : '▲'}</span></th>`;

  return `
  <div class="ch-card">
    <div class="ch-card-head">
      <h3>فهرست چک‌ها</h3>
      <span class="sub">${fa(list.length)} فقره · مجموع ${moneyShort(sum(list))} تومان</span>
      <span class="sp"></span>
      <button class="ch-mini" id="chPrint" title="چاپ فهرست">${Icons.printer}</button>
      <button class="ch-mini" id="chRefresh" title="بازخوانی">${Icons.refresh}</button>
    </div>
    ${!list.length ? emptyState() : `
    <div style="overflow-x:auto">
    <table class="ch-tbl">
      <thead><tr>
        <th class="col-chk"><input type="checkbox" class="ch-cbx" id="chAll"/></th>
        ${th('no', 'شماره چک')}
        ${th('party', 'طرف حساب')}
        <th class="hide-sm">بانک / شعبه</th>
        ${th('amount', 'مبلغ')}
        ${th('due', 'سررسید')}
        ${th('status', 'وضعیت')}
        <th class="col-act"></th>
      </tr></thead>
      <tbody>
        ${rows.map(c => {
          const cd = cdInfo(c);
          const d = window.CH_DIR[c.dir];
          return `<tr data-id="${c.id}" class="${CH.sel.has(c.id) ? 'sel' : ''}">
            <td class="col-chk"><input type="checkbox" class="ch-cbx" data-pick="${c.id}" ${CH.sel.has(c.id) ? 'checked' : ''}/></td>
            <td>
              <div class="ch-no">
                <span class="n">${fa(c.no)}</span>
                <span class="s">${fa(c.sayad.slice(0, 9))}…</span>
              </div>
            </td>
            <td>
              <div class="ch-party">
                <span class="av" style="background:linear-gradient(135deg,${avColor(c.party)},${avColor(c.party + 'x')})">${initials(c.party)}</span>
                <span style="min-width:0">
                  <span class="nm" title="${c.party}">${c.party}</span>
                  <span class="rl">${c.role} · <span class="dir-tag ${c.dir}">${Icons[d.icon]}${d.label}</span></span>
                </span>
              </div>
            </td>
            <td class="hide-sm">
              <span class="bank-chip">${bankMark(c.bank)}
                <span><span class="bn">${bankOf(c.bank).name}</span><br/><span class="br">${c.branch}</span></span>
              </span>
            </td>
            <td><span class="ch-amt ${c.dir}">${money(c.amount)}<span class="u">تومان</span></span></td>
            <td>
              <div class="due-cell">
                <span class="d">${fa(c.due)}</span>
                <span class="cd ${cd.cls}">${Icons[cd.icon]}${cd.txt}</span>
              </div>
            </td>
            <td>${statusPill(c.status)}</td>
            <td class="col-act"><button class="ch-mini" data-menu="${c.id}">${Icons.more}</button></td>
          </tr>`;
        }).join('')}
      </tbody>
    </table>
    </div>
    ${pager(list.length)}`}
  </div>`;
}

function pager(total) {
  const pages = Math.max(1, Math.ceil(total / CH.per));
  if (CH.page > pages) CH.page = pages;
  const btns = [];
  for (let i = 1; i <= pages; i++) {
    if (pages > 7 && i > 2 && i < pages - 1 && Math.abs(i - CH.page) > 1) {
      if (btns[btns.length - 1] !== '…') btns.push('…');
      continue;
    }
    btns.push(i);
  }
  return `<div class="ch-pager">
    نمایش ${fa((CH.page - 1) * CH.per + 1)}–${fa(Math.min(CH.page * CH.per, total))} از ${fa(total)} فقره
    <span class="pages">
      <button data-pg="${CH.page - 1}" ${CH.page === 1 ? 'disabled' : ''}>${Icons.chevronRight}</button>
      ${btns.map(b => b === '…' ? '<button disabled>…</button>'
        : `<button data-pg="${b}" class="${b === CH.page ? 'on' : ''}">${fa(b)}</button>`).join('')}
      <button data-pg="${CH.page + 1}" ${CH.page === pages ? 'disabled' : ''}>${Icons.chevronLeft}</button>
    </span>
  </div>`;
}

function emptyState() {
  return `<div class="ch-empty">
    <div class="il">${Icons.cheque}</div>
    <h4>چکی با این فیلترها پیدا نشد</h4>
    <p>عبارت جستجو یا فیلترهای وضعیت را تغییر دهید.</p>
    <button class="btn btn-outline" onclick="chClearFilters()">${Icons.refresh} پاک کردن فیلترها</button>
  </div>`;
}
window.chClearFilters = function () {
  CH.statuses.clear(); CH.bank = ''; CH.q = ''; CH.range = 'all'; CH.tab = 'all'; CH.page = 1;
  syncControls(); renderAll();
};

/* =========================================================
   نمای کارتی (برگ چک)
   ========================================================= */
function renderGrid(list) {
  const start = (CH.page - 1) * 9;
  const rows = list.slice(start, start + 9);
  if (!list.length) return `<div class="ch-card">${emptyState()}</div>`;
  return `
  <div class="ch-grid">
    ${rows.map(c => {
      const b = bankOf(c.bank), cd = cdInfo(c), d = window.CH_DIR[c.dir];
      return `<div class="leaf rv" style="--b1:${b.c};--b2:${b.c2}" data-id="${c.id}">
        <div class="leaf-top">
          <span class="bm">${b.short.slice(0, 3)}</span>
          <span><span class="bt">${b.name}</span><br/><span class="bs">${c.branch}</span></span>
          <span class="dir">${d.label}</span>
        </div>
        <div class="leaf-mid">
          <div class="leaf-amt">${money(c.amount)}<span class="u">تومان</span></div>
          <div class="leaf-words">${numToWords(c.amount)} تومان</div>
          <div class="leaf-rows">
            <div><div class="k">در وجه / از</div><div class="v">${c.party}</div></div>
            <div><div class="k">تاریخ سررسید</div><div class="v">${fa(c.due)}</div></div>
          </div>
        </div>
        <div class="leaf-perf"></div>
        <div class="leaf-foot">
          <span class="num">${fa(c.no)}</span>
          <span class="sp"></span>
          <span class="cd ${cd.cls}">${Icons[cd.icon]}${cd.txt}</span>
          ${statusPill(c.status)}
        </div>
      </div>`;
    }).join('')}
  </div>
  <div class="ch-card" style="margin-top:14px">${pager(list.length).replace('ch-pager', 'ch-pager')}</div>`;
}

/* =========================================================
   تقویم سررسید
   ========================================================= */
function renderCalendar(list) {
  const { jy, jm } = CH.cal;
  const len = window.J.monthLen(jy, jm);
  const first = `${jy}/${String(jm).padStart(2, '0')}/01`;
  const startDow = window.J.weekday(first);
  const prevLen = window.J.monthLen(jm === 1 ? jy - 1 : jy, jm === 1 ? 12 : jm - 1);
  const byDay = {};
  list.forEach(c => {
    const p = window.J.parse(c.due);
    if (p.jy === jy && p.jm === jm) (byDay[p.jd] = byDay[p.jd] || []).push(c);
  });
  const monthTotal = sum(list.filter(c => { const p = window.J.parse(c.due); return p.jy === jy && p.jm === jm; }));

  const cells = [];
  for (let i = 0; i < startDow; i++) cells.push({ off: true, d: prevLen - startDow + i + 1 });
  for (let d = 1; d <= len; d++) cells.push({ d });
  while (cells.length % 7) cells.push({ off: true, d: cells.length - len - startDow + 1 });

  const tp = window.J.parse(CH.today);
  const isThis = tp.jy === jy && tp.jm === jm;

  return `<div class="ch-card">
    <div class="cal-top">
      <div>
        <h3>${window.J.MONTHS[jm - 1]} ${fa(jy)}</h3>
        <span class="sub" style="font-size:11px;color:var(--muted)">مجموع سررسیدهای این ماه: ${moneyShort(monthTotal)} تومان</span>
      </div>
      <div class="cal-legend" style="margin-inline-start:24px">
        <span><i style="background:#027a48"></i>دریافتی</span>
        <span><i style="background:#e08a5c"></i>پرداختی</span>
      </div>
      <div class="nav">
        <button class="ch-mini" data-cal="-1" title="ماه قبل">${Icons.chevronRight}</button>
        <button class="ch-mini" data-cal="0" title="ماه جاری">${Icons.home}</button>
        <button class="ch-mini" data-cal="1" title="ماه بعد">${Icons.chevronLeft}</button>
      </div>
    </div>
    <div class="cal-grid">
      ${window.J.DAYS.map(d => `<div class="cal-dow">${d}</div>`).join('')}
      ${cells.map((c, i) => {
        const evs = c.off ? [] : (byDay[c.d] || []);
        const dayTotal = sum(evs);
        return `<div class="cal-cell ${c.off ? 'off' : ''} ${(!c.off && isThis && c.d === tp.jd) ? 'today' : ''} ${i % 7 === 6 ? 'fri' : ''}">
          <div class="cal-d">${fa(c.d)}</div>
          ${dayTotal ? `<div class="cal-sum">${moneyShort(dayTotal)}</div>` : ''}
          ${evs.slice(0, 2).map(e => {
            const t = e.dir === 'in'
              ? { ec: '#027a48', eb: '#e6f2ec', et: '#1d6b4d' }
              : { ec: '#e08a5c', eb: '#fbeade', et: '#a95a22' };
            return `<div class="cal-ev" data-id="${e.id}" style="--ec:${t.ec};--eb:${t.eb};--et:${t.et}" title="${e.party} — ${money(e.amount)} تومان">
              <span class="m">${moneyShort(e.amount)}</span>
            </div>`;
          }).join('')}
          ${evs.length > 2 ? `<div class="cal-more" data-day="${c.d}">+${fa(evs.length - 2)} مورد دیگر</div>` : ''}
        </div>`;
      }).join('')}
    </div>
  </div>`;
}

/* =========================================================
   نوار زمانی سررسید
   ========================================================= */
function renderTimeline(list) {
  const buckets = [
    { key: 'over', t: 'سررسید گذشته', tone: 'danger', f: c => isOpen(c) && daysLeft(c) < 0 },
    { key: 'today', t: 'امروز', tone: 'danger', f: c => daysLeft(c) === 0 },
    { key: 'week', t: 'تا ۷ روز آینده', tone: 'warn', f: c => daysLeft(c) > 0 && daysLeft(c) <= 7 },
    { key: 'm', t: '۸ تا ۳۰ روز آینده', tone: '', f: c => daysLeft(c) > 7 && daysLeft(c) <= 30 },
    { key: 'q', t: 'بیش از ۳۰ روز آینده', tone: '', f: c => daysLeft(c) > 30 },
    { key: 'done', t: 'بسته‌شده (وصول/برگشت/ابطال)', tone: '', f: c => !isOpen(c) && daysLeft(c) <= 0 },
  ];
  const groups = buckets.map(b => ({ ...b, items: list.filter(b.f) })).filter(g => g.items.length);
  if (!groups.length) return `<div class="ch-card">${emptyState()}</div>`;

  return `<div class="ch-card"><div class="tl">
    ${groups.map(g => `
      <div class="tl-group ${g.tone}">
        <span class="tl-dot"></span>
        <div class="tl-title">${g.t}<span class="cnt">${fa(g.items.length)}</span>
          <span class="sum">${moneyShort(sum(g.items))} تومان</span></div>
        <div class="tl-items">
          ${g.items.map(c => {
            const cd = cdInfo(c), d = window.CH_DIR[c.dir];
            return `<div class="tl-item" data-id="${c.id}">
              ${bankMark(c.bank)}
              <span style="min-width:0">
                <span class="nm">${c.party}</span>
                <span class="sb">${fa(c.due)} · ${fa(c.no)} · ${bankOf(c.bank).name}</span>
              </span>
              <span class="sp"></span>
              <span class="dir-tag ${c.dir}">${Icons[d.icon]}${d.label}</span>
              <span class="ch-amt ${c.dir}">${money(c.amount)}<span class="u">ت</span></span>
              <span class="cd ${cd.cls}">${Icons[cd.icon]}${cd.txt}</span>
            </div>`;
          }).join('')}
        </div>
      </div>`).join('')}
  </div></div>`;
}

/* =========================================================
   ستون کناری
   ========================================================= */
function renderSide(list) {
  document.getElementById('chSide').innerHTML = [
    cardCashflow(), cardBanks(list), cardAlerts(),
  ].join('');
  bindSide();
}

/* جریان نقدی ۸ هفته آینده */
function cardCashflow() {
  const t0 = window.J.toDay(CH.today);
  const weeks = Array.from({ length: 8 }, (_, i) => {
    const a = t0 + i * 7, b = a + 6;
    const inW = window.CHEQUES.filter(c => isOpen(c) && window.J.toDay(c.due) >= a && window.J.toDay(c.due) <= b);
    return {
      i: sum(inW.filter(c => c.dir === 'in')),
      o: sum(inW.filter(c => c.dir === 'out')),
      lbl: window.J.medium(window.J.fromDay(a)),
    };
  });
  const max = Math.max(1, ...weeks.map(w => Math.max(w.i, w.o)));
  const netIn = sum(weeks.map(w => ({ amount: w.i }))), netOut = sum(weeks.map(w => ({ amount: w.o })));
  const net = netIn - netOut;

  return `<div class="side-card rv">
    <div class="hd">
      <div><h3>جریان نقدی چک‌ها</h3><div class="sub">۸ هفته آینده</div></div>
      <span class="sp"></span>
      <span class="st ${net >= 0 ? 'ok' : 'danger'}">${net >= 0 ? Icons.trendUp : Icons.alert} ${net >= 0 ? '+' : '−'}${moneyShort(Math.abs(net))}</span>
    </div>
    <div class="bd">
      <div class="flow">
        ${weeks.map(w => `<div class="flow-col">
          <div class="flow-tip">${w.lbl}<br/>دریافتی: ${moneyShort(w.i)}<br/>پرداختی: ${moneyShort(w.o)}</div>
          <div class="bar i" style="height:${(w.i / max * 46).toFixed(1)}%"></div>
          <div class="bar o" style="height:${(w.o / max * 46).toFixed(1)}%"></div>
        </div>`).join('')}
      </div>
      <div class="flow-lbl">${weeks.map((w, i) => `<span>${i % 2 === 0 ? w.lbl.split(' ')[0] : ''}</span>`).join('')}</div>
      <div style="display:flex;gap:14px;font-size:11px;color:var(--muted);margin-top:10px">
        <span><i style="width:9px;height:9px;border-radius:3px;background:#0d9488;display:inline-block;margin-inline-end:5px"></i>دریافتی ${moneyShort(netIn)}</span>
        <span><i style="width:9px;height:9px;border-radius:3px;background:#de8f52;display:inline-block;margin-inline-end:5px"></i>پرداختی ${moneyShort(netOut)}</span>
      </div>
    </div>
  </div>`;
}

/* توزیع بانکی */
function cardBanks(list) {
  const map = {};
  list.forEach(c => { map[c.bank] = (map[c.bank] || 0) + c.amount; });
  const rows = Object.entries(map).sort((a, b) => b[1] - a[1]).slice(0, 5);
  const max = rows.length ? rows[0][1] : 1;
  return `<div class="side-card rv">
    <div class="hd"><div><h3>توزیع بانکی</h3><div class="sub">۵ بانک برتر بر اساس مبلغ</div></div></div>
    <div class="bd">
      ${rows.length ? rows.map(([k, v]) => {
        const b = bankOf(k);
        return `<div class="bank-row" role="button" data-bankf="${k}" style="cursor:pointer">
          ${bankMark(k)}
          <div>
            <div class="bl">${b.name}<span class="c">${fa(list.filter(c => c.bank === k).length)} فقره</span></div>
            <div class="track"><span style="width:${(v / max * 100).toFixed(0)}%;background:linear-gradient(90deg,${b.c2},${b.c})"></span></div>
          </div>
          <div class="bv">${moneyShort(v)}</div>
        </div>`;
      }).join('') : '<div style="color:var(--muted);font-size:12px;padding:8px 0">موردی برای نمایش نیست.</div>'}
    </div>
  </div>`;
}

/* هشدارها */
function cardAlerts() {
  const t = window.CHEQUES;
  const overdue = t.filter(c => isOpen(c) && daysLeft(c) < 0);
  const today = t.filter(c => isOpen(c) && daysLeft(c) === 0);
  const soonOut = t.filter(c => c.dir === 'out' && isOpen(c) && daysLeft(c) > 0 && daysLeft(c) <= 5);
  const bounced = t.filter(c => c.status === 'bounced');

  const items = [];
  if (today.length) items.push({ tone: 'danger', icon: 'clock', t: `${fa(today.length)} چک امروز سررسید می‌شود`, d: `مجموع ${moneyShort(sum(today))} تومان`, act: () => { chClearFilters(); CH.range = 'week'; } });
  if (overdue.length) items.push({ tone: 'danger', icon: 'alert', t: `${fa(overdue.length)} چک سررسید گذشته دارد`, d: 'نیازمند پیگیری فوری', act: () => { chClearFilters(); CH.range = 'overdue'; } });
  if (soonOut.length) items.push({ tone: 'warn', icon: 'wallet', t: `تأمین وجه ${fa(soonOut.length)} چک پرداختی`, d: `تا ۵ روز آینده · ${moneyShort(sum(soonOut))} تومان`, act: () => { chClearFilters(); CH.tab = 'out'; CH.range = 'week'; } });
  if (bounced.length) items.push({ tone: 'danger', icon: 'shield', t: `${fa(bounced.length)} چک برگشتی باز`, d: 'ثبت پیگیری حقوقی توصیه می‌شود', act: () => { chClearFilters(); CH.statuses = new Set(['bounced']); } });
  items.push({ tone: 'ok', icon: 'msg', t: 'ارسال پیامک یادآوری سررسید', d: 'برای مالکین چک‌های هفته آینده', act: () => toast('پیامک یادآوری برای ۶ نفر در صف ارسال قرار گرفت', 'msg') });

  window.__chAlerts = items;
  return `<div class="side-card rv">
    <div class="hd"><div><h3>هشدارها و اقدام‌ها</h3><div class="sub">هوشمند بر اساس سررسید</div></div></div>
    <div class="bd" style="padding:2px 10px 12px">
      ${items.map((a, i) => `<div class="alert-item" data-alert="${i}">
        <span class="ic" style="background:${TONE_C[a.tone]}1f;color:${TONE_C[a.tone]}">${Icons[a.icon]}</span>
        <span><span class="t">${a.t}</span><span class="d">${a.d}</span></span>
      </div>`).join('')}
    </div>
  </div>`;
}

function bindSide() {
  document.querySelectorAll('[data-alert]').forEach(el => el.addEventListener('click', () => {
    const a = window.__chAlerts[+el.dataset.alert];
    a.act(); CH.page = 1; syncControls(); renderAll();
  }));
  document.querySelectorAll('[data-bankf]').forEach(el => el.addEventListener('click', () => {
    CH.bank = CH.bank === el.dataset.bankf ? '' : el.dataset.bankf;
    CH.page = 1; syncControls(); renderAll();
  }));
}

/* =========================================================
   نوار عملیات گروهی
   ========================================================= */
function renderBulk() {
  const host = document.getElementById('chBulk');
  if (!CH.sel.size) { host.innerHTML = ''; return; }
  const picked = window.CHEQUES.filter(c => CH.sel.has(c.id));
  host.innerHTML = `<div class="ch-bulk">
    <span class="cnt">${fa(picked.length)} چک انتخاب شده</span>
    <span class="sum">مجموع ${money(sum(picked))} تومان</span>
    <span class="sp"></span>
    <button data-bulk="bank">${Icons.bank} واگذاری به بانک</button>
    <button data-bulk="clear">${Icons.check} ثبت وصول</button>
    <button data-bulk="sms">${Icons.msg} یادآوری پیامکی</button>
    <button data-bulk="print">${Icons.printer} چاپ</button>
    <button class="x" data-bulk="none">${Icons.x}</button>
  </div>`;
  host.querySelectorAll('[data-bulk]').forEach(b => b.addEventListener('click', () => {
    const a = b.dataset.bulk;
    if (a === 'none') { CH.sel.clear(); renderAll(); return; }
    const msg = {
      bank: 'دسته چک انتخابی به بانک واگذار شد (نمونه طراحی)',
      clear: 'وصول چک‌های انتخابی ثبت شد (نمونه طراحی)',
      sms: 'پیامک یادآوری برای طرف‌حساب‌ها ارسال شد (نمونه طراحی)',
      print: 'فایل چاپ فهرست آماده شد (نمونه طراحی)',
    }[a];
    toast(msg, a === 'print' ? 'printer' : a === 'sms' ? 'msg' : 'check');
    CH.sel.clear(); renderAll();
  }));
}

/* =========================================================
   رندر کلی + اتصال رویدادها
   ========================================================= */
function renderAll() {
  const list = filtered();
  renderChips();
  renderBulk();
  const main = document.getElementById('chMain');
  main.innerHTML =
    CH.view === 'table' ? renderTable(list) :
    CH.view === 'grid' ? renderGrid(list) :
    CH.view === 'calendar' ? renderCalendar(list) :
    renderTimeline(list);
  renderSide(list);
  bindMain();
  mountReveal();
  syncControls();
}

function bindMain() {
  const main = document.getElementById('chMain');

  main.querySelectorAll('[data-sort]').forEach(th => th.addEventListener('click', () => {
    const k = th.dataset.sort;
    if (CH.sort.k === k) CH.sort.dir = CH.sort.dir === 'asc' ? 'desc' : 'asc';
    else CH.sort = { k, dir: k === 'amount' ? 'desc' : 'asc' };
    renderAll();
  }));

  main.querySelectorAll('[data-pg]').forEach(b => b.addEventListener('click', () => {
    CH.page = +b.dataset.pg; renderAll();
    document.getElementById('chMain').scrollIntoView({ behavior: 'smooth', block: 'start' });
  }));

  const all = document.getElementById('chAll');
  if (all) all.addEventListener('change', () => {
    const list = filtered().slice((CH.page - 1) * CH.per, CH.page * CH.per);
    list.forEach(c => all.checked ? CH.sel.add(c.id) : CH.sel.delete(c.id));
    renderAll();
  });

  main.querySelectorAll('[data-pick]').forEach(cb => cb.addEventListener('click', (e) => {
    e.stopPropagation();
    const id = cb.dataset.pick;
    cb.checked ? CH.sel.add(id) : CH.sel.delete(id);
    renderBulk();
    cb.closest('tr').classList.toggle('sel', cb.checked);
  }));

  main.querySelectorAll('tbody tr[data-id]').forEach(tr => tr.addEventListener('click', (e) => {
    if (e.target.closest('[data-menu]') || e.target.closest('.ch-cbx')) return;
    openCheque(tr.dataset.id);
  }));
  main.querySelectorAll('.leaf[data-id], .tl-item[data-id], .cal-ev[data-id]').forEach(el =>
    el.addEventListener('click', () => openCheque(el.dataset.id)));

  main.querySelectorAll('[data-menu]').forEach(b => b.addEventListener('click', (e) => {
    e.stopPropagation(); openRowMenu(b, b.dataset.menu);
  }));

  main.querySelectorAll('[data-cal]').forEach(b => b.addEventListener('click', () => {
    const s = +b.dataset.cal;
    if (s === 0) { const t = window.J.parse(CH.today); CH.cal = { jy: t.jy, jm: t.jm }; }
    else {
      let { jy, jm } = CH.cal; jm += s;
      if (jm > 12) { jm = 1; jy++; } if (jm < 1) { jm = 12; jy--; }
      CH.cal = { jy, jm };
    }
    renderAll();
  }));

  main.querySelectorAll('[data-day]').forEach(el => el.addEventListener('click', () => {
    CH.view = 'timeline'; CH.range = 'all'; renderAll();
    toast('نمای نوار زمانی برای مشاهده همه سررسیدها فعال شد', 'timeline');
  }));

  const pr = document.getElementById('chPrint');
  if (pr) pr.addEventListener('click', () => window.print());
  const rf = document.getElementById('chRefresh');
  if (rf) rf.addEventListener('click', () => { toast('اطلاعات بروزرسانی شد', 'refresh'); renderAll(); });
}

/* منوی سطر */
function openRowMenu(btn, id) {
  closeRowMenu();
  const c = window.CHEQUES.find(x => x.id === id);
  const m = document.createElement('div');
  m.className = 'ch-menu';
  m.id = 'chRowMenu';
  m.innerHTML = `
    <div class="mtitle">چک ${fa(c.no)}</div>
    <button data-a="open">${Icons.file} مشاهده جزئیات</button>
    <button data-a="edit">${Icons.edit} ویرایش اطلاعات</button>
    <button data-a="copy">${Icons.copy} کپی شناسه صیادی</button>
    <div class="sepline"></div>
    ${isOpen(c) ? `
      <button data-a="bank">${Icons.bank} واگذاری به بانک</button>
      <button data-a="cleared">${Icons.check} ثبت وصول</button>
      <button data-a="bounced">${Icons.alert} ثبت برگشت</button>
      ${c.dir === 'in' ? `<button data-a="spent">${Icons.share} خرج کردن چک</button>` : ''}
      <div class="sepline"></div>` : ''}
    <button data-a="print">${Icons.printer} چاپ رسید</button>
    <button data-a="sms">${Icons.msg} یادآوری به ${c.party}</button>
    <div class="sepline"></div>
    <button class="danger" data-a="del">${Icons.trash} حذف چک</button>
  `;
  document.body.appendChild(m);
  const r = btn.getBoundingClientRect();
  const w = 200, h = m.offsetHeight;
  m.style.top = Math.min(r.bottom + 6, window.innerHeight - h - 12) + 'px';
  m.style.left = Math.max(12, r.left - w + r.width) + 'px';

  m.querySelectorAll('[data-a]').forEach(b => b.addEventListener('click', () => {
    const a = b.dataset.a; closeRowMenu();
    if (a === 'open') return openCheque(id);
    if (a === 'edit') return openChequeModal(c);
    if (a === 'copy') { navigator.clipboard?.writeText(c.sayad).catch(() => {}); return toast('شناسه صیادی کپی شد', 'copy'); }
    if (['bank', 'cleared', 'bounced', 'spent'].includes(a)) return setStatus(id, a === 'bank' ? 'atbank' : a);
    if (a === 'print') { toast('رسید چک برای چاپ آماده شد (نمونه طراحی)', 'printer'); return; }
    if (a === 'sms') return toast(`پیامک یادآوری برای ${c.party} ارسال شد`, 'msg');
    if (a === 'del') {
      const i = window.CHEQUES.findIndex(x => x.id === id);
      window.CHEQUES.splice(i, 1); CH.sel.delete(id);
      toast('چک حذف شد (نمونه طراحی)', 'trash'); renderKpis(); renderAll();
    }
  }));
  setTimeout(() => document.addEventListener('click', closeRowMenu, { once: true }), 0);
}
function closeRowMenu() { const m = document.getElementById('chRowMenu'); if (m) m.remove(); }

window.setStatus = function (id, st) {
  const c = window.CHEQUES.find(x => x.id === id);
  if (!c) return;
  c.status = st;
  const label = window.CH_STATUS[st].label;
  toast(`وضعیت چک ${fa(c.no)} به «${label}» تغییر کرد`, window.CH_STATUS[st].icon);
  renderKpis(); renderAll();
  if (document.querySelector('.ch-drawer-root.open')) openCheque(id, true);
};

/* =========================================================
   راه‌اندازی
   ========================================================= */
window.initChequesPage = function () {
  CH.today = window.J.today();
  const t = window.J.parse(CH.today);
  CH.cal = { jy: t.jy, jm: t.jm };

  /* بانک‌ها در فیلتر */
  const bs = document.getElementById('chBank');
  bs.innerHTML = '<option value="">همه بانک‌ها</option>' +
    Object.entries(window.CH_BANKS).map(([k, b]) => `<option value="${k}">${b.name}</option>`).join('');

  /* شمارنده تب‌ها */
  document.querySelectorAll('#chTabs button').forEach(b => {
    const k = b.dataset.tab;
    const n = window.CHEQUES.filter(c => k === 'all' || c.dir === k).length;
    b.querySelector('.seg-n').textContent = fa(n);
    b.addEventListener('click', () => { CH.tab = k; CH.page = 1; CH.sel.clear(); renderKpis(); renderAll(); });
  });

  document.querySelectorAll('#chViews button').forEach(b => b.addEventListener('click', () => {
    CH.view = b.dataset.view; CH.page = 1; renderAll();
  }));

  const q = document.getElementById('chQ');
  let tmr;
  q.addEventListener('input', () => {
    q.parentElement.classList.toggle('has-value', !!q.value);
    clearTimeout(tmr);
    tmr = setTimeout(() => { CH.q = q.value; CH.page = 1; renderAll(); }, 180);
  });
  document.getElementById('chQClear').addEventListener('click', () => { CH.q = ''; q.value = ''; renderAll(); q.focus(); });

  bs.addEventListener('change', () => { CH.bank = bs.value; CH.page = 1; renderAll(); });
  const rs = document.getElementById('chRange');
  rs.addEventListener('change', () => { CH.range = rs.value; CH.page = 1; renderAll(); });

  document.getElementById('chNew').addEventListener('click', () => openChequeModal());
  document.getElementById('chExport').addEventListener('click', () => toast('خروجی اکسل چک‌ها آماده شد (نمونه طراحی)', 'download'));

  /* میان‌برهای صفحه‌کلید */
  document.addEventListener('keydown', (e) => {
    if (e.target.matches('input, textarea, select')) return;
    if (e.key === '/' ) { e.preventDefault(); q.focus(); }
    if (e.key.toLowerCase() === 'n' && !e.metaKey && !e.ctrlKey) { e.preventDefault(); openChequeModal(); }
    if (e.key === 'Escape') { closeCheque(); closeChequeModal(); closeRowMenu(); }
  });

  renderKpis();
  renderAll();
};
