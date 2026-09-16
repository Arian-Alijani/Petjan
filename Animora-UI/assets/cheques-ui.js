/* =========================================================
   وت‌پلاس · مدیریت چک — کشوی جزئیات، مودال ثبت و تاریخ‌گزین
   ========================================================= */

/* ---------- reveal on scroll ---------- */
if (!window.mountReveal) {
  window.mountReveal = function (sel = '.rv') {
    const els = document.querySelectorAll(sel + ':not(.in)');
    if (!('IntersectionObserver' in window)) { els.forEach(e => e.classList.add('in')); return; }
    const io = new IntersectionObserver((ents) => {
      ents.forEach((e, i) => {
        if (e.isIntersecting) {
          setTimeout(() => e.target.classList.add('in'), Math.min(i * 40, 200));
          io.unobserve(e.target);
        }
      });
    }, { rootMargin: '0px 0px -30px 0px', threshold: .04 });
    els.forEach(e => io.observe(e));
  };
}

/* =========================================================
   کشوی جزئیات چک
   ========================================================= */
function ensureDrawer() {
  if (document.getElementById('chDrawer')) return;
  const el = document.createElement('div');
  el.className = 'ch-drawer-root';
  el.id = 'chDrawer';
  el.innerHTML = `
    <div class="ch-drawer-bd" onclick="closeCheque()"></div>
    <aside class="ch-drawer" role="dialog" aria-modal="true" aria-label="جزئیات چک">
      <div class="ch-dh" id="chDh"></div>
      <div class="ch-db" id="chDb"></div>
      <div class="ch-df" id="chDf"></div>
    </aside>`;
  document.body.appendChild(el);
}

