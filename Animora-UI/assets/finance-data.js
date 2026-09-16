/* =========================================================
   Vetplus · Finance — mock data + aggregation helpers
   Single source of truth for the redesigned Finance / Sales
   workspace (expense.html + reports.html share this).

   No backend, no persistence: everything is hardcoded Persian
   mock data. Mutations (add expense) edit the in-memory array
   then trigger a re-render, and vanish on reload — by design.
   ========================================================= */
(function () {
  'use strict';

  /* small local formatters that fall back gracefully if the
     shared helpers (cheques-data.js) are not yet loaded. */
  const fa = (v) => (window.fa ? window.fa(v) : String(v).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]));

  /* ---------------- 1) دسته‌بندی هزینه‌ها ---------------- */
  // color + tone drive both the legend swatch and the icon chip.
  const EXPENSE_CATEGORIES = [
    { id: 'salary',    label: 'حقوق و دستمزد',   icon: 'users',   color: '#0d9488', tone: 'teal'   },
    { id: 'rent',      label: 'اجاره و قبوض',    icon: 'home',    color: '#e0a95b', tone: 'amber'  },
    { id: 'supply',    label: 'خرید دارو و کالا', icon: 'box',     color: '#5c8ad6', tone: 'blue'   },
    { id: 'marketing', label: 'بازاریابی',       icon: 'megaphone',color: '#b8a2e6', tone: 'lilac'  },
    { id: 'repair',    label: 'تعمیر و نگهداری', icon: 'tools',   color: '#e08a5c', tone: 'peach'  },
    { id: 'tax',       label: 'مالیات و بیمه',   icon: 'shield',  color: '#7a8b89', tone: 'mute'   },
    { id: 'other',     label: 'متفرقه',          icon: 'more',    color: '#b54708', tone: 'warn'   },
  ];

  /* ---------------- 2) کانال‌های درآمدی ---------------- */
  const REVENUE_CHANNELS = [
    { id: 'clinic',   label: 'خدمات درمانی', icon: 'clipboard', color: '#0d9488' },
    { id: 'pharmacy', label: 'داروخانه',     icon: 'flask',     color: '#5c8ad6' },
    { id: 'petshop',  label: 'پت‌شاپ',        icon: 'cart',      color: '#b8a2e6' },
    { id: 'lab',      label: 'آزمایشگاه',     icon: 'spark',     color: '#eab749' },
  ];

  /* ---------------- 3) روش‌های پرداخت ---------------- */
  const PAYMENT_METHODS = [
    { id: 'pos',      label: 'کارتخوان', color: '#0d9488' },
    { id: 'cash',     label: 'نقدی',      color: '#8fb5f0' },
    { id: 'transfer', label: 'انتقال',    color: '#e08a5c' },
    { id: 'credit',   label: 'اعتباری',   color: '#b8a2e6' },
  ];

  /* =========================================================
     4) سری‌های زمانی روزانه (۹۰ روز اخیر)
     هر روز: درآمد به تفکیک کانال + هزینه به تفکیک دسته.
     برای شکل‌دهی به روند از یک ژنراتور شبه‌تصادفیِ ثابت
     (seeded) استفاده می‌کنیم تا نمودارها بین رفرش‌ها پایدار
     بمانند (اسکرین‌شات‌پذیر).
     ========================================================= */

  // Deterministic PRNG (mulberry32) — stable charts across reloads.
  function makeRng(seed) {
    let a = seed >>> 0;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  const TODAY = (window.J && window.J.today) ? window.J.today() : '1405/08/07';
  const todayDay = (window.J && window.J.toDay) ? window.J.toDay(TODAY) : 0;

  // weekday weighting: Thursday busy, Friday quiet (0=شنبه … 6=جمعه)
  const DOW_WEIGHT = [1.0, 1.05, 1.1, 1.0, 1.15, 1.35, 0.55];

  const DAILY = (function build() {
    const rng = makeRng(20250816);
    const rows = [];
    for (let i = 89; i >= 0; i--) {
      const dayAbs = todayDay - i;
      const jdate = (window.J && window.J.fromDay) ? window.J.fromDay(dayAbs) : TODAY;
      const dow = (window.J && window.J.weekday) ? window.J.weekday(jdate) : (i % 7);
      const w = DOW_WEIGHT[dow] || 1;
      // slow upward trend across the window
      const trend = 1 + (89 - i) / 89 * 0.28;
      const noise = 0.82 + rng() * 0.36;

      const base = 11.8e6 * w * trend * noise;   // clinic base
      const revenue = {
        clinic:   Math.round(base),
        pharmacy: Math.round(base * (0.5 + rng() * 0.2)),
        petshop:  Math.round(base * (0.28 + rng() * 0.16)),
        lab:      Math.round(base * (0.14 + rng() * 0.1)),
      };
      // expenses are steadier than revenue
      const en = 0.9 + rng() * 0.2;
      const expense = {
        salary:    Math.round(4.9e6 * en),
        rent:      Math.round(2.7e6 * en),
        supply:    Math.round(base * (0.34 + rng() * 0.12)),
        marketing: Math.round(1.4e6 * (0.6 + rng() * 0.9)),
        repair:    Math.round(0.9e6 * (0.2 + rng() * 1.4)),
        tax:       Math.round(1.6e6 * en),
        other:     Math.round(0.7e6 * (0.3 + rng() * 1.2)),
      };
      rows.push({ i: 89 - i, dayAbs, date: jdate, dow, revenue, expense });
    }
    return rows;
  })();

  const sumObj = (o) => Object.values(o).reduce((a, b) => a + b, 0);

  // enrich each row with totals + profit
  DAILY.forEach(r => {
    r.revTotal = sumObj(r.revenue);
    r.expTotal = sumObj(r.expense);
    r.profit = r.revTotal - r.expTotal;
  });

  /* =========================================================
     5) بازه‌های زمانی قابل انتخاب
     هر بازه تعداد روزهای اخیر را مشخص می‌کند + گروه‌بندی نمودار
     (روزانه / هفتگی / ماهانه) برای خوانایی محور افقی.
     ========================================================= */
  const RANGES = [
    { id: '7d',  label: '۷ روز اخیر',   days: 7,  bucket: 'day'  },
    { id: '30d', label: '۳۰ روز اخیر',  days: 30, bucket: 'day'  },
    { id: '90d', label: '۳ ماه اخیر',   days: 90, bucket: 'week' },
    { id: 'ytd', label: 'از ابتدای سال', days: 90, bucket: 'week' },
  ];

  /* =========================================================
     6) موتور تجمیع — قلب صفحه
     برای هر بازه، تمام اعداد لازم را یک‌جا حساب می‌کند.
     ========================================================= */
  function pctDelta(cur, prev) {
    if (!prev) return 0;
    return (cur - prev) / prev * 100;
  }

  function aggregate(rangeId) {
    const range = RANGES.find(r => r.id === rangeId) || RANGES[1];
    const n = range.days;
    const slice = DAILY.slice(-n);
    const prevSlice = DAILY.slice(-2 * n, -n); // previous window for deltas

    const revTotal = slice.reduce((a, r) => a + r.revTotal, 0);
    const expTotal = slice.reduce((a, r) => a + r.expTotal, 0);
    const profit = revTotal - expTotal;
    const margin = revTotal ? profit / revTotal * 100 : 0;

    const prevRev = prevSlice.reduce((a, r) => a + r.revTotal, 0);
    const prevExp = prevSlice.reduce((a, r) => a + r.expTotal, 0);
    const prevProfit = prevRev - prevExp;
    const prevMargin = prevRev ? prevProfit / prevRev * 100 : 0;

    // per-channel revenue
    const byChannel = REVENUE_CHANNELS.map(c => ({
      ...c,
      value: slice.reduce((a, r) => a + r.revenue[c.id], 0),
    }));
    // per-category expense
    const byCategory = EXPENSE_CATEGORIES.map(c => {
      const value = slice.reduce((a, r) => a + (r.expense[c.id] || 0), 0);
      const prev = prevSlice.reduce((a, r) => a + (r.expense[c.id] || 0), 0);
      return { ...c, value, delta: pctDelta(value, prev) };
    }).sort((a, b) => b.value - a.value);

    // time-bucketed series for the main chart
    const series = bucketSeries(slice, range.bucket);

    return {
      range, n,
      revTotal, expTotal, profit, margin,
      delta: {
        revenue: pctDelta(revTotal, prevRev),
        expense: pctDelta(expTotal, prevExp),
        profit:  pctDelta(profit, prevProfit),
        margin:  margin - prevMargin,
      },
      avgDailyProfit: profit / n,
      byChannel, byCategory, series,
    };
  }

  // group daily rows into day / week / month buckets for the chart
  function bucketSeries(slice, bucket) {
    if (bucket === 'day') {
      return slice.map(r => ({
        label: labelForDay(r),
        full: r.date,
        revenue: r.revTotal,
        expense: r.expTotal,
        profit: r.profit,
      }));
    }
    // weekly buckets of 7
    const out = [];
    for (let i = 0; i < slice.length; i += 7) {
      const grp = slice.slice(i, i + 7);
      if (!grp.length) continue;
      out.push({
        label: 'هفته ' + fa(Math.floor(i / 7) + 1),
        full: grp[0].date + ' تا ' + grp[grp.length - 1].date,
        revenue: grp.reduce((a, r) => a + r.revTotal, 0),
        expense: grp.reduce((a, r) => a + r.expTotal, 0),
        profit: grp.reduce((a, r) => a + r.profit, 0),
      });
    }
    return out;
  }

  const DOW_NAMES = ['شنبه', 'یک‌شنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنج‌شنبه', 'جمعه'];
  function labelForDay(r) {
    if (window.J && window.J.parse) {
      const p = window.J.parse(r.date);
      return fa(p.jd) + ' ' + window.J.MONTHS[p.jm - 1];
    }
    return r.date;
  }

  /* =========================================================
     7) پرفروش‌ترین کالا و خدمات (با سود ناخالص)
     price = فروش کل ، cost = بهای تمام‌شده ، profit مشتق می‌شود.
     ========================================================= */
  // kind: 'service' (ویزیت/جراحی/آزمایش…) یا 'product' (کالای فروشگاهی/دارو)
  const TOP_PRODUCTS = [
    { name: 'ویزیت عمومی',        cat: 'خدمات درمانی', kind: 'service', icon: 'clipboard', tone: 'teal',  qty: 132, revenue: 47_520_000, cost: 12_800_000 },
    { name: 'غذای خشک گربه رویال', cat: 'پت‌شاپ',       kind: 'product', icon: 'cart',      tone: 'blue',  qty: 88,  revenue: 30_800_000, cost: 22_600_000 },
    { name: 'واکسن سه‌گانه سگ',    cat: 'واکسیناسیون',  kind: 'service', icon: 'syringe',   tone: 'peach', qty: 76,  revenue: 26_600_000, cost: 11_400_000 },
    { name: 'آزمایش CBC کامل',     cat: 'آزمایشگاه',    kind: 'service', icon: 'flask',     tone: 'lilac', qty: 64,  revenue: 25_600_000, cost: 7_680_000 },
    { name: 'سونوگرافی شکمی',      cat: 'خدمات درمانی', kind: 'service', icon: 'spark',     tone: 'teal',  qty: 41,  revenue: 24_600_000, cost: 6_150_000 },
    { name: 'قرص ضد انگل داخلی',   cat: 'داروخانه',     kind: 'product', icon: 'flask',     tone: 'blue',  qty: 154, revenue: 18_480_000, cost: 9_240_000 },
    { name: 'جراحی عقیم‌سازی گربه', cat: 'خدمات درمانی', kind: 'service', icon: 'scissors',  tone: 'teal',  qty: 18,  revenue: 21_600_000, cost: 5_400_000 },
    { name: 'شامپو ضد کنه',         cat: 'پت‌شاپ',       kind: 'product', icon: 'bath',      tone: 'blue',  qty: 63,  revenue: 9_450_000,  cost: 6_930_000 },
  ].map(p => {
    const profit = p.revenue - p.cost;
    return { ...p, profit, margin: p.revenue ? profit / p.revenue * 100 : 0 };
  });

  /* تفکیک خدمات در برابر کالا — پاسخ به «درآمد از ویزیت یا فروش اجناس؟» */
  function serviceVsProduct() {
    const acc = {
      service: { kind: 'service', label: 'خدمات و ویزیت', icon: 'clipboard', color: '#0d9488', revenue: 0, profit: 0, qty: 0 },
      product: { kind: 'product', label: 'فروش کالا و دارو', icon: 'cart',    color: '#5c8ad6', revenue: 0, profit: 0, qty: 0 },
    };
    TOP_PRODUCTS.forEach(p => {
      const b = acc[p.kind] || acc.product;
      b.revenue += p.revenue; b.profit += p.profit; b.qty += p.qty;
    });
    return [acc.service, acc.product].map(b => ({ ...b, margin: b.revenue ? b.profit / b.revenue * 100 : 0 }));
  }

  /* =========================================================
     8) گونه‌های مراجعه‌کننده (تحلیل مشتری)
     ========================================================= */
  const SPECIES = [
    { label: 'سگ',    value: 168, color: '#0d9488' },
    { label: 'گربه',  value: 142, color: '#8fb5f0' },
    { label: 'پرنده', value: 68,  color: '#eab749' },
    { label: 'خرگوش', value: 40,  color: '#b8a2e6' },
    { label: 'خزنده', value: 14,  color: '#e08a5c' },
  ];

  /* =========================================================
     9) هزینه‌های ثبت‌شده اخیر (جدول قابل رشد)
     mutable — «ثبت هزینه» یک رکورد به ابتدای این آرایه می‌افزاید.
     ========================================================= */
  const EXPENSE_LOG = [
    { id: 'e1', desc: 'شارژ ساختمان آبان',        cat: 'rent',      date: '1405/08/01', method: 'transfer', amount: 3_200_000, by: 'آریا نیک' },
    { id: 'e2', desc: 'سرویس دستگاه اتوکلاو',      cat: 'repair',    date: '1405/07/28', method: 'pos',      amount: 2_850_000, by: 'پریسا صداقت' },
    { id: 'e3', desc: 'خرید سرم و لوازم مصرفی',    cat: 'supply',    date: '1405/07/26', method: 'transfer', amount: 6_400_000, by: 'آریا نیک' },
    { id: 'e4', desc: 'حقوق پرسنل مهر',            cat: 'salary',    date: '1405/07/25', method: 'transfer', amount: 48_000_000,by: 'واحد مالی' },
    { id: 'e5', desc: 'تبلیغات اینستاگرام',        cat: 'marketing', date: '1405/07/22', method: 'credit',   amount: 4_200_000, by: 'واحد مارکتینگ' },
    { id: 'e6', desc: 'قبض برق دوماهه',            cat: 'rent',      date: '1405/07/20', method: 'transfer', amount: 1_850_000, by: 'آریا نیک' },
    { id: 'e7', desc: 'خرید غذای خشک (پت‌شاپ)',    cat: 'supply',    date: '1405/07/18', method: 'transfer', amount: 12_300_000,by: 'واحد خرید' },
    { id: 'e8', desc: 'بیمه تکمیلی پرسنل',         cat: 'tax',       date: '1405/07/15', method: 'transfer', amount: 5_600_000, by: 'واحد مالی' },
    { id: 'e9', desc: 'تعمیر کولر گازی اتاق عمل',  cat: 'repair',    date: '1405/07/12', method: 'cash',     amount: 1_650_000, by: 'پریسا صداقت' },
    { id: 'e10',desc: 'طراحی بروشور خدمات',        cat: 'marketing', date: '1405/07/10', method: 'pos',      amount: 2_100_000, by: 'واحد مارکتینگ' },
    { id: 'e11',desc: 'خرید داروی بیهوشی',         cat: 'supply',    date: '1405/07/08', method: 'transfer', amount: 8_700_000, by: 'دکتر نیک' },
    { id: 'e12',desc: 'ملزومات اداری و پرینتر',    cat: 'other',     date: '1405/07/05', method: 'cash',     amount: 1_320_000, by: 'واحد اداری' },
  ];

  let expUid = 100;

  /* =========================================================
     10) بینش‌های هوشمند (کارت‌های تحلیلی متنی)
     ========================================================= */
  const INSIGHTS = [
    { icon: 'trendUp', tone: 'ok',    text: 'حاشیه سود «سونوگرافی شکمی» با ۷۵٪ بالاترین سودآوری را دارد؛ ظرفیت رزرو آن را افزایش دهید.' },
    { icon: 'alert',   tone: 'warn',  text: 'هزینه «خرید دارو و کالا» نسبت به بازه قبل ۱۲٪ رشد داشته — قیمت تأمین‌کنندگان را بازبینی کنید.' },
    { icon: 'bulb',    tone: 'info',  text: 'پنج‌شنبه‌ها پرترافیک‌ترین روز هفته است؛ افزودن یک شیفت می‌تواند درآمد را بیشتر کند.' },
    { icon: 'piggy',   tone: 'lilac', text: 'با روند فعلی، سود خالص این ماه حدود ۱۸٪ بیشتر از ماه گذشته پیش‌بینی می‌شود.' },
  ];

  /* =========================================================
     خروجی سراسری
     ========================================================= */
  window.FinanceData = {
    EXPENSE_CATEGORIES, REVENUE_CHANNELS, PAYMENT_METHODS,
    RANGES, DAILY, TOP_PRODUCTS, SPECIES, INSIGHTS,
    TODAY,
    // heatmap intensity 0..5 (روز هفته × ساعت ۹تا۲۰) — الگوی مراجعه
    HEATMAP: [
      [1,0,1,2,3,2,4,3,4,4,3,2],
      [0,1,2,3,4,3,4,4,4,4,3,1],
      [1,1,2,3,4,4,3,4,3,4,3,2],
      [1,2,3,4,4,3,4,4,4,3,2,1],
      [1,2,4,4,3,3,4,3,4,4,3,2],
      [2,3,4,4,4,4,4,4,3,3,2,1],
      [0,0,1,1,2,2,3,2,2,1,1,0],
    ],
    PAYMENT_SPLIT: [
      { id: 'pos',      value: 49 },
      { id: 'cash',     value: 21 },
      { id: 'transfer', value: 17 },
      { id: 'credit',   value: 13 },
    ],

    aggregate,
    serviceVsProduct,

    get expenseLog() { return EXPENSE_LOG; },
    catById: (id) => EXPENSE_CATEGORIES.find(c => c.id === id) || { label: id, color: '#7a8b89' },
    methodById: (id) => PAYMENT_METHODS.find(m => m.id === id) || { label: id },

    // add a new expense record to the in-memory log (design-only mutation)
    addExpense(rec) {
      const row = {
        id: 'e' + (++expUid),
        desc: rec.desc || 'هزینه بدون شرح',
        cat: rec.cat || 'other',
        date: rec.date || TODAY,
        method: rec.method || 'cash',
        amount: Number(rec.amount) || 0,
        by: 'دکتر آریا نیک',
      };
      EXPENSE_LOG.unshift(row);
      return row;
    },
  };
})();
