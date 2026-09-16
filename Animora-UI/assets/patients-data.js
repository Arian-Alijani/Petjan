/* =========================================================
   Vetplus — Patients & Owners : mock data + helpers
   Design prototype only (no backend).
   ========================================================= */

/* ---------- Jalali <-> Gregorian (compact, jalaali-js algorithm) ---------- */
(function () {
  const bp = [-61,9,38,199,426,686,756,818,1111,1181,1210,1635,2060,2097,2192,2262,
              2324,2394,2456,3178];
  function jalCal(jy){
    const gy = jy + 621; let leapJ = -14, jp = bp[0], jump = 0;
    for (let i = 1; i < 20; i++) {
      const jm = bp[i]; jump = jm - jp;
      if (jy < jm) break;
      leapJ += Math.floor(jump / 33) * 8 + Math.floor((jump % 33) / 4);
      jp = jm;
    }
    let n = jy - jp;
    leapJ += Math.floor(n / 33) * 8 + Math.floor(((n % 33) + 3) / 4);
    if ((jump % 33) === 4 && jump - n === 4) leapJ += 1;
    const leapG = Math.floor(gy / 4) - Math.floor((Math.floor(gy / 100) + 1) * 3 / 4) - 150;
    const march = 20 + leapJ - leapG;
    return { gy, march };
  }
  function j2d(jy, jm, jd) {
    const r = jalCal(jy);
    return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - Math.floor(jm / 7) * (jm - 7) + jd - 1;
  }
  function g2d(gy, gm, gd) {
    let d = Math.floor((gy + Math.floor((gm - 8) / 6) + 100100) * 1461 / 4)
      + Math.floor((153 * ((gm + 9) % 12) + 2) / 5) + gd - 34840408;
    d = d - Math.floor(Math.floor((gy + 100100 + Math.floor((gm - 8) / 6)) / 100) * 3 / 4) + 752;
    return d;
  }
  function d2g(jdn) {
    let j = 4 * jdn + 139361631;
    j = j + Math.floor((Math.floor((4 * jdn + 183187720) / 146097) * 3) / 4) * 4 - 3908;
    const i = Math.floor((j % 1461) / 4) * 5 + 308;
    const gd = Math.floor((i % 153) / 5) + 1;
    const gm = Math.floor(i / 153) % 12 + 1;
    const gy = Math.floor(j / 1461) - 100100 + Math.floor((8 - gm) / 6);
    return { gy, gm, gd };
  }
  function d2j(jdn) {
    let gy = d2g(jdn).gy, jy = gy - 621;
    const r = jalCal(jy);
    const jdn1f = g2d(gy, 3, r.march);
    let k = jdn - jdn1f, jm, jd;
    if (k >= 0) {
      if (k <= 185) { jm = 1 + Math.floor(k / 31); jd = (k % 31) + 1; return { jy, jm, jd }; }
      k -= 186;
    } else { jy -= 1; k += 179; if (r.leap === 1) k += 1; }
    jm = 7 + Math.floor(k / 30); jd = (k % 30) + 1;
    return { jy, jm, jd };
  }
  window.jalaliToDate = function (s) {
    if (!s) return null;
    const p = String(s).split('/').map(Number);
    if (p.length < 3 || p.some(isNaN)) return null;
    const g = d2g(j2d(p[0], p[1], p[2]));
    return new Date(g.gy, g.gm - 1, g.gd);
  };
  window.dateToJalali = function (d) {
    const r = d2j(g2d(d.getFullYear(), d.getMonth() + 1, d.getDate()));
    return `${r.jy}/${String(r.jm).padStart(2, '0')}/${String(r.jd).padStart(2, '0')}`;
  };
})();

/* today, fixed for a stable prototype */
window.TODAY_J = '1404/05/11';
window.TODAY = window.jalaliToDate(window.TODAY_J) || new Date();

/* ---------- small helpers ---------- */
window.fa = (v) => String(v).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]);
window.faNum = (n) => window.fa(Number(n).toLocaleString('en-US'));

window.ageFromJalali = function (birth) {
  const d = window.jalaliToDate(birth);
  if (!d) return { label: '—', years: 0, months: 0, days: 0 };
  let ms = window.TODAY - d;
  if (ms < 0) ms = 0;
  const totalDays = Math.floor(ms / 86400000);
  const years = Math.floor(totalDays / 365.2425);
  const months = Math.floor((totalDays - years * 365.2425) / 30.4375);
  let label;
  if (years >= 1) label = months > 0 ? `${window.fa(years)} سال و ${window.fa(months)} ماه` : `${window.fa(years)} سال`;
  else if (months >= 1) label = `${window.fa(months)} ماه`;
  else label = `${window.fa(totalDays)} روز`;
  return { label, years, months, days: totalDays, short: years >= 1 ? `${window.fa(years)} ساله` : `${window.fa(months)} ماهه` };
};

