/* =========================================================
   وت‌پلاس · جریان بالینی–مالی — لایهٔ داده (فاز ۳)
   assets/clinic-data.js — تک‌منبع دادهٔ جریان (P7)
   Static UI prototype: in-memory mock only (L1–L3).
   ========================================================= */
(function () {
  'use strict';

  /* ---------------- helpers ---------------- */
  const toFa = (v) => (window.toFa ? window.toFa(v) : String(v).replace(/[0-9]/g, (d) => '۰۱۲۳۴۵۶۷۸۹'[d]));

  /* ---------------- CLINIC.J — Jalali minimal (same shape as window.J) ---------------- */
  // Subset of window.J (cheques-data.js:76): identical outputs on same input.
  const div = (a, b) => ~~(a / b);
  const mod = (a, b) => a - ~~(a / b) * b;
  const breaks = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210,
    1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];

  function jalCal(jy, withoutLeap) {
    const bl = breaks.length, gy = jy + 621;
    let leapJ = -14, jp = breaks[0], jm, jump = 0, leap, leapG, march, n, i;
    for (i = 1; i < bl; i += 1) {
      jm = breaks[i]; jump = jm - jp;
      if (jy < jm) break;
      leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4);
      jp = jm;
    }
    n = jy - jp;
    leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
    if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
    leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
    march = 20 + leapJ - leapG;
    if (!withoutLeap) {
      if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
      leap = mod(mod(n + 1, 33) - 1, 4);
      if (leap === -1) leap = 4;
    }
    return { leap, gy, march };
  }

  function g2d(gy, gm, gd) {
    let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4)
      + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
    d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
    return d;
  }
  function d2g(jdn) {
    let j = 4 * jdn + 139361631;
    j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
    const i = div(mod(j, 1461), 4) * 5 + 308;
    const gd = div(mod(i, 153), 5) + 1;
    const gm = mod(div(i, 153), 12) + 1;
    const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
    return { gy, gm, gd };
  }
  function j2d(jy, jm, jd) {
    const r = jalCal(jy, true);
    return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
  }
  function d2j(jdn) {
    const gy = d2g(jdn).gy;
    let jy = gy - 621, k;
    const r = jalCal(jy, false);
    const jdn1f = g2d(gy, 3, r.march);
    k = jdn - jdn1f;
    if (k >= 0) {
      if (k <= 185) return { jy, jm: 1 + div(k, 31), jd: mod(k, 31) + 1 };
      k -= 186;
    } else {
      jy -= 1; k += 179;
      if (r.leap === 1) k += 1;
    }
    return { jy, jm: 7 + div(k, 30), jd: mod(k, 30) + 1 };
  }

  const MONTHS = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
    'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'];
  const DAYS = ['شنبه', 'یک‌شنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنج‌شنبه', 'جمعه'];
  const DAYS_S = ['ش', 'ی', 'د', 'س', 'چ', 'پ', 'ج'];

  let _today = null; // frozen on first call (D8/D12)

  const J = {
    MONTHS, DAYS, DAYS_S,
    isLeap: (jy) => jalCal(jy, false).leap === 0,
    monthLen: (jy, jm) => (jm <= 6 ? 31 : (jm <= 11 ? 30 : (jalCal(jy, false).leap === 0 ? 30 : 29))),
    toDay(s) {
      const [y, m, d] = String(s).split('/').map(Number);
      return j2d(y, m, d);
    },
    fromDay(n) {
      const o = d2j(n);
      return `${o.jy}/${String(o.jm).padStart(2, '0')}/${String(o.jd).padStart(2, '0')}`;
    },
    parse(s) {
      const [jy, jm, jd] = String(s).split('/').map(Number);
      return { jy, jm, jd };
    },
    today() {
      if (_today === null) {
        const n = new Date();
        const o = d2j(g2d(n.getFullYear(), n.getMonth() + 1, n.getDate()));
        _today = `${o.jy}/${String(o.jm).padStart(2, '0')}/${String(o.jd).padStart(2, '0')}`;
      }
      return _today;
    },
    weekday(s) {
      const { jy, jm, jd } = J.parse(s);
      const g = d2g(j2d(jy, jm, jd));
      const dt = new Date(g.gy, g.gm - 1, g.gd);
      return (dt.getDay() + 1) % 7; // 0 = شنبه
    },
    diff(s, from) {
      return J.toDay(s) - J.toDay(from || J.today()); // + = future
    },
    long(s) {
      const { jy, jm, jd } = J.parse(s);
      return `${toFa(jd)} ${MONTHS[jm - 1]} ${toFa(jy)}`;
    },
    medium(s) {
      const { jm, jd } = J.parse(s);
      return `${toFa(jd)} ${MONTHS[jm - 1]}`;
    },
  };

  /* ---------------- enums (single reference for FLOW, phase 4) ---------------- */
  const QT_STATES = ['waiting', 'called', 'inVisit', 'done', 'left', 'noshow', 'cancel'];
  const ENC_STATES = ['draft', 'done'];
  const INV_STATES = ['draft', 'partial', 'settled', 'void', 'refunded'];
  const PAY_METHODS = ['cash', 'card', 'cheque', 'credit', 'mixed'];
  const TICKET_SOURCES = ['appointment', 'walkin', 'urgent'];
  const PRIO = ['normal', 'urgent'];

  /* ---------------- catalogs (read-only, frozen) ---------------- */
  const PRIORITY = {
    normal: { label: 'عادی', tone: 'ok' },
    urgent: { label: 'اورژانس', tone: 'danger' },
  };

  // Same 4 doctors as the scheduler (SCH.DOCTORS) — identical shape.
  const DOCTORS = [
    { id: 'd1', name: 'دکتر آریا نیک', short: 'آریا نیک', spec: 'داخلی و جراحی', ini: 'آ', ac: '#0d9488' },
    { id: 'd2', name: 'دکتر مینا همتی', short: 'مینا همتی', spec: 'واکسن و پیشگیری', ini: 'م', ac: '#1570cd' },
    { id: 'd3', name: 'دکتر سهیل کاویانی', short: 'سهیل کاویانی', spec: 'دندان‌پزشکی', ini: 'س', ac: '#8f74d1' },
    { id: 'd4', name: 'نگار راد', short: 'نگار راد', spec: 'آرایش و نگهداری', ini: 'ن', ac: '#e08a5c' },
  ];

  // Services only: tax = true.
  const SERVICES = [
    { id: 'sv-1',  title: 'ویزیت عمومی',          cat: 'svc', price: 250000,  tax: true },
    { id: 'sv-2',  title: 'واکسن چهارگانه برونشیت', cat: 'svc', price: 380000,  tax: true },
    { id: 'sv-3',  title: 'جراحی عقیم‌سازی',       cat: 'svc', price: 2800000, tax: true },
    { id: 'sv-4',  title: 'دندان‌پزشکی و جرم‌گیری', cat: 'svc', price: 1200000, tax: true },
    { id: 'sv-5',  title: 'آرایش و پیرایش',        cat: 'svc', price: 650000,  tax: true },
    { id: 'sv-6',  title: 'آزمایش خون کامل',       cat: 'svc', price: 800000,  tax: true },
    { id: 'sv-7',  title: 'سونوگرافی شکمی',        cat: 'svc', price: 1500000, tax: true },
    { id: 'sv-8',  title: 'رادیولوژی',             cat: 'svc', price: 900000,  tax: true },
    { id: 'sv-9',  title: 'ویزیت تخصصی پوست',      cat: 'svc', price: 350000,  tax: true },
    { id: 'sv-10', title: 'تزریق و سرم‌تراپی',      cat: 'svc', price: 450000,  tax: true },
    { id: 'sv-11', title: 'بستری شبانه‌روزی',      cat: 'svc', price: 1000000, tax: true },
    { id: 'sv-12', title: 'معاینه پیگیری',         cat: 'svc', price: 180000,  tax: true },
  ];

  // Items only: tax = false.
  const ITEMS = [
    { id: 'it-1',  title: 'قرص ضد انگل',           cat: 'item', price: 160000, tax: false },
    { id: 'it-2',  title: 'غذای کنسرو مرغ',        cat: 'item', price: 85000,  tax: false },
    { id: 'it-3',  title: 'غذای خشک بالغ سگ (۵ کیلو)', cat: 'item', price: 850000, tax: false },
    { id: 'it-4',  title: 'شامپوی مخصوص حیوانات',  cat: 'item', price: 220000, tax: false },
    { id: 'it-5',  title: 'مکمل تقویت مفصل',       cat: 'item', price: 450000, tax: false },
    { id: 'it-6',  title: 'آنتی‌بیوتیک خوراکی',     cat: 'item', price: 320000, tax: false },
    { id: 'it-7',  title: 'برس و شانه پاکسازی',    cat: 'item', price: 125000, tax: false },
    { id: 'it-8',  title: 'خاک مخصوص گربه',        cat: 'item', price: 280000, tax: false },
    { id: 'it-9',  title: 'ویتامین مایع',          cat: 'item', price: 150000, tax: false },
    { id: 'it-10', title: 'قلاده ضدکک',            cat: 'item', price: 390000, tax: false },
  ];

  // Insurance orgs — pct = co-pay share of afterDiscount (D2).
  const INSURERS = [
    { id: 'organ-1', name: 'بیمه آسیا — پوشش حیوانات خانگی', pct: 15 },
    { id: 'organ-2', name: 'بیمه پاسارگاد — پوشش درمان',     pct: 20 },
    { id: 'organ-3', name: 'بیمه دانا — پوشش جامع',          pct: 25 },
    { id: 'organ-4', name: 'بیمه ملت — پوشش پایه',           pct: 10 },
  ];

  function deepFreeze(obj) {
    Object.freeze(obj);
    Object.keys(obj).forEach((k) => {
      const v = obj[k];
      if (v && typeof v === 'object') deepFreeze(v);
    });
    return obj;
  }

  /* ---------------- seed — today's queue (deterministic, in-memory) ---------------- */
  const T = J.today(); // today, anchored to scheduler's real date (D8)

  const QUEUE = [
    { id: 'qt-1', apptId: 'a8',  src: 'appointment', date: T, at: 480, du: 30,
      pet: { name: 'پشمک', kind: 'گربه پرشین', owner: 'نگار محمدی', phone: '۰۹۱۲۳۴۵۶۷۸۹', av: 'a', ic: 'cat' },
      doc: 'd1', type: 'visit', prio: 'normal', state: 'waiting',
      admittedBy: 'پذیرش', times: { admitted: `${T} 08:00`, called: null, entered: null, finished: null } },
    { id: 'qt-2', apptId: 'a12', src: 'appointment', date: T, at: 510, du: 20,
      pet: { name: 'لئو', kind: 'سگ ژرمن', owner: 'امیر رستگار', phone: '۰۹۳۵۱۱۲۲۳۳۴', av: 'b', ic: 'dog' },
      doc: 'd2', type: 'vaccine', prio: 'normal', state: 'waiting',
      admittedBy: 'پذیرش', times: { admitted: `${T} 08:30`, called: null, entered: null, finished: null } },
    { id: 'qt-3', apptId: null, src: 'urgent', date: T, at: 530, du: 30,
      pet: { name: 'رکس', kind: 'سگ هاسکی', owner: 'محسن دهقان', phone: '۰۹۳۶۲۲۳۳۴۴۵', av: 'f', ic: 'dog' },
      doc: 'd1', type: 'visit', prio: 'urgent', state: 'waiting',
      admittedBy: 'پذیرش', times: { admitted: `${T} 08:50`, called: null, entered: null, finished: null } },
    { id: 'qt-4', apptId: null, src: 'walkin', date: T, at: 540, du: 60,
      pet: { name: 'آبی', kind: 'کاسکو', owner: 'سارا یوسفی', phone: '۰۹۱۹۸۷۶۵۴۳۲', av: 'c', ic: 'bird' },
      doc: 'd3', type: 'dental', prio: 'normal', state: 'waiting',
      admittedBy: 'پذیرش', times: { admitted: `${T} 09:00`, called: null, entered: null, finished: null } },
    { id: 'qt-5', apptId: 'a14', src: 'appointment', date: T, at: 570, du: 30,
      pet: { name: 'میلو', kind: 'سگ پامرانین', owner: 'رضا کریمی', phone: '۰۹۳۰۴۴۵۵۶۶۷', av: 'd', ic: 'dog' },
      doc: 'd1', type: 'visit', prio: 'normal', state: 'called',
      admittedBy: 'پذیرش', times: { admitted: `${T} 09:30`, called: `${T} 09:32`, entered: null, finished: null } },
    { id: 'qt-6', apptId: null, src: 'walkin', date: T, at: 600, du: 20,
      pet: { name: 'نانا', kind: 'گربه اسکاتیش', owner: 'الهام صادقی', phone: '۰۹۱۲۷۷۸۸۹۹۰', av: 'e', ic: 'cat' },
      doc: 'd2', type: 'vaccine', prio: 'normal', state: 'inVisit',
      admittedBy: 'پذیرش', times: { admitted: `${T} 10:00`, called: `${T} 10:02`, entered: `${T} 10:05`, finished: null } },
    { id: 'qt-7', apptId: 'a16', src: 'appointment', date: T, at: 615, du: 60,
      pet: { name: 'کوکو', kind: 'خرگوش لوپ', owner: 'مریم آذری', phone: '۰۹۱۰۵۵۶۶۷۷۸', av: 'a', ic: 'rabbit' },
      doc: 'd4', type: 'groom', prio: 'normal', state: 'done',
      admittedBy: 'پذیرش', times: { admitted: `${T} 10:15`, called: `${T} 10:16`, entered: `${T} 10:20`, finished: `${T} 10:10` } },
  ];

  /* ---------------- seed — encounters ---------------- */
  const ENCOUNTERS = [
    { id: 'enc-1', ticketId: 'qt-7', petId: 'pt-7', docId: 'd4', date: T,
      vitals: { w: 2.1, t: 38.4, p: 150, r: 30 },
      history: 'گروم دوره‌ای؛ حساسیت پوستی خفیف در ناحیه شکم از هفته گذشته گزارش شد.',
      exam: 'پوشش مویی مناسب؛ خشکی جزئی پوست در ناحیه شکم.',
      diagnosis: 'واکنش پوستی خفیف', plan: 'ضدحساسیت خوراکی + شامپوی ملایم به مدت یک هفته',
      services: [{ sv: 'sv-1', qty: 1, note: 'ویزیت همراه گروم' }],
      items: [{ it: 'it-1', qty: 2 }],
      rx: [{ drug: 'کلرفنیرامین', d: 'BID', q: '۰/۵ mg/kg', days: 5, note: '' }],
      tests: [{ t: 'مالش پوستی', done: true, note: '' }],
      followUp: { inDays: 7, note: 'کنترل مجدد پوست' },
      state: 'done', completedAt: `${T} 11:05` },
    { id: 'enc-2', ticketId: 'qt-6', petId: 'pt-5', docId: 'd2', date: T,
      vitals: { w: 3.8, t: 38.9, p: 138, r: 26 },
      history: 'عطسه و ترشحات بینی از دیروز؛ اشتهای طبیعی.',
      exam: 'در حال معاینه…', diagnosis: '', plan: '',
      services: [{ sv: 'sv-6', qty: 1, note: 'آزمایش خون اولیه' }],
      items: [],
      rx: [],
      tests: [{ t: 'آزمایش خون', done: false, note: 'در انتظار جواب' }],
      followUp: null,
      state: 'draft', completedAt: null },
    // enc-3, enc-4 — ویزیت‌های پایان‌یافته که هنوز فاکتور نشده‌اند (برای صف «آمادهٔ فاکتور»)
    { id: 'enc-3', ticketId: 'qt-1', petId: 'pt-1', docId: 'd1', date: T,
      vitals: { w: 4.2, t: 38.6, p: 145, r: 28 },
      history: 'مراجعه جهت واکسیناسیون سالانه و بررسی اشتها.',
      exam: 'وضعیت عمومی خوب، اشتها طبیعی.',
      diagnosis: 'سالم — واکسیناسیون روتین', plan: 'واکسن سالانه + پیگیری یک ماه بعد',
      services: [
        { sv: 'sv-1', qty: 1, note: 'ویزیت عمومی' },
        { sv: 'sv-2', qty: 1, note: 'واکسن چهارگانه' },
      ],
      items: [{ it: 'it-9', qty: 1 }],
      rx: [],
      tests: [],
      followUp: { inDays: 30, note: 'کنترل یک ماه بعد' },
      state: 'done', completedAt: `${T} 09:45` },
    { id: 'enc-4', ticketId: 'qt-2', petId: 'pt-2', docId: 'd2', date: T,
      vitals: { w: 24.5, t: 38.2, p: 92, r: 22 },
      history: 'جراحی سطحی برای کیست پوستی روی ران.',
      exam: 'ناحیه پس از جراحی تمیز، بدون خونریزی.',
      diagnosis: 'کیست چربی — برداشته شد', plan: 'آنتی‌بیوتیک ۷ روز + تعویض پانسمان',
      services: [
        { sv: 'sv-3', qty: 1, note: 'جراحی سطحی' },
        { sv: 'sv-6', qty: 1, note: 'آزمایش پیش از جراحی' },
      ],
      items: [{ it: 'it-6', qty: 1 }, { it: 'it-4', qty: 1 }],
      rx: [{ drug: 'آموکسی‌کلاو', d: 'BID', q: '۲۵ mg/kg', days: 7, note: '' }],
      tests: [{ t: 'شمارش خون', done: true, note: 'طبیعی' }],
      followUp: { inDays: 7, note: 'تعویض پانسمان' },
      state: 'done', completedAt: `${T} 10:30` },
  ];

  /* ---------------- seed — invoices (values cached per §8.2 order) ---------------- */
  const INVOICES = [
    { id: 'INV-001', encId: 'enc-1', src: 'visit', date: T,
      customer: { owner: 'مریم آذری', pet: 'کوکو', phone: '۰۹۱۰۵۵۶۶۷۷۸' },
      lines: [
        { ref: 'sv-1', kind: 'svc', title: 'ویزیت عمومی', qty: 1, price: 250000, discPct: 0 },
        { ref: 'it-1', kind: 'item', title: 'قرص ضد انگل', qty: 2, price: 160000, discPct: 0 },
      ],
      discount: { kind: 'pct', value: 10 },
      insurance: { org: 'organ-2' },
      taxPct: 9,
      base: 570000, discountAmt: 57000, afterDiscount: 513000,
      tax: 20250, insShare: 102600,
      total: 430650, paid: 0, due: 430650,
      state: 'draft', payments: [] },
    { id: 'INV-002', encId: null, src: 'sale', date: T,
      customer: { owner: 'سارا یوسفی', pet: 'آبی', phone: '۰۹۱۹۸۷۶۵۴۳۲' },
      lines: [
        { ref: 'sv-9', kind: 'svc', title: 'ویزیت تخصصی پوست', qty: 1, price: 350000, discPct: 0 },
        { ref: 'it-5', kind: 'item', title: 'مکمل تقویت مفصل', qty: 1, price: 450000, discPct: 0 },
      ],
      discount: { kind: 'pct', value: 0 },
      insurance: null,
      taxPct: 9,
      base: 800000, discountAmt: 0, afterDiscount: 800000,
      tax: 31500, insShare: 0,
      total: 831500, paid: 400000, due: 431500,
      state: 'partial',
      payments: [{ method: 'cash', amount: 400000, ref: null, note: 'پیش‌پرداخت نقدی', at: `${T} 11:20` }] },
  ];

  /* ---------------- CLINIC global (pattern B) ---------------- */
  const _ctr = { qt: 8, enc: 3, INV: 3 }; // next id per prefix (seeds used up to 7 / 2 / INV-002)

  window.CLINIC = {
    // catalogs — read-only
    PRIORITY: deepFreeze(PRIORITY),
    DOCTORS: deepFreeze(DOCTORS),
    SERVICES: deepFreeze(SERVICES),
    ITEMS: deepFreeze(ITEMS),
    INSURERS: deepFreeze(INSURERS),
    // enum references — single source for FLOW (phase 4)
    QT_STATES: Object.freeze(QT_STATES),
    ENC_STATES: Object.freeze(ENC_STATES),
    INV_STATES: Object.freeze(INV_STATES),
    PAY_METHODS: Object.freeze(PAY_METHODS),
    TICKET_SOURCES: Object.freeze(TICKET_SOURCES),
    PRIO: Object.freeze(PRIO),
    // helpers + mutable seed collections
    J: deepFreeze(J),
    QUEUE, ENCOUNTERS, INVOICES,
    today: () => J.today(),
    setToday(s) { // debug-only pin hook (D12) — normal flow never calls it
      if (!/^\d{4}\/\d{2}\/\d{2}$/.test(String(s))) return false;
      _today = String(s);
      return true;
    },
    ticket: (id) => QUEUE.find((q) => q.id === id) || null,
    encounter: (id) => ENCOUNTERS.find((e) => e.id === id) || null,
    invoice: (id) => INVOICES.find((i) => i.id === id) || null,
    lookup(hash) {
      const h = String(hash || '').replace(/^#/, '').trim();
      let m;
      if ((m = h.match(/^(?:admit-)?(qt-\d+)$/))) {
        const id = m[1];
        return CLINIC.ticket(id) ? { kind: 'ticket', id } : null;
      }
      if ((m = h.match(/^(enc-\d+)$/))) {
        const id = m[1];
        return CLINIC.encounter(id) ? { kind: 'enc', id } : null;
      }
      if ((m = h.match(/^inv-(INV-\d+)$/i))) {
        const id = m[1].toUpperCase();
        return CLINIC.invoice(id) ? { kind: 'inv', id } : null;
      }
      return null;
    },
    nextId(prefix) {
      if (prefix === 'INV') return 'INV-' + String(_ctr.INV++).padStart(3, '0');
      if (!(prefix in _ctr)) throw new TypeError('CLINIC.nextId: unknown prefix "' + prefix + '"');
      return prefix + '-' + _ctr[prefix]++;
    },
  };

  /* ---------------- smoke test (console asserts, P3.9) ---------------- */
  const CLINIC = window.CLINIC;
  const ok = (cond, msg) => console.assert(cond, '[clinic-data] ' + msg);

  ok(SERVICES.length >= 10 && SERVICES.length <= 12, 'SERVICES 10–12');
  ok(ITEMS.length >= 8 && ITEMS.length <= 10, 'ITEMS 8–10');
  ok(DOCTORS.length === 4, 'DOCTORS = 4 (match scheduler)');
  ok(INSURERS.length >= 3 && INSURERS.length <= 4, 'INSURERS 3–4');

  ok(QUEUE.length >= 5 && QUEUE.length <= 7, 'QUEUE 5–7 tickets');
  ok(QUEUE.filter((q) => q.src === 'appointment').length >= 3, '≥3 from appointment');
  ok(QUEUE.filter((q) => q.src === 'walkin').length >= 1, '≥1 walk-in');
  ok(QUEUE.filter((q) => q.src === 'urgent').length >= 1, '≥1 urgent source');
  ok(QUEUE.filter((q) => q.prio === 'urgent').length >= 1, '≥1 prio urgent');
  ok(QUEUE.every((q) => QT_STATES.indexOf(q.state) >= 0), 'ticket state ∈ enum');
  ok(QUEUE.every((q) => TICKET_SOURCES.indexOf(q.src) >= 0), 'ticket src ∈ enum');
  ok(QUEUE.every((q) => PRIO.indexOf(q.prio) >= 0), 'ticket prio ∈ enum');

  ok(ENCOUNTERS.some((e) => e.state === 'draft') && ENCOUNTERS.some((e) => e.state === 'done'), 'enc draft + done');
  ok(ENCOUNTERS.every((e) => ENC_STATES.indexOf(e.state) >= 0), 'enc state ∈ enum');

  ok(INVOICES.some((i) => i.state === 'draft') && INVOICES.some((i) => i.state === 'partial'), 'inv draft + partial');
  ok(INVOICES.every((i) => INV_STATES.indexOf(i.state) >= 0), 'inv state ∈ enum');
  ok(INVOICES.every((i) => i.payments.every((p) => PAY_METHODS.indexOf(p.method) >= 0)), 'pay method ∈ enum');

  ok(CLINIC.ticket('qt-1') && CLINIC.ticket('qt-1').id === 'qt-1', 'ticket() finds');
  ok(CLINIC.encounter('enc-2') && CLINIC.encounter('enc-2').state === 'draft', 'encounter() finds');
  ok(CLINIC.invoice('INV-002') && CLINIC.invoice('INV-002').state === 'partial', 'invoice() finds');

  const lEnc = CLINIC.lookup('enc-1');
  ok(lEnc && lEnc.kind === 'enc' && lEnc.id === 'enc-1', 'lookup("enc-1")');
  ok((CLINIC.lookup('#admit-qt-3') || {}).id === 'qt-3', 'lookup("#admit-qt-3")');
  ok((CLINIC.lookup('#inv-INV-001') || {}).kind === 'inv', 'lookup("#inv-INV-001")');
  ok(CLINIC.lookup('#enc-999') === null, 'lookup invalid → null');

  ok(CLINIC.nextId('qt') !== CLINIC.nextId('qt'), 'nextId("qt") unique');
  ok(/^INV-\d{3}$/.test(CLINIC.nextId('INV')), 'nextId("INV") → INV-NNN');

  ok(Object.isFrozen(CLINIC.SERVICES), 'SERVICES frozen');
  ok(Object.isFrozen(CLINIC.ITEMS), 'ITEMS frozen');
  ok(Object.isFrozen(CLINIC.PRIORITY), 'PRIORITY frozen');
  ok(Object.isFrozen(CLINIC.INSURERS), 'INSURERS frozen');
  ok(Object.isFrozen(CLINIC.J), 'CLINIC.J frozen');

  ok(J.weekday('1405/05/12') === 2, 'weekday(1405/05/12) = 2 (دوشنبه) — matches window.J');
  ok(J.long('1405/05/12') === '۱۲ مرداد ۱۴۰۵', 'J.long parity');
  ok(J.medium('1405/05/12') === '۱۲ مرداد', 'J.medium parity');
  ok(J.diff('1405/05/12', '1405/05/12') === 0, 'J.diff 0');
  ok(J.fromDay(J.toDay('1405/05/12')) === '1405/05/12', 'J.fromDay(J.toDay) round-trip');
  ok(J.today() === J.today(), 'today frozen on first call');
})();