window.openCheque = function (id, keepScroll) {
  const c = window.CHEQUES.find(x => x.id === id);
  if (!c) return;
  ensureDrawer();
  const root = document.getElementById('chDrawer');
  const b = window.CH_BANKS[c.bank];
  const dir = window.CH_DIR[c.dir];
  const st = window.CH_STATUS[c.status];
  const today = window.J.today();
  const d = window.J.diff(c.due, today);
  const total = Math.max(1, window.J.toDay(c.due) - window.J.toDay(c.issue));
  const passed = Math.min(total, Math.max(0, window.J.toDay(today) - window.J.toDay(c.issue)));
  const pct = Math.round(passed / total * 100);
  const meterTone = d < 0 ? 'danger' : d <= 7 ? 'warn' : '';
  const scroll = keepScroll ? document.getElementById('chDb').scrollTop : 0;

  document.getElementById('chDh').innerHTML = `
    <span class="bank-mark" style="background:linear-gradient(135deg,${b.c},${b.c2});width:38px;height:38px;border-radius:12px;font-size:10px">${b.short.slice(0, 3)}</span>
    <div class="titles">
      <div class="e">${dir.label} · ${c.id}</div>
      <h3>چک شماره ${fa(c.no)}</h3>
    </div>
    <button class="icon-btn close" onclick="closeCheque()" title="بستن">${Icons.x}</button>`;

  document.getElementById('chDb').innerHTML = `
    <div class="big-leaf" style="--b1:${b.c};--b2:${b.c2}">
      <div class="bl-top">
        <span class="bm">${b.short.slice(0, 3)}</span>
        <span><span class="t">${b.name}</span><br/><span class="s">${c.branch}</span></span>
        <span class="tag">${dir.label}</span>
      </div>
      <div class="bl-amt">${money(c.amount)}<span class="u">تومان</span></div>
      <div class="bl-words">${numToWords(c.amount)} تومان</div>
      <div class="bl-grid">
        <div><div class="k">${c.dir === 'in' ? 'دریافت از' : 'در وجه'}</div><div class="v">${c.party}</div></div>
        <div><div class="k">تاریخ سررسید</div><div class="v">${fa(c.due)}</div></div>
        <div><div class="k">تاریخ صدور</div><div class="v">${fa(c.issue)}</div></div>
        <div><div class="k">حساب کلینیک</div><div class="v">${fa(c.account)}</div></div>
      </div>
      <div class="bl-micr">${fa(c.sayad.replace(/-/g, ' '))}</div>
    </div>

    <div class="dsec">
      <div class="h">${Icons.hourglass} وضعیت و سررسید <span class="sp"></span> ${statusPillUI(c.status)}</div>
      <div class="b">
        <div class="due-meter ${meterTone}">
          <div class="lbl">
            <span>${fa(c.issue)}</span>
            <span>${d < 0 ? `${fa(Math.abs(d))} روز از سررسید گذشته` : d === 0 ? 'سررسید امروز' : `${fa(d)} روز تا سررسید`}</span>
            <span>${fa(c.due)}</span>
          </div>
          <div class="tr"><span style="width:${pct}%"></span></div>
        </div>
        <div style="font-size:11.5px;color:var(--muted);margin-top:10px">${st.desc} — آخرین بروزرسانی توسط واحد مالی</div>
      </div>
    </div>

    <div class="dsec">
      <div class="h">${Icons.file} اطلاعات چک</div>
      <div class="b">
        <div class="kv">
          <span class="k">شناسه صیادی</span><span class="v mono">${fa(c.sayad)}
            <button class="ch-mini" style="width:22px;height:22px;display:inline-grid;vertical-align:-5px"
              onclick="navigator.clipboard&&navigator.clipboard.writeText('${c.sayad}');toast('شناسه صیادی کپی شد','copy')">${Icons.copy}</button></span>
          <span class="k">بانک / شعبه</span><span class="v">${b.name} — ${c.branch}</span>
          <span class="k">طرف حساب</span><span class="v">${c.party} <span style="color:var(--muted);font-weight:500">(${c.role})</span></span>
          <span class="k">شماره تماس</span><span class="v mono">${fa(c.phone)}</span>
          <span class="k">بابت</span><span class="v">${c.subject}</span>
          <span class="k">سند مرتبط</span><span class="v mono">${c.ref}</span>
        </div>
      </div>
    </div>

    <div class="dsec">
      <div class="h">${Icons.timeline} چرخه عمر چک</div>
      <div class="b">
        <div class="dtl">
          ${window.chequeTimeline(c).map(t => `
            <div class="dtl-i">
              <span class="dot" style="--c:${toneColor(t.tone)}"></span>
              <div class="t">${t.title}</div>
              <div class="m"><span class="d">${fa(t.date)}</span><span>·</span><span>${t.by}</span></div>
            </div>`).join('')}
        </div>
      </div>
    </div>

    <div class="dsec">
      <div class="h">${Icons.image} تصاویر و پیوست‌ها</div>
      <div class="b">
        <div class="att-row">
          <div class="att" onclick="toast('پیش‌نمایش تصویر روی چک (نمونه طراحی)','image')">
            ${Icons.image}<div class="t">روی چک</div><div class="s">IMG_${fa(c.no)}.jpg</div>
          </div>
          <div class="att" onclick="toast('برای افزودن پیوست، فایل را رها کنید (نمونه طراحی)','plus')">
            ${Icons.plus}<div class="t">افزودن پیوست</div><div class="s">تصویر یا PDF</div>
          </div>
        </div>
      </div>
    </div>

    <div class="dsec">
      <div class="h">${Icons.edit} یادداشت داخلی</div>
      <div class="b">
        <textarea class="f-ta" id="chNote" placeholder="یادداشتی برای همکاران بنویسید…">${c.note || ''}</textarea>
        <div style="display:flex;gap:8px;margin-top:8px">
          <button class="btn btn-outline" style="height:34px;font-size:12px" onclick="toast('یادداشت ذخیره شد (نمونه طراحی)','check')">${Icons.check} ذخیره یادداشت</button>
          <button class="btn btn-ghost" style="height:34px;font-size:12px" onclick="toast('تماس با ${c.party} برقرار شد (نمونه طراحی)','phone')">${Icons.phone} تماس</button>
        </div>
      </div>
    </div>`;

  const acts = [];
  if (['inhand'].includes(c.status)) acts.push(`<button class="btn btn-outline" onclick="setStatus('${c.id}','atbank')">${Icons.bank} واگذاری به بانک</button>`);
  if (['inhand', 'atbank'].includes(c.status)) {
    acts.push(`<button class="btn btn-primary" onclick="setStatus('${c.id}','cleared')">${Icons.check} ثبت وصول</button>`);
    acts.push(`<button class="btn btn-outline" style="color:var(--danger);border-color:var(--danger-bg)" onclick="setStatus('${c.id}','bounced')">${Icons.alert} برگشت</button>`);
  }
  if (c.status === 'bounced') acts.push(`<button class="btn btn-primary" onclick="setStatus('${c.id}','inhand')">${Icons.refresh} بازگشت به جریان</button>`);
  if (!acts.length) acts.push(`<button class="btn btn-outline" onclick="toast('رسید چک برای چاپ آماده شد','printer')">${Icons.printer} چاپ رسید</button>`);
  acts.push(`<button class="btn btn-ghost" onclick="openChequeModal(window.CHEQUES.find(x=>x.id==='${c.id}'))">${Icons.edit}</button>`);
  document.getElementById('chDf').innerHTML = acts.join('');

  root.classList.add('open');
  document.body.style.overflow = 'hidden';
  if (keepScroll) document.getElementById('chDb').scrollTop = scroll;
};