window.sinceJalali = function (jdate) {
  const d = window.jalaliToDate(jdate);
  if (!d) return '—';
  const days = Math.round((window.TODAY - d) / 86400000);
  if (days <= 0) return 'امروز';
  if (days === 1) return 'دیروز';
  if (days < 7) return `${window.fa(days)} روز پیش`;
  if (days < 31) return `${window.fa(Math.floor(days / 7))} هفته پیش`;
  if (days < 365) return `${window.fa(Math.floor(days / 30))} ماه پیش`;
  return `${window.fa(Math.floor(days / 365))} سال پیش`;
};
window.untilJalali = function (jdate) {
  const d = window.jalaliToDate(jdate);
  if (!d) return { days: 0, label: '—' };
  const days = Math.round((d - window.TODAY) / 86400000);
  let label;
  if (days === 0) label = 'امروز';
  else if (days > 0) label = days < 31 ? `${window.fa(days)} روز دیگر` : `${window.fa(Math.floor(days / 30))} ماه دیگر`;
  else label = `${window.fa(Math.abs(days))} روز گذشته`;
  return { days, label };
};

/* ---------- taxonomy ---------- */
window.SPECIES = {
  cat:    { label: 'گربه',    icon: 'cat',    tone: 'lilac',  breeds: ['پرشین', 'اسکاتیش فولد', 'برمه‌ای', 'مو کوتاه اهلی', 'رگدال', 'بنگال'] },
  dog:    { label: 'سگ',      icon: 'dog',    tone: 'peach',  breeds: ['گلدن رتریور', 'شیتزو', 'پامرانین', 'هاسکی', 'ژرمن شپرد', 'پودل'] },
  bird:   { label: 'پرنده',   icon: 'bird',   tone: 'amber',  breeds: ['طوطی کاسکو', 'کاکادو', 'مرغ عشق', 'قناری', 'روزلا'] },
  rabbit: { label: 'خرگوش',   icon: 'rabbit', tone: 'rose',   breeds: ['لوپ هلندی', 'آنقوره', 'رکس', 'مینی لوپ'] },
  rodent: { label: 'جوندگان', icon: 'more2',  tone: 'mint',   breeds: ['همستر سوری', 'خوکچه هندی', 'چینچیلا'] },
  other:  { label: 'سایر',    icon: 'more2',  tone: 'mint',   breeds: ['لاک‌پشت', 'ایگوانا', 'مار'] },
};

window.REFERRALS = ['معرفی دوستان', 'اینستاگرام', 'جستجوی گوگل', 'تابلوی کلینیک', 'پت‌شاپ همکار', 'دامپزشک ارجاع‌دهنده', 'سایر'];
window.HABITATS  = ['آپارتمان', 'خانه حیاط‌دار', 'باغ / فضای باز', 'قفس', 'آکواریوم / تراریوم', 'خیابانی (سرپرستی)'];
window.BEHAVIORS = ['آرام و اجتماعی', 'بازیگوش', 'مضطرب', 'پرخاشگر با غریبه', 'ترسو', 'کنجکاو'];
window.DIETS     = ['غذای خشک تجاری', 'غذای کنسروی', 'غذای خانگی', 'رژیم درمانی (رنال)', 'رژیم درمانی (گوارشی)', 'مخلوط خشک و تر', 'دانه و میوه'];

window.VACCINE_TYPES = {
  cat:    ['واکسن سه‌گانه (FVRCP)', 'واکسن چهارگانه', 'هاری', 'لوسمی گربه (FeLV)'],
  dog:    ['واکسن چهارگانه (DHPP)', 'واکسن هشت‌گانه', 'هاری', 'سرفه سگ‌ها (کنل کاف)', 'لپتوسپیروز'],
  bird:   ['نیوکاسل', 'پلی‌اوما'],
  rabbit: ['میکسوماتوز', 'RHD'],
  rodent: ['ضد انگل عمومی'],
  other:  ['ضد انگل عمومی'],
};

