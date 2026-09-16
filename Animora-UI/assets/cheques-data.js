/* =========================================================
   وت‌پلاس · مدیریت چک — داده‌ها و ابزارها (design-only mock)
   ========================================================= */

/* ---------------------------------------------------------
   1) تقویم جلالی (الگوریتم jalaali)
   --------------------------------------------------------- */
(function () {
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

  window.J = {
    MONTHS, DAYS, DAYS_S,
    isLeap: (jy) => jalCal(jy, false).leap === 0,
    monthLen: (jy, jm) => jm <= 6 ? 31 : (jm <= 11 ? 30 : (jalCal(jy, false).leap === 0 ? 30 : 29)),
    /** '1405/05/18' → عدد روز مطلق */
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
      const n = new Date();
      const o = d2j(g2d(n.getFullYear(), n.getMonth() + 1, n.getDate()));
      return `${o.jy}/${String(o.jm).padStart(2, '0')}/${String(o.jd).padStart(2, '0')}`;
    },
    /** 0 = شنبه */
    weekday(s) {
      const { jy, jm, jd } = this.parse(s);
      const g = d2g(j2d(jy, jm, jd));
      const dt = new Date(g.gy, g.gm - 1, g.gd);
      return (dt.getDay() + 1) % 7;
    },
    /** اختلاف روز نسبت به امروز (مثبت = آینده) */
    diff(s, from) {
      return this.toDay(s) - this.toDay(from || this.today());
    },
    long(s) {
      const { jy, jm, jd } = this.parse(s);
      return `${window.toFa(jd)} ${MONTHS[jm - 1]} ${window.toFa(jy)}`;
    },
    medium(s) {
      const { jm, jd } = this.parse(s);
      return `${window.toFa(jd)} ${MONTHS[jm - 1]}`;
    },
  };
})();

/* ---------------------------------------------------------
   2) قالب‌بندی اعداد و مبالغ
   --------------------------------------------------------- */
window.fa = (v) => String(v).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]);
window.money = (n) => window.fa(Number(n).toLocaleString('en-US'));
/** ۱۲٫۵ م / ۱٫۲ میلیارد */
window.moneyShort = function (n) {
  if (n >= 1e9) return window.fa((n / 1e9).toFixed(n % 1e9 === 0 ? 0 : 1)) + ' میلیارد';
  if (n >= 1e6) return window.fa((n / 1e6).toFixed(n % 1e6 === 0 ? 0 : 1)) + ' م';
  if (n >= 1e3) return window.fa(Math.round(n / 1e3)) + ' هزار';
  return window.fa(n);
};