window.closeCheque = function () {
  const r = document.getElementById('chDrawer');
  if (!r || !r.classList.contains('open')) return;
  r.classList.remove('open');
  document.body.style.overflow = '';
};

function statusPillUI(st) {
  const s = window.CH_STATUS[st];
  return `<span class="st ${s.tone}">${Icons[s.icon]}${s.label}</span>`;
}
function toneColor(t) {
  return { ok: '#027a48', info: '#1570cd', warn: '#b54708', danger: '#d92d20', lilac: '#7b5cc4', mute: '#9aa8a5', peach: '#e08a5c', brand: '#0d9488' }[t] || '#0d9488';
}

/* =========================================================
   مودال ثبت / ویرایش چک
   ========================================================= */
let CH_FORM = null;

function ensureModal() {
  if (document.getElementById('chModal')) return;
  const el = document.createElement('div');
  el.className = 'ch-modal-root';
  el.id = 'chModal';
  el.innerHTML = `
    <div class="ch-modal-bd" onclick="closeChequeModal()"></div>
    <div class="ch-modal" role="dialog" aria-modal="true" aria-label="ثبت چک">
      <div class="ch-mh">
        <div class="ib">${Icons.cheque}</div>
        <div>
          <div class="e" id="chMEyebrow">خزانه‌داری</div>
          <h3 id="chMTitle">ثبت چک جدید</h3>
        </div>
        <button class="icon-btn close" onclick="closeChequeModal()" title="بستن">${Icons.x}</button>
      </div>
      <div class="ch-mb" id="chMb"></div>
      <div class="ch-mf">
        <button class="btn btn-ghost" onclick="closeChequeModal()">انصراف</button>
        <span class="sp"></span>
        <button class="btn btn-outline" id="chSaveNew">${Icons.plus} ثبت و جدید</button>
        <button class="btn btn-primary" id="chSave">${Icons.check} <span id="chSaveTxt">ثبت چک</span></button>
      </div>
    </div>`;
  document.body.appendChild(el);
}

window.openChequeModal = function (edit) {
  ensureModal();
  const t = window.J.today();
  CH_FORM = edit ? { ...edit, _edit: true } : {
    dir: 'in', bank: 'melli', no: '', sayad: '', party: '', role: 'مالک بیمار',
    phone: '', amount: '', issue: t, due: window.J.fromDay(window.J.toDay(t) + 30),
    branch: '', subject: '', ref: '', note: '', account: '0106-5521-04',
  };
  document.getElementById('chMTitle').textContent = edit ? `ویرایش چک ${fa(edit.no)}` : 'ثبت چک جدید';
  document.getElementById('chMEyebrow').textContent = edit ? 'اصلاح اطلاعات' : 'خزانه‌داری · فرم ثبت';
  document.getElementById('chSaveTxt').textContent = edit ? 'ذخیره تغییرات' : 'ثبت چک';
  document.getElementById('chSaveNew').style.display = edit ? 'none' : '';
  paintForm();
  const r = document.getElementById('chModal');
  r.classList.add('open');
  document.body.style.overflow = 'hidden';
  setTimeout(() => document.getElementById('fParty')?.focus(), 320);

  document.getElementById('chSave').onclick = () => saveCheque(false);
  document.getElementById('chSaveNew').onclick = () => saveCheque(true);
};