/* ---------- OWNERS ---------- */
window.OWNERS = [
  {
    id: 'ow-1', file: '۷۲-۱۰۱۲', first: 'نگار', last: 'محمدی', gender: 'female',
    mobile1: '09121234567', mobile2: '09301234567', phone: '02188776655',
    nationalId: '0079384512', referral: 'اینستاگرام',
    address: 'تهران، سعادت‌آباد، خیابان علامه شمالی، کوچه ۱۸ شرقی، پلاک ۲۴، واحد ۵',
    since: '1400/02/14', tone: 'a', balance: 0, tags: ['مشتری وفادار'],
  },
  {
    id: 'ow-2', file: '۷۲-۱۰۳۴', first: 'امیر', last: 'رستگار', gender: 'male',
    mobile1: '09124445566', mobile2: '', phone: '02144332211',
    nationalId: '0064128877', referral: 'معرفی دوستان',
    address: 'تهران، شهرک غرب، بلوار دادمان، مجتمع آسمان، بلوک ب، طبقه ۷',
    since: '1399/11/03', tone: 'b', balance: 1250000, tags: ['بدهکار'],
  },
  {
    id: 'ow-3', file: '۷۲-۱۰۵۸', first: 'سارا', last: 'یوسفی', gender: 'female',
    mobile1: '09357778899', mobile2: '09197778899', phone: '02166554433',
    nationalId: '0018765432', referral: 'جستجوی گوگل',
    address: 'کرج، عظیمیه، میدان مهران، خیابان گلستان، پلاک ۹',
    since: '1401/07/22', tone: 'c', balance: 0, tags: [],
  },
  {
    id: 'ow-4', file: '۷۲-۱۰۷۷', first: 'علی', last: 'نوروزی', gender: 'male',
    mobile1: '09192223344', mobile2: '', phone: '',
    nationalId: '0092233445', referral: 'پت‌شاپ همکار',
    address: 'تهران، پاسداران، گلستان پنجم، پلاک ۱۲',
    since: '1402/01/19', tone: 'd', balance: 0, tags: [],
  },
  {
    id: 'ow-5', file: '۷۲-۱۰۹۱', first: 'آرمان', last: 'توسلی', gender: 'male',
    mobile1: '09333334455', mobile2: '09121119988', phone: '02122119933',
    nationalId: '0037788991', referral: 'دامپزشک ارجاع‌دهنده',
    address: 'تهران، نیاوران، خیابان جمشیدیه، کوچه یاس، پلاک ۳',
    since: '1398/05/30', tone: 'e', balance: 0, tags: ['VIP'],
  },
  {
    id: 'ow-6', file: '۷۲-۱۱۰۴', first: 'مهسا', last: 'کریمی', gender: 'female',
    mobile1: '09025556677', mobile2: '', phone: '02133445566',
    nationalId: '0055443322', referral: 'تابلوی کلینیک',
    address: 'تهران، تهرانپارس، فلکه دوم، خیابان ۱۹۶ غربی، پلاک ۴۱',
    since: '1403/03/08', tone: 'f', balance: 0, tags: [],
  },
];

