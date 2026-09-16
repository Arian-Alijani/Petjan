/* =========================================================
   وت‌پلاس · تاریخچه — دفتر رویدادهای مدیریتی
   Aggregates events from ALL data sources into a unified
   chronological ledger for the manager view.
   ========================================================= */
(function () {
  'use strict';

  /* ── helpers ────────────────────────────────────────── */
  const fa   = (v) => (window.fa ? window.fa(v) : String(v).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]));
  const money = (n) => fa(Number(n).toLocaleString('en-US'));
  const el   = (id) => document.getElementById(id);
  const I    = window.Icons || {};
  const J    = window.J;

  /* Doctor name lookup */
  function docName(id) {
    if (!id) return '';
    const CLINIC = window.CLINIC;
    if (CLINIC && CLINIC.DOCTORS) {
      const d = CLINIC.DOCTORS.find(x => x.id === id);
      if (d) return d.short || d.name || id;
    }
    return id;
  }

  /* Jalali → 'yyyy/mm/dd' key ─────────────────────────── */
  function todayKey() {
    if (J && J.today) return J.today();
    return window.TODAY_J || '1405/08/09';
  }
  function dateKey(d /* Date */) {
    if (J && J.fromDay && J.toDay) return J.fromDay(J.toDay(todayKey()) - daysDiff(d));
    return todayKey();
  }
  function daysDiff(d /* Date or 'yyyy/mm/dd' */) {
    if (typeof d === 'string') {
      // already a jdate string — treat as 0 diff
      return 0;
    }
    const now = new Date();
    return Math.round((now - d) / 86400000);
  }
  /** Make a numeric sortable timestamp from a jdate string + optional hhmm */
  function jSort(jdate, hhmm = '00:00') {
    // e.g. '1405/08/09' '14:30' → 140508091430
    const [h, m] = hhmm.split(':').map(Number);
    const [y, mo, d] = jdate.split('/').map(Number);
    return y * 1e10 + mo * 1e8 + d * 1e6 + (h || 0) * 1e4 + (m || 0) * 1e2;
  }

  /* ── Event category registry ─────────────────────────── */
  const CATEGORIES = {
    sale:        { label: 'فروش / درآمد',   icon: 'cart',      color: '#0d9488', bg: 'var(--ok-bg)',    chipColor: 'var(--ok)',    chipBg: 'var(--ok-bg)' },
    appointment: { label: 'نوبت',           icon: 'calendar',  color: '#5c8ad6', bg: 'var(--info-bg)',  chipColor: 'var(--info)',  chipBg: 'var(--info-bg)' },
    patient:     { label: 'ثبت بیمار',      icon: 'paw',       color: '#b8a2e6', bg: 'var(--lilac-bg)', chipColor: 'var(--lilac)', chipBg: 'var(--lilac-bg)' },
    vaccine:     { label: 'واکسن',          icon: 'syringe',   color: '#0d9488', bg: 'var(--ok-bg)',    chipColor: 'var(--ok)',    chipBg: 'var(--ok-bg)' },
    expense:     { label: 'هزینه',          icon: 'receipt',   color: '#d92d20', bg: 'var(--danger-bg)',chipColor: 'var(--danger)',chipBg: 'var(--danger-bg)' },
    inventory:   { label: 'انبار / کالا',   icon: 'box',       color: '#e0a95b', bg: 'var(--warn-bg)',  chipColor: 'var(--warn)',  chipBg: 'var(--warn-bg)' },
    cheque:      { label: 'چک',             icon: 'bank',      color: '#667085', bg: 'var(--bg-2)',     chipColor: 'var(--muted)', chipBg: 'var(--bg-2)' },
    boarding:    { label: 'پانسیون',        icon: 'home',      color: '#b8a2e6', bg: 'var(--lilac-bg)', chipColor: 'var(--lilac)', chipBg: 'var(--lilac-bg)' },
    visit:       { label: 'ویزیت',          icon: 'clipboard', color: '#5c8ad6', bg: 'var(--info-bg)',  chipColor: 'var(--info)',  chipBg: 'var(--info-bg)' },
    purchase:    { label: 'خرید / تأمین',   icon: 'truck',     color: '#e08a5c', bg: 'var(--warn-bg)',  chipColor: 'var(--warn)',  chipBg: 'var(--warn-bg)' },
  };

  /* ── STATUS chip map ─────────────────────────────────── */
  const STATUS = {
    paid:     { label: 'پرداخت شد',    cls: 'ok'     },
    partial:  { label: 'نیمه‌پرداخت',  cls: 'warn'   },
    unpaid:   { label: 'پرداخت نشده',  cls: 'danger' },
    done:     { label: 'انجام شد',     cls: 'ok'     },
    cancel:   { label: 'لغو شده',      cls: 'danger' },
    confirm:  { label: 'تأیید شده',    cls: 'ok'     },
    pending:  { label: 'در انتظار',    cls: 'warn'   },
    new:      { label: 'جدید',         cls: 'info'   },
    overdue:  { label: 'معوق',         cls: 'danger' },
    cleared:  { label: 'تسویه شد',     cls: 'ok'     },
    bounced:  { label: 'برگشت خورد',   cls: 'danger' },
    low:      { label: 'موجودی کم',    cls: 'warn'   },
    normal:   { label: 'عادی',         cls: 'mute'   },
    discharge:{ label: 'ترخیص',        cls: 'ok'     },
    checkin:  { label: 'ورود',         cls: 'info'   },
  };

  /* ── Event builders from data sources ───────────────── */

  /** Generate sale events from FinanceData.DAILY finance series */
  function buildSaleEvents() {
    const evs = [];
    const FD = window.FinanceData;
    if (!FD) return evs;
    const DAILY = FD.DAILY || [];
    const channels = FD.REVENUE_CHANNELS || [];
    DAILY.slice(-60).forEach((day) => {
      // rev is array of channel amounts
      const revArr = Array.isArray(day.rev) ? day.rev : [];
      const total = revArr.reduce((s, r) => s + r, 0);
      if (!total) return;
      const chIdx = Math.abs(day.date ? day.date.charCodeAt(5) : 0) % Math.max(channels.length, 1);
      const ch = channels[chIdx];
      evs.push({
        id:     'sale-' + day.date,
        cat:    'sale',
        jdate:  day.date,
        time:   '',
        title:  'درآمد روزانه' + (ch ? ' — ' + ch.label : ''),
        sub:    `${fa(day.visits || 0)} ویزیت · ${fa(day.rx || 0)} نسخه`,
        entity: '',
        amount: total,
        amtSign:1,
        status: 'paid',
        sort:   jSort(day.date, '23:59'),
      });
    });
    return evs;
  }

  /** Appointments events from CLINIC.QUEUE */
  function buildAppointmentEvents() {
    const evs = [];
    const CLINIC = window.CLINIC;
    const APTS = (CLINIC && CLINIC.QUEUE) ? CLINIC.QUEUE : (window.APTS || window.APPOINTMENTS || []);
    APTS.forEach((a, i) => {
      const pet  = window.petById  && window.petById(a.petId);
      const owner= window.ownerById && window.ownerById(a.ownerId);
      const status = a.status === 'done'    ? 'done'
                   : a.status === 'cancel'  ? 'cancel'
                   : a.status === 'confirm' ? 'confirm' : 'pending';
      evs.push({
        id:      'apt-' + (a.id || i),
        cat:     'appointment',
        jdate:   a.date || todayKey(),
        time:    a.time || '',
        title:   (a.type || 'نوبت') + (pet ? ' — ' + pet.name : ''),
        sub:     owner ? (window.ownerName ? window.ownerName(owner) : owner.first + ' ' + owner.last) : '',
        entity:  docName(a.doc) || a.doc || '',
        amount:  a.fee || 0,
        amtSign: 1,
        status,
        sort:    jSort(a.date || todayKey(), a.time || '00:00'),
      });
    });
    return evs;
  }

  /** Patient registration events from PETS */
  function buildPatientEvents() {
    const evs = [];
    const PETS = window.PETS || [];
    PETS.forEach((pet, i) => {
      if (!pet.createdAt && !pet.joined) return;
      const jdate = pet.createdAt || pet.joined || todayKey();
      const owner = window.ownerById && window.ownerById(pet.ownerId);
      evs.push({
        id:      'pet-' + (pet.id || i),
        cat:     'patient',
        jdate,
        time:    '',
        title:   'ثبت بیمار جدید — ' + pet.name,
        sub:     window.SPECIES && pet.species ? (window.SPECIES[pet.species] || pet.species) : pet.species || '',
        entity:  owner ? (owner.first + ' ' + owner.last) : '',
        amount:  0,
        amtSign: 0,
        status:  'new',
        sort:    jSort(jdate),
      });
    });
    return evs;
  }

  /** Vaccine events */
  function buildVaccineEvents() {
    const evs = [];
    const PETS = window.PETS || [];
    PETS.forEach((pet) => {
      (pet.vaccines || []).forEach((v, i) => {
        const jdate = v.date || todayKey();
        evs.push({
          id:      'vac-' + (pet.id || '') + '-' + i,
          cat:     'vaccine',
          jdate,
          time:    '',
          title:   (v.name || v.type || 'واکسن') + ' — ' + pet.name,
          sub:     v.dose ? ('دوز ' + fa(v.dose)) : '',
          entity:  v.doc || '',
          amount:  v.fee || 0,
          amtSign: v.fee ? 1 : 0,
          status:  v.next ? 'done' : 'done',
          sort:    jSort(jdate),
        });
      });
    });
    return evs;
  }

  /** Visit events from CLINIC.ENCOUNTERS */
  function buildVisitEvents() {
    const evs = [];
    const CLINIC = window.CLINIC;
    if (CLINIC && CLINIC.ENCOUNTERS && CLINIC.ENCOUNTERS.length) {
      CLINIC.ENCOUNTERS.forEach((enc, i) => {
        if (!enc.date) return;
        const pet = window.petById && window.petById(enc.petId);
        evs.push({
          id:      'vis-enc-' + (enc.id || i),
          cat:     'visit',
          jdate:   enc.date,
          time:    enc.time || '',
          title:   (enc.type || 'ویزیت') + (pet ? ' — ' + pet.name : ''),
          sub:     enc.complaint || enc.note || '',
          entity:  docName(enc.doc) || enc.doc || '',
          amount:  enc.fee || 0,
          amtSign: enc.fee ? 1 : 0,
          status:  enc.paid ? 'paid' : (enc.fee ? 'unpaid' : 'done'),
          sort:    jSort(enc.date, enc.time || '00:00'),
        });
      });
      return evs;
    }
    // fallback to PETS.visits
    const PETS = window.PETS || [];
    PETS.forEach((pet) => {
      (pet.visits || []).forEach((v, i) => {
        const jdate = v.date || todayKey();
        evs.push({
          id:      'vis-' + (pet.id || '') + '-' + i,
          cat:     'visit',
          jdate,
          time:    '',
          title:   (v.type || 'ویزیت') + ' — ' + pet.name,
          sub:     v.complaint || v.note || '',
          entity:  v.doc || '',
          amount:  v.fee || 0,
          amtSign: v.fee ? 1 : 0,
          status:  v.paid ? 'paid' : (v.fee ? 'unpaid' : 'done'),
          sort:    jSort(jdate),
        });
      });
    });
    return evs;
  }

  /** Expense events — from FinanceData or window.EXPENSES */
  function buildExpenseEvents() {
    const evs = [];
    const FD = window.FinanceData;
    // FinanceData has no separate expense array by default; use window.EXPENSES if available
    const EXP = window.EXPENSES || (FD && FD.EXPENSES) || [];
    EXP.forEach((ex, i) => {
      evs.push({
        id:      'exp-' + (ex.id || i),
        cat:     'expense',
        jdate:   ex.date || todayKey(),
        time:    '',
        title:   ex.title || ex.label || 'هزینه',
        sub:     ex.category || '',
        entity:  ex.vendor || ex.by || '',
        amount:  ex.amount || 0,
        amtSign: -1,
        status:  ex.status || 'paid',
        sort:    jSort(ex.date || todayKey()),
      });
    });
    return evs;
  }

  /** Inventory / product low-stock events from window.INV */
  function buildInventoryEvents() {
    const evs = [];
    const INV = window.INV ? window.INV.PRODUCTS : (window.PRODUCTS || window.INVENTORY || []);
    INV.forEach((p, i) => {
      if (p.qty < (p.minQty || 5)) {
        evs.push({
          id:      'inv-low-' + (p.id || i),
          cat:     'inventory',
          jdate:   todayKey(),
          time:    '',
          title:   'هشدار موجودی — ' + (p.name || 'کالا'),
          sub:     `موجودی: ${fa(p.qty)} ${p.unit || 'عدد'}`,
          entity:  p.brand || p.category || '',
          amount:  p.price || 0,
          amtSign: 0,
          status:  'low',
          sort:    jSort(todayKey(), '08:00'),
        });
      }
    });
    // Also add restock events if any
    const RESTOCKS = window.RESTOCKS || [];
    RESTOCKS.forEach((r, i) => {
      evs.push({
        id:      'rst-' + (r.id || i),
        cat:     'purchase',
        jdate:   r.date || todayKey(),
        time:    '',
        title:   'خرید — ' + (r.name || 'کالا'),
        sub:     `${fa(r.qty)} ${r.unit || 'عدد'} · ${r.supplier || ''}`,
        entity:  r.supplier || '',
        amount:  r.total || (r.price * r.qty) || 0,
        amtSign: -1,
        status:  r.paid ? 'paid' : 'unpaid',
        sort:    jSort(r.date || todayKey()),
      });
    });
    return evs;
  }

  /** Cheque events */
  function buildChequeEvents() {
    const evs = [];
    const CQ = window.CHEQUES || [];
    CQ.forEach((c, i) => {
      const status = c.status === 'cleared'  ? 'cleared'
                   : c.status === 'bounced'  ? 'bounced'
                   : c.status === 'overdue'  ? 'overdue' : 'pending';
      evs.push({
        id:      'chq-' + (c.id || i),
        cat:     'cheque',
        jdate:   c.date || c.dueDate || todayKey(),
        time:    '',
        title:   (c.type === 'in' ? 'دریافت چک' : 'پرداخت چک') + ' — ' + (c.bank || ''),
        sub:     c.desc || c.note || '',
        entity:  c.party || c.owner || '',
        amount:  c.amount || 0,
        amtSign: c.type === 'in' ? 1 : -1,
        status,
        sort:    jSort(c.date || todayKey()),
      });
    });
    return evs;
  }

  /** Boarding events from window.STAYS or window.BOARDINGS */
  function buildBoardingEvents() {
    const evs = [];
    const STAYS = window.STAYS || window.BOARDINGS || window.BOARDING_STAYS || [];
    STAYS.forEach((s, i) => {
      const status = s.status === 'active'     ? 'checkin'
                   : s.status === 'discharged' ? 'discharge' : 'pending';
      const jdate  = s.checkIn || s.startDate || todayKey();
      const pet    = window.petById && window.petById(s.petId);
      evs.push({
        id:      'brd-' + (s.id || i),
        cat:     'boarding',
        jdate,
        time:    '',
        title:   (status === 'discharge' ? 'ترخیص پانسیون' : 'ورود پانسیون')
                 + (pet ? ' — ' + pet.name : ''),
        sub:     s.checkOut ? ('خروج: ' + s.checkOut) : (s.days ? `${fa(s.days)} شب` : ''),
        entity:  s.room || s.cabinId || '',
        amount:  s.total || s.fee || 0,
        amtSign: 1,
        status,
        sort:    jSort(jdate),
      });
    });
    return evs;
  }

  /* ── Synthetic mock events (when data sources don't have enough) ── */
  function buildMockEvents() {
    /* Deterministic PRNG */
    let seed = 20250831;
    const rng = () => { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return Math.abs(seed) / 2147483647; };

    const TODAY_J = todayKey();
    const addDaysJ = (jdate, n) => {
      if (!J || !J.toDay || !J.fromDay) return jdate;
      return J.fromDay(J.toDay(jdate) + n);
    };

    const petNames   = ['کوکو', 'سیمبا', 'لو', 'نانا', 'آبی', 'توتی', 'بلا', 'میلو', 'رکسی', 'گوچی'];
    const ownerNames = ['حسین مرادی', 'امیر رستگار', 'الهام صادقی', 'لیلا کریمی', 'رضا نوروزی'];
    const doctors    = ['دکتر مینا همتی', 'دکتر آریا نیک', 'دکتر سهیل کاویانی'];
    const types      = ['ویزیت عمومی', 'آرایش و پیرایش', 'جراحی', 'دندانپزشکی', 'آزمایشگاه'];
    const payMethods = ['کارتخوان', 'نقدی', 'انتقال'];
    const drugNames  = ['آموکسی‌سیلین', 'مترونیدازول', 'پردنیزولون', 'ویتامین B12', 'آنتی‌فلی'];
    const banks      = ['ملت', 'ملی', 'سامان', 'تجارت'];

    const evs = [];

    /* last 45 days of sale/expense mock */
    for (let i = 0; i < 45; i++) {
      const jdate = addDaysJ(TODAY_J, -i);
      const dow = rng() > 0.5;

      /* daily revenue */
      const rev = Math.round((2000000 + rng() * 8000000) * (dow ? 1.2 : 0.7) / 50000) * 50000;
      const visitCount = 3 + Math.floor(rng() * 15);
      const ch = ['خدمات درمانی', 'داروخانه', 'پت‌شاپ', 'آزمایشگاه'][Math.floor(rng() * 4)];
      evs.push({
        id: 'mock-sale-' + i, cat: 'sale',
        jdate, time: '۱۸:۰۰',
        title: 'بستن صندوق — ' + ch,
        sub: `${fa(visitCount)} مراجعه · ${payMethods[Math.floor(rng()*3)]}`,
        entity: doctors[Math.floor(rng() * doctors.length)],
        amount: rev, amtSign: 1, status: 'paid',
        sort: jSort(jdate, '18:00'),
      });

      /* some individual service sales */
      if (rng() > 0.4) {
        const fee = Math.round((150000 + rng() * 850000) / 10000) * 10000;
        const pet = petNames[Math.floor(rng() * petNames.length)];
        const doc = doctors[Math.floor(rng() * doctors.length)];
        const type = types[Math.floor(rng() * types.length)];
        evs.push({
          id: 'mock-svc-' + i, cat: 'visit',
          jdate, time: fa(8 + Math.floor(rng() * 9)) + ':' + (rng() > .5 ? '۰۰' : '۳۰'),
          title: type + ' — ' + pet,
          sub: ownerNames[Math.floor(rng() * ownerNames.length)],
          entity: doc,
          amount: fee, amtSign: 1,
          status: rng() > .2 ? 'paid' : (rng() > .5 ? 'partial' : 'unpaid'),
          sort: jSort(jdate, (8 + Math.floor(rng() * 9)) + ':' + (rng() > .5 ? '00' : '30')),
        });
      }

      /* expense */
      if (rng() > 0.55) {
        const cats = ['حقوق', 'اجاره', 'خرید دارو', 'بازاریابی', 'تعمیر'];
        const expAmt = Math.round((300000 + rng() * 4500000) / 100000) * 100000;
        evs.push({
          id: 'mock-exp-' + i, cat: 'expense',
          jdate, time: '',
          title: cats[Math.floor(rng() * cats.length)],
          sub: '',
          entity: '',
          amount: expAmt, amtSign: -1, status: 'paid',
          sort: jSort(jdate, '10:00'),
        });
      }

      /* appointment */
      if (rng() > 0.5) {
        const pet = petNames[Math.floor(rng() * petNames.length)];
        const st = rng() > .6 ? 'done' : (rng() > .5 ? 'confirm' : 'cancel');
        evs.push({
          id: 'mock-apt-' + i, cat: 'appointment',
          jdate,
          time: fa(9 + Math.floor(rng() * 7)) + ':' + (rng() > .5 ? '۰۰' : '۳۰'),
          title: types[Math.floor(rng() * types.length)] + ' — ' + pet,
          sub: ownerNames[Math.floor(rng() * ownerNames.length)],
          entity: doctors[Math.floor(rng() * doctors.length)],
          amount: 0, amtSign: 0, status: st,
          sort: jSort(jdate, (9 + Math.floor(rng() * 7)) + ':' + (rng() > .5 ? '00' : '30')),
        });
      }

      /* drug sale */
      if (rng() > 0.65) {
        const drug = drugNames[Math.floor(rng() * drugNames.length)];
        const qty  = 1 + Math.floor(rng() * 4);
        const fee  = Math.round((50000 + rng() * 350000) / 5000) * 5000;
        evs.push({
          id: 'mock-drug-' + i, cat: 'sale',
          jdate, time: '',
          title: 'فروش دارو — ' + drug,
          sub: `${fa(qty)} بسته · ${ownerNames[Math.floor(rng() * ownerNames.length)]}`,
          entity: '',
          amount: fee * qty, amtSign: 1, status: 'paid',
          sort: jSort(jdate, '11:00'),
        });
      }

      /* vaccine */
      if (rng() > 0.72) {
        const pet = petNames[Math.floor(rng() * petNames.length)];
        const vacNames = ['سه‌گانه گربه', 'هاری', 'پاروو', 'لپتوسپیروز', 'دیستمپر'];
        evs.push({
          id: 'mock-vac-' + i, cat: 'vaccine',
          jdate, time: '',
          title: vacNames[Math.floor(rng() * vacNames.length)] + ' — ' + pet,
          sub: ownerNames[Math.floor(rng() * ownerNames.length)],
          entity: doctors[Math.floor(rng() * doctors.length)],
          amount: Math.round((120000 + rng() * 280000) / 10000) * 10000,
          amtSign: 1, status: 'done',
          sort: jSort(jdate, '09:30'),
        });
      }

      /* cheque */
      if (rng() > 0.82) {
        const bank = banks[Math.floor(rng() * banks.length)];
        const amt  = Math.round((1000000 + rng() * 15000000) / 500000) * 500000;
        const st   = rng() > .7 ? 'cleared' : (rng() > .5 ? 'pending' : 'overdue');
        evs.push({
          id: 'mock-chq-' + i, cat: 'cheque',
          jdate, time: '',
          title: (rng() > .5 ? 'دریافت چک' : 'پرداخت چک') + ' — ' + bank,
          sub: ownerNames[Math.floor(rng() * ownerNames.length)],
          entity: bank,
          amount: amt, amtSign: rng() > .5 ? 1 : -1, status: st,
          sort: jSort(jdate, '10:00'),
        });
      }

      /* boarding */
      if (rng() > 0.82) {
        const pet = petNames[Math.floor(rng() * petNames.length)];
        const days = 1 + Math.floor(rng() * 7);
        evs.push({
          id: 'mock-brd-' + i, cat: 'boarding',
          jdate, time: '',
          title: (rng() > .5 ? 'ورود پانسیون' : 'ترخیص پانسیون') + ' — ' + pet,
          sub: `${fa(days)} شب · ${ownerNames[Math.floor(rng() * ownerNames.length)]}`,
          entity: 'کابین ' + fa(1 + Math.floor(rng() * 8)),
          amount: days * Math.round((150000 + rng() * 200000) / 50000) * 50000,
          amtSign: 1,
          status: rng() > .4 ? 'discharge' : 'checkin',
          sort: jSort(jdate),
        });
      }

      /* patient registration (rare) */
      if (rng() > 0.88) {
        const pet = petNames[Math.floor(rng() * petNames.length)];
        const spp = ['گربه', 'سگ', 'پرنده', 'خرگوش'][Math.floor(rng() * 4)];
        evs.push({
          id: 'mock-pet-' + i, cat: 'patient',
          jdate, time: '',
          title: 'ثبت بیمار جدید — ' + pet,
          sub: spp,
          entity: ownerNames[Math.floor(rng() * ownerNames.length)],
          amount: 0, amtSign: 0, status: 'new',
          sort: jSort(jdate, '08:30'),
        });
      }

      /* inventory purchase */
      if (rng() > 0.78) {
        const drug = drugNames[Math.floor(rng() * drugNames.length)];
        const qty  = 10 + Math.floor(rng() * 90);
        const amt  = Math.round(qty * (30000 + rng() * 120000) / 10000) * 10000;
        evs.push({
          id: 'mock-inv-' + i, cat: 'purchase',
          jdate, time: '',
          title: 'خرید — ' + drug,
          sub: `${fa(qty)} عدد`,
          entity: 'تأمین‌کننده',
          amount: amt, amtSign: -1, status: rng() > .3 ? 'paid' : 'unpaid',
          sort: jSort(jdate, '11:00'),
        });
      }
    }
    return evs;
  }

  /* ── Aggregate all events ─────────────────────────────── */
  function buildAllEvents() {
    let evs = [];

    // Prefer real data if available, fill with mock for the rest
    const realSale = buildSaleEvents();
    const realApt  = buildAppointmentEvents();
    const realPet  = buildPatientEvents();
    const realVac  = buildVaccineEvents();
    const realVis  = buildVisitEvents();
    const realExp  = buildExpenseEvents();
    const realInv  = buildInventoryEvents();
    const realChq  = buildChequeEvents();
    const realBrd  = buildBoardingEvents();

    const realTotal = realSale.length + realApt.length + realPet.length
                    + realVac.length + realVis.length + realExp.length
                    + realInv.length + realChq.length + realBrd.length;

    evs = [...realSale, ...realApt, ...realPet, ...realVac, ...realVis,
           ...realExp, ...realInv, ...realChq, ...realBrd];

    // Always add mock events for richness (won't duplicate by id prefix)
    if (realTotal < 20) {
      evs = [...evs, ...buildMockEvents()];
    } else {
      // Still add a few mock events for categories with no data
      const mockEvs = buildMockEvents();
      const presentCats = new Set(evs.map(e => e.cat));
      const cats = ['sale', 'expense', 'cheque', 'boarding', 'purchase', 'inventory'];
      cats.forEach(c => {
        if (!presentCats.has(c)) {
          evs.push(...mockEvs.filter(e => e.cat === c).slice(0, 5));
        }
      });
      if (evs.length < 30) evs.push(...mockEvs.slice(0, 30 - evs.length));
    }

    // Sort descending (newest first)
    evs.sort((a, b) => b.sort - a.sort);

    // De-dup by id
    const seen = new Set();
    return evs.filter(e => { if (seen.has(e.id)) return false; seen.add(e.id); return true; });
  }

  /* ── State ──────────────────────────────────────────────── */
  const ALL_EVENTS = buildAllEvents();

  const HS = {
    period: '30',      // 'today' | '7' | '30' | 'month' | 'all'
    cats:   new Set(), // empty = all
    search: '',
    sortKey:'sort',    // 'sort' | 'amount'
    sortDir: -1,       // -1=desc 1=asc
    page:    1,
    pageSize: 40,
  };

  /* ── Period filter ─────────────────────────────────────── */
  function periodStart(period) {
    const TODAY_J = todayKey();
    if (!J || !J.toDay || !J.fromDay) return null;
    const todayN = J.toDay(TODAY_J);
    const limits = { today: 0, '7': 6, '30': 29, month: 30 };
    if (period === 'today')  return J.fromDay(todayN);
    if (period === '7')      return J.fromDay(todayN - 6);
    if (period === '30')     return J.fromDay(todayN - 29);
    if (period === 'month')  return J.fromDay(todayN - 29);
    return null; // 'all'
  }

  function inPeriod(ev) {
    if (HS.period === 'all') return true;
    const start = periodStart(HS.period);
    if (!start) return true;
    return ev.jdate >= start;
  }

  function filterEvents() {
    return ALL_EVENTS.filter(ev => {
      if (!inPeriod(ev)) return false;
      if (HS.cats.size && !HS.cats.has(ev.cat)) return false;
      if (HS.search) {
        const q = HS.search;
        if (!ev.title.includes(q) && !ev.sub.includes(q) && !ev.entity.includes(q)) return false;
      }
      return true;
    });
  }

  /* ── KPI computation ────────────────────────────────────── */
  function computeKpi(evs) {
    let income = 0, expense = 0, aptCnt = 0, patCnt = 0;
    evs.forEach(ev => {
      if (ev.amtSign === 1)  income  += ev.amount;
      if (ev.amtSign === -1) expense += ev.amount;
      if (ev.cat === 'appointment') aptCnt++;
      if (ev.cat === 'patient')     patCnt++;
    });
    return { income, expense, apt: aptCnt, pat: patCnt, total: evs.length };
  }

  /* ── Render helpers ─────────────────────────────────────── */
  function catBadge(cat) {
    const c = CATEGORIES[cat] || CATEGORIES.sale;
    const icon = I[c.icon] || '';
    return `<span class="hy-cat-badge" style="background:${c.bg};color:${c.color}">${icon}</span>`;
  }

  function statusChip(status) {
    const s = STATUS[status] || STATUS.normal;
    return `<span class="hy-status ${s.cls}"><span class="hy-status-dot"></span>${s.label}</span>`;
  }

  function amtCell(ev) {
    if (!ev.amount && ev.amtSign === 0) return `<span class="hy-amt neutral">—</span>`;
    const sign = ev.amtSign < 0 ? '−' : '+';
    const cls  = ev.amtSign < 0 ? 'neg' : 'pos';
    return `<span class="hy-amt ${cls}">${sign}${money(ev.amount)}</span>`;
  }

  /* Group events by day */
  function groupByDay(evs) {
    const map = new Map();
    evs.forEach(ev => {
      const k = ev.jdate || todayKey();
      if (!map.has(k)) map.set(k, []);
      map.get(k).push(ev);
    });
    return Array.from(map.entries()).sort((a, b) => b[0] > a[0] ? 1 : -1);
  }

  /* Friendly Jalali date label */
  function jdateLabel(jdate) {
    if (!J || !J.MONTHS || !J.toDay || !J.fromDay) return jdate;
    const todayN = J.toDay(todayKey());
    const evN    = J.toDay(jdate);
    const diff   = todayN - evN;
    if (diff === 0) return 'امروز';
    if (diff === 1) return 'دیروز';
    if (diff === 2) return 'پریروز';
    const [y, m, d] = jdate.split('/').map(Number);
    const month = J.MONTHS[m - 1] || '';
    return `${fa(d)} ${month} ${fa(y)}`;
  }

  /* ── RENDER ─────────────────────────────────────────────── */
  function render() {
    const evs = filterEvents();
    const page = evs.slice(0, HS.page * HS.pageSize);
    const kpi  = computeKpi(evs);

    renderKpi(kpi);
    renderFeed(page, evs.length);
  }

  function renderKpi(kpi) {
    const host = el('hyKpi');
    if (!host) return;

    const kpis = [
      {
        label: 'جمع رویدادها',
        value: fa(kpi.total),
        sub: 'در بازه انتخاب‌شده',
        icon: 'receipt',
        iconBg: 'var(--info-bg)',
        iconColor: 'var(--info)',
      },
      {
        label: 'درآمد کل',
        value: money(kpi.income) + ' ت',
        sub: 'نوبت + فروش + واکسن + …',
        icon: 'wallet',
        iconBg: 'var(--ok-bg)',
        iconColor: 'var(--ok)',
      },
      {
        label: 'هزینه‌ها',
        value: money(kpi.expense) + ' ت',
        sub: 'هزینه + خرید + …',
        icon: 'receipt',
        iconBg: 'var(--danger-bg)',
        iconColor: 'var(--danger)',
      },
      {
        label: 'خالص',
        value: money(kpi.income - kpi.expense) + ' ت',
        sub: 'درآمد − هزینه',
        icon: 'chart',
        iconBg: 'var(--brand-50)',
        iconColor: 'var(--brand-600)',
      },
      {
        label: 'نوبت‌ها',
        value: fa(kpi.apt),
        sub: 'در بازه',
        icon: 'calendar',
        iconBg: 'var(--info-bg)',
        iconColor: 'var(--info)',
      },
      {
        label: 'بیمار جدید',
        value: fa(kpi.pat),
        sub: 'ثبت‌شده',
        icon: 'paw',
        iconBg: 'var(--lilac-bg)',
        iconColor: 'var(--lilac)',
      },
    ];

    host.innerHTML = kpis.map(k => `
      <div class="hy-kpi">
        <div class="hy-kpi-icon" style="background:${k.iconBg};color:${k.iconColor}">
          ${I[k.icon] || ''}
        </div>
        <div class="hy-kpi-label">${k.label}</div>
        <div class="hy-kpi-value">${k.value}</div>
        <div class="hy-kpi-sub">${k.sub}</div>
      </div>
    `).join('');
  }

  function renderFeed(page, total) {
    const host = el('hyFeed');
    if (!host) return;

    if (!page.length) {
      host.innerHTML = `
        <div class="hy-summary">
          <span>نتیجه‌ای یافت نشد</span>
        </div>
        <div class="hy-ledger">
          <div class="hy-empty">
            <div class="hy-empty-icon">${I.receipt || ''}</div>
            <b>رویدادی در این بازه ثبت نشده است</b>
            <span>بازه زمانی یا فیلتر را تغییر دهید.</span>
          </div>
        </div>`;
      return;
    }

    const income  = page.filter(e => e.amtSign === 1).reduce((s, e) => s + e.amount, 0);
    const expense = page.filter(e => e.amtSign === -1).reduce((s, e) => s + e.amount, 0);

    const summaryHtml = `
      <div class="hy-summary">
        <span>نمایش <b>${fa(page.length)}</b> از <b>${fa(total)}</b> رویداد</span>
        <div class="hy-summary-sep"></div>
        <span>درآمد: <b class="hy-summary-amt">+${money(income)} ت</b></span>
        <div class="hy-summary-sep"></div>
        <span>هزینه: <b class="hy-summary-exp">−${money(expense)} ت</b></span>
        <div class="hy-summary-sep"></div>
        <span>خالص: <b style="color:${income-expense >= 0 ? 'var(--ok)' : 'var(--danger)'}">${income-expense >= 0 ? '+' : '−'}${money(Math.abs(income-expense))} ت</b></span>
        <span class="hy-summary-end">مرتب‌سازی: جدیدترین</span>
      </div>`;

    /* Column config: --hcols must match below */
    const COLS = '36px 80px 1fr 140px 130px 130px 120px';
    const headHtml = `
      <div class="hy-ledger-head" style="--hcols:${COLS}">
        <div class="hcol"></div>
        <div class="hcol">زمان</div>
        <div class="hcol">رویداد</div>
        <div class="hcol">موجودیت / پزشک</div>
        <div class="hcol">مبلغ</div>
        <div class="hcol">وضعیت</div>
        <div class="hcol">دسته</div>
      </div>`;

    const groups = groupByDay(page);
    const bodyHtml = groups.map(([jdate, devs]) => {
      const dIncome  = devs.filter(e => e.amtSign === 1).reduce((s, e) => s + e.amount, 0);
      const dExpense = devs.filter(e => e.amtSign === -1).reduce((s, e) => s + e.amount, 0);
      const net = dIncome - dExpense;

      const rowsHtml = devs.map(ev => {
        const cat = CATEGORIES[ev.cat] || CATEGORIES.sale;
        return `
          <div class="hy-row" style="--hcols:${COLS}" title="${ev.title}">
            <div class="hcol">${catBadge(ev.cat)}</div>
            <div class="hcol hy-time">${ev.time || '—'}</div>
            <div class="hcol">
              <div class="hy-desc">
                <div class="hy-desc-main">${ev.title}</div>
                ${ev.sub ? `<div class="hy-desc-sub">${ev.sub}</div>` : ''}
              </div>
            </div>
            <div class="hcol hy-entity">${ev.entity || '—'}</div>
            <div class="hcol">${amtCell(ev)}</div>
            <div class="hcol">${statusChip(ev.status)}</div>
            <div class="hcol">
              <span class="hy-cat-chip on" style="--chip-color:${cat.color};--chip-bg:${cat.bg};font-size:11px;padding:3px 8px">
                ${cat.label}
              </span>
            </div>
          </div>`;
      }).join('');

      return `
        <div class="hy-day-group">
          <span class="hy-day-label">${jdateLabel(jdate)} &ndash; ${jdate}</span>
          <span class="hy-day-badge">${fa(devs.length)} رویداد</span>
          ${net !== 0 ? `<span class="hy-day-amt ${net >= 0 ? 'pos' : 'neg'}">${net >= 0 ? '+' : '−'}${money(Math.abs(net))} ت</span>` : ''}
        </div>
        ${rowsHtml}`;
    }).join('');

    const hasMore = page.length < total;
    const loadMore = hasMore ? `
      <div class="hy-load-more">
        <button id="hyLoadMore" onclick="HIST.loadMore()">
          نمایش رویدادهای بیشتر (${fa(total - page.length)} رویداد دیگر)
        </button>
      </div>` : '';

    host.innerHTML = summaryHtml + `
      <div class="hy-ledger">
        ${headHtml}
        ${bodyHtml}
        ${loadMore}
      </div>`;
  }

  /* ── Category chips ─────────────────────────────────────── */
  function renderCatChips() {
    const host = el('hyCats');
    if (!host) return;

    const all = `
      <span class="hy-cat-chip ${HS.cats.size === 0 ? 'on' : ''}"
            style="--chip-color:var(--ink-2);--chip-bg:var(--line-2)"
            onclick="HIST.setCat('all')">
        همه
      </span>`;

    const chips = Object.entries(CATEGORIES).map(([key, c]) => {
      const on = HS.cats.has(key);
      return `
        <span class="hy-cat-chip ${on ? 'on' : ''}"
              style="--chip-color:${c.color};--chip-bg:${c.bg}"
              onclick="HIST.setCat('${key}')">
          ${I[c.icon] || ''}${c.label}
        </span>`;
    }).join('');

    host.innerHTML = all + chips;
  }

  /* ── Period chips ─────────────────────────────────────────── */
  function renderPeriodChips() {
    const host = el('hyPeriod');
    if (!host) return;

    const periods = [
      { id: 'today', label: 'امروز' },
      { id: '7',     label: '۷ روز اخیر' },
      { id: '30',    label: '۳۰ روز اخیر' },
      { id: 'all',   label: 'همه' },
    ];

    host.innerHTML = periods.map(p => `
      <span class="chip ${HS.period === p.id ? 'on' : ''}"
            onclick="HIST.setPeriod('${p.id}')">
        ${p.label}
      </span>`).join('');
  }

  /* ── Public API ─────────────────────────────────────────── */
  window.HIST = {
    init() {
      renderCatChips();
      renderPeriodChips();
      render();

      /* search input */
      const sq = el('hySearch');
      if (sq) {
        sq.addEventListener('input', () => {
          HS.search = sq.value.trim();
          HS.page = 1;
          render();
        });
      }
    },

    setPeriod(p) {
      HS.period = p;
      HS.page   = 1;
      renderPeriodChips();
      render();
    },

    setCat(key) {
      if (key === 'all') {
        HS.cats.clear();
      } else {
        if (HS.cats.has(key)) {
          HS.cats.delete(key);
        } else {
          HS.cats.add(key);
        }
        if (HS.cats.size === Object.keys(CATEGORIES).length) HS.cats.clear();
      }
      HS.page = 1;
      renderCatChips();
      render();
    },

    loadMore() {
      HS.page++;
      render();
    },

    exportCSV() {
      const evs = filterEvents();
      const rows = [['تاریخ', 'زمان', 'دسته', 'رویداد', 'توضیح', 'موجودیت', 'مبلغ', 'وضعیت']];
      evs.forEach(ev => {
        rows.push([
          ev.jdate, ev.time,
          CATEGORIES[ev.cat]?.label || ev.cat,
          ev.title, ev.sub, ev.entity,
          ev.amtSign < 0 ? '-' + ev.amount : (ev.amtSign > 0 ? ev.amount : 0),
          STATUS[ev.status]?.label || ev.status,
        ]);
      });
      const csv = rows.map(r => r.map(c => '"' + String(c).replace(/"/g, '""') + '"').join(',')).join('\n');
      const a = document.createElement('a');
      a.href = 'data:text/csv;charset=utf-8,\uFEFF' + encodeURIComponent(csv);
      a.download = 'history-' + todayKey().replace(/\//g, '-') + '.csv';
      a.click();
    },
  };

})();