window.closeChequeModal = function () {
  const r = document.getElementById('chModal');
  if (!r || !r.classList.contains('open')) return;
  r.classList.remove('open');
  document.body.style.overflow = '';
};

function paintForm() {
  const f = CH_FORM;
  document.getElementById('chMb').innerHTML = `
    <div class="dir-pick">
      ${Object.entries(window.CH_DIR).map(([k, d]) => `
        <div class="dir-opt ${f.dir === k ? 'on' : ''}" data-dir="${k}" style="--oc:${k === 'in' ? '#027a48' : '#d98032'}">
          <span class="ic" style="background:${k === 'in' ? 'var(--ok-bg)' : 'var(--peach-bg)'};color:${k === 'in' ? '#027a48' : '#c06a29'}">${Icons[d.icon]}</span>
          <span>
            <span class="t">چک ${d.label}</span>
            <span class="s">${k === 'in' ? 'چکی که از مشتری گرفته‌ایم' : 'چکی که ما صادر کرده‌ایم'}</span>
          </span>
          <span class="chk">${Icons.check}</span>
        </div>`).join('')}
    </div>

    <div style="font-size:11.5px;font-weight:800;color:var(--ink-2);margin:0 0 8px">بانک صادرکننده <span style="color:var(--danger)">*</span></div>
    <div class="bank-pick" style="margin-bottom:16px">
      ${Object.entries(window.CH_BANKS).map(([k, b]) => `
        <div class="bank-opt ${f.bank === k ? 'on' : ''}" data-bank="${k}" style="--bc:${b.c}" title="${b.name}">
          <div class="m" style="background:linear-gradient(135deg,${b.c},${b.c2})">${b.short.slice(0, 3)}</div>
          <div class="n">${b.short}</div>
        </div>`).join('')}
    </div>

    <div class="frm">
      <div>
        <label class="f-lbl" for="fParty">طرف حساب <span class="req">*</span></label>
        <input class="f-in" id="fParty" value="${f.party}" placeholder="نام شخص یا شرکت" autocomplete="off"/>
      </div>
      <div>
        <label class="f-lbl" for="fRole">نوع طرف حساب</label>
        <select class="f-sel" id="fRole">
          ${['مالک بیمار', 'تأمین‌کننده', 'مشتری سازمانی', 'پیمانکار', 'موجر', 'سایر']
            .map(r => `<option ${f.role === r ? 'selected' : ''}>${r}</option>`).join('')}
        </select>
      </div>

      <div>
        <label class="f-lbl" for="fNo">شماره چک <span class="req">*</span></label>
        <input class="f-in" id="fNo" value="${f.no}" placeholder="۶ رقم روی برگ چک" inputmode="numeric"/>
      </div>
      <div>
        <label class="f-lbl" for="fSayad">شناسه صیادی (۱۶ رقم)</label>
        <input class="f-in" id="fSayad" value="${f.sayad}" placeholder="۰۰۰۰-۰۰۰۰-۰۰۰۰-۰۰۰۰" inputmode="numeric"/>
      </div>

      <div class="f-full">
        <label class="f-lbl" for="fAmount">مبلغ چک <span class="req">*</span></label>
        <div class="f-wrap has-suffix">
          <input class="f-in" id="fAmount" value="${f.amount ? Number(f.amount).toLocaleString('en-US') : ''}" placeholder="۰" inputmode="numeric"/>
          <span class="suffix">تومان</span>
        </div>
        <div class="f-hint words" id="fWords">${f.amount ? numToWords(f.amount) + ' تومان' : 'مبلغ را وارد کنید تا به حروف نمایش داده شود'}</div>
      </div>

      <div>
        <label class="f-lbl">تاریخ صدور</label>
        ${dpHtml('fIssue', f.issue)}
      </div>
      <div>
        <label class="f-lbl">تاریخ سررسید <span class="req">*</span></label>
        ${dpHtml('fDue', f.due)}
        <div class="quick-dates">
          <button data-quick="7">۱ هفته</button>
          <button data-quick="30">۱ ماه</button>
          <button data-quick="60">۲ ماه</button>
          <button data-quick="90">۳ ماه</button>
        </div>
      </div>

      <div>
        <label class="f-lbl" for="fBranch">شعبه</label>
        <input class="f-in" id="fBranch" value="${f.branch}" placeholder="مثلاً شعبه ونک"/>
      </div>
      <div>
        <label class="f-lbl" for="fRef">سند مرتبط</label>
        <input class="f-in" id="fRef" value="${f.ref}" placeholder="INV-… / PO-…"/>
      </div>

      <div class="f-full">
        <label class="f-lbl" for="fSubject">بابت</label>
        <input class="f-in" id="fSubject" value="${f.subject}" placeholder="شرح کوتاه بابت چک"/>
      </div>
      <div class="f-full">
        <label class="f-lbl" for="fNoteM">یادداشت</label>
        <textarea class="f-ta" id="fNoteM" placeholder="توضیحات تکمیلی…">${f.note}</textarea>
      </div>
    </div>`;

  /* رویدادها */
  document.querySelectorAll('[data-dir]').forEach(el => el.addEventListener('click', () => {
    CH_FORM.dir = el.dataset.dir; readForm(); paintForm();
  }));
  document.querySelectorAll('[data-bank]').forEach(el => el.addEventListener('click', () => {
    CH_FORM.bank = el.dataset.bank; readForm(); paintForm();
  }));
  const amt = document.getElementById('fAmount');
  amt.addEventListener('input', () => {
    const raw = amt.value.replace(/[^\d۰-۹]/g, '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
    amt.value = raw ? Number(raw).toLocaleString('en-US') : '';
    document.getElementById('fWords').textContent = raw
      ? numToWords(raw) + ' تومان' : 'مبلغ را وارد کنید تا به حروف نمایش داده شود';
  });
  const sy = document.getElementById('fSayad');
  sy.addEventListener('input', () => {
    const raw = sy.value.replace(/[^\d۰-۹]/g, '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).slice(0, 16);
    sy.value = (raw.match(/.{1,4}/g) || []).join('-');
  });
  document.querySelectorAll('[data-quick]').forEach(b => b.addEventListener('click', () => {
    readForm();
    CH_FORM.due = window.J.fromDay(window.J.toDay(CH_FORM.issue) + (+b.dataset.quick));
    paintForm();
  }));
  bindDp('fIssue'); bindDp('fDue');
}

function readForm() {
  const g = (id) => document.getElementById(id)?.value || '';
  CH_FORM.party = g('fParty'); CH_FORM.role = g('fRole');
  CH_FORM.no = g('fNo').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
  CH_FORM.sayad = g('fSayad');
  CH_FORM.amount = Number(g('fAmount').replace(/[^\d]/g, '')) || 0;
  CH_FORM.issue = document.getElementById('fIssue')?.dataset.val || CH_FORM.issue;
  CH_FORM.due = document.getElementById('fDue')?.dataset.val || CH_FORM.due;
  CH_FORM.branch = g('fBranch'); CH_FORM.ref = g('fRef');
  CH_FORM.subject = g('fSubject'); CH_FORM.note = g('fNoteM');
}

function saveCheque(again) {
  readForm();
  const bad = [];
  if (!CH_FORM.party.trim()) bad.push('fParty');
  if (!CH_FORM.no.trim()) bad.push('fNo');
  if (!CH_FORM.amount) bad.push('fAmount');
  if (bad.length) {
    bad.forEach(id => {
      const el = document.getElementById(id);
      el.style.borderColor = 'var(--danger)';
      el.style.boxShadow = '0 0 0 4px rgba(208,70,58,.10)';
      el.addEventListener('input', () => { el.style.borderColor = ''; el.style.boxShadow = ''; }, { once: true });
    });
    document.getElementById(bad[0]).focus();
    toast('لطفاً فیلدهای الزامی را کامل کنید', 'alert');
    return;
  }

  if (CH_FORM._edit) {
    const c = window.CHEQUES.find(x => x.id === CH_FORM.id);
    Object.assign(c, { ...CH_FORM });
    delete c._edit;
    toast(`تغییرات چک ${fa(c.no)} ذخیره شد`, 'check');
  } else {
    const id = 'CH-' + (1100 + window.CHEQUES.length);
    window.CHEQUES.unshift({
      id, dir: CH_FORM.dir, no: CH_FORM.no,
      sayad: CH_FORM.sayad || '0000-0000-0000-0000',
      bank: CH_FORM.bank, branch: CH_FORM.branch || 'شعبه مرکزی',
      party: CH_FORM.party, role: CH_FORM.role, phone: CH_FORM.phone || '09120000000',
      amount: CH_FORM.amount, issue: CH_FORM.issue, due: CH_FORM.due,
      status: 'inhand', subject: CH_FORM.subject || '—', ref: CH_FORM.ref || '—',
      account: CH_FORM.account, note: CH_FORM.note,
    });
    toast(`چک ${fa(CH_FORM.no)} به مبلغ ${moneyShort(CH_FORM.amount)} تومان ثبت شد`, 'check');
  }

  renderKpis(); renderAll();
  if (again) { openChequeModal(); return; }
  closeChequeModal();
}

/* =========================================================
   تاریخ‌گزین شمسی کوچک
   ========================================================= */
function dpHtml(id, val) {
  return `<div class="dp" id="${id}Wrap">
    <input class="f-in" id="${id}" data-val="${val}" value="${fa(val)}" readonly style="cursor:pointer"/>
    <div class="dp-pop" id="${id}Pop"></div>
  </div>`;
}

function bindDp(id) {
  const wrap = document.getElementById(id + 'Wrap');
  const inp = document.getElementById(id);
  const pop = document.getElementById(id + 'Pop');
  let view = window.J.parse(inp.dataset.val);

  function paint() {
    const { jy, jm } = view;
    const len = window.J.monthLen(jy, jm);
    const start = window.J.weekday(`${jy}/${String(jm).padStart(2, '0')}/01`);
    const sel = inp.dataset.val;
    const today = window.J.today();
    let cells = '';
    for (let i = 0; i < start; i++) cells += '<button class="off" disabled></button>';
    for (let d = 1; d <= len; d++) {
      const ds = `${jy}/${String(jm).padStart(2, '0')}/${String(d).padStart(2, '0')}`;
      cells += `<button data-d="${ds}" class="${ds === sel ? 'on' : ''} ${ds === today ? 'today' : ''}">${fa(d)}</button>`;
    }
    pop.innerHTML = `
      <div class="dp-head">
        <button data-m="-1">${Icons.chevronRight}</button>
        <span class="t">${window.J.MONTHS[jm - 1]} ${fa(jy)}</span>
        <button data-m="1">${Icons.chevronLeft}</button>
      </div>
      <div class="dp-grid">
        ${window.J.DAYS_S.map(d => `<span class="dw">${d}</span>`).join('')}
        ${cells}
      </div>`;
    pop.querySelectorAll('[data-m]').forEach(b => b.addEventListener('click', (e) => {
      e.stopPropagation();
      view.jm += +b.dataset.m;
      if (view.jm > 12) { view.jm = 1; view.jy++; }
      if (view.jm < 1) { view.jm = 12; view.jy--; }
      paint();
    }));
    pop.querySelectorAll('[data-d]').forEach(b => b.addEventListener('click', (e) => {
      e.stopPropagation();
      inp.dataset.val = b.dataset.d;
      inp.value = fa(b.dataset.d);
      wrap.classList.remove('open');
      if (id === 'fIssue') { readForm(); }
    }));
  }

  inp.addEventListener('click', (e) => {
    e.stopPropagation();
    document.querySelectorAll('.dp.open').forEach(d => d !== wrap && d.classList.remove('open'));
    view = window.J.parse(inp.dataset.val);
    paint();
    wrap.classList.toggle('open');
  });
  pop.addEventListener('click', e => e.stopPropagation());
  document.addEventListener('click', () => wrap.classList.remove('open'));
}