/* ---------- PETS ---------- */
window.PETS = [
  {
    id: 'pt-1', ownerId: 'ow-1', code: '۷۲-۱۳۸۶', name: 'پشمک', species: 'cat', breed: 'پرشین',
    gender: 'female', birth: '1401/04/21', color: 'سفید کرم', behavior: 'آرام و اجتماعی',
    habitat: 'آپارتمان', diet: 'مخلوط خشک و تر', neutered: true, neuteredDate: '1402/02/10',
    blood: 'A', insurance: 'بیمه پت‌کر — طلایی',
    microchip: { code: '985141002317654', date: '1401/09/02', vendor: 'Trovan', location: 'بین دو کتف' },
    allergies: ['حساسیت به گوشت مرغ'],
    chronic: [],
    surgeries: [
      { title: 'عقیم‌سازی (اواریوهیسترکتومی)', date: '1402/02/10', surgeon: 'دکتر آریا نیک', note: 'بدون عارضه، بخیه‌ها روز دهم کشیده شد.' },
    ],
    status: 'healthy', tone: 'a',
    weights: [
      { d: '1401/06/01', w: 1.4 }, { d: '1401/09/02', w: 2.3 }, { d: '1402/01/15', w: 3.1 },
      { d: '1402/06/20', w: 3.6 }, { d: '1403/01/10', w: 3.9 }, { d: '1403/08/05', w: 4.2 },
      { d: '1404/02/12', w: 4.5 }, { d: '1404/05/11', w: 4.4 },
    ],
    sizes: [
      { d: '1401/09/02', h: 18, l: 32 }, { d: '1402/06/20', h: 24, l: 41 },
      { d: '1403/08/05', h: 26, l: 45 }, { d: '1404/05/11', h: 27, l: 46 },
    ],
    vitals: { temp: 38.6, pulse: 148, resp: 26, bcs: '۵ از ۹' },
    visits: [
      { id:'v1', date: '1404/05/11', type: 'واکسیناسیون', vet: 'دکتر آریا نیک', reason: 'نوبت سالانه واکسن سه‌گانه',
        exam: 'وضعیت عمومی مطلوب، مخاطات صورتی، غدد لنفاوی طبیعی.', dx: 'حیوان سالم — واجد شرایط واکسن',
        rx: ['واکسن FVRCP — تزریق زیرجلدی'], weight: 4.4, temp: 38.6, cost: 850000, status: 'done' },
      { id:'v2', date: '1404/02/12', type: 'معاینه دوره‌ای', vet: 'دکتر سمانه راد', reason: 'چکاپ فصلی و بررسی وزن',
        exam: 'شکم نرم، بدون درد لمسی. دندان‌ها جرم خفیف دارند.', dx: 'جرم‌گیری توصیه شد',
        rx: ['خمیر دندان آنزیمی — روزانه'], weight: 4.5, temp: 38.4, cost: 450000, status: 'done' },
      { id:'v3', date: '1403/11/28', type: 'درمانی', vet: 'دکتر آریا نیک', reason: 'استفراغ و بی‌اشتهایی ۲ روزه',
        exam: 'دهیدراسیون خفیف، شکم حساس در ناحیه قدامی.', dx: 'گاستریت حاد — احتمال هربال',
        rx: ['مترونیدازول ۵۰ میلی‌گرم — ۲ بار در روز، ۵ روز', 'سرم زیرجلدی ۱۰۰ سی‌سی', 'مالت پیشگیری از هربال'],
        weight: 4.1, temp: 39.1, cost: 1850000, status: 'done' },
      { id:'v4', date: '1403/08/05', type: 'واکسیناسیون', vet: 'دکتر آریا نیک', reason: 'واکسن هاری سالانه',
        exam: 'سالم', dx: 'واجد شرایط', rx: ['واکسن هاری'], weight: 4.2, temp: 38.5, cost: 700000, status: 'done' },
      { id:'v5', date: '1402/02/10', type: 'جراحی', vet: 'دکتر آریا نیک', reason: 'عقیم‌سازی انتخابی',
        exam: 'آزمایش قبل از بیهوشی طبیعی.', dx: 'اواریوهیسترکتومی انجام شد',
        rx: ['ملوکسیکام ۳ روز', 'سفالکسین ۷ روز', 'یقه محافظ ۱۰ روز'], weight: 3.3, temp: 38.2, cost: 4200000, status: 'done' },
    ],
    vaccines: [
      { name: 'واکسن سه‌گانه (FVRCP)', date: '1404/05/11', next: '1405/05/11', lot: 'FV-8821', vet: 'دکتر آریا نیک', site: 'زیرجلدی شانه راست' },
      { name: 'هاری', date: '1403/08/05', next: '1404/08/05', lot: 'RB-4412', vet: 'دکتر آریا نیک', site: 'زیرجلدی ران چپ' },
      { name: 'واکسن سه‌گانه (FVRCP)', date: '1403/05/09', next: '1404/05/09', lot: 'FV-7734', vet: 'دکتر سمانه راد', site: 'زیرجلدی شانه راست' },
      { name: 'ضد انگل خارجی', date: '1404/04/02', next: '1404/07/02', lot: 'BR-1190', vet: 'دکتر سمانه راد', site: 'موضعی پشت گردن' },
    ],
    labs: [
      { name: 'CBC + بیوشیمی کامل', date: '1403/11/28', result: 'در محدوده طبیعی', status: 'normal' },
      { name: 'سونوگرافی شکم', date: '1403/11/28', result: 'ضخامت جدار معده مختصر افزایش', status: 'warn' },
    ],
    docs: [
      { name: 'شناسنامه سلامت.pdf', size: '۴۲۰ کیلوبایت', date: '1401/09/02' },
      { name: 'رادیوگرافی-قفسه-سینه.jpg', size: '۱٫۲ مگابایت', date: '1403/11/28' },
    ],
    notes: 'در زمان معاینه مضطرب می‌شود؛ استفاده از حوله آرام‌بخش توصیه شده است.',
  },
  {
    id: 'pt-2', ownerId: 'ow-1', code: '۷۲-۱۴۱۰', name: 'نارنجی', species: 'cat', breed: 'مو کوتاه اهلی',
    gender: 'male', birth: '1403/01/05', color: 'نارنجی تبی', behavior: 'بازیگوش',
    habitat: 'آپارتمان', diet: 'غذای خشک تجاری', neutered: false, neuteredDate: '',
    blood: 'A', insurance: '',
    microchip: { code: '985141004498210', date: '1403/06/18', vendor: 'Datamars', location: 'بین دو کتف' },
    allergies: [], chronic: [],
    surgeries: [],
    status: 'followup', tone: 'c',
    weights: [
      { d: '1403/03/10', w: 0.9 }, { d: '1403/06/18', w: 2.1 }, { d: '1403/10/01', w: 3.2 },
      { d: '1404/02/20', w: 4.0 }, { d: '1404/05/02', w: 4.3 },
    ],
    sizes: [ { d: '1403/06/18', h: 16, l: 28 }, { d: '1404/02/20', h: 23, l: 39 }, { d: '1404/05/02', h: 25, l: 42 } ],
    vitals: { temp: 38.9, pulse: 160, resp: 30, bcs: '۴ از ۹' },
    visits: [
      { id:'v1', date: '1404/05/02', type: 'معاینه', vet: 'دکتر سمانه راد', reason: 'خارش گوش و تکان دادن سر',
        exam: 'ترشح قهوه‌ای در مجرای گوش راست.', dx: 'اوتودکتوز (جرب گوش)',
        rx: ['قطره گوش ایورمکتین — ۱۰ روز', 'شست‌وشوی گوش هفتگی'], weight: 4.3, temp: 38.9, cost: 950000, status: 'done' },
      { id:'v2', date: '1403/10/01', type: 'واکسیناسیون', vet: 'دکتر آریا نیک', reason: 'نوبت دوم واکسن سه‌گانه',
        exam: 'سالم', dx: 'واجد شرایط', rx: ['FVRCP دوز دوم'], weight: 3.2, temp: 38.6, cost: 800000, status: 'done' },
    ],
    vaccines: [
      { name: 'واکسن سه‌گانه (FVRCP)', date: '1403/10/01', next: '1404/10/01', lot: 'FV-8102', vet: 'دکتر آریا نیک', site: 'زیرجلدی شانه راست' },
      { name: 'هاری', date: '1403/11/12', next: '1404/11/12', lot: 'RB-5521', vet: 'دکتر آریا نیک', site: 'زیرجلدی ران چپ' },
    ],
    labs: [ { name: 'تست FeLV/FIV', date: '1403/06/18', result: 'منفی', status: 'normal' } ],
    docs: [ { name: 'کارت-واکسن.pdf', size: '۲۱۰ کیلوبایت', date: '1403/10/01' } ],
    notes: 'هنوز عقیم نشده — یادآوری برای بهار ۱۴۰۵.',
  },
  {
    id: 'pt-3', ownerId: 'ow-2', code: '۷۲-۱۳۸۹', name: 'لئو', species: 'dog', breed: 'گلدن رتریور',
    gender: 'male', birth: '1399/03/12', color: 'کرم طلایی', behavior: 'آرام و اجتماعی',
    habitat: 'خانه حیاط‌دار', diet: 'رژیم درمانی (گوارشی)', neutered: true, neuteredDate: '1400/08/22',
    blood: 'DEA 1.1 مثبت', insurance: 'بیمه رازی — نقره‌ای',
    microchip: { code: '900182001765430', date: '1399/09/14', vendor: 'Trovan', location: 'گردن چپ' },
    allergies: ['حساسیت فصلی به گرده'], chronic: ['دیسپلازی خفیف مفصل ران'],
    surgeries: [
      { title: 'عقیم‌سازی (ارکیدکتومی)', date: '1400/08/22', surgeon: 'دکتر آریا نیک', note: 'ریکاوری طبیعی.' },
      { title: 'برداشت جسم خارجی از معده', date: '1402/12/03', surgeon: 'دکتر سمانه راد', note: 'یک جوراب؛ ۲ روز بستری.' },
    ],
    status: 'followup', tone: 'b',
    weights: [
      { d: '1399/06/01', w: 8 }, { d: '1399/12/01', w: 21 }, { d: '1400/06/01', w: 28 },
      { d: '1401/06/01', w: 31 }, { d: '1402/06/01', w: 33.5 }, { d: '1403/06/01', w: 35.2 },
      { d: '1404/01/20', w: 34.1 }, { d: '1404/05/09', w: 33.2 },
    ],
    sizes: [
      { d: '1399/12/01', h: 48, l: 78 }, { d: '1400/12/01', h: 56, l: 92 },
      { d: '1402/06/01', h: 58, l: 96 }, { d: '1404/05/09', h: 58, l: 97 },
    ],
    vitals: { temp: 38.3, pulse: 92, resp: 22, bcs: '۶ از ۹' },
    visits: [
      { id:'v1', date: '1404/05/09', type: 'پیگیری', vet: 'دکتر سمانه راد', reason: 'لنگش پای عقب راست پس از دویدن',
        exam: 'درد در اکستنشن مفصل ران راست، دامنه حرکتی کاهش‌یافته.', dx: 'تشدید دیسپلازی مفصل ران',
        rx: ['گاباپنتین ۱۰۰ میلی‌گرم شبانه', 'مکمل گلوکزامین', 'محدودیت فعالیت ۲ هفته', 'فیزیوتراپی هفتگی'],
        weight: 33.2, temp: 38.3, cost: 2100000, status: 'done' },
      { id:'v2', date: '1404/01/20', type: 'معاینه دوره‌ای', vet: 'دکتر آریا نیک', reason: 'چکاپ سالانه',
        exam: 'قلب و ریه طبیعی، اضافه‌وزن خفیف.', dx: 'کاهش وزن توصیه شد',
        rx: ['رژیم کنترل وزن', 'پیاده‌روی روزانه ۴۵ دقیقه'], weight: 34.1, temp: 38.1, cost: 600000, status: 'done' },
      { id:'v3', date: '1402/12/03', type: 'جراحی', vet: 'دکتر سمانه راد', reason: 'استفراغ مکرر و بی‌اشتهایی',
        exam: 'رادیوگرافی: انسداد نسبی روده باریک.', dx: 'جسم خارجی گوارشی',
        rx: ['لاپاراتومی و انتروتومی', 'آنتی‌بیوتیک ۱۰ روز', 'غذای نرم ۷ روز'], weight: 32.8, temp: 39.4, cost: 12500000, status: 'done' },
    ],
    vaccines: [
      { name: 'واکسن چهارگانه (DHPP)', date: '1404/01/20', next: '1405/01/20', lot: 'DH-3391', vet: 'دکتر آریا نیک', site: 'زیرجلدی شانه چپ' },
      { name: 'هاری', date: '1403/12/02', next: '1404/12/02', lot: 'RB-6612', vet: 'دکتر سمانه راد', site: 'زیرجلدی ران راست' },
      { name: 'سرفه سگ‌ها (کنل کاف)', date: '1403/09/15', next: '1404/09/15', lot: 'KC-2210', vet: 'دکتر آریا نیک', site: 'داخل بینی' },
      { name: 'لپتوسپیروز', date: '1403/06/11', next: '1404/06/11', lot: 'LP-7781', vet: 'دکتر آریا نیک', site: 'زیرجلدی شانه چپ' },
    ],
    labs: [
      { name: 'رادیوگرافی لگن', date: '1404/05/09', result: 'کاهش فضای مفصلی دو طرفه', status: 'warn' },
      { name: 'پروفایل کبدی', date: '1404/01/20', result: 'ALT مرزی', status: 'warn' },
    ],
    docs: [
      { name: 'رادیولوژی-لگن.dcm', size: '۸٫۴ مگابایت', date: '1404/05/09' },
      { name: 'گزارش-جراحی-۱۴۰۲.pdf', size: '۶۸۰ کیلوبایت', date: '1402/12/03' },
    ],
    notes: 'در محیط کلینیک بسیار همکار است. مالک برنامه فیزیوتراپی هفتگی دارد.',
  },
  {
    id: 'pt-4', ownerId: 'ow-2', code: '۷۲-۱۴۲۲', name: 'روبی', species: 'dog', breed: 'پامرانین',
    gender: 'female', birth: '1402/09/01', color: 'قهوه‌ای روشن', behavior: 'مضطرب',
    habitat: 'آپارتمان', diet: 'غذای خشک تجاری', neutered: false, neuteredDate: '',
    blood: '—', insurance: '',
    microchip: { code: '', date: '', vendor: '', location: '' },
    allergies: [], chronic: [],
    surgeries: [],
    status: 'healthy', tone: 'f',
    weights: [ { d: '1403/01/10', w: 1.2 }, { d: '1403/08/01', w: 2.4 }, { d: '1404/03/15', w: 3.1 } ],
    sizes: [ { d: '1403/08/01', h: 20, l: 30 }, { d: '1404/03/15', h: 23, l: 34 } ],
    vitals: { temp: 38.7, pulse: 120, resp: 24, bcs: '۵ از ۹' },
    visits: [
      { id:'v1', date: '1404/03/15', type: 'اصلاح و بهداشت', vet: 'کارشناس گرومینگ', reason: 'کوتاه کردن مو و ناخن',
        exam: 'پوست سالم', dx: '—', rx: ['شامپو ضد شوره ماهانه'], weight: 3.1, temp: 38.7, cost: 900000, status: 'done' },
    ],
    vaccines: [
      { name: 'واکسن چهارگانه (DHPP)', date: '1403/08/01', next: '1404/08/01', lot: 'DH-2214', vet: 'دکتر آریا نیک', site: 'زیرجلدی شانه چپ' },
    ],
    labs: [], docs: [],
    notes: 'میکروچیپ هنوز کاشته نشده — در نوبت بعدی انجام شود.',
  },
  {
    id: 'pt-5', ownerId: 'ow-3', code: '۷۲-۱۳۹۱', name: 'آبی', species: 'bird', breed: 'طوطی کاسکو',
    gender: 'unknown', birth: '1400/10/08', color: 'خاکستری با دم قرمز', behavior: 'کنجکاو',
    habitat: 'قفس', diet: 'دانه و میوه', neutered: false, neuteredDate: '',
    blood: '—', insurance: '',
    microchip: { code: '985141007712345', date: '1402/03/11', vendor: 'Datamars', location: 'عضله سینه' },
    allergies: [], chronic: [],
    surgeries: [],
    status: 'healthy', tone: 'c',
    weights: [ { d: '1402/03/11', w: 0.41 }, { d: '1403/03/09', w: 0.44 }, { d: '1404/04/28', w: 0.46 } ],
    sizes: [ { d: '1402/03/11', h: 31, l: 33 }, { d: '1404/04/28', h: 33, l: 35 } ],
    vitals: { temp: 41.2, pulse: 340, resp: 40, bcs: '۵ از ۹' },
    visits: [
      { id:'v1', date: '1404/04/28', type: 'آزمایش', vet: 'دکتر سمانه راد', reason: 'غربالگری سالانه و تعیین جنسیت',
        exam: 'پرها سالم، منقار و ناخن مرتب شد.', dx: 'سالم — نتیجه DNA در انتظار',
        rx: ['مکمل کلسیم هفتگی'], weight: 0.46, temp: 41.2, cost: 1400000, status: 'done' },
    ],
    vaccines: [
      { name: 'نیوکاسل', date: '1403/12/20', next: '1404/12/20', lot: 'ND-1102', vet: 'دکتر سمانه راد', site: 'عضلانی سینه' },
    ],
    labs: [ { name: 'تست DNA تعیین جنسیت', date: '1404/04/28', result: 'در انتظار نتیجه', status: 'pending' } ],
    docs: [],
    notes: 'به صداهای بلند حساس است؛ اتاق معاینه آرام ترجیح داده شود.',
  },
  {
    id: 'pt-6', ownerId: 'ow-4', code: '۷۲-۱۳۹۵', name: 'برفی', species: 'rabbit', breed: 'لوپ هلندی',
    gender: 'female', birth: '1403/02/17', color: 'سفید و خاکستری', behavior: 'ترسو',
    habitat: 'خانه حیاط‌دار', diet: 'غذای خانگی', neutered: false, neuteredDate: '',
    blood: '—', insurance: '',
    microchip: { code: '', date: '', vendor: '', location: '' },
    allergies: [], chronic: ['رشد بیش از حد دندان'],
    surgeries: [],
    status: 'followup', tone: 'd',
    weights: [ { d: '1403/05/01', w: 0.8 }, { d: '1403/11/12', w: 1.6 }, { d: '1404/04/29', w: 1.9 } ],
    sizes: [ { d: '1403/11/12', h: 17, l: 30 }, { d: '1404/04/29', h: 19, l: 33 } ],
    vitals: { temp: 39.0, pulse: 180, resp: 42, bcs: '۵ از ۹' },
    visits: [
      { id:'v1', date: '1404/04/29', type: 'درمانی', vet: 'دکتر آریا نیک', reason: 'کاهش اشتها و ریزش آب دهان',
        exam: 'رشد بیش از حد دندان‌های آسیاب، زخم گونه.', dx: 'مال‌اکلوژن دندانی',
        rx: ['سوهان‌کاری دندان تحت آرام‌بخشی', 'مسکن ۳ روز', 'افزایش علوفه خشک'], weight: 1.9, temp: 39.0, cost: 3200000, status: 'done' },
    ],
    vaccines: [
      { name: 'میکسوماتوز', date: '1403/11/12', next: '1404/11/12', lot: 'MX-3301', vet: 'دکتر آریا نیک', site: 'زیرجلدی' },
      { name: 'RHD', date: '1403/11/12', next: '1404/11/12', lot: 'RH-3302', vet: 'دکتر آریا نیک', site: 'زیرجلدی' },
    ],
    labs: [], docs: [],
    notes: 'سوهان‌کاری دندان هر ۳ ماه یک‌بار پیگیری شود.',
  },
  {
    id: 'pt-7', ownerId: 'ow-5', code: '۷۲-۱۴۰۲', name: 'میلو', species: 'dog', breed: 'شیتزو',
    gender: 'male', birth: '1397/06/25', color: 'سفید و طلایی', behavior: 'آرام و اجتماعی',
    habitat: 'آپارتمان', diet: 'رژیم درمانی (رنال)', neutered: true, neuteredDate: '1398/10/05',
    blood: 'DEA 1.1 منفی', insurance: 'بیمه پت‌کر — طلایی',
    microchip: { code: '900182005543219', date: '1397/12/01', vendor: 'Trovan', location: 'بین دو کتف' },
    allergies: ['حساسیت به سولفانامید'], chronic: ['نارسایی مزمن کلیه — مرحله ۲'],
    surgeries: [
      { title: 'عقیم‌سازی', date: '1398/10/05', surgeon: 'دکتر آریا نیک', note: '—' },
      { title: 'جرم‌گیری و کشیدن ۳ دندان', date: '1404/04/18', surgeon: 'دکتر سمانه راد', note: 'بیهوشی کوتاه، ریکاوری خوب.' },
    ],
    status: 'critical', tone: 'e',
    weights: [
      { d: '1400/01/10', w: 7.8 }, { d: '1401/01/10', w: 7.6 }, { d: '1402/01/10', w: 7.2 },
      { d: '1403/01/10', w: 6.8 }, { d: '1403/09/10', w: 6.4 }, { d: '1404/04/18', w: 6.1 }, { d: '1404/05/06', w: 6.0 },
    ],
    sizes: [ { d: '1400/01/10', h: 26, l: 44 }, { d: '1402/01/10', h: 26, l: 44 }, { d: '1404/05/06', h: 26, l: 43 } ],
    vitals: { temp: 38.0, pulse: 110, resp: 25, bcs: '۳ از ۹' },
    visits: [
      { id:'v1', date: '1404/05/06', type: 'پیگیری', vet: 'دکتر آریا نیک', reason: 'کنترل ماهانه کلیه',
        exam: 'دهیدراسیون خفیف، لاغری مشهود.', dx: 'CKD مرحله ۲ — پایدار',
        rx: ['سرم زیرجلدی هفتگی', 'بنازپریل روزانه', 'ادامه رژیم رنال'], weight: 6.0, temp: 38.0, cost: 2600000, status: 'done' },
      { id:'v2', date: '1404/04/18', type: 'جراحی', vet: 'دکتر سمانه راد', reason: 'بیماری پیشرفته لثه',
        exam: 'ژنژیویت شدید، ۳ دندان لق.', dx: 'پریودنتیت مرحله ۳',
        rx: ['جرم‌گیری اولتراسونیک', 'کشیدن ۳ دندان', 'آنتی‌بیوتیک ۷ روز'], weight: 6.1, temp: 38.2, cost: 7800000, status: 'done' },
    ],
    vaccines: [
      { name: 'واکسن چهارگانه (DHPP)', date: '1403/10/22', next: '1404/10/22', lot: 'DH-4410', vet: 'دکتر آریا نیک', site: 'زیرجلدی شانه چپ' },
      { name: 'هاری', date: '1403/10/22', next: '1404/10/22', lot: 'RB-4411', vet: 'دکتر آریا نیک', site: 'زیرجلدی ران راست' },
    ],
    labs: [
      { name: 'کراتینین و BUN', date: '1404/05/06', result: 'Cre 2.4 — بالاتر از نرمال', status: 'danger' },
      { name: 'آنالیز ادرار', date: '1404/05/06', result: 'وزن مخصوص پایین', status: 'warn' },
    ],
    docs: [ { name: 'روند-آزمایش-کلیه.xlsx', size: '۹۰ کیلوبایت', date: '1404/05/06' } ],
    notes: 'بیمار مزمن — یادآور ماهانه سرم‌تراپی فعال است.',
  },
  {
    id: 'pt-8', ownerId: 'ow-6', code: '۷۲-۱۴۳۱', name: 'کوکو', species: 'rodent', breed: 'خوکچه هندی',
    gender: 'male', birth: '1403/11/02', color: 'سه‌رنگ', behavior: 'ترسو',
    habitat: 'قفس', diet: 'غذای خانگی', neutered: false, neuteredDate: '',
    blood: '—', insurance: '',
    microchip: { code: '', date: '', vendor: '', location: '' },
    allergies: [], chronic: [],
    surgeries: [],
    status: 'healthy', tone: 'a',
    weights: [ { d: '1404/01/05', w: 0.42 }, { d: '1404/05/01', w: 0.71 } ],
    sizes: [ { d: '1404/05/01', h: 10, l: 24 } ],
    vitals: { temp: 38.8, pulse: 250, resp: 60, bcs: '۵ از ۹' },
    visits: [
      { id:'v1', date: '1404/05/01', type: 'معاینه', vet: 'دکتر سمانه راد', reason: 'اولین معاینه پس از خرید',
        exam: 'پوست و پشم سالم، دندان‌ها مرتب.', dx: 'سالم',
        rx: ['ویتامین C روزانه'], weight: 0.71, temp: 38.8, cost: 550000, status: 'done' },
    ],
    vaccines: [],
    labs: [], docs: [],
    notes: '',
  },
];

