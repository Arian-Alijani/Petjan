/* =========================================================
   Vetplus · Appointments — calendar core
   Static UI prototype: all data is mock and lives in memory.
   ========================================================= */
(function () {
  'use strict';

  const fa = (v) => (window.toFa ? window.toFa(v) : String(v));
  const pad = (n) => String(n).padStart(2, '0');
  const hhmm = (m) => fa(pad(Math.floor(m / 60)) + ':' + pad(m % 60));
  const el = (id) => document.getElementById(id);
  /* keep latin/persian digit runs in a proper LTR island inside RTL text */
  const bdi = (s) => `<bdi class="num">${s}</bdi>`;
  const rangeTxt = (a, b) => bdi(hhmm(a) + ' – ' + hhmm(b));

  /* ---------------- reference data ---------------- */
  const TYPES = [
    { id: 'visit',    label: 'ویزیت عمومی',   cls: 't-visit',    ac: '#0d9488', icon: 'clipboard', dur: 30 },
    { id: 'vaccine',  label: 'واکسیناسیون',    cls: 't-vaccine',  ac: '#1570cd', icon: 'syringe',   dur: 20 },
    { id: 'dental',   label: 'دندان‌پزشکی',    cls: 't-dental',   ac: '#8f74d1', icon: 'tooth',     dur: 60 },
    { id: 'surgery',  label: 'جراحی',          cls: 't-surgery',  ac: '#d92d20', icon: 'tools',     dur: 90 },
    { id: 'groom',    label: 'آرایش و پیرایش', cls: 't-groom',    ac: '#e08a5c', icon: 'scissors',  dur: 60 },
    { id: 'lab',      label: 'آزمایش',         cls: 't-lab',      ac: '#2f9fb0', icon: 'flask',     dur: 20 },
    { id: 'followup', label: 'پیگیری',         cls: 't-followup', ac: '#b54708', icon: 'clock',     dur: 20 },
  ];
  const DOCTORS = [
    { id: 'd1', name: 'دکتر آریا نیک',    short: 'آریا نیک',    spec: 'داخلی و جراحی',   ini: 'آ', ac: '#0d9488' },
    { id: 'd2', name: 'دکتر مینا همتی',   short: 'مینا همتی',   spec: 'واکسن و پیشگیری', ini: 'م', ac: '#1570cd' },
    { id: 'd3', name: 'دکتر سهیل کاویانی', short: 'سهیل کاویانی', spec: 'دندان‌پزشکی',    ini: 'س', ac: '#8f74d1' },
    { id: 'd4', name: 'نگار راد',          short: 'نگار راد',    spec: 'آرایش و نگهداری', ini: 'ن', ac: '#e08a5c' },
  ];
  const STATUS = {
    confirmed: { label: 'تأیید شده',      pill: 'pill-info' },
    pending:   { label: 'در انتظار تأیید', pill: 'pill-warn' },
    done:      { label: 'پذیرش شده',      pill: 'pill-ok' },
    cancel:    { label: 'لغو شده',        pill: 'pill-danger' },
  };
  const PETS = [
    { name: 'پشمک',  kind: 'گربه پرشین',   owner: 'نگار محمدی',  phone: '۰۹۱۲۳۴۵۶۷۸۹', av: 'a', ic: 'cat' },
    { name: 'لئو',   kind: 'سگ ژرمن',      owner: 'امیر رستگار', phone: '۰۹۳۵۱۱۲۲۳۳۴', av: 'b', ic: 'dog' },
    { name: 'آبی',   kind: 'کاسکو',        owner: 'سارا یوسفی',  phone: '۰۹۱۹۸۷۶۵۴۳۲', av: 'c', ic: 'bird' },
    { name: 'میلو',  kind: 'سگ پامرانین',  owner: 'رضا کریمی',   phone: '۰۹۳۰۴۴۵۵۶۶۷', av: 'd', ic: 'dog' },
    { name: 'نانا',  kind: 'گربه اسکاتیش', owner: 'الهام صادقی', phone: '۰۹۱۲۷۷۸۸۹۹۰', av: 'e', ic: 'cat' },
    { name: 'رکس',   kind: 'سگ هاسکی',     owner: 'محسن دهقان',  phone: '۰۹۳۶۲۲۳۳۴۴۵', av: 'f', ic: 'dog' },
    { name: 'کوکو',  kind: 'خرگوش لوپ',    owner: 'مریم آذری',   phone: '۰۹۱۰۵۵۶۶۷۷۸', av: 'a', ic: 'rabbit' },
    { name: 'سیمبا', kind: 'گربه بنگال',   owner: 'حسین مرادی',  phone: '۰۹۱۲۳۳۲۲۱۱۰', av: 'b', ic: 'cat' },
    { name: 'بلا',   kind: 'سگ شیتزو',     owner: 'زهرا اکبری',  phone: '۰۹۳۸۹۹۸۸۷۷۶', av: 'd', ic: 'dog' },
    { name: 'توتی',  kind: 'طوطی برزیلی',  owner: 'کامران فرد',  phone: '۰۹۱۴۱۲۳۴۵۶۷', av: 'c', ic: 'bird' },
  ];
  const NOTES = [
    'مالک از بی‌اشتهایی طی سه روز گذشته گزارش داده است.',
    'ادامه درمان قبلی؛ بررسی پاسخ به آنتی‌بیوتیک.',
    'نوبت دوره‌ای طبق برنامه پیشگیری کلینیک.',
    'حیوان مضطرب است، زمان بیشتری برای معاینه در نظر گرفته شود.',
    '',
    'قبل از مراجعه ۸ ساعت ناشتا بوده است.',
  ];

  /* ---------------- date helpers (Jalali via Intl) ---------------- */
  const JMONTH = ['فروردین','اردیبهشت','خرداد','تیر','مرداد','شهریور','مهر','آبان','آذر','دی','بهمن','اسفند'];
  const JDOW = ['شنبه','یکشنبه','دوشنبه','سه‌شنبه','چهارشنبه','پنجشنبه','جمعه'];
  const JDOW_S = ['ش','ی','د','س','چ','پ','ج'];
  const jfmt = new Intl.DateTimeFormat('en-US-u-ca-persian', { year: 'numeric', month: 'numeric', day: 'numeric' });

  function jp(d) {
    const o = {};
    jfmt.formatToParts(d).forEach((p) => (o[p.type] = p.value));
    return { y: parseInt(o.year, 10), m: parseInt(o.month, 10), d: parseInt(o.day, 10) };
  }
  const dstart = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const addDays = (d, n) => { const x = dstart(d); x.setDate(x.getDate() + n); return x; };
  const key = (d) => d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
  const wIdx = (d) => (d.getDay() + 1) % 7;             // Saturday = 0
  const weekStart = (d) => addDays(d, -wIdx(d));
  const sameDay = (a, b) => key(a) === key(b);
  const jDate = (d) => { const p = jp(d); return fa(p.d) + ' ' + JMONTH[p.m - 1]; };
  const jFull = (d) => { const p = jp(d); return fa(p.d) + ' ' + JMONTH[p.m - 1] + ' ' + fa(p.y); };
  function jMonthStart(d) { let x = dstart(d); while (jp(x).d !== 1) x = addDays(x, -1); return x; }

  /* ---------------- mock appointment set ---------------- */
  const TODAY = dstart(new Date());
  function rng(seed) { let t = seed + 0x6d2b79f5; return function () { t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

  const APPTS = [];
  let uid = 1;
  (function seedData() {
    /* Clinic hours: 07:00 → 22:00. Appointments are spread across the whole
       day so morning, noon and evening slots all have content in the mock. */
    const DAY_START = 7 * 60, DAY_END = 22 * 60;
    for (let off = -18; off <= 24; off++) {
      const day = addDays(TODAY, off);
      if (wIdx(day) === 6) continue;                    // Friday closed
      const r = rng(off * 977 + 13);
      const count = 5 + Math.floor(r() * 6);
      const used = {};
      for (let i = 0; i < count; i++) {
        const t = TYPES[Math.floor(r() * TYPES.length)];
        const doc = DOCTORS[Math.floor(r() * DOCTORS.length)];
        const pet = PETS[Math.floor(r() * PETS.length)];
        const hour = 7 + Math.floor(r() * 15);          // 07:00 … 21:xx
        let start = hour * 60 + (r() > 0.5 ? 30 : 0);
        const slotKey = doc.id + start;
        if (used[slotKey]) start += 30;
        used[doc.id + start] = 1;
        if (start + t.dur > DAY_END) continue;
        let status = 'confirmed';
        const rr = r();
        if (off < 0) status = rr > 0.12 ? 'done' : 'cancel';
        else if (off === 0) status = start < new Date().getHours() * 60 ? 'done' : rr > 0.75 ? 'pending' : 'confirmed';
        /* keep a couple of demo items very early / very late so the whole 7→22 span reads as live */
        else status = rr > 0.72 ? 'pending' : 'confirmed';
        /* invoice + payment + visit outcome (only meaningful for past/done rows) */
        const baseRate = ({visit:12000, vaccine:15000, dental:22000, surgery:45000, groom:9000, lab:18000, followup:8000})[t.id] || 12000;
        const extras = Math.round(r() * 3) * 5000;
        const total = t.dur * baseRate + extras;
        let invoice = null, pay = null, visit = null;
        if (status === 'done') {
          const pr = r();
          if (pr < 0.72)      invoice = 'paid';
          else if (pr < 0.90) invoice = 'unpaid';
          else                invoice = 'partial';
          if (invoice !== 'unpaid') pay = r() < 0.55 ? 'card' : (r() < 0.5 ? 'cash' : 'online');
          visit = r() < 0.94 ? 'complete' : 'no-show';
        } else if (status === 'cancel') {
          visit = 'no-show';
        }
        APPTS.push({
          id: 'a' + uid++, date: key(day), start, dur: t.dur, type: t.id, doc: doc.id,
          pet: pet.name, kind: pet.kind, owner: pet.owner, phone: pet.phone, av: pet.av, ic: pet.ic,
          status, note: NOTES[Math.floor(r() * NOTES.length)],
          created: 'ثبت توسط پذیرش', code: 'AP-' + fa(14050000 + uid * 7),
          invoice, pay, visit, total,
        });
      }
    }
  })();

  /* ---------------- state ---------------- */
  const S = {
    tab: 'cal',
    view: 'week',
    cursor: new Date(TODAY),
    types: new Set(TYPES.map((t) => t.id)),
    docs: new Set(DOCTORS.map((d) => d.id)),
    hideCancel: false,
    q: '',
    selected: null,
  };

  const H0 = 7, H1 = 22, PXH = 78, SNAP = 15;
  const typeOf = (id) => TYPES.find((t) => t.id === id);
  const docOf = (id) => DOCTORS.find((d) => d.id === id);

  function matches(a) {
    if (!S.q) return true;
    const q = S.q.trim();
    return (a.pet + ' ' + a.owner + ' ' + a.kind + ' ' + docOf(a.doc).name + ' ' + typeOf(a.type).label).includes(q);
  }
  function visible(a) {
    if (!S.types.has(a.type) || !S.docs.has(a.doc)) return false;
    if (S.hideCancel && a.status === 'cancel') return false;
    return true;
  }
  const onDate = (d) => APPTS.filter((a) => a.date === key(d) && visible(a)).sort((x, y) => x.start - y.start);

  /* ---------------- icons ---------------- */
  const I = window.Icons || {};
  const IC = {
    tooth: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5.5c-1.5-1.2-4-2-5.6-1C4.4 5.7 4 8.2 4.6 11c.5 2.4 1 4.6 1.4 6.6.3 1.5.7 2.4 1.6 2.4 1 0 1.3-1 1.6-2.6l.5-2.6c.2-1 .6-1.6 1.3-1.6.8 0 1.1.6 1.3 1.6l.5 2.6c.3 1.6.6 2.6 1.6 2.6.9 0 1.3-.9 1.6-2.4.4-2 .9-4.2 1.4-6.6.6-2.8.2-5.3-1.8-6.5-1.6-1-4.1-.2-5.6 1Z"/></svg>',
    phone: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2 4.2 2 2 0 0 1 4 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.1a2 2 0 0 1 2.1-.5c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2Z"/></svg>',
    pin: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
    sms: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.4 8.4 0 0 1-9 8.4 9 9 0 0 1-3.8-.8L3 21l1.9-4.8A8.4 8.4 0 0 1 12 3a8.4 8.4 0 0 1 9 8.5Z"/></svg>',
    tag: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20.6 13.4 12 22l-9-9V3h10l7.6 7.6a2 2 0 0 1 0 2.8Z"/><circle cx="7.5" cy="7.5" r="1.5"/></svg>',
  };
  const icon = (n) => IC[n] || I[n] || '';

  /* ---------------- overlap packing ---------------- */
  function pack(list) {
    const out = [];
    let cluster = [], end = -1;
    const flush = () => {
      if (!cluster.length) return;
      const lanes = [];
      cluster.forEach((a) => {
        let li = lanes.findIndex((e) => e <= a.start);
        if (li === -1) { lanes.push(a.start + a.dur); li = lanes.length - 1; }
        else lanes[li] = a.start + a.dur;
        a._lane = li;
      });
      cluster.forEach((a) => { a._lanes = lanes.length; out.push(a); });
      cluster = []; end = -1;
    };
    list.slice().sort((a, b) => a.start - b.start || b.dur - a.dur).forEach((a) => {
      if (a.start >= end) flush();
      cluster.push(a); end = Math.max(end, a.start + a.dur);
    });
    flush();
    return out;
  }

  /* ---------------- event card ---------------- */
  function evCard(a) {
    const t = typeOf(a.type), d = docOf(a.doc);
    const st = STATUS[a.status];
    const top = ((a.start - H0 * 60) / 60) * PXH;
    const raw = (a.dur / 60) * PXH - 3;
    const h = Math.max(raw, 26);                     // never collapse below a readable line
    const w = 100 / (a._lanes || 1);
    const pos = `top:${top + 1}px;min-height:${h}px;right:calc(${(a._lane || 0) * w}% + 4px);width:calc(${w}% - 8px);`;
    const tier = h < 36 ? 'xs' : h < 64 ? 'sm' : 'md';
    const dim = matches(a) ? '' : ' dim';
    const range = rangeTxt(a.start, a.start + a.dur);
    const flag = a.status === 'pending' ? '<span class="ev-dot pend" title="در انتظار تأیید"></span>'
      : a.status === 'done' ? '<span class="ev-dot done" title="پذیرش شده"></span>'
      : a.status === 'cancel' ? '<span class="ev-dot canc" title="لغو شده"></span>' : '';
    let body;
    if (tier === 'xs') {
      body = `<span class="ev-line"><span class="ev-bul"></span><span class="ev-n">${a.pet}</span>
              ${flag}<span class="ev-t">${bdi(hhmm(a.start))}</span></span>`;
    } else if (tier === 'sm') {
      body = `<span class="ev-line"><span class="ev-n">${a.pet}</span>${flag}</span>
              <span class="ev-t">${range}<span class="t-rest"> · ${t.label}</span></span>`;
    } else {
      body = `<span class="ev-line"><span class="ev-n">${a.pet}</span>${flag}</span>
              <span class="ev-t">${range}</span>
              <span class="ev-m">${t.label} · ${d.short}</span>`;
    }
    // rich content revealed on hover for cards that are too short to show it
    const pop = `<span class="ev-pop"><b>${a.pet}</b><span>${t.label} · ${d.short}</span>
        <span>${range} · ${st.label}</span></span>`;
    return `<button class="ev ${t.cls} st-${a.status} tier-${tier}${dim}${S.selected === a.id ? ' sel' : ''}"
      style="${pos}" data-ev="${a.id}"
      title="${a.pet} · ${t.label} · ${d.name} · ${hhmm(a.start)}-${hhmm(a.start + a.dur)} · ${st.label}">${body}${pop}</button>`;
  }

  /* ---------------- selection layer (click / drag to create) ---------------- */
  function slotsLayer(dateKey, docId) {
    return `<div class="tg-pick" data-pick="${dateKey}|${docId || ''}">
              <div class="tg-hint"><span class="lb"></span></div>
              <div class="tg-range"><span class="lb"></span></div>
            </div>`;
  }

  /* shared minute math for picking, dragging and ghost placement */
  const minFromY = (layer, clientY, mode) => {
    const r = layer.getBoundingClientRect();
    const y = Math.min(Math.max(clientY - r.top, 0), r.height);
    const raw = H0 * 60 + (y / PXH) * 60;
    const f = mode === 'ceil' ? Math.ceil : mode === 'round' ? Math.round : Math.floor;
    return Math.min(Math.max(f(raw / SNAP) * SNAP, H0 * 60), H1 * 60);
  };
  /* drag-and-drop in progress flag — suppresses the "new appointment" hover hint */
  const dndState = { active: false };

  /* pointer driven range picking */
  (function pickInit() {
    let drag = null;                                     // { layer, a, b }
    const minAt = (layer, clientY, mode) => minFromY(layer, clientY, mode);
    const place = (node, from, to) => {
      node.style.top = (((from - H0 * 60) / 60) * PXH) + 'px';
      node.style.height = (((to - from) / 60) * PXH) + 'px';
      node.querySelector('.lb').innerHTML = `${rangeTxt(from, to)} <span class="mn">${fa(to - from)} دقیقه</span>`;
    };
    const clearAll = () => document.querySelectorAll('.tg-pick.hot,.tg-pick.dragging')
      .forEach((p) => p.classList.remove('hot', 'dragging'));

    document.addEventListener('pointermove', (e) => {
      if (drag) {
        const m = minAt(drag.layer, e.clientY, 'round');
        drag.b = m;
        const from = Math.min(drag.a, drag.b), to = Math.max(drag.a, drag.b);
        place(drag.layer.querySelector('.tg-range'), from, Math.max(to, from + SNAP));
        return;
      }
      if (dndState.active) return;
      const layer = e.target.closest && e.target.closest('.tg-pick');
      if (!layer) { clearAll(); return; }
      clearAll();
      layer.classList.add('hot');
      const m = minAt(layer, e.clientY, 'floor');
      const hint = layer.querySelector('.tg-hint');
      hint.style.top = (((m - H0 * 60) / 60) * PXH) + 'px';
      hint.querySelector('.lb').innerHTML = '+ ' + bdi(hhmm(m)) + ' <span class="mn">نوبت جدید</span>';
    }, { passive: true });

    document.addEventListener('pointerdown', (e) => {
      const layer = e.target.closest && e.target.closest('.tg-pick');
      if (!layer || e.button !== 0) return;
      e.preventDefault();
      drag = { layer, a: minAt(layer, e.clientY, 'floor'), b: null, moved: false, y0: e.clientY };
      layer.classList.add('dragging');
      place(layer.querySelector('.tg-range'), drag.a, drag.a + SNAP);
      layer.setPointerCapture && layer.setPointerCapture(e.pointerId);
    });

    document.addEventListener('pointerup', (e) => {
      if (!drag) return;
      const moved = Math.abs(e.clientY - drag.y0) > 6;
      const [dk, doc] = drag.layer.dataset.pick.split('|');
      let from = drag.a, dur = null;
      if (moved && drag.b != null) {
        from = Math.min(drag.a, drag.b);
        dur = Math.max(Math.abs(drag.b - drag.a), SNAP);
      }
      drag.layer.classList.remove('dragging', 'hot');
      drag = null;
      window.SCH.book({ date: dk, start: from, dur, doc: doc || null });
    });
    document.addEventListener('pointercancel', () => { clearAll(); drag = null; });
  })();

  const hoursCol = (gutter) => {
    let s = '';
    for (let h = H0; h < H1; h++) s += `<div class="tg-hour"${gutter ? ` data-h="${hhmm(h * 60)}"` : ''}></div>`;
    return s;
  };

  function nowLine(dateKey) {
    const n = new Date();
    if (key(n) !== dateKey) return '';
    const m = n.getHours() * 60 + n.getMinutes();
    if (m < H0 * 60 || m > H1 * 60) return '';
    const top = ((m - H0 * 60) / 60) * PXH;
    return `<div class="tg-now" style="top:${top}px"></div>`;
  }

  /* =========================================================
     Drag & drop — move an appointment by dragging its card
     across columns (day / doctor) and slots (time).
     ========================================================= */
  (function dndInit() {
    let d = null;   // { ev, id, card, lastCol, dur, grabMin, startX, startY, moved, ghost, target }

    const colInfo = (node) => {
      const col = node && node.closest ? node.closest('.tg-col') : null;
      const pick = col ? col.querySelector('.tg-pick') : null;
      if (!pick) return null;
      const [dk, doc] = pick.dataset.pick.split('|');
      return { col, layer: pick, dk, doc: doc || null };
    };

    const conflictsAt = (dateKey, docId, start, dur, skipId) =>
      APPTS.some((a) => a.date === dateKey && a.doc === docId && a.status !== 'cancel' &&
        a.id !== skipId && start < a.start + a.dur && a.start < start + dur);

    const clean = () => {
      if (!d) return;
      d.card.classList.remove('dragging');
      d.card.style.visibility = '';
      if (d.ghost) d.ghost.remove();
      if (d.lastCol) d.lastCol.classList.remove('drop-here');
      document.body.classList.remove('ev-dragging');
      dndState.active = false;
      d = null;
    };

    document.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      const card = e.target.closest && e.target.closest('.tg-col .ev[data-ev]');
      if (!card) return;
      const a = APPTS.find((x) => x.id === card.dataset.ev);
      if (!a) return;
      const info = colInfo(card);
      if (!info) return;
      e.preventDefault();
      d = { ev: a, id: a.id, card, lastCol: null, dur: a.dur, moved: false,
        startX: e.clientX, startY: e.clientY, ghost: null, target: null };
      d.grabMin = minFromY(info.layer, e.clientY, 'round') - a.start;
      dndState.active = true;
      document.body.classList.add('ev-dragging');
    });

    document.addEventListener('pointermove', (e) => {
      if (!d) return;
      if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) < 5) return;
      if (!d.moved) {
        d.moved = true;
        d.ghost = document.createElement('div');
        d.ghost.className = 'ev-ghost ' + typeOf(d.ev.type).cls;
        d.ghost.innerHTML = '<span class="g-lb"></span>';
        d.card.closest('.tg-col').appendChild(d.ghost);
        d.card.classList.add('dragging');
        d.card.style.visibility = 'hidden';
      }
      /* which column (date / doctor) is under the pointer? */
      d.card.style.pointerEvents = 'none';
      const under = document.elementFromPoint(e.clientX, e.clientY);
      d.card.style.pointerEvents = '';
      const info = colInfo(under);
      if (!info) {
        if (d.lastCol) { d.lastCol.classList.remove('drop-here'); d.lastCol = null; }
        d.ghost.style.opacity = '0';
        d.target = null;
        return;
      }
      d.ghost.style.opacity = '';
      /* minute under pointer minus grab offset, clamped to clinic hours */
      let start = minFromY(info.layer, e.clientY, 'round') - d.grabMin;
      start = Math.max(H0 * 60, Math.min(start, H1 * 60 - d.dur));
      start = Math.round(start / SNAP) * SNAP;
      const clash = conflictsAt(info.dk, info.doc || d.ev.doc, start, d.dur, d.id);
      d.ghost.style.top = (((start - H0 * 60) / 60) * PXH + 1) + 'px';
      d.ghost.style.height = ((d.dur / 60) * PXH - 3) + 'px';
      d.ghost.classList.toggle('conflict', clash);
      d.ghost.querySelector('.g-lb').innerHTML =
        `${rangeTxt(start, start + d.dur)} <span class="mn">${clash ? '⚠ تداخل زمانی' : fa(d.dur) + ' دقیقه'}</span>`;
      if (info.col !== d.lastCol) {
        if (d.lastCol) d.lastCol.classList.remove('drop-here');
        info.col.classList.add('drop-here');
        d.lastCol = info.col;
      }
      d.target = { dk: info.dk, doc: info.doc, start };
    }, { passive: true });

    document.addEventListener('pointerup', () => {
      if (!d) return;
      const r = d;
      clean();
      if (!r.moved || !r.target) return;
      const { dk, doc, start } = r.target;
      const a = APPTS.find((x) => x.id === r.id);
      if (!a) return;
      const newDoc = doc || a.doc;
      const movedSome = dk !== a.date || start !== a.start || newDoc !== a.doc;
      if (!movedSome) return;
      if (conflictsAt(dk, newDoc, start, a.dur, a.id)) {
        window.SCH.toast('این بازه با نوبت دیگری تداخل دارد؛ جابه‌جایی انجام نشد.', 'alert');
        render();
        return;
      }
      a.doc = newDoc; a.date = dk; a.start = start;
      S.selected = a.id;
      window.SCH.toast('نوبت «' + a.pet + '» به ' + hhmm(start) + ' جابه‌جا شد (نمونه طراحی).', 'check');
      render();
    });

    document.addEventListener('pointercancel', clean);
  })();

  /* =========================================================
     Drag & drop — month view: drag an event chip onto another day
     ========================================================= */
  (function mvDndInit() {
    let mvd = null;
    document.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      const chip = e.target.closest && e.target.closest('.mv-ev[data-ev]');
      if (!chip) return;
      const a = APPTS.find((x) => x.id === chip.dataset.ev);
      if (!a) return;
      e.preventDefault();
      mvd = { id: a.id, chip, moved: false, x0: e.clientX, y0: e.clientY, over: null };
      dndState.active = true;
    });
    document.addEventListener('pointermove', (e) => {
      if (!mvd) return;
      if (!mvd.moved && Math.hypot(e.clientX - mvd.x0, e.clientY - mvd.y0) < 5) return;
      mvd.moved = true;
      mvd.chip.classList.add('dragging');
      document.body.classList.add('ev-dragging');
      mvd.chip.style.pointerEvents = 'none';
      const under = document.elementFromPoint(e.clientX, e.clientY);
      mvd.chip.style.pointerEvents = '';
      const cell = under && under.closest ? under.closest('.mv-cell[data-day]') : null;
      if (mvd.over && mvd.over !== cell) mvd.over.classList.remove('drop-here');
      if (cell) { cell.classList.add('drop-here'); }
      mvd.over = cell || null;
    }, { passive: true });
    const finish = () => {
      if (!mvd) return null;
      mvd.chip.classList.remove('dragging');
      if (mvd.over) mvd.over.classList.remove('drop-here');
      document.body.classList.remove('ev-dragging');
      dndState.active = false;
      const r = mvd; mvd = null;
      return r;
    };
    document.addEventListener('pointerup', () => {
      const r = finish();
      if (!r || !r.moved || !r.over) return;
      const a = APPTS.find((x) => x.id === r.id);
      const dk = r.over.dataset.day;
      if (!a || dk === a.date) return;
      const clash = APPTS.some((x) => x.date === dk && x.doc === a.doc && x.status !== 'cancel' &&
        x.id !== a.id && a.start < x.start + x.dur && x.start < a.start + a.dur);
      if (clash) { window.SCH.toast('در روز مقصد برای پزشک این نوبت تداخل زمانی وجود دارد.', 'alert'); return; }
      a.date = dk;
      S.selected = a.id;
      window.SCH.toast('نوبت «' + a.pet + '» به روز جدید منتقل شد (نمونه طراحی).', 'check');
      render();
    });
    document.addEventListener('pointercancel', finish);
  })();

  /* ---------------- views ---------------- */
  function viewDay() {
    const d = S.cursor, dk = key(d);
    const docs = DOCTORS.filter((x) => S.docs.has(x.id));
    const cols = docs.length ? docs : DOCTORS;
    const tpl = `56px repeat(${cols.length}, minmax(0,1fr))`;
    const list = onDate(d);
    const head = cols.map((c) => {
      const n = list.filter((a) => a.doc === c.id).length;
      return `<div class="tg-hcell"><div class="hd-doc"><span class="av" style="--ac:${c.ac}">${c.ini}</span>
        <span><span class="hd-name">${c.short}</span><span class="hd-sub">${fa(n)} نوبت</span></span></div></div>`;
    }).join('');
    const body = cols.map((c) => {
      const evs = pack(list.filter((a) => a.doc === c.id));
      return `<div class="tg-col">${hoursCol()}${slotsLayer(dk, c.id)}${evs.map(evCard).join('')}${nowLine(dk)}</div>`;
    }).join('');
    return grid(tpl, head, body, list.length, dk);
  }

  function viewWeek() {
    const ws = weekStart(S.cursor);
    const days = Array.from({ length: 7 }, (_, i) => addDays(ws, i));
    const tpl = `56px repeat(7, minmax(0,1fr))`;
    let total = 0;
    const head = days.map((d) => {
      const p = jp(d), t = sameDay(d, TODAY), off = wIdx(d) === 6;
      return `<div class="tg-hcell${t ? ' today' : ''}${off ? ' off' : ''}">
        <div class="hd-sub">${JDOW[wIdx(d)]}</div>
        <div class="hd-daynum">${fa(p.d)}</div></div>`;
    }).join('');
    const body = days.map((d) => {
      const list = onDate(d); total += list.length;
      const evs = pack(list);
      return `<div class="tg-col${wIdx(d) === 6 ? ' off' : ''}">${hoursCol()}${slotsLayer(key(d), '')}${evs.map(evCard).join('')}${nowLine(key(d))}</div>`;
    }).join('');
    return grid(tpl, head, body, total, key(S.cursor));
  }

  function grid(tpl, head, body, count, dateKey) {
    if (!count) return emptyState(dateKey);
    return `<div class="tg-wrap" id="tgWrap">
      <div class="tg-head" style="grid-template-columns:${tpl}"><div class="tg-hcell"></div>${head}</div>
      <div class="tg-body" style="grid-template-columns:${tpl}">
        <div class="tg-gutter">${hoursCol(true)}</div>${body}
      </div></div>`;
  }

  function viewMonth() {
    const ms = jMonthStart(S.cursor);
    const first = weekStart(ms);
    const mNum = jp(ms).m;
    let cells = '', total = 0;
    for (let i = 0; i < 42; i++) {
      const d = addDays(first, i);
      const p = jp(d), out = p.m !== mNum, t = sameDay(d, TODAY), off = wIdx(d) === 6;
      if (i >= 35 && out) break;
      const list = onDate(d).filter(matches);
      total += out ? 0 : list.length;
      const shown = list.slice(0, 3).map((a) => {
        const ty = typeOf(a.type);
        return `<div class="mv-ev ${ty.cls} st-${a.status}" data-ev="${a.id}">
          <span class="d"></span><span class="tm">${hhmm(a.start)}</span><span class="nm">${a.pet}</span></div>`;
      }).join('');
      cells += `<div class="mv-cell${out ? ' out' : ''}${t ? ' today' : ''}${off ? ' off' : ''}" data-day="${key(d)}">
        <span class="mv-day">${fa(p.d)}</span>${shown}
        ${list.length > 3 ? `<span class="mv-more" data-day-open="${key(d)}">+ ${fa(list.length - 3)} نوبت دیگر</span>` : ''}
        <button class="mv-add" data-new="${key(d)}|540|" title="افزودن نوبت">${I.plus || '+'}</button>
      </div>`;
    }
    if (!total) return emptyState();
    return `<div class="tg-wrap"><div class="mv">
      ${JDOW.map((n) => `<div class="mv-h">${n}</div>`).join('')}${cells}</div></div>`;
  }

  function emptyState(dateKey) {
    return `<div class="sc-empty">
      <div class="ic">${I.calendar || ''}</div>
      <b>نوبتی برای نمایش نیست</b>
      <div>فیلترها را تغییر دهید یا برای این بازه نوبت جدیدی ثبت کنید.</div>
      <div class="sc-empty-hint">می‌توانید روی تقویم کلیک کنید یا در بازه دلخواه بکشید تا مستقیم نوبت‌دهی کنید.</div>
      <button class="btn btn-outline" style="margin-top:14px" onclick="SCH.book(${dateKey ? `{date:'${dateKey}'}` : ''})">${I.plus || ''} ثبت نوبت جدید</button>
    </div>`;
  }

  /* ---------------- chrome (toolbar / filters / stats) ---------------- */
  function label() {
    if (S.view === 'day') return { main: jFull(S.cursor), sub: JDOW[wIdx(S.cursor)] + (sameDay(S.cursor, TODAY) ? ' · امروز' : '') };
    if (S.view === 'week') {
      const a = weekStart(S.cursor), b = addDays(a, 6);
      return { main: jDate(a) + ' – ' + jDate(b), sub: 'هفته ' + fa(jp(a).y) };
    }
    const p = jp(jMonthStart(S.cursor));
    return { main: JMONTH[p.m - 1] + ' ' + fa(p.y), sub: 'نمای ماهانه' };
  }

  function renderChrome() {
    const L = label();
    el('scLabel').innerHTML = `<b>${L.main}</b><span>${L.sub}</span>`;
    document.querySelectorAll('.seg [data-view]').forEach((b) => b.classList.toggle('on', b.dataset.view === S.view));

    // counters
    const scope = scopeDates();
    const inScope = APPTS.filter((a) => scope.includes(a.date));
    el('scTypes').innerHTML = TYPES.map((t) => {
      const n = inScope.filter((a) => a.type === t.id).length;
      return `<button class="chip ${t.cls}${S.types.has(t.id) ? ' on' : ''}" data-type="${t.id}">
        <span class="dot"></span>${t.label}<span class="n">${fa(n)}</span></button>`;
    }).join('');
    el('scDocs').innerHTML = DOCTORS.map((d) => {
      const n = inScope.filter((a) => a.doc === d.id).length;
      return `<button class="chip doc-chip${S.docs.has(d.id) ? ' on' : ''}" style="--ac:${d.ac}" data-doc="${d.id}">
        <span class="av">${d.ini}</span>${d.short}<span class="n">${fa(n)}</span></button>`;
    }).join('');
    el('scHideCancel').classList.toggle('on', S.hideCancel);
  }

  function scopeDates() {
    if (S.view === 'day') return [key(S.cursor)];
    if (S.view === 'week') { const a = weekStart(S.cursor); return Array.from({ length: 7 }, (_, i) => key(addDays(a, i))); }
    const ms = jMonthStart(S.cursor), out = [];
    let d = ms; const m = jp(ms).m;
    while (jp(d).m === m) { out.push(key(d)); d = addDays(d, 1); }
    return out;
  }

  function render() {
    renderChrome();
    const host = el('scView');
    host.innerHTML = S.view === 'day' ? viewDay() : S.view === 'week' ? viewWeek() : viewMonth();
    const w = el('tgWrap');
    if (w && S.view !== 'month') w.scrollTop = Math.max(0, ((new Date().getHours() - H0 - 1) * PXH));
  }

  /* ---------------- interactions ---------------- */
  function step(dir) {
    if (S.view === 'day') S.cursor = addDays(S.cursor, dir);
    else if (S.view === 'week') S.cursor = addDays(S.cursor, dir * 7);
    else {
      let d = jMonthStart(S.cursor);
      S.cursor = dir > 0 ? jMonthStart(addDays(d, 35)) : jMonthStart(addDays(d, -1));
    }
    render();
  }

  document.addEventListener('click', (e) => {
    const tb = e.target.closest('#scTabs [data-tab]');
    if (tb) { setTab(tb.dataset.tab); return; }
    /* — queue tab interactions — */
    const qd = e.target.closest('[data-q-day]');
    if (qd) { Q.day = qd.dataset.qDay; renderQueue(); return; }
    const qs = e.target.closest('[data-q-shift]');
    if (qs) { Q.off = Math.max(0, Q.off + +qs.dataset.qShift); renderQueue(); return; }
    if (e.target.closest('[data-q-cancel]')) { Q.showCancel = !Q.showCancel; renderQueue(); return; }
    /* — history tab interactions — */
    const hsc = e.target.closest('[data-h-scope]');
    if (hsc) {
      HS.scope = hsc.dataset.hScope;
      HS.calOpen = false;
      renderHist();
      return;
    }
    const hst = e.target.closest('[data-h-status]');
    if (hst) { HS.status = hst.dataset.hStatus; renderHist(); return; }
    /* toggle the mini-calendar popover */
    if (e.target.closest('[data-h-cal-toggle]')) {
      HS.calOpen = !HS.calOpen;
      if (HS.calOpen && HS.rangeStart && !HS.rangeEnd) HS.rangeStart = null; /* start fresh */
      renderHist(); return;
    }
    /* month step inside the mini-calendar */
    const hcn = e.target.closest('[data-h-cal-nav]');
    if (hcn) {
      const dir = +hcn.dataset.hCalNav;
      let d = jMonthStart(HS.calCursor);
      HS.calCursor = dir > 0 ? jMonthStart(addDays(d, 35)) : jMonthStart(addDays(d, -1));
      renderHist(); return;
    }
    if (e.target.closest('[data-h-cal-today]')) { HS.calCursor = new Date(TODAY); renderHist(); return; }
    if (e.target.closest('[data-h-cal-clear]')) {
      HS.rangeStart = null; HS.rangeEnd = null;
      if (HS.scope === 'range') HS.scope = '30';
      renderHist(); return;
    }
    const hcApply = e.target.closest('[data-h-cal-apply]');
    if (hcApply && !hcApply.disabled) {
      if (HS.rangeStart && HS.rangeEnd) {
        HS.scope = 'range'; HS.calOpen = false;
        renderHist();
      }
      return;
    }
    const hpick = e.target.closest('[data-h-pick]');
    if (hpick && !hpick.disabled) {
      const k = hpick.dataset.hPick;
      if (!HS.rangeStart || (HS.rangeStart && HS.rangeEnd)) {
        HS.rangeStart = k; HS.rangeEnd = null;
      } else {
        HS.rangeEnd = k;
      }
      renderHist(); return;
    }
    /* «مشاهده در تقویم» chip on the day-group heading — jump to that date in calendar tab */
    const hcal = e.target.closest('[data-h-cal]');
    if (hcal) { S.cursor = new Date(hcal.dataset.hCal.replace(/-/g, '/')); S.view = 'day'; setTab('cal'); render(); return; }
    const seg = e.target.closest('[data-view]');
    if (seg) { S.view = seg.dataset.view; render(); return; }
    const ty = e.target.closest('[data-type]');
    if (ty) { const id = ty.dataset.type; S.types.has(id) ? S.types.delete(id) : S.types.add(id); if (!S.types.size) TYPES.forEach((t) => S.types.add(t.id)); render(); return; }
    const dc = e.target.closest('[data-doc]');
    if (dc) { const id = dc.dataset.doc; S.docs.has(id) ? S.docs.delete(id) : S.docs.add(id); if (!S.docs.size) DOCTORS.forEach((d) => S.docs.add(d.id)); render(); return; }
    const ev = e.target.closest('[data-ev]');
    if (ev) { S.selected = ev.dataset.ev; window.SCH.openDetail(ev.dataset.ev); render(); return; }
    const nw = e.target.closest('[data-new]');
    if (nw) {
      const [dk, m, doc] = nw.dataset.new.split('|');
      window.SCH.book({ date: dk, start: parseInt(m, 10), doc: doc || null });
      return;
    }
    const dop = e.target.closest('[data-day-open]');
    if (dop) { S.cursor = new Date(dop.dataset.dayOpen.replace(/-/g, '/')); S.view = 'day'; render(); return; }
    const cell = e.target.closest('[data-day]');
    if (cell) { S.cursor = new Date(cell.dataset.day.replace(/-/g, '/')); S.view = 'day'; render(); return; }
  });

  /* =========================================================
     Tabs — calendar / upcoming queue / history
     ========================================================= */
  function setTab(tab) {
    S.tab = tab;
    document.querySelectorAll('#scTabs [data-tab]').forEach((b) => {
      const on = b.dataset.tab === tab;
      b.classList.toggle('active', on);
      b.setAttribute('aria-selected', on ? 'true' : 'false');
    });
    el('scCalPanel').hidden = tab !== 'cal';
    el('scQueuePanel').hidden = tab !== 'queue';
    el('scHistPanel').hidden = tab !== 'hist';
    /* calendar-only chrome hides on the other tabs */
    el('scCalChrome').style.display = tab === 'cal' ? '' : 'none';
    el('scViewSeg').style.display = tab === 'cal' ? '' : 'none';
    el('scFilterRow').style.display = tab === 'cal' ? '' : 'none';
    if (tab === 'queue') renderQueue();
    if (tab === 'hist') renderHist();
  }

  /* =========================================================
     «نوبت‌های پیش رو» — day strip + one ordered queue per doctor
     ========================================================= */
  const Q = { day: key(TODAY), off: 0, showCancel: false };
  const nowMin = () => new Date().getHours() * 60 + new Date().getMinutes();

  function renderQueue() {
    const host = el('scQueuePanel');
    if (!host) return;
    const tk = key(TODAY);
    const selDay = new Date(Q.day.replace(/-/g, '/'));
    /* day strip — a sliding 14-day window starting at today (7-day pages) */
    const days = Array.from({ length: 14 }, (_, i) => addDays(TODAY, Q.off * 7 + i));
    const strip = days.map((d) => {
      const k = key(d), p = jp(d), on = k === Q.day, isT = k === tk, closed = wIdx(d) === 6;
      const n = APPTS.filter((a) => a.date === k && a.status !== 'cancel').length;
      return `<button class="qd${on ? ' on' : ''}${isT ? ' tdy' : ''}${closed ? ' closed' : ''}" data-q-day="${k}"
        title="${JDOW[wIdx(d)]} ${jFull(d)} · ${fa(n)} نوبت">
        <span class="qd-dow">${JDOW_S[wIdx(d)]}</span>
        <span class="qd-n">${fa(p.d)}</span>
        <span class="qd-cnt${n ? '' : ' z'}">${fa(n)}</span>
      </button>`;
    }).join('');

    const list = APPTS.filter((a) => a.date === Q.day && visible(a) && matches(a));
    const cnt = { all: list.length, pend: 0, done: 0, cancel: 0 };
    list.forEach((a) => {
      if (a.status === 'pending') cnt.pend++;
      else if (a.status === 'done') cnt.done++;
      else if (a.status === 'cancel') cnt.cancel++;
    });
    const isToday = Q.day === tk, nm = nowMin();

    const cols = DOCTORS.map((d) => {
      const items = list.filter((a) => a.doc === d.id && (Q.showCancel || a.status !== 'cancel'))
        .sort((x, y) => x.start - y.start);
      /* live badges — who is being served / who is next (today only) */
      let nowId = null, nextId = null;
      if (isToday) {
        const live = items.filter((a) => a.status !== 'cancel' && a.status !== 'done');
        nowId = (live.find((a) => a.start <= nm && nm < a.start + a.dur) || {}).id || null;
        if (!nowId) nextId = (live.find((a) => a.start > nm) || {}).id || null;
      }
      const cards = items.map((a, i) => {
        const t = typeOf(a.type), st = STATUS[a.status];
        const live = a.id === nowId
          ? `<span class="qp-live now">${I.play || ''} در حال پذیرش</span>`
          : a.id === nextId ? '<span class="qp-live next">نوبت بعدی</span>' : '';
        return `<button class="qp-card st-${a.status}" data-ev="${a.id}"
          title="${a.pet} · ${t.label} · ${d.name} · ${hhmm(a.start)}–${hhmm(a.start + a.dur)} · ${st.label}">
          <span class="qp-num">${fa(i + 1)}</span>
          <span class="qp-time"><b>${hhmm(a.start)}</b><span>${hhmm(a.start + a.dur)}</span></span>
          <span class="avatar-pet ${a.av}">${I[a.ic] || ''}</span>
          <span class="qp-main">
            <span class="qp-l1"><b>${a.pet}</b><span class="qp-type ${t.cls}">${t.label}</span>${live}</span>
            <span class="qp-l2">${a.kind} · مالک: ${a.owner}</span>
          </span>
          <span class="pill ${st.pill}">${st.label}</span>
        </button>`;
      }).join('');
      const empty = items.length ? '' : `<div class="qp-empty">
          <span class="ic">${I.calendar || ''}</span>
          <b>نوبتی ثبت نشده است</b>
          <span>برنامهٔ ${d.short} در این روز خالی است.</span>
          <button class="btn sm" data-new="${Q.day}|540|${d.id}" title="افزودن نوبت جدید برای ${d.short}">${I.plus || ''} ثبت اولین نوبت</button>
        </div>`;
      /* persistent bottom CTA — always visible so staff can add a slot for this doctor
         even when the column already has cards */
      const foot = `<div class="qp-foot">
          <button class="qp-add" data-new="${Q.day}|540|${d.id}"
            title="افزودن نوبت برای ${d.short}">${I.plus || ''} افزودن نوبت</button>
        </div>`;
      return `<div class="qp-doc" style="--ac:${d.ac}">
        <div class="qp-dh">
          <span class="av">${d.ini}</span>
          <span class="qp-dt"><b>${d.short}</b><span>${d.spec}</span></span>
          <span class="qp-dcnt">${fa(items.length)}<small>نوبت</small></span>
        </div>
        <div class="qp-list">${cards}${empty}</div>
        ${foot}
      </div>`;
    }).join('');

    host.innerHTML = `<div class="qp-shell">
      <div class="qp-strip">
        <button class="icon-btn nav-btn qp-nav" data-q-shift="-1" title="هفتهٔ قبل">${I.chevronRight || ''}</button>
        ${strip}
        <button class="icon-btn nav-btn qp-nav" data-q-shift="1" title="هفتهٔ بعد">${I.chevronLeft || ''}</button>
      </div>
      <div class="qp-head">
        <div class="qp-date">
          <b>${JDOW[wIdx(selDay)]}، ${jFull(selDay)}</b>
          ${isToday ? '<span class="qp-t">امروز</span>' : `<a class="qp-t ghost" data-q-day="${tk}" href="javascript:void(0)">برو به امروز</a>`}
        </div>
        <div class="qp-stats">
          <span class="qp-k">کل <b>${fa(cnt.all)}</b></span>
          <span class="qp-k warn">در انتظار تأیید <b>${fa(cnt.pend)}</b></span>
          <span class="qp-k ok">پذیرش‌شده <b>${fa(cnt.done)}</b></span>
          <span class="qp-k bad">لغوشده <b>${fa(cnt.cancel)}</b></span>
        </div>
        <button class="chip ghost${Q.showCancel ? ' on' : ''}" data-q-cancel>نمایش لغوشده‌ها</button>
      </div>
      <div class="qp-cols">${cols}</div>
    </div>`;
  }

  /* =========================================================
     «تاریخچه» — professional archive of past appointments
     - MD3 tonal chips
     - Jalali mini-calendar with range selection
     - Rich rows: code, type, invoice, payment, visit, total
     ========================================================= */
  const HS = {
    scope: '30',      // 'today' | '7' | '30' | 'month' | 'all' | 'range'
    status: 'all',    // 'all' | 'done' | 'cancel'
    doc: 'all',
    type: 'all',
    invoice: 'all',   // 'all' | 'paid' | 'unpaid' | 'partial' | 'none'
    rangeStart: null, // 'yyyy-mm-dd'
    rangeEnd: null,
    calCursor: new Date(TODAY),  // month currently shown in the mini-calendar
    calOpen: false,
  };
  const HSCOPES = [
    { id: 'today', label: 'امروز' },
    { id: '7',     label: '۷ روز اخیر' },
    { id: '30',    label: '۳۰ روز اخیر' },
    { id: 'month', label: 'ماه جاری' },
    { id: 'all',   label: 'همه' },
  ];
  const HSSTATES = [
    { id: 'all',    label: 'همه وضعیت‌ها' },
    { id: 'done',   label: 'پذیرش شده' },
    { id: 'cancel', label: 'لغو شده' },
  ];
  const HSINV = [
    { id: 'all',     label: 'همه فاکتورها' },
    { id: 'paid',    label: 'پرداخت شده' },
    { id: 'unpaid',  label: 'پرداخت نشده' },
    { id: 'partial', label: 'پرداخت جزئی' },
    { id: 'none',    label: 'بدون فاکتور' },
  ];
  const INV_META = {
    paid:    { label: 'پرداخت شده',  cls: 'inv-paid'   },
    unpaid:  { label: 'پرداخت نشده', cls: 'inv-unpaid' },
    partial: { label: 'پرداخت جزئی', cls: 'inv-partial'},
    none:    { label: 'بدون فاکتور', cls: 'inv-none'   },
  };
  const PAY_META = {
    cash:   { label: 'نقد',    ic: 'coins'  },
    card:   { label: 'کارت',   ic: 'card'   },
    online: { label: 'آنلاین', ic: 'zap'    },
  };
  const VISIT_META = {
    complete: { label: 'ویزیت انجام شد',   cls: 'vst-ok'   },
    'no-show': { label: 'عدم مراجعه',       cls: 'vst-bad'  },
    refunded: { label: 'مسترد شد',          cls: 'vst-warn' },
  };

  function histList() {
    const tk = key(TODAY);
    /* history = every past day + today's already-concluded appointments */
    let list = APPTS.filter((a) => visible(a) && matches(a) &&
      (a.date < tk || (a.date === tk && (a.status === 'done' || a.status === 'cancel'))));
    const s = HS.scope;
    if (s === 'today') list = list.filter((a) => a.date === tk);
    else if (s === '7') list = list.filter((a) => a.date >= key(addDays(TODAY, -7)));
    else if (s === '30') list = list.filter((a) => a.date >= key(addDays(TODAY, -30)));
    else if (s === 'month') {
      const m = jp(TODAY).m;
      list = list.filter((a) => jp(new Date(a.date.replace(/-/g, '/'))).m === m);
    }
    else if (s === 'range' && HS.rangeStart && HS.rangeEnd) {
      const lo = HS.rangeStart <= HS.rangeEnd ? HS.rangeStart : HS.rangeEnd;
      const hi = HS.rangeStart <= HS.rangeEnd ? HS.rangeEnd : HS.rangeStart;
      list = list.filter((a) => a.date >= lo && a.date <= hi);
    }
    if (HS.status !== 'all') list = list.filter((a) => a.status === HS.status);
    if (HS.doc !== 'all') list = list.filter((a) => a.doc === HS.doc);
    if (HS.type !== 'all') list = list.filter((a) => a.type === HS.type);
    if (HS.invoice !== 'all') {
      if (HS.invoice === 'none') list = list.filter((a) => !a.invoice);
      else list = list.filter((a) => a.invoice === HS.invoice);
    }
    return list.sort((x, y) => (x.date < y.date ? 1 : x.date > y.date ? -1 : y.start - x.start));
  }

  /* ---------- Jalali mini-calendar with range selection ---------- */
  function histCalMonth() {
    /* returns metadata for the currently-shown Jalali month:
       {label, prevAllowed, days[42] each = {k, d, out, today, inRange, edge, hasHist, isFri}} */
    const cursor = HS.calCursor;
    const monthStart = jMonthStart(cursor);
    const mNum = jp(monthStart).m;
    const yNum = jp(monthStart).y;
    /* build the 6x7 grid: start on the same weekday as monthStart, pad backwards */
    const firstW = wIdx(monthStart);            // 0=Sat..6=Fri (Jalali week starts Sat)
    const gridStart = addDays(monthStart, -firstW);
    /* which dates hold history? — pre-compute per month */
    const histDates = new Set(APPTS.filter((a) => a.date <= key(TODAY)).map((a) => a.date));
    const lo = HS.rangeStart && HS.rangeEnd
      ? (HS.rangeStart <= HS.rangeEnd ? HS.rangeStart : HS.rangeEnd) : null;
    const hi = HS.rangeStart && HS.rangeEnd
      ? (HS.rangeStart <= HS.rangeEnd ? HS.rangeEnd : HS.rangeStart) : null;
    const days = [];
    for (let i = 0; i < 42; i++) {
      const d = addDays(gridStart, i);
      const jd = jp(d);
      const k = key(d);
      const inRange = (lo && hi) ? (k >= lo && k <= hi)
        : (HS.rangeStart && !HS.rangeEnd && k === HS.rangeStart);
      const edge = k === HS.rangeStart || k === HS.rangeEnd;
      days.push({
        k, d: jd.d,
        out: jd.m !== mNum,
        today: sameDay(d, TODAY),
        past: d <= TODAY,
        hasHist: histDates.has(k),
        isFri: wIdx(d) === 6,
        inRange, edge,
      });
    }
    return {
      label: JMONTH[mNum - 1] + ' ' + fa(yNum),
      monthNum: mNum, yearNum: yNum, days,
    };
  }

  function renderHist() {
    const host = el('scHistPanel');
    if (!host) return;
    const list = histList();
    const perDay = new Map();
    list.forEach((a) => perDay.set(a.date, (perDay.get(a.date) || 0) + 1));
    const done = list.filter((a) => a.status === 'done');
    const cancel = list.filter((a) => a.status === 'cancel').length;
    const noShow = list.filter((a) => a.visit === 'no-show').length;
    const rate = list.length ? Math.round((done.length / list.length) * 100) : 0;
    /* revenue = only PAID rows count as collected; outstanding = unpaid + half of partial */
    const revPaid = list.filter((a) => a.invoice === 'paid').reduce((s, a) => s + a.total, 0);
    const revOwed = list.filter((a) => a.invoice === 'unpaid').reduce((s, a) => s + a.total, 0)
                  + list.filter((a) => a.invoice === 'partial').reduce((s, a) => s + Math.round(a.total / 2), 0);

    const scopeChips = HSCOPES.map((s) =>
      `<button class="chip${HS.scope === s.id ? ' on' : ''}" data-h-scope="${s.id}">${s.label}</button>`).join('');
    const rangeChip = `<button class="chip range-chip${HS.scope === 'range' ? ' on' : ''}${HS.calOpen ? ' open' : ''}" data-h-cal-toggle title="انتخاب بازه از تقویم">
        ${I.calendar || ''}
        <span>${HS.scope === 'range' && HS.rangeStart && HS.rangeEnd
          ? bdi(jFull(new Date(HS.rangeStart.replace(/-/g,'/')))) + ' – ' + bdi(jFull(new Date(HS.rangeEnd.replace(/-/g,'/'))))
          : 'انتخاب بازه از تقویم'}</span>
      </button>`;
    const statusChips = HSSTATES.map((s) =>
      `<button class="chip${HS.status === s.id ? ' on' : ''}" data-h-status="${s.id}">${s.label}</button>`).join('');
    const docSel = `<select class="pb-select" data-h-doc>
        <option value="all">همه پزشکان</option>
        ${DOCTORS.map((d) => `<option value="${d.id}"${HS.doc === d.id ? ' selected' : ''}>${d.short}</option>`).join('')}
      </select>`;
    const typeSel = `<select class="pb-select" data-h-type>
        <option value="all">همه انواع نوبت</option>
        ${TYPES.map((t) => `<option value="${t.id}"${HS.type === t.id ? ' selected' : ''}>${t.label}</option>`).join('')}
      </select>`;
    const invSel = `<select class="pb-select" data-h-invoice>
        ${HSINV.map((o) => `<option value="${o.id}"${HS.invoice === o.id ? ' selected' : ''}>${o.label}</option>`).join('')}
      </select>`;

    /* ---- mini-calendar popover (inline, RTL) ---- */
    let calHTML = '';
    if (HS.calOpen) {
      const m = histCalMonth();
      const cells = m.days.map((d) => {
        const cls = [
          'hcal-d',
          d.out ? 'out' : '',
          d.today ? 'today' : '',
          d.hasHist && !d.out ? 'hist' : '',
          d.isFri ? 'fri' : '',
          d.past ? '' : 'future',
          d.inRange ? 'rng' : '',
          d.edge ? 'edge' : '',
        ].filter(Boolean).join(' ');
        return `<button class="${cls}" data-h-pick="${d.k}"${d.past ? '' : ' disabled'}>${fa(d.d)}${d.hasHist && !d.out ? '<i class="hd"></i>' : ''}</button>`;
      }).join('');
      const dowRow = ['ش','ی','د','س','چ','پ','ج'].map((x) => `<span class="hcal-dow">${x}</span>`).join('');
      const hint = HS.rangeStart && !HS.rangeEnd
        ? 'حالا روز پایان بازه را انتخاب کنید'
        : HS.rangeStart && HS.rangeEnd
          ? `بازه انتخاب‌شده: ${bdi(jFull(new Date(HS.rangeStart.replace(/-/g,'/'))))} تا ${bdi(jFull(new Date(HS.rangeEnd.replace(/-/g,'/'))))}`
          : 'برای انتخاب بازه، روی روز شروع و سپس روز پایان کلیک کنید';
      calHTML = `<div class="hcal">
        <div class="hcal-head">
          <button class="icon-btn nav-btn" data-h-cal-nav="-1" title="ماه قبل" aria-label="ماه قبل">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
          </button>
          <b>${m.label}</b>
          <button class="icon-btn nav-btn" data-h-cal-nav="1" title="ماه بعد" aria-label="ماه بعد">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
          </button>
          <span class="hcal-sp"></span>
          <button class="chip sm" data-h-cal-today>امروز</button>
        </div>
        <div class="hcal-grid">
          <div class="hcal-dows">${dowRow}</div>
          <div class="hcal-days">${cells}</div>
        </div>
        <div class="hcal-foot">
          <span class="hcal-hint">${hint}</span>
          <div class="hcal-acts">
            <button class="chip ghost" data-h-cal-clear title="پاک کردن بازه">پاک کردن</button>
            <button class="chip on" data-h-cal-apply${HS.rangeStart && HS.rangeEnd ? '' : ' disabled'}>اعمال بازه</button>
          </div>
        </div>
      </div>`;
    }

    let body = '';
    if (!list.length) {
      body = `<div class="sc-empty"><div class="ic">${I.timeline || I.clock || ''}</div>
        <b>تاریخچه‌ای برای این بازه نیست</b>
        <div>بازه، وضعیت یا فیلترها را تغییر دهید.</div></div>`;
    } else {
      let lastDay = '';
      list.forEach((a) => {
        if (a.date !== lastDay) {
          lastDay = a.date;
          const d = new Date(a.date.replace(/-/g, '/'));
          const isToday = sameDay(d, TODAY);
          body += `<div class="al-day${isToday ? ' today' : ''}">
            <span class="al-dow">${JDOW[wIdx(d)]}${isToday ? ' · امروز' : ''}</span>
            <b>${jFull(d)}</b>
            <button class="al-cal" data-h-cal="${a.date}" title="مشاهدهٔ این روز در تقویم">${I.calendar || ''} مشاهده در تقویم</button>
            <span class="al-cnt">${fa(perDay.get(a.date))} نوبت</span>
          </div>`;
        }
        const t = typeOf(a.type), doc = docOf(a.doc), st = STATUS[a.status];
        const invMeta = a.invoice ? INV_META[a.invoice] : INV_META.none;
        const payMeta = a.pay ? PAY_META[a.pay] : null;
        const vstMeta = a.visit ? VISIT_META[a.visit] : null;
        const priceTxt = fa(a.total.toLocaleString('en-US'));
        body += `<button class="hs-row st-${a.status}" data-ev="${a.id}"
          title="${a.pet} · ${t.label} · ${doc.name} · ${st.label}">
          <span class="al-time">
            <b>${hhmm(a.start)}</b>
            <span>${hhmm(a.start + a.dur)}</span>
          </span>
          <span class="avatar-pet ${a.av}">${I[a.ic] || ''}</span>
          <span class="hs-main">
            <span class="hs-l1">
              <b>${a.pet}</b>
              <span class="al-type ${t.cls}">${t.label}</span>
              <span class="hs-code">${a.code}</span>
              <span class="pill ${st.pill}">${st.label}</span>
              ${vstMeta ? `<span class="vst ${vstMeta.cls}">${vstMeta.label}</span>` : ''}
            </span>
            <span class="hs-l2">
              ${I.stethoscope || I.doc || ''}<b>${doc.short}</b>
              <span class="sep">·</span>
              مالک: ${a.owner}
              <span class="sep">·</span>
              ${a.kind}
              <span class="sep">·</span>
              ${I.phone || ''} <span class="hs-phone">${a.phone}</span>
            </span>
            ${a.note ? `<span class="hs-l3">${I.msg || I.edit || ''} ${a.note}</span>` : ''}
          </span>
          <span class="hs-fin">
            <span class="inv ${invMeta.cls}">
              ${I.receipt || I.file || ''}
              <span>${invMeta.label}</span>
            </span>
            ${payMeta ? `<span class="pay">${I[payMeta.ic] || I.wallet || ''} ${payMeta.label}</span>` : ''}
            <b class="hs-price">${priceTxt}<small>تومان</small></b>
          </span>
        </button>`;
      });
    }

    host.innerHTML = `<div class="hs-shell">
      <!-- tier 1 · quick scope chips + custom range -->
      <div class="hs-toolbar hs-tier1">
        <span class="sc-flabel">بازه زمانی</span>
        <div class="sc-fgroup">${scopeChips}${rangeChip}</div>
        <span class="al-total">${fa(list.length)} نوبت${S.q ? ` · نتیجهٔ «${S.q}»` : ''}</span>
      </div>
      ${calHTML}
      <!-- tier 2 · status + doctor + type + invoice -->
      <div class="hs-toolbar hs-tier2">
        <span class="sc-flabel">وضعیت</span>
        <div class="sc-fgroup">${statusChips}</div>
        <span class="sc-div"></span>
        <span class="sc-flabel">فیلترها</span>
        ${docSel}${typeSel}${invSel}
      </div>
      <div class="hs-stats">
        <div class="hs-k"><span class="ic">${I.rows || I.gridIc || ''}</span><b>${fa(list.length)}</b><span>کل نوبت‌ها</span></div>
        <div class="hs-k ok"><span class="ic">${I.check || ''}</span><b>${fa(done.length)}</b><span>پذیرش شده</span></div>
        <div class="hs-k bad"><span class="ic">${I.x || ''}</span><b>${fa(cancel)}</b><span>لغو شده</span></div>
        <div class="hs-k warn"><span class="ic">${I.alert || I.clock || ''}</span><b>${fa(noShow)}</b><span>عدم مراجعه</span></div>
        <div class="hs-k ok"><span class="ic">${I.coins || I.wallet || ''}</span><b>${fa(revPaid.toLocaleString('en-US'))}</b><span>درآمد وصولی (تومان)</span></div>
        <div class="hs-k bad"><span class="ic">${I.alert || I.wallet || ''}</span><b>${fa(revOwed.toLocaleString('en-US'))}</b><span>مطالبات معوق (تومان)</span></div>
      </div>
      <div class="hs-list">${body}</div>
    </div>`;

    /* wire the <select> change handlers (they can't use event delegation on click) */
    const wire = (sel, key) => {
      const s = host.querySelector(sel);
      if (s) s.addEventListener('change', () => { HS[key] = s.value; renderHist(); });
    };
    wire('[data-h-doc]', 'doc');
    wire('[data-h-type]', 'type');
    wire('[data-h-invoice]', 'invoice');
  }

    /* ---------------- public API ---------------- */
  window.SCH = {
    S, TYPES, DOCTORS, PETS, STATUS, APPTS, H0, H1,
    fa, hhmm, bdi, rangeTxt, key, jFull, jDate, jp, jMonthStart, weekStart, JDOW, JDOW_S, JMONTH,
    addDays, dstart, wIdx, icon, typeOf, docOf, TODAY,
    render,
    goToday() { S.cursor = new Date(TODAY); render(); },
    step,
    goto(dateKey, view) { S.cursor = new Date(dateKey.replace(/-/g, '/')); if (view) S.view = view; render(); },
    setTab,
    search(v) {
      S.q = v;
      document.querySelector('.sc-search').classList.toggle('has', !!v);
      render();
      if (S.tab === 'queue') renderQueue();
      else if (S.tab === 'hist') renderHist();
    },
    clearSearch() { const i = el('scQ'); i.value = ''; this.search(''); i.focus(); },
    toggleCancel() { S.hideCancel = !S.hideCancel; render(); },
    get(id) { return APPTS.find((a) => a.id === id); },
    add(a) { a.id = 'a' + uid++; APPTS.push(a); return a; },
    freeSlots(dateKey, docId, dur) {
      const busy = APPTS.filter((a) => a.date === dateKey && a.doc === docId && a.status !== 'cancel');
      const out = [];
      const step = dur < 30 ? 15 : 30;
      for (let m = H0 * 60; m + dur <= H1 * 60; m += step) {
        const clash = busy.some((b) => m < b.start + b.dur && b.start < m + dur);
        out.push({ m, free: !clash });
      }
      return out;
    },
    toast(msg, icon) {
      let w = document.querySelector('.toast-wrap');
      if (!w) { w = document.createElement('div'); w.className = 'toast-wrap'; document.body.appendChild(w); }
      const t = document.createElement('div');
      t.className = 'toast';
      t.innerHTML = `<span class="ic">${I[icon || 'check'] || ''}</span><span>${msg}</span>`;
      w.appendChild(t);
      setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 300); }, 2600);
    },
  };

  document.addEventListener('keydown', (e) => {
    if (/input|textarea|select/i.test((e.target.tagName || ''))) return;
    /* calendar shortcuts must not fire while the booking sheet is open */
    if (window.SCH.isBookOpen && window.SCH.isBookOpen()) return;
    if (e.key === '1') { setTab('cal'); return; }
    if (e.key === '2') { setTab('queue'); return; }
    if (e.key === '3') { setTab('hist'); return; }
    if (S.tab !== 'cal') return;                        // calendar shortcuts stay on the calendar tab
    if (e.key === 'ArrowLeft') step(1);
    else if (e.key === 'ArrowRight') step(-1);
    else if (e.key === 't' || e.key === 'T') window.SCH.goToday();
    else if (e.key === 'd') { S.view = 'day'; render(); }
    else if (e.key === 'w') { S.view = 'week'; render(); }
    else if (e.key === 'm') { S.view = 'month'; render(); }
  });
})();
