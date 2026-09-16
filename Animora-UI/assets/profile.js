/* =========================================================
   Vetplus · Profile & Settings page
   Static UI prototype: settings live in memory only; every
   save shows a toast and closes nothing (design sample).
   ========================================================= */
(function () {
  'use strict';
  const I = window.Icons || {};
  const el = (id) => document.getElementById(id);
  const fa = (v) => (window.toFa ? window.toFa(v) : String(v));

  /* ---------------- reference data ---------------- */
  const USER = {
    name: 'دکتر آریا نیک', role: 'مدیر کلینیک', ini: 'آ',
    spec: 'داخلی و جراحی', phone: '۰۹۱۲۳۴۵۶۷۸۹', email: 'aria.nik@vetplus.ir',
    nvd: 'نظام veterinarians شمارهٔ ۱۲۴۵۸',
  };
  const TEAM = [
    { name: 'دکتر مینا همتی',   sub: 'واکسن و پیشگیری · شیفت صبح', ini: 'م', ac: '#1570cd', pill: 'pill-info',  label: 'پزشک حیوانات' },
    { name: 'دکتر سهیل کاویانی', sub: 'دندان‌پزشکی · شیفت عصر',    ini: 'س', ac: '#8f74d1', pill: 'pill-info',  label: 'پزشک حیوانات' },
    { name: 'نگار راد',          sub: 'آرایش و نگهداری · شیفت صبح', ini: 'ن', ac: '#e08a5c', pill: 'pill-mute',  label: 'کارشناس آرایش' },
    { name: 'سمیرا کاویانی',     sub: 'مسئول صندوق · تمام‌وقت',     ini: 'س', ac: '#0d9488', pill: 'pill-mute',  label: 'صندوق‌دار' },
  ];
  const JDOW = ['شنبه','یکشنبه','دوشنبه','سه‌شنبه','چهارشنبه','پنجشنبه','جمعه'];

  /* ---------------- in-memory settings state ---------------- */
  const ST = {
    tab: 'profile',
    theme: 'light',
    /* scheduling (migrated from the old appointments settings tab) */
    hoursFrom: '۰۷:۰۰', hoursTo: '۲۲:۰۰', days: [0,1,2,3,4,5],
    dur: '۳۰ دقیقه', gap: '۵ دقیقه', overlap: 'بدون هم‌پوشانی', defView: 'هفته',
    /* owner notifications */
    smsRemind: true, smsConfirm: true, waitlist: false,
    /* calendar display */
    showNow: true, showCancel: true, showHint: true,
    /* app notifications matrix */
    nMat: {
      cancel:   { sms: true,  inapp: true },
      overdue:  { sms: true,  inapp: true },
      cheque:   { sms: false, inapp: true },
      stock:     { sms: true,  inapp: false },
      vaccine:   { sms: true,  inapp: true },
    },
    /* profile form */
    uname: USER.name, uphone: USER.phone, uemail: USER.email,
  };

  /* ---------------- shared paint helpers ---------------- */
  const sel = (opts, cur) => opts.map((o) => `<option${o === cur ? ' selected' : ''}>${o}</option>`).join('');
  const sw = (label, sub, on, id) => `<label class="switch-row">
      <span class="switch"><input type="checkbox" id="${id}"${on ? ' checked' : ''}/><span class="sl"></span></span>
      <span class="tx">${label}<small>${sub}</small></span>
    </label>`;
  const card = (icon, title, sub, inner) => `<div class="ps-card">
      <h3><span class="ic">${icon}</span>${title}</h3>
      <div class="sub">${sub}</div>
      <div class="ps-rows">${inner}</div>
    </div>`;

  /* =========================================================
     tab chrome — settings nav (left column, sticky)
     ========================================================= */
  const TABS = [
    { id: 'profile',  label: 'پروفایل کاربری', icon: 'users' },
    { id: 'sched',   label: 'تنظیمات نوبت‌دهی', icon: 'calendar' },
    { id: 'alerts',  label: 'اعلان‌ها',          icon: 'bell' },
    { id: 'look',    label: 'ظاهر و تم',          icon: 'moon' },
    { id: 'sec',     label: 'امنیت و نشست‌ها',   icon: 'shield' },
  ];

  function navHtml() {
    return TABS.map((t) => `
      <button class="pb-seg-item${ST.tab === t.id ? ' active' : ''}" data-tab="${t.id}">
        <span class="nv-ic">${I[t.icon] || I.settings || ''}</span>
        <span>${t.label}</span>
      </button>`).join('');
  }

  function paintTab() {
    document.querySelectorAll('[data-tab]').forEach((b) => {
      const on = b.dataset.tab === ST.tab;
      b.classList.toggle('active', on);
    });
    el('pfTabProfile').hidden = ST.tab !== 'profile';
    el('pfTabSched').hidden = ST.tab !== 'sched';
    el('pfTabAlerts').hidden = ST.tab !== 'alerts';
    el('pfTabLook').hidden = ST.tab !== 'look';
    el('pfTabSec').hidden = ST.tab !== 'sec';
  }

  /* =========================================================
     tab: profile
     ========================================================= */
  function tabProfile() {
    const team = TEAM.map((m) => `
      <div class="pf-member" style="--ac:${m.ac}">
        <span class="av">${m.ini}</span>
        <span class="pf-mt"><b>${m.name}</b><span>${m.sub}</span></span>
        <span class="pill ${m.pill}">${m.label}</span>
      </div>`).join('');
    return `
      <div class="ps-grid">
        ${card(I.users, 'اطلاعات شخصی', 'نام و راه‌های تماسی که در سربرگ‌ها و پیامک‌ها استفاده می‌شود.', `
          <div class="ps-field"><label>نام و نام خانوادگی</label><input class="ps-input" id="pfName" value="${ST.uname}"/></div>
          <div class="ps-field"><label>شمارهٔ تماس</label><input class="ps-input" id="pfPhone" value="${ST.uphone}"/></div>
          <div class="ps-field"><label>ایمیل کاری</label><input class="ps-input" id="pfEmail" value="${ST.uemail}"/></div>
          <div class="ps-field"><label>شمارهٔ نظام veterinarians</label><input class="ps-input" value="${USER.nvd}" disabled/></div>
        `)}
        ${card(I.users, 'تیم کلینیک', 'اعضای فعال و شیفت آن‌ها.', `
          <div class="pf-team">${team}</div>
        `)}
      </div>`;
  }

  /* =========================================================
     tab: scheduling (migrated from the appointments page)
     ========================================================= */
  function tabSched() {
    const dayChips = JDOW.map((n, i) =>
      `<button class="chip${ST.days.includes(i) ? ' on' : ''}" data-day="${i}">${n}</button>`).join('');
    return `
      <div class="ps-grid">
        ${card(I.calendar, 'ساعات و روزهای کاری', 'بازه‌ای که تقویم برای نوبت‌دهی نمایش می‌دهد.', `
          <div class="ps-field"><label>ساعت شروع</label><select class="pb-select" id="stFrom">${sel(['۰۶:۰۰','۰۷:۰۰','۰۸:۰۰','۰۹:۰۰'], ST.hoursFrom)}</select></div>
          <div class="ps-field"><label>ساعت پایان</label><select class="pb-select" id="stTo">${sel(['۱۸:۰۰','۲۰:۰۰','۲۲:۰۰','۲۳:۰۰'], ST.hoursTo)}</select></div>
          <div class="ps-field col"><label>روزهای کاری هفته</label><div class="sc-fgroup">${dayChips}</div></div>
        `)}
        ${card(I.calendar, 'قوانین نوبت‌دهی', 'مدت و تراکم پیش‌فرض نوبت‌های جدید.', `
          <div class="ps-field"><label>مدت پیش‌فرض هر نوبت</label><select class="pb-select" id="stDur">${sel(['۱۵ دقیقه','۲۰ دقیقه','۳۰ دقیقه','۴۵ دقیقه','۶۰ دقیقه'], ST.dur)}</select></div>
          <div class="ps-field"><label>فاصلهٔ استراحت بین نوبت‌ها</label><select class="pb-select" id="stGap">${sel(['بدون فاصله','۵ دقیقه','۱۰ دقیقه','۱۵ دقیقه'], ST.gap)}</select></div>
          <div class="ps-field"><label>حد مجاز هم‌پوشانی هر پزشک</label><select class="pb-select" id="stOv">${sel(['بدون هم‌پوشانی','۲ نوبت','۳ نوبت'], ST.overlap)}</select></div>
          <div class="ps-field"><label>نمای پیش‌فرض تقویم</label><select class="pb-select" id="stView">${sel(['روز','هفته','ماه'], ST.defView)}</select></div>
        `)}
        ${card(I.msg || I.bell, 'اطلاع‌رسانی به مالک', 'پیام‌های خودکار مرتبط با نوبت.', `
          ${sw('یادآوری پیامکی', '۲۴ ساعت قبل از نوبت ارسال می‌شود', ST.smsRemind, 'stRemind')}
          ${sw('پیامک تأیید فوری', 'بلافاصله پس از ثبت یا تأیید نوبت', ST.smsConfirm, 'stConfirm')}
          ${sw('اطلاع لغو به لیست انتظار', 'پس از لغو، نوبت خالی به متقاضیان اعلان می‌شود', ST.waitlist, 'stWaitlist')}
        `)}
        ${card(I.calendar, 'نمایش تقویم', 'چیدمان و عناصر دیداری جدول زمانی.', `
          ${sw('نمایش خط «اکنون»', 'نشانگر لحظهٔ فعلی روی نمای روز و هفته', ST.showNow, 'stNow')}
          ${sw('نمایش نوبت‌های لغوشده', 'با حالت کم‌رنگ در تقویم باقی می‌مانند', ST.showCancel, 'stCancel')}
          ${sw('راهنمای «نوبت جدید»', 'پیش‌نمایش هنگام حرکت نشانگر روی خانه‌های خالی', ST.showHint, 'stHint')}
        `)}
      </div>`;
  }

  /* =========================================================
     tab: alerts — notification matrix
     ========================================================= */
  const NMAT = [
    { id: 'cancel',  label: 'لغو نوبت',            sub: 'وقتی مالک یا پذیرش نوبتی را لغو می‌کند' },
    { id: 'overdue', label: 'چک سررسیدگذشته',     sub: 'چک‌های وصول‌نشده پس از سررسید' },
    { id: 'cheque',  label: 'چک در حال تسویه',    sub: 'روزشمارش تا سررسید چک‌های دریافتی' },
    { id: 'stock',  label: 'موجودی انبار بحرانی',  sub: 'کالا زیر نقطهٔ سفارش‌گذاری' },
    { id: 'vaccine', label: 'یادآوری واکسن',        sub: 'برنامهٔ پیشگیری مراجعین' },
  ];
  function tabAlerts() {
    const rows = NMAT.map((m) => `
      <div class="pf-mrow">
        <span class="pf-mn"><b>${m.label}</b><span>${m.sub}</span></span>
        <span class="pf-ch">
          <label><input type="checkbox" data-nm="${m.id}" data-ch="sms"${ST.nMat[m.id].sms ? ' checked' : ''}/> پیامک</label>
          <label><input type="checkbox" data-nm="${m.id}" data-ch="inapp"${ST.nMat[m.id].inapp ? ' checked' : ''}/> درون‌برنامه</label>
        </span>
      </div>`).join('');
    return `
      <div class="ps-grid">
        ${card(I.bell, 'ماتریس اعلان‌ها', 'برای هر رویداد، کانال‌های ارسال را روشن یا خاموش کنید.', `<div class="pf-matrix">${rows}</div>`)}
        ${card(I.clock, 'ساعات سکوت', 'در این بازه پیامک‌های غیرضرور ارسال نمی‌شوند.', `
          <div class="ps-field"><label>از ساعت</label><select class="pb-select">${sel(['۲۲:۰۰','۲۳:۰۰','۰۰:۰۰'], '۲۲:۰۰')}</select></div>
          <div class="ps-field"><label>تا ساعت</label><select class="pb-select">${sel(['۰۶:۰۰','۰۷:۰۰','۰۸:۰۰'], '۰۷:۰۰')}</select></div>
          ${sw('فقط اعلان‌های بحرانی', 'لغو نوبت امروز و موارد فوری همیشه ارسال می‌شوند', true, 'stQuiet')}
        `)}
      </div>`;
  }

  /* =========================================================
     tab: look — theme & display
     ========================================================= */
  function tabLook() {
    return `
      <div class="ps-grid">
        ${card(I.moon, 'حالت نمایش', 'تم روشن یا تیره برای همهٔ صفحات.', `
          <div class="pf-theme">
            <button class="pf-topt light${ST.theme === 'light' ? ' on' : ''}" data-theme="light">
              <span class="pf-tg"></span><b>روشن</b><span>پیش‌فرض کلینیک</span><span class="tick">${I.check}</span>
            </button>
            <button class="pf-topt dark${ST.theme === 'dark' ? ' on' : ''}" data-theme="dark">
              <span class="pf-tg"></span><b>تیره</b><span>مناسب محیط کم‌نور</span><span class="tick">${I.check}</span>
            </button>
          </div>
        `)}
        ${card(I.gridIc || I.rows, 'چیدمان فشرده', 'فاصلهٔ کمتر در جدول‌ها برای نمایش بیشتر ردیف‌ها.', `
          ${sw('حالت فشردهٔ جدول‌ها', 'ارتفاع ردیف‌ها ۲۰٪ کم می‌شود', false, 'stDense')}
          ${sw('نمایش راهنمای میان‌برها', 'کیبوردهای میان‌بر زیر نوار ابزار', true, 'stKbd')}
          ${sw('انیمیشن مقدماتی ورود', 'جلوهٔ ورود صفحات و پنل‌ها', true, 'stAnim')}
        `)}
      </div>`;
  }

  /* =========================================================
     tab: security & sessions
     ========================================================= */
  const SESS = [
    { name: 'کروم روی ویندوز — این دستگاه', t: 'فعال · تهران', cur: true },
    { name: 'اپلیکیشن پذیرش — تبلت صندوق',  t: 'آخرین فعالیت: امروز ۰۹:۴۲' },
    { name: 'سافاری روی مک — منزل',         t: 'آخرین فعالیت: دیروز ۲۱:۱۰' },
  ];
  function tabSec() {
    const sess = SESS.map((s) => `
      <div class="pf-sitem">
        <span class="ic">${I.shield || ''}</span>
        <span class="pf-st"><b>${s.name}${s.cur ? ' <span class="pill pill-ok">این نشست</span>' : ''}</b><span>${s.t}</span></span>
        ${s.cur ? '' : `<button class="btn btn-outline sm" data-endsess="1">خروج</button>`}
      </div>`).join('');
    return `
      <div class="ps-grid">
        ${card(I.shield, 'امنیت حساب', 'گذرواژه و ورود دومرحله‌ای.', `
          <div class="ps-field"><label>گذرواژهٔ فعلی</label><input class="ps-input" type="password" value="••••••••"/></div>
          <div class="ps-field"><label>گذرواژهٔ جدید</label><input class="ps-input" type="password" placeholder="حداقل ۸ نویسه"/></div>
          ${sw('ورود دومرحله‌ای', 'کد یک‌بارمصرف پیامکی هنگام ورود', true, 'st2fa')}
        `)}
        ${card(I.users, 'نشست‌های فعال', 'دستگاه‌هایی که با حساب شما وارد شده‌اند.', `<div class="pf-sess">${sess}</div>`)}
      </div>`;
  }

  /* =========================================================
     hero + KPI band
     ========================================================= */
  function hero() {
    const kpis = [
      { ic: I.calendar, t: '۱۲۴', s: 'نوبت این ماه', tone: '' },
      { ic: I.check,   t: '۹۸٪', s: 'نرخ پذیرش',    tone: 't-warn' },
      { ic: I.paw,     t: '۳۱۲', s: 'پروندهٔ فعال',  tone: 't-info' },
      { ic: I.bell,    t: '۵',   s: 'اعلان نخوانده', tone: 't-violet' },
    ].map((k) => `<div class="pf-kpi ${k.tone}">
        <span class="ic">${k.ic}</span>
        <span class="pf-kv"><b>${fa(k.t)}</b><span>${k.s}</span></span>
      </div>`).join('');
    return `
      <div class="pf-hero">
        <div class="pf-avatar">${USER.ini}</div>
        <div class="pf-id">
          <h2>${USER.name}<span class="pill pill-info">${USER.role}</span></h2>
          <div class="pf-role">${USER.spec} · وت‌پلاس شعبهٔ مرکزی</div>
          <div class="pf-quick">
            <span class="pf-q">${I.phone || I.msg || ''} ${fa(USER.phone)}</span>
            <span class="pf-q">${I.msg || ''} ${USER.email}</span>
            <span class="pf-q">${I.clock || ''} آخرین ورود: امروز ${fa('۰۸:۱۵')}</span>
          </div>
        </div>
      </div>
      <div class="pf-kpis">${kpis}</div>`;
  }

  /* =========================================================
     root paint
     ========================================================= */
  function paint() {
    el('pf-root').innerHTML = `
      ${hero()}
      <div class="pagebar pf-pagebar">
        <div class="pagebar-row">
          <div class="pb-seg lg pf-nav" role="tablist">${navHtml()}</div>
        </div>
      </div>
      <div id="pfTabProfile" role="tabpanel">${tabProfile()}</div>
      <div id="pfTabSched"   role="tabpanel" hidden>${tabSched()}</div>
      <div id="pfTabAlerts"  role="tabpanel" hidden>${tabAlerts()}</div>
      <div id="pfTabLook"    role="tabpanel" hidden>${tabLook()}</div>
      <div id="pfTabSec"     role="tabpanel" hidden>${tabSec()}</div>`;
    paintTab();
  }

  /* ---------------- interactions ---------------- */
  document.addEventListener('click', (e) => {
    const tb = e.target.closest('[data-tab]');
    if (tb) { ST.tab = tb.dataset.tab; paintTab(); return; }
    const day = e.target.closest('[data-day]');
    if (day) {
      const d = +day.dataset.day;
      const i = ST.days.indexOf(d);
      i === -1 ? ST.days.push(d) : ST.days.splice(i, 1);
      day.classList.toggle('on');
      return;
    }
    const th = e.target.closest('[data-theme]');
    if (th) { ST.theme = th.dataset.theme; paint(); return; }
    if (e.target.closest('[data-endsess]')) {
      window.PF.toast('نشست انتخاب‌شده بسته شد (نمونه طراحی).', 'shield');
      return;
    }
  });

  document.addEventListener('input', (e) => {
    const t = e.target;
    const nm = t.closest('[data-nm]');
    if (nm) { ST.nMat[nm.dataset.nm][nm.dataset.ch === 'sms' ? 'sms' : 'inapp'] = t.checked; return; }
    if (t.id === 'pfName') ST.uname = t.value;
    else if (t.id === 'pfPhone') ST.uphone = t.value;
    else if (t.id === 'pfEmail') ST.uemail = t.value;
  });

  document.addEventListener('change', (e) => {
    const t = e.target, id = t.id;
    const map = { stFrom: 'hoursFrom', stTo: 'hoursTo', stDur: 'dur', stGap: 'gap', stOv: 'overlap', stView: 'defView' };
    if (map[id]) ST[map[id]] = t.value;
    const swMap = {
      stRemind: 'smsRemind', stConfirm: 'smsConfirm', stWaitlist: 'waitlist',
      stNow: 'showNow', stCancel: 'showCancel', stHint: 'showHint',
      stQuiet: 'quiet', stDense: 'dense', stKbd: 'kbd', stAnim: 'anim', st2fa: 'tfa',
    };
    if (swMap[id]) ST[swMap[id]] = t.checked;
  });

  /* ---------------- public API ---------------- */
  window.PF = {
    ST,
    paint,
    save() {
      /* L6: design-only — close nothing, just toast */
      window.PF.toast('تنظیمات ذخیره شد (نمونه طراحی).', 'check');
    },
    discard() {
      window.PF.toast('تغییرات بازگردانده شد (نمونه طراحی).', 'refresh');
      paint();
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

  paint();
})();