/* ---------- derived helpers ---------- */
window.ownerById = (id) => window.OWNERS.find(o => o.id === id);
window.petById   = (id) => window.PETS.find(p => p.id === id);
window.petsOfOwner = (id) => window.PETS.filter(p => p.ownerId === id);
window.ownerName = (o) => o ? `${o.first} ${o.last}` : '—';

window.lastVisit = (pet) => (pet.visits && pet.visits.length) ? pet.visits[0] : null;
window.nextVaccine = (pet) => {
  if (!pet.vaccines || !pet.vaccines.length) return null;
  const sorted = pet.vaccines
    .filter(v => v.next)
    .map(v => ({ ...v, u: window.untilJalali(v.next) }))
    .sort((a, b) => a.u.days - b.u.days);
  const upcoming = sorted.find(v => v.u.days >= 0);
  return upcoming || sorted[sorted.length - 1] || null;
};
window.petHasVaccine = (pet) => !!(pet && pet.vaccines && pet.vaccines.length);
window.ownerHasVaccine = (ownerId) => window.petsOfOwner(ownerId).some(window.petHasVaccine);

window.currentWeight = (pet) => pet.weights && pet.weights.length ? pet.weights[pet.weights.length - 1].w : null;
window.weightDelta = (pet) => {
  if (!pet.weights || pet.weights.length < 2) return 0;
  const a = pet.weights[pet.weights.length - 2].w, b = pet.weights[pet.weights.length - 1].w;
  return +(b - a).toFixed(2);
};

window.STATUS_MAP = {
  healthy:  { label: 'سالم',            cls: 'pill-ok' },
  followup: { label: 'در حال پیگیری',   cls: 'pill-warn' },
  critical: { label: 'بیمار مزمن',      cls: 'pill-danger' },
};
window.GENDER_MAP = {
  male:    { label: 'نر',      icon: '♂' },
  female:  { label: 'ماده',    icon: '♀' },
  unknown: { label: 'نامشخص',  icon: '?' },
};
window.OWNER_GENDER = { male: 'آقا', female: 'خانم' };