/* عدد به حروف فارسی */
window.numToWords = (function () {
  const yekan = ['', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه'];
  const dahgan = ['', '', 'بیست', 'سی', 'چهل', 'پنجاه', 'شصت', 'هفتاد', 'هشتاد', 'نود'];
  const dahyek = ['ده', 'یازده', 'دوازده', 'سیزده', 'چهارده', 'پانزده', 'شانزده', 'هفده', 'هجده', 'نوزده'];
  const sadgan = ['', 'صد', 'دویست', 'سیصد', 'چهارصد', 'پانصد', 'ششصد', 'هفتصد', 'هشتصد', 'نهصد'];
  const scale = ['', ' هزار', ' میلیون', ' میلیارد', ' هزار میلیارد'];
  function three(n) {
    const out = [];
    const s = Math.floor(n / 100), r = n % 100;
    if (s) out.push(sadgan[s]);
    if (r >= 10 && r <= 19) out.push(dahyek[r - 10]);
    else {
      const d = Math.floor(r / 10), y = r % 10;
      if (d) out.push(dahgan[d]);
      if (y) out.push(yekan[y]);
    }
    return out.join(' و ');
  }
  return function (num) {
    num = Math.floor(Math.abs(Number(num) || 0));
    if (num === 0) return 'صفر';
    const parts = [];
    let i = 0;
    while (num > 0) {
      const c = num % 1000;
      if (c) parts.unshift(three(c) + scale[i]);
      num = Math.floor(num / 1000);
      i++;
    }
    return parts.join(' و ');
  };
})();

/* ---------------------------------------------------------
   3) بانک‌ها
   --------------------------------------------------------- */
window.CH_BANKS = {
  melli:      { name: 'بانک ملی ایران', short: 'ملی',      c: '#12508f', c2: '#2a79c4' },
  mellat:     { name: 'بانک ملت',       short: 'ملت',      c: '#b5123c', c2: '#e0426b' },
  saderat:    { name: 'بانک صادرات',    short: 'صادرات',   c: '#173e8e', c2: '#3a68c9' },
  tejarat:    { name: 'بانک تجارت',     short: 'تجارت',    c: '#146a9c', c2: '#2f9bd4' },
  parsian:    { name: 'بانک پارسیان',   short: 'پارسیان',  c: '#7b1733', c2: '#ad3556' },
  pasargad:   { name: 'بانک پاسارگاد',  short: 'پاسارگاد', c: '#9c7418', c2: '#d6a63a' },
  saman:      { name: 'بانک سامان',     short: 'سامان',    c: '#0d6ba8', c2: '#2f9ada' },
  refah:      { name: 'بانک رفاه',      short: 'رفاه',     c: '#16704a', c2: '#2fa073' },
  keshavarzi: { name: 'بانک کشاورزی',   short: 'کشاورزی',  c: '#1c6b33', c2: '#3d9a58' },
  ayandeh:    { name: 'بانک آینده',     short: 'آینده',    c: '#553a92', c2: '#8163c4' },
  sepah:      { name: 'بانک سپه',       short: 'سپه',      c: '#1c5f8c', c2: '#3f8dbd' },
  shahr:      { name: 'بانک شهر',       short: 'شهر',      c: '#a02b2b', c2: '#cf5a52' },
};

/* ---------------------------------------------------------
   4) وضعیت‌ها و جهت
   --------------------------------------------------------- */
window.CH_STATUS = {
  inhand:  { label: 'در جریان',       tone: 'info',   icon: 'wallet', desc: 'نزد ما / صادر شده' },
  atbank:  { label: 'واگذار به بانک', tone: 'warn',   icon: 'bank',   desc: 'در جریان وصول' },
  cleared: { label: 'وصول شده',       tone: 'ok',     icon: 'check',  desc: 'پاس شده' },
  bounced: { label: 'برگشتی',         tone: 'danger', icon: 'alert',  desc: 'عدم پرداخت' },
  spent:   { label: 'خرج شده',        tone: 'lilac',  icon: 'share',  desc: 'واگذار به شخص ثالث' },
  void:    { label: 'باطل شده',       tone: 'mute',   icon: 'x',      desc: 'ابطال' },
};
window.CH_DIR = {
  in:  { label: 'دریافتی', short: 'در', tone: 'ok',   icon: 'arrowDownLeft' },
  out: { label: 'پرداختی', short: 'پر', tone: 'peach', icon: 'arrowUpRight' },
};

/* ---------------------------------------------------------
   5) داده نمونه چک‌ها
   --------------------------------------------------------- */
window.CHEQUES = [
  { id:'CH-1051', dir:'in',  no:'842517', sayad:'0184-2517-9930-4471', bank:'mellat',     branch:'شعبه ونک',            party:'مهسا رضایی',            role:'مالک بیمار',   phone:'09121148820', amount:  8500000, issue:'1405/04/28', due:'1405/05/12', status:'inhand',  subject:'تسویه فاکتور جراحی ارتوپدی «آنو»', ref:'INV-14058217', account:'0203-8845-11', note:'' },
  { id:'CH-1049', dir:'in',  no:'119640', sayad:'0311-9640-2287-6650', bank:'melli',      branch:'شعبه شریعتی',         party:'کامران دلاور',          role:'مالک بیمار',   phone:'09355512207', amount: 12400000, issue:'1405/04/15', due:'1405/05/14', status:'atbank',  subject:'بستری و مراقبت ویژه «رکسی»', ref:'INV-14058190', account:'0106-5521-04', note:'به شعبه مرکزی واگذار شد.' },
  { id:'CH-1047', dir:'out', no:'557102', sayad:'0455-7102-1178-9032', bank:'pasargad',   branch:'شعبه میرداماد',       party:'پخش دارویی سپهر',       role:'تأمین‌کننده',  phone:'02188774411', amount: 46800000, issue:'1405/04/10', due:'1405/05/16', status:'inhand',  subject:'فاکتور خرید دارو و مکمل — PO-3391', ref:'PO-3391',      account:'2101-9902-33', note:'' },
  { id:'CH-1045', dir:'in',  no:'308874', sayad:'0230-8874-5561-2298', bank:'saman',      branch:'شعبه جردن',           party:'سمیرا کیانی',           role:'مالک بیمار',   phone:'09127765430', amount:  5250000, issue:'1405/04/30', due:'1405/05/11', status:'atbank',  subject:'پکیج واکسیناسیون سالانه', ref:'INV-14058244', account:'8330-1174-02', note:'' },
  { id:'CH-1044', dir:'in',  no:'664219', sayad:'0466-4219-7745-1102', bank:'parsian',    branch:'شعبه سعادت‌آباد',      party:'آرش موحد',              role:'مالک بیمار',   phone:'09122204471', amount:  3200000, issue:'1405/04/02', due:'1405/05/06', status:'bounced', subject:'ویزیت و آزمایش خون', ref:'INV-14058102', account:'0620-4471-19', note:'کسر موجودی — پیگیری تلفنی انجام شد.' },
  { id:'CH-1042', dir:'out', no:'901337', sayad:'0590-1337-8890-3312', bank:'tejarat',    branch:'شعبه آرژانتین',       party:'تجهیزات دامپزشکی رادین', role:'تأمین‌کننده', phone:'02166553312', amount: 92000000, issue:'1405/03/24', due:'1405/05/25', status:'inhand',  subject:'خرید دستگاه سونوگرافی پرتابل', ref:'PO-3352',      account:'2101-9902-33', note:'یک فقره از سه فقره چک قرارداد.' },
  { id:'CH-1040', dir:'in',  no:'473911', sayad:'0147-3911-2204-8876', bank:'saderat',    branch:'شعبه گاندی',          party:'نگین شفیعی',            role:'مالک بیمار',   phone:'09193348821', amount:  6750000, issue:'1405/04/18', due:'1405/05/19', status:'inhand',  subject:'عمل دندان و جرم‌گیری', ref:'INV-14058173', account:'0106-5521-04', note:'' },
  { id:'CH-1038', dir:'in',  no:'220458', sayad:'0122-0458-6612-7745', bank:'keshavarzi', branch:'شعبه پاسداران',       party:'دامداری بهدشت',         role:'مشتری سازمانی', phone:'02177412200', amount: 34500000, issue:'1405/03/12', due:'1405/05/02', status:'cleared', subject:'قرارداد پایش سلامت گله — تیر', ref:'CNT-118',      account:'0106-5521-04', note:'' },
  { id:'CH-1036', dir:'out', no:'778120', sayad:'0677-8120-3345-9901', bank:'refah',      branch:'شعبه ولیعصر',         party:'اجاره مطب — آقای صدری', role:'موجر',        phone:'09121002233', amount: 28000000, issue:'1405/04/01', due:'1405/05/01', status:'cleared', subject:'اجاره ماهانه مرداد', ref:'EXP-2205',     account:'2101-9902-33', note:'' },
  { id:'CH-1034', dir:'in',  no:'392066', sayad:'0339-2066-1178-4432', bank:'ayandeh',    branch:'شعبه ولنجک',          party:'پانیذ حیدری',           role:'مالک بیمار',   phone:'09128873310', amount:  4100000, issue:'1405/04/22', due:'1405/05/22', status:'inhand',  subject:'گروم و بستری کوتاه‌مدت', ref:'INV-14058221', account:'8330-1174-02', note:'' },
  { id:'CH-1032', dir:'in',  no:'510473', sayad:'0451-0473-9982-1120', bank:'melli',      branch:'شعبه نیاوران',        party:'رضا فراهانی',           role:'مالک بیمار',   phone:'09121190045', amount:  9800000, issue:'1405/04/08', due:'1405/05/28', status:'spent',   subject:'جراحی چشم «میلو»', ref:'INV-14058144', account:'0106-5521-04', note:'بابت تسویه با پخش سپهر خرج شد.' },
  { id:'CH-1030', dir:'out', no:'846591', sayad:'0784-6591-2230-7761', bank:'mellat',     branch:'شعبه ونک',            party:'آزمایشگاه پاتوبیولوژی مهر', role:'تأمین‌کننده', phone:'02188221177', amount: 15600000, issue:'1405/04/05', due:'1405/05/13', status:'atbank',  subject:'تسویه آزمایش‌های ارجاعی تیر', ref:'PO-3378',      account:'2101-9902-33', note:'' },
  { id:'CH-1028', dir:'in',  no:'163925', sayad:'0216-3925-4471-8830', bank:'sepah',      branch:'شعبه تجریش',          party:'مریم اسدی',             role:'مالک بیمار',   phone:'09354471203', amount:  2850000, issue:'1405/04/26', due:'1405/05/09', status:'cleared', subject:'ویزیت تخصصی و رادیولوژی', ref:'INV-14058231', account:'8330-1174-02', note:'' },
  { id:'CH-1026', dir:'in',  no:'729384', sayad:'0672-9384-1102-5563', bank:'shahr',      branch:'شعبه فرمانیه',        party:'پت‌شاپ آرمان',           role:'مشتری سازمانی', phone:'02122334455', amount: 18300000, issue:'1405/04/12', due:'1405/06/03', status:'inhand',  subject:'فروش عمده غذای درمانی', ref:'INV-14058158', account:'0106-5521-04', note:'' },
  { id:'CH-1024', dir:'out', no:'384710', sayad:'0538-4710-6690-2214', bank:'saman',      branch:'شعبه جردن',           party:'شرکت پخش نوین‌دارو',     role:'تأمین‌کننده',  phone:'02177889900', amount: 23900000, issue:'1405/03/30', due:'1405/05/18', status:'inhand',  subject:'سفارش دوره‌ای واکسن', ref:'PO-3360',      account:'2101-9902-33', note:'' },
  { id:'CH-1022', dir:'in',  no:'957301', sayad:'0795-7301-8823-4470', bank:'tejarat',    branch:'شعبه ظفر',            party:'حامد نوروزی',           role:'مالک بیمار',   phone:'09122998811', amount:  7400000, issue:'1405/03/28', due:'1405/04/30', status:'bounced', subject:'جراحی اورژانسی «بلک»', ref:'INV-14058088', account:'0106-5521-04', note:'مغایرت امضا — چک عودت داده شد.' },
  { id:'CH-1020', dir:'in',  no:'112907', sayad:'0111-2907-5540-6612', bank:'pasargad',   branch:'شعبه الهیه',          party:'شیوا مقدم',             role:'مالک بیمار',   phone:'09121457788', amount: 11200000, issue:'1405/04/20', due:'1405/05/15', status:'inhand',  subject:'درمان طولانی‌مدت نارسایی کلیه', ref:'INV-14058205', account:'8330-1174-02', note:'' },
  { id:'CH-1018', dir:'out', no:'640281', sayad:'0464-0281-1179-3345', bank:'melli',      branch:'شعبه شریعتی',         party:'خدمات نظافت پاک‌آور',    role:'پیمانکار',    phone:'02144556677', amount:  6400000, issue:'1405/04/03', due:'1405/05/08', status:'cleared', subject:'قرارداد نظافت فصلی', ref:'EXP-2198',     account:'2101-9902-33', note:'' },
  { id:'CH-1016', dir:'in',  no:'285014', sayad:'0328-5014-7761-2290', bank:'refah',      branch:'شعبه قیطریه',         party:'الهام ساداتی',          role:'مالک بیمار',   phone:'09127741122', amount:  5900000, issue:'1405/04/24', due:'1405/05/20', status:'atbank',  subject:'بستری سه‌روزه و سرم‌درمانی', ref:'INV-14058226', account:'0106-5521-04', note:'' },
  { id:'CH-1014', dir:'in',  no:'701163', sayad:'0570-1163-2284-9910', bank:'saderat',    branch:'شعبه میرداماد',       party:'باشگاه سگ‌های کاری آرتا', role:'مشتری سازمانی', phone:'02126445566', amount: 41000000, issue:'1405/03/18', due:'1405/06/12', status:'inhand',  subject:'قرارداد سالانه سلامت — قسط دوم', ref:'CNT-124',      account:'0106-5521-04', note:'' },
  { id:'CH-1012', dir:'out', no:'459738', sayad:'0345-9738-6613-1102', bank:'parsian',    branch:'شعبه سعادت‌آباد',      party:'لیزینگ تجهیزات آریا',    role:'تأمین‌کننده',  phone:'02188990011', amount: 19750000, issue:'1405/03/20', due:'1405/05/24', status:'inhand',  subject:'قسط ماهانه دستگاه بیهوشی', ref:'PO-3301',      account:'2101-9902-33', note:'' },
  { id:'CH-1010', dir:'in',  no:'836420', sayad:'0683-6420-9930-7745', bank:'ayandeh',    branch:'شعبه زعفرانیه',       party:'بهنام کریمی',           role:'مالک بیمار',   phone:'09123310099', amount:  3600000, issue:'1405/04/06', due:'1405/04/28', status:'cleared', subject:'واکسن هاری و میکروچیپ', ref:'INV-14058121', account:'8330-1174-02', note:'' },
  { id:'CH-1008', dir:'in',  no:'572901', sayad:'0257-2901-4478-6630', bank:'mellat',     branch:'شعبه گیشا',           party:'ندا پورعلی',            role:'مالک بیمار',   phone:'09195540021', amount:  8900000, issue:'1405/04/29', due:'1405/05/26', status:'inhand',  subject:'شیمی‌درمانی دوره‌ای «سیمبا»', ref:'INV-14058248', account:'0106-5521-04', note:'' },
  { id:'CH-1006', dir:'out', no:'318665', sayad:'0431-8665-2201-9934', bank:'sepah',      branch:'شعبه میدان کاج',      party:'چاپ و تبلیغات رسانه‌نو',  role:'پیمانکار',    phone:'02133445566', amount:  4300000, issue:'1405/04/14', due:'1405/05/21', status:'inhand',  subject:'کمپین تبلیغاتی پاییز', ref:'EXP-2211',     account:'2101-9902-33', note:'' },
  { id:'CH-1004', dir:'in',  no:'904472', sayad:'0790-4472-1163-5520', bank:'keshavarzi', branch:'شعبه شهرک غرب',       party:'فرهاد بیات',            role:'مالک بیمار',   phone:'09121876655', amount:  2400000, issue:'1405/04/11', due:'1405/05/03', status:'void',    subject:'ویزیت — لغو شد', ref:'INV-14058151', account:'0106-5521-04', note:'به درخواست مالک ابطال و نقدی پرداخت شد.' },
  { id:'CH-1002', dir:'in',  no:'661158', sayad:'0166-1158-3392-4470', bank:'saman',      branch:'شعبه ونک',            party:'کلینیک همکار پارس',     role:'مشتری سازمانی', phone:'02188006611', amount: 27500000, issue:'1405/04/16', due:'1405/06/20', status:'inhand',  subject:'ارجاع بیماران جراحی — تسویه فصلی', ref:'CNT-131',      account:'0106-5521-04', note:'' },
  { id:'CH-1000', dir:'out', no:'147203', sayad:'0214-7203-8830-1176', bank:'shahr',      branch:'شعبه اندرزگو',        party:'بیمه تکمیلی دام‌یار',    role:'تأمین‌کننده',  phone:'02191001100', amount: 12800000, issue:'1405/04/09', due:'1405/05/10', status:'atbank',  subject:'حق بیمه فصلی پرسنل', ref:'EXP-2203',     account:'2101-9902-33', note:'' },
  { id:'CH-0998', dir:'in',  no:'523864', sayad:'0352-3864-9917-2203', bank:'melli',      branch:'شعبه پونک',           party:'سعید ملکی',             role:'مالک بیمار',   phone:'09364471290', amount:  4950000, issue:'1405/04/19', due:'1405/05/17', status:'inhand',  subject:'ارتوپدی و فیزیوتراپی', ref:'INV-14058186', account:'8330-1174-02', note:'' },
];

/* تاریخچه چرخه عمر — بر اساس وضعیت ساخته می‌شود */
window.chequeTimeline = function (c) {
  const t = [{ title: 'ثبت چک در سامانه', date: c.issue, by: 'دکتر آریا نیک', tone: 'brand', icon: 'plus' }];
  if (c.dir === 'in') t.push({ title: 'دریافت از ' + c.party, date: c.issue, by: 'پریسا صداری', tone: 'info', icon: 'wallet' });
  else t.push({ title: 'صدور و تحویل به ' + c.party, date: c.issue, by: 'پریسا صداری', tone: 'peach', icon: 'edit' });

  const mid = window.J.fromDay(window.J.toDay(c.due) - 3);
  if (['atbank', 'cleared'].includes(c.status)) t.push({ title: 'واگذاری به بانک', date: mid, by: 'واحد مالی', tone: 'warn', icon: 'bank' });
  if (c.status === 'cleared') t.push({ title: 'وصول شد', date: c.due, by: 'سامانه بانکی', tone: 'ok', icon: 'check' });
  if (c.status === 'bounced') t.push({ title: 'برگشت خورد', date: c.due, by: 'سامانه بانکی', tone: 'danger', icon: 'alert' });
  if (c.status === 'spent') t.push({ title: 'خرج (واگذاری به شخص ثالث)', date: mid, by: 'دکتر آریا نیک', tone: 'lilac', icon: 'share' });
  if (c.status === 'void') t.push({ title: 'ابطال چک', date: mid, by: 'دکتر آریا نیک', tone: 'mute', icon: 'x' });
  return t;
};
