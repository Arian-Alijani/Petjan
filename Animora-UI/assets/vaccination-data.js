/* =========================================================
   Vetplus · Vaccination — mock data + domain helpers
   Design-only. All content is hardcoded Persian mock data.
   Self-contained: its own pinned "today" + tiny Jalali helpers so
   the page does not depend on cheques-data.js / patients-data.js.
   ========================================================= */
(function () {
  'use strict';

  /* ---------------- Persian digits + numbers ---------------- */
  const FA = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];
  const fa = (v) => String(v).replace(/\d/g, (d) => FA[+d]);
  window.vfa = fa;
  window.vfaNum = (n) => fa(String(n).replace(/\B(?=(\d{3})+(?!\d))/g, '٬'));

  /* ---------------- Minimal Jalali helpers ----------------
     Pinned "today" for a stable prototype (see CONVENTIONS §4). */
  const MONTHS = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
    'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'];
  const DAYS = ['شنبه', 'یک‌شنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنج‌شنبه', 'جمعه'];

  // day-count only needs to be internally consistent, not astronomically exact.
  const isLeap = (jy) => [1, 5, 9, 13, 17, 22, 26, 30].includes(jy % 33);
  const monthLen = (jy, jm) => jm <= 6 ? 31 : (jm <= 11 ? 30 : (isLeap(jy) ? 30 : 29));
  function toDay(s) {
    const [y, m, d] = String(s).split('/').map(Number);
    let days = 0;
    for (let yy = 1400; yy < y; yy++) days += isLeap(yy) ? 366 : 365;
    for (let mm = 1; mm < m; mm++) days += monthLen(y, mm);
    return days + d;
  }
  function fromDay(n) {
    let y = 1400;
    while (n > (isLeap(y) ? 366 : 365)) { n -= isLeap(y) ? 366 : 365; y++; }
    let m = 1;
    while (n > monthLen(y, m)) { n -= monthLen(y, m); m++; }
    return `${y}/${String(m).padStart(2, '0')}/${String(n).padStart(2, '0')}`;
  }
  const parse = (s) => { const [jy, jm, jd] = String(s).split('/').map(Number); return { jy, jm, jd }; };

  const TODAY_J = '1404/05/11';           // pinned "today"
  const TODAY_N = toDay(TODAY_J);

  window.VJ = {
    MONTHS, DAYS, TODAY_J,
    toDay, fromDay, parse,
    diff: (s) => toDay(s) - TODAY_N,               // + = future
    long: (s) => { const { jy, jm, jd } = parse(s); return `${fa(jd)} ${MONTHS[jm - 1]} ${fa(jy)}`; },
    medium: (s) => { const { jm, jd } = parse(s); return `${fa(jd)} ${MONTHS[jm - 1]}`; },
    weekday: (s) => { const { jy } = parse(s); return (toDay(s) + jy) % 7; }, // decorative
  };

  /* ---------------- Vaccine catalog ----------------
     One row per vaccine product the clinic stocks. */
  window.VAX_CATALOG = [
    { id: 'FVRCP', name: 'واکسن سه‌گانه گربه', short: 'FVRCP', species: 'cat', tone: 'a',
      protects: 'رینوتراکئیت، کالسی‌ویروس، پان‌لوکوپنی', boosterMonths: 12, stock: 24, min: 10 },
    { id: 'FeLV', name: 'لوسمی گربه', short: 'FeLV', species: 'cat', tone: 'b',
      protects: 'ویروس لوسمی گربه‌سانان', boosterMonths: 12, stock: 8, min: 8 },
    { id: 'DHPP', name: 'واکسن چهارگانه سگ', short: 'DHPP', species: 'dog', tone: 'c',
      protects: 'دیستمپر، هپاتیت، پاروو، پاراآنفلوانزا', boosterMonths: 12, stock: 31, min: 12 },
    { id: 'DHPPL', name: 'واکسن هشت‌گانه سگ', short: 'DHPPL', species: 'dog', tone: 'e',
      protects: 'چهارگانه + لپتوسپیروز', boosterMonths: 12, stock: 5, min: 10 },
    { id: 'RABIES', name: 'واکسن هاری', short: 'Rabies', species: 'all', tone: 'd',
      protects: 'ویروس هاری (زئونوز)', boosterMonths: 12, stock: 42, min: 15 },
    { id: 'KC', name: 'سرفه کِنِل (کِنل‌کاف)', short: 'KC', species: 'dog', tone: 'f',
      protects: 'بوردتلا، پاراآنفلوانزا', boosterMonths: 12, stock: 3, min: 8 },
    { id: 'POX', name: 'واکسن آبله پرندگان', short: 'Pox', species: 'bird', tone: 'b',
      protects: 'ویروس آبله طیور', boosterMonths: 12, stock: 14, min: 6 },
    { id: 'RHDV', name: 'واکسن خرگوش (RHDV)', short: 'RHDV', species: 'rabbit', tone: 'a',
      protects: 'بیماری هموراژیک خرگوش', boosterMonths: 12, stock: 9, min: 6 },
  ];
  window.vaxById = (id) => VAX_CATALOG.find((v) => v.id === id);

  /* ---------------- Species meta ---------------- */
  window.VAX_SPECIES = {
    cat: { label: 'گربه', icon: 'cat', tone: 'a' },
    dog: { label: 'سگ', icon: 'dog', tone: 'c' },
    bird: { label: 'پرنده', icon: 'bird', tone: 'e' },
    rabbit: { label: 'خرگوش', icon: 'rabbit', tone: 'f' },
    all: { label: 'همه گونه‌ها', icon: 'paw', tone: 'd' },
  };

  /* ---------------- SMS templates ---------------- */
  window.VAX_SMS_TEMPLATES = [
    { id: 't1', name: 'یادآوری نوبت واکسن', channel: 'reminder',
      body: 'سلام {owner} عزیز، نوبت واکسن «{vaccine}» {pet} در تاریخ {date} فرا رسیده است. لطفاً جهت هماهنگی با کلینیک وت‌پلاس تماس بگیرید. ☎ ۰۲۱-۸۸۰۰۱۲۳۴' },
    { id: 't2', name: 'اخطار سررسید گذشته', channel: 'overdue',
      body: '{owner} گرامی، واکسن «{vaccine}» {pet} از تاریخ {date} به تعویق افتاده است. برای حفظ سلامت حیوان لطفاً هرچه زودتر مراجعه فرمایید. کلینیک وت‌پلاس' },
    { id: 't3', name: 'تأیید انجام واکسن', channel: 'done',
      body: 'واکسن «{vaccine}» برای {pet} با موفقیت تزریق شد. نوبت بعدی: {next}. با آرزوی سلامتی — وت‌پلاس' },
    { id: 't4', name: 'یادآوری سه روز مانده', channel: 'reminder',
      body: 'یادآوری دوستانه 🐾 تنها ۳ روز تا نوبت واکسن {pet} ({vaccine}) در {date} باقی مانده است. وت‌پلاس' },
  ];

  /* ---------------- Vaccination records ----------------
     status: done | due (0..14 روز) | overdue (گذشته) | scheduled (آینده دور)
     smsState: none | sent | scheduled | failed
  */
  const R = [
    // pet / owner / mobile / vaccine / dose / lastDate / dueDate / vet / lot / site / tone
    ['پشمک', 'cat', 'نگار محمدی', '0912 445 8890', 'FVRCP', 'نوبت سالانه', '1404/04/28', '1404/05/12', 'دکتر آریا نیک', 'FV-8821', 'زیرجلدی شانه راست', 'a', 'due', 'scheduled'],
    ['لئو', 'dog', 'امیر رستگار', '0935 220 7781', 'DHPP', 'نوبت سالانه', '1403/05/09', '1404/05/09', 'دکتر آریا نیک', 'DH-3391', 'زیرجلدی شانه چپ', 'b', 'due', 'sent'],
    ['آبی', 'cat', 'سارا یوسفی', '0901 118 2244', 'RABIES', 'نوبت اول', '—', '1404/05/06', 'دکتر سمانه راد', '—', 'زیرجلدی ران چپ', 'c', 'overdue', 'sent'],
    ['برفی', 'dog', 'علی نوروزی', '0919 663 1120', 'DHPPL', 'یادآور دوم', '1403/11/02', '1404/05/10', 'دکتر آریا نیک', 'DH-2214', 'زیرجلدی شانه چپ', 'd', 'due', 'none'],
    ['ماکسی', 'dog', 'رضا کاظمی', '0937 900 4412', 'RABIES', 'نوبت سالانه', '1403/05/01', '1404/05/01', 'دکتر آریا نیک', 'RB-6612', 'زیرجلدی ران راست', 'e', 'overdue', 'failed'],
    ['نیمو', 'cat', 'مریم اکبری', '0912 771 3390', 'FeLV', 'نوبت اول', '—', '1404/05/18', 'دکتر سمانه راد', '—', 'زیرجلدی شانه راست', 'f', 'due', 'scheduled'],
    ['رکس', 'dog', 'حسین مرادی', '0938 442 7788', 'KC', 'نوبت سالانه', '1403/06/20', '1404/06/20', 'دکتر آریا نیک', 'KC-1180', 'داخل بینی', 'a', 'scheduled', 'none'],
    ['کوکو', 'bird', 'زهرا سلطانی', '0930 512 6677', 'POX', 'نوبت سالانه', '1403/07/14', '1404/07/14', 'دکتر سمانه راد', 'PX-2210', 'زیر بال', 'b', 'scheduled', 'none'],
    ['بلک', 'cat', 'محمد قاسمی', '0912 330 9911', 'FVRCP', 'یادآور اول', '1403/12/28', '1404/06/28', 'دکتر آریا نیک', 'FV-9014', 'زیرجلدی شانه راست', 'c', 'scheduled', 'none'],
    ['هاپو', 'dog', 'فاطمه رحیمی', '0935 108 5566', 'RABIES', 'نوبت سالانه', '1404/05/04', '1405/05/04', 'دکتر آریا نیک', 'RB-7781', 'زیرجلدی ران چپ', 'd', 'done', 'sent'],
    ['سیمبا', 'cat', 'کیانا فتحی', '0921 664 2210', 'FVRCP', 'نوبت اول', '1404/05/08', '1405/05/08', 'دکتر سمانه راد', 'FV-9120', 'زیرجلدی شانه راست', 'e', 'done', 'sent'],
    ['گلی', 'rabbit', 'سعید بهرامی', '0919 220 8834', 'RHDV', 'نوبت سالانه', '1403/09/03', '1404/05/03', 'دکتر آریا نیک', 'RH-4412', 'زیرجلدی گردن', 'f', 'overdue', 'none'],
    ['تامی', 'dog', 'الهام نادری', '0937 771 0098', 'DHPP', 'یادآور سوم', '1403/05/14', '1404/05/14', 'دکتر آریا نیک', 'DH-6621', 'زیرجلدی شانه چپ', 'a', 'due', 'none'],
    ['میلو', 'cat', 'پویا اسدی', '0912 889 4471', 'RABIES', 'نوبت اول', '1404/05/09', '1405/05/09', 'دکتر سمانه راد', 'RB-8890', 'زیرجلدی ران چپ', 'b', 'done', 'sent'],
    ['شارلوت', 'dog', 'نازنین شریفی', '0930 445 1120', 'DHPPL', 'نوبت سالانه', '1403/05/22', '1404/05/22', 'دکتر آریا نیک', 'DH-7734', 'زیرجلدی شانه چپ', 'c', 'due', 'scheduled'],
    ['اسکار', 'cat', 'بهنام کریمی', '0938 220 6677', 'FeLV', 'یادآور اول', '1403/12/17', '1404/06/17', 'دکتر سمانه راد', 'FL-3320', 'زیرجلدی شانه راست', 'd', 'scheduled', 'none'],
    ['جسی', 'dog', 'شیما موسوی', '0912 118 3344', 'RABIES', 'نوبت سالانه', '1403/04/30', '1404/04/30', 'دکتر آریا نیک', 'RB-2214', 'زیرجلدی ران راست', 'e', 'overdue', 'sent'],
    ['فندق', 'rodent', 'آرش جعفری', '0935 662 8890', 'RABIES', 'نوبت اول', '—', '1404/05/20', 'دکتر سمانه راد', '—', 'زیرجلدی گردن', 'f', 'due', 'none'],
    ['ونوس', 'cat', 'ترانه علوی', '0919 337 1122', 'FVRCP', 'یادآور دوم', '1403/05/11', '1404/05/11', 'دکتر آریا نیک', 'FV-5541', 'زیرجلدی شانه راست', 'a', 'due', 'scheduled'],
    ['بادی', 'dog', 'وحید صادقی', '0937 220 4455', 'KC', 'نوبت سالانه', '1403/08/08', '1404/08/08', 'دکتر آریا نیک', 'KC-2290', 'داخل بینی', 'b', 'scheduled', 'none'],
    ['پرنس', 'dog', 'یاسمن رفیعی', '0912 554 7788', 'DHPP', 'نوبت سالانه', '1403/05/02', '1404/05/02', 'دکتر آریا نیک', 'DH-8812', 'زیرجلدی شانه چپ', 'c', 'overdue', 'none'],
    ['لیلی', 'cat', 'سینا محمودی', '0930 118 9900', 'RABIES', 'نوبت سالانه', '1404/04/25', '1405/04/25', 'دکتر سمانه راد', 'RB-9931', 'زیرجلدی ران چپ', 'd', 'done', 'sent'],
    ['رامبو', 'dog', 'مهسا کاشانی', '0938 771 2233', 'DHPPL', 'یادآور اول', '1403/12/05', '1404/06/05', 'دکتر آریا نیک', 'DH-5590', 'زیرجلدی شانه چپ', 'e', 'scheduled', 'none'],
    ['چی‌چی', 'bird', 'کامران احمدی', '0912 663 8844', 'POX', 'نوبت اول', '—', '1404/05/13', 'دکتر سمانه راد', '—', 'زیر بال', 'f', 'due', 'none'],
  ];

  let uid = 1;
  window.VAX_RECORDS = R.map((r) => {
    const rec = {
      id: 'vx-' + (uid++),
      pet: r[0], species: r[1], owner: r[2], mobile: r[3],
      vaccineId: r[4], dose: r[5], lastDate: r[6], dueDate: r[7],
      vet: r[8], lot: r[9], site: r[10], tone: r[11],
      status: r[12], smsState: r[13],
    };
    // Keep the status badge consistent with the (pinned) due date so the UI
    // never shows "نزدیک" next to a past date. 'done' is left untouched.
    if (rec.status !== 'done') {
      const d = window.VJ.diff(rec.dueDate);      // + = future
      rec.status = d < 0 ? 'overdue' : (d <= 14 ? 'due' : 'scheduled');
    }
    return rec;
  });

  /* ---------------- Derived helpers ---------------- */
  window.vaxStatusMeta = {
    due: { label: 'نزدیک', tone: 'warn', icon: 'clock' },
    overdue: { label: 'عقب‌افتاده', tone: 'danger', icon: 'alert' },
    scheduled: { label: 'برنامه‌ریزی‌شده', tone: 'info', icon: 'calendar' },
    done: { label: 'انجام‌شده', tone: 'ok', icon: 'check' },
  };
  window.vaxSmsMeta = {
    none: { label: 'ارسال‌نشده', tone: 'mute', icon: 'msg' },
    sent: { label: 'ارسال‌شده', tone: 'ok', icon: 'check' },
    scheduled: { label: 'زمان‌بندی‌شده', tone: 'info', icon: 'clock' },
    failed: { label: 'ناموفق', tone: 'danger', icon: 'alert' },
  };

  /* KPI + chart aggregates derived from records */
  window.vaxCounts = () => {
    const c = { due: 0, overdue: 0, scheduled: 0, done: 0, total: VAX_RECORDS.length };
    VAX_RECORDS.forEach((r) => { c[r.status] = (c[r.status] || 0) + 1; });
    return c;
  };

})();
