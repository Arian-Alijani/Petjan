/* =========================================================
   Vetplus · Appointments — detail drawer + booking panel
   ========================================================= */
(function () {
  'use strict';
  const S = window.SCH;
  const I = window.Icons || {};
  const el = (id) => document.getElementById(id);
  const fa = S.fa, hhmm = S.hhmm, bdi = S.bdi, rangeTxt = S.rangeTxt;
  const dkey = (s) => new Date(s.replace(/-/g, '/'));

  /* =============== detail drawer =============== */
  function detailHtml(a) {
    const t = S.typeOf(a.type), d = S.docOf(a.doc), st = S.STATUS[a.status];
    const day = dkey(a.date);
    const isToday = S.key(day) === S.key(S.TODAY);
    return `
      <div class="dtl-top ${t.cls}">
        <div class="dtl-row1">
          <span class="dtl-type"><span class="dot"></span>${t.label}</span>
          <span class="pill ${st.pill}">${st.label}</span>
          <button class="icon-btn" onclick="SCH.closeDetail()" title="بستن">${I.x}</button>
        </div>
        <div class="dtl-pet">
          <div class="avatar-pet ${a.av}">${I[a.ic] || ''}</div>
          <div>
            <h3>${a.pet}</h3>
            <div class="sub">${a.kind} · مالک: ${a.owner}</div>
          </div>
        </div>
      </div>
      <div class="dtl-body">
        <div class="dtl-when">
          ${I.clock}
          <div>
            <div class="big">${rangeTxt(a.start, a.start + a.dur)}</div>
            <div class="sm">${S.JDOW[S.wIdx(day)]} ${S.jFull(day)}${isToday ? ' · امروز' : ''}</div>
          </div>
          <span class="dur">${fa(a.dur)} دقیقه</span>
        </div>

        <div class="dtl-sec">
          <h4>اطلاعات نوبت</h4>
          <div class="dtl-kv">
            <span class="k">${I.users}پزشک</span><span class="v">${d.name} · ${d.spec}</span>
            <span class="k">${S.icon('phone')}تماس مالک</span><span class="v">${bdi(a.phone)}</span>
            <span class="k">${S.icon('tag')}کد نوبت</span><span class="v">${a.code}</span>
            <span class="k">${I.wallet}هزینه تخمینی</span><span class="v">${fa((a.dur * 12000).toLocaleString('en-US'))} تومان</span>
          </div>
        </div>

        <div class="dtl-sec">
          <h4>یادداشت پذیرش</h4>
          <div class="dtl-note">${a.note || 'یادداشتی ثبت نشده است.'}</div>
        </div>

        <div class="dtl-sec">
          <h4>سوابق و رویدادها</h4>
          <div class="dtl-tl">
            <div class="it"><b>نوبت ثبت شد</b> <span>· ${a.created}</span></div>
            <div class="it"><b>پیامک تأیید ارسال شد</b> <span>· به ${a.owner}</span></div>
            <div class="it ${a.status === 'done' ? '' : 'mute'}"><b>${a.status === 'done' ? 'پذیرش انجام شد' : 'در انتظار پذیرش'}</b>
              <span>· ${a.status === 'done' ? 'ثبت در پرونده' : 'پس از حضور مالک'}</span></div>
          </div>
        </div>
      </div>
      <div class="dtl-foot">
        ${a.status === 'done'
          ? `<button class="btn btn-outline" onclick="SCH.act('visit','${a.id}')">${I.clipboard} مشاهده پرونده ویزیت</button>`
          : `<button class="btn btn-primary" onclick="SCH.act('checkin','${a.id}')">${I.check} پذیرش بیمار</button>`}
        <button class="btn btn-outline" onclick="SCH.act('edit','${a.id}')">${I.edit} ویرایش</button>
        <button class="btn btn-outline btn-icon" onclick="SCH.act('sms','${a.id}')" title="ارسال یادآور پیامکی">${S.icon('sms')}</button>
        <button class="btn btn-outline btn-icon" onclick="SCH.act('cancel','${a.id}')" title="لغو نوبت" style="color:var(--danger)">${I.trash}</button>
      </div>`;
  }

  S.openDetail = function (id) {
    const a = S.get(id);
    if (!a) return;
    const p = el('dtl');
    p.innerHTML = detailHtml(a);
    p.classList.add('open');
    el('dtlBackdrop').classList.add('show');
  };
  S.closeDetail = function () {
    el('dtl').classList.remove('open');
    el('dtlBackdrop').classList.remove('show');
    S.S.selected = null;
    S.render();
  };
  S.act = function (what, id) {
    const a = S.get(id);
    if (what === 'checkin') { a.status = 'done'; S.toast('بیمار «' + a.pet + '» پذیرش شد.'); S.openDetail(id); S.render(); }
    else if (what === 'cancel') { a.status = 'cancel'; S.toast('نوبت لغو شد و به مالک اطلاع داده شد.', 'x'); S.openDetail(id); S.render(); }
    else if (what === 'sms') S.toast('یادآور پیامکی برای ' + a.owner + ' ارسال شد.', 'bell');
    else if (what === 'edit') S.book({ edit: a });
    else if (what === 'visit') S.toast('انتقال به پرونده ویزیت…', 'clipboard');
  };

  /* =========================================================
     booking panel
     ========================================================= */
  const B = {
    mode: 'new', id: null, type: 'visit', pet: null, doc: 'd1', date: null, start: 570, dur: 30,
    sms: true, remind: true, note: '', q: '', kindF: 'all', hi: 0,
    picking: true, adding: false, nu: { name: '', owner: '', phone: '', kind: 'سگ' },
  };
  const selectedPet = () => (B.pet != null ? S.PETS[B.pet] : null);
  const KINDS = [
    { id: 'all', label: 'همه', ic: null },
    { id: 'dog', label: 'سگ', ic: 'dog' },
    { id: 'cat', label: 'گربه', ic: 'cat' },
    { id: 'bird', label: 'پرنده', ic: 'bird' },
    { id: 'other', label: 'سایر', ic: 'rabbit' },
  ];
  const norm = (s) => String(s || '').replace(/\s+/g, ' ').trim();

  function petList() {
    const q = norm(B.q);
    return S.PETS.map((p, i) => ({ p, i })).filter(({ p }) => {
      if (B.kindF !== 'all') {
        const isOther = !['dog', 'cat', 'bird'].includes(p.ic);
        if (B.kindF === 'other' ? !isOther : p.ic !== B.kindF) return false;
      }
      if (!q) return true;
      return (p.name + ' ' + p.owner + ' ' + p.kind + ' ' + p.phone).includes(q);
    });
  }

  function conflicts() {
    return S.APPTS.filter((a) =>
      a.date === B.date && a.doc === B.doc && a.status !== 'cancel' && a.id !== B.id &&
      B.start < a.start + a.dur && a.start < B.start + B.dur);
  }

  /* ---------------- 1 · type ---------------- */
  function typeInner() {
    return `<div class="type-grid">
      ${S.TYPES.map((x) => `<button class="type-opt ${x.cls}${B.type === x.id ? ' on' : ''}" data-bk-type="${x.id}">
        <span class="ic">${S.icon(x.icon)}</span>
        <span class="tx"><span class="nm">${x.label}</span><span class="sb">${fa(x.dur)} دقیقه</span></span>
        <span class="tick">${I.check}</span>
      </button>`).join('')}
    </div>`;
  }

  /* ---------------- 2 · patient & owner ---------------- */
  function petRow({ p, i }, k) {
    return `<button class="pat-opt${B.pet === i && !B.picking ? ' on' : ''}${B.hi === k ? ' hi' : ''}" data-bk-pet="${i}">
      <span class="avatar-pet ${p.av}">${I[p.ic] || ''}</span>
      <span class="pi">
        <span class="nm">${p.name}<span class="kd">${p.kind}</span></span>
        <span class="sb">${I.users}${p.owner} <span class="sep">·</span> ${bdi(p.phone)}</span>
      </span>
      <span class="chk">${I.check}</span>
    </button>`;
  }
  function listInner() {
    const list = petList();
    if (!list.length) {
      return `<div class="pat-empty">
        <span class="ic">${I.search}</span>
        <b>بیماری با این مشخصات پیدا نشد</b>
        <span>می‌توانید همین‌جا پرونده جدیدی بسازید.</span>
        <button class="btn btn-outline sm" data-bk-add="1">${I.plus} ثبت بیمار جدید</button>
      </div>`;
    }
    return list.map(petRow).join('');
  }
  function addForm() {
    return `<div class="pat-add">
      <div class="pa-h">${I.paw} ثبت سریع بیمار جدید</div>
      <div class="pa-grid">
        <label class="fld"><span>نام حیوان</span><input class="input" id="nuName" value="${B.nu.name}" placeholder="مثلاً میلو"/></label>
        <label class="fld"><span>گونه / نژاد</span><input class="input" id="nuKind" value="${B.nu.kind}" placeholder="مثلاً سگ پامرانین"/></label>
        <label class="fld"><span>نام مالک</span><input class="input" id="nuOwner" value="${B.nu.owner}" placeholder="نام و نام خانوادگی"/></label>
        <label class="fld"><span>شماره تماس</span><input class="input" id="nuPhone" value="${B.nu.phone}" placeholder="۰۹۱۲…"/></label>
      </div>
      <div class="pa-foot">
        <button class="btn btn-primary sm" data-bk-addsave="1">${I.check} افزودن و انتخاب</button>
        <button class="btn btn-ghost sm" data-bk-addcancel="1">انصراف</button>
      </div>
    </div>`;
  }
  function selectedCard(p) {
    return `<div class="pat-card">
      <span class="avatar-pet ${p.av}">${I[p.ic] || ''}</span>
      <span class="info">
        <span class="nm">${p.name}<span class="kd">${p.kind}</span></span>
        <span class="meta">
          <span>${I.users}<b>${p.owner}</b></span>
          <span>${S.icon('phone')}${bdi(p.phone)}</span>
        </span>
      </span>
      <span class="acts">
        <button class="btn btn-outline sm" data-bk-pick="1">${I.search} تغییر بیمار</button>
        <a class="btn btn-ghost sm" href="patients.html">${I.file} پرونده</a>
      </span>
    </div>`;
  }
  function patientInner() {
    const sel = selectedPet();
    if (!B.picking && sel) return selectedCard(sel);
    return `<div class="pat-pane">
      <div class="pat-search">
        <span class="si">${I.search}</span>
        <input id="bkQ" placeholder="نام حیوان، مالک، نژاد یا شماره تماس…" value="${B.q}" autocomplete="off"/>
        ${B.q ? `<button class="clr" data-bk-clearq="1" title="پاک کردن">${I.x}</button>` : ''}
      </div>
      <div class="pat-kinds">
        ${KINDS.map((k) => `<button class="mini-chip${B.kindF === k.id ? ' on' : ''}" data-bk-kind="${k.id}">${k.label}</button>`).join('')}
        <span class="cnt">${fa(petList().length)} نتیجه</span>
      </div>
      <div class="pat-list" id="bkPatList">${listInner()}</div>
      ${B.adding ? addForm() : `<button class="pat-new" data-bk-add="1">${I.plus} بیمار جدیدی ثبت کنید</button>`}
    </div>`;
  }

  /* ---------------- 3 · doctor ---------------- */
  function docInner() {
    return `<div class="doc-grid">
      ${S.DOCTORS.map((d) => {
        const busy = S.APPTS.filter((a) => a.date === B.date && a.doc === d.id && a.status !== 'cancel').length;
        const free = S.freeSlots(B.date, d.id, B.dur).filter((s) => s.free).length;
        return `<button class="doc-opt${B.doc === d.id ? ' on' : ''}" style="--ac:${d.ac}" data-bk-doc="${d.id}">
          <span class="av">${d.ini}</span>
          <span class="di"><span class="nm">${d.short}</span><span class="sb">${d.spec}</span></span>
          <span class="st"><b>${fa(busy)}</b> نوبت<span class="fr">${fa(free)} زمان آزاد</span></span>
          <span class="tick">${I.check}</span>
        </button>`;
      }).join('')}
    </div>`;
  }

  /* ---------------- 4 · date & time ---------------- */
  function calInner() {
    const cur = dkey(B.date);
    const ms = S.jMonthStart(cur), p = S.jp(ms), first = S.weekStart(ms);
    let cells = '';
    for (let i = 0; i < 42; i++) {
      const d = S.addDays(first, i), q = S.jp(d), out = q.m !== p.m;
      if (i >= 35 && out) break;
      const k = S.key(d);
      const n = S.APPTS.filter((a) => a.date === k && a.status !== 'cancel').length;
      const cls = [out ? 'out' : '', k === B.date ? 'on' : '', k === S.key(S.TODAY) ? 'today' : '', S.wIdx(d) === 6 ? 'off' : ''].join(' ').trim();
      cells += `<button class="cal-d ${cls}" data-bk-date="${k}" title="${S.JDOW[S.wIdx(d)]} ${S.jFull(d)} · ${fa(n)} نوبت">
        <span class="n">${fa(q.d)}</span>${n && !out ? `<span class="ld${n > 5 ? ' hi' : ''}"></span>` : ''}</button>`;
    }
    return `<div class="cal-head">
        <b>${S.JMONTH[p.m - 1]} ${fa(p.y)}</b>
        <span class="cal-nav">
          <button class="icon-btn nav-btn" data-bk-month="-1" title="ماه قبل">${I.chevronRight}</button>
          <button class="icon-btn nav-btn" data-bk-month="1" title="ماه بعد">${I.chevronLeft}</button>
        </span>
      </div>
      <div class="cal-dow">${S.JDOW_S.map((x) => `<span>${x}</span>`).join('')}</div>
      <div class="cal-grid">${cells}</div>
      <div class="cal-foot">
        <button class="mini-chip" data-bk-date="${S.key(S.TODAY)}">امروز</button>
        <button class="mini-chip" data-bk-date="${S.key(S.addDays(S.TODAY, 1))}">فردا</button>
        <button class="mini-chip" data-bk-date="${S.key(S.addDays(S.TODAY, 7))}">هفتهٔ بعد</button>
      </div>`;
  }
  function slotsInner() {
    const all = S.freeSlots(B.date, B.doc, B.dur);
    const freeN = all.filter((s) => s.free).length;
    const first = (all.find((s) => s.free) || {}).m;
    /* slot groups cover the full clinic day (07:00 → 22:00) */
    const groups = [
      { l: 'صبح', f: (m) => m < 12 * 60 },
      { l: 'ظهر', f: (m) => m >= 12 * 60 && m < 16 * 60 },
      { l: 'عصر', f: (m) => m >= 16 * 60 && m < 19 * 60 },
      { l: 'شب', f: (m) => m >= 19 * 60 },
    ];
    return `<div class="dur-row">
        <span class="lbl">مدت نوبت</span>
        ${[15, 20, 30, 45, 60, 90].map((m) => `<button class="dur${B.dur === m ? ' on' : ''}" data-bk-dur="${m}">${fa(m)} دقیقه</button>`).join('')}
      </div>
      <div class="slot-head">
        <span class="lbl">ساعت شروع</span>
        <span class="meta">${fa(freeN)} زمان آزاد
          ${first != null ? ` · <button class="linkbtn" data-bk-slot="${first}">اولین زمان آزاد ${bdi(hhmm(first))}</button>` : ''}</span>
      </div>
      ${groups.map((g) => {
        const items = all.filter((s) => g.f(s.m));
        if (!items.length) return '';
        return `<div class="slot-group"><span class="sg-l">${g.l}</span>
          <div class="slot-grid">${items.map((s) => `<button class="slot${B.start === s.m ? ' on' : ''}"
            data-bk-slot="${s.m}" ${s.free ? '' : 'disabled title="این ساعت رزرو شده است"'}>${bdi(hhmm(s.m))}</button>`).join('')}</div>
        </div>`;
      }).join('')}`;
  }
  function dateInner() {
    return `<div class="dt-grid"><div class="dt-cal">${calInner()}</div><div class="dt-slots">${slotsInner()}</div></div>`;
  }

  /* ---------------- 5 · extras ---------------- */
  function extraInner() {
    return `<textarea class="textarea bk-note" id="bkNote" rows="3"
        placeholder="علت مراجعه، علائم یا توضیح لازم برای پذیرش…">${B.note}</textarea>
      <div class="sw-grid">
        <label class="switch-row">
          <span class="switch"><input type="checkbox" id="bkSms" ${B.sms ? 'checked' : ''}/><span class="sl"></span></span>
          <span class="tx">پیامک تأیید<small>بلافاصله پس از ثبت برای مالک ارسال می‌شود</small></span>
        </label>
        <label class="switch-row">
          <span class="switch"><input type="checkbox" id="bkRemind" ${B.remind ? 'checked' : ''}/><span class="sl"></span></span>
          <span class="tx">یادآور ۲۴ ساعت قبل<small>یادآوری خودکار پیش از زمان نوبت</small></span>
        </label>
      </div>`;
  }

  /* ---------------- summary aside ---------------- */
  function sideInner() {
    const p = selectedPet(), t = S.typeOf(B.type), d = S.docOf(B.doc), day = dkey(B.date);
    const conf = conflicts();
    const price = fa(((B.dur * 12000)).toLocaleString('en-US'));
    const petBlock = p
      ? `<div class="sum-pet">
          <span class="avatar-pet ${p.av}">${I[p.ic] || ''}</span>
          <span><b>${p.name}</b><span>${p.kind} · ${p.owner}</span></span>
        </div>`
      : `<div class="sum-pet sum-pet-empty">
          <span class="avatar-pet ph">${I.paw || ''}</span>
          <span><b>بیماری انتخاب نشده</b><span>از بخش «بیمار و مالک» بیمار را انتخاب کنید</span></span>
        </div>`;
    return `<div class="bk-card ${t.cls}">
        <div class="bk-card-h"><span class="dot"></span>خلاصهٔ نوبت</div>
        ${petBlock}
        <div class="sum-rows">
          <div><span class="k">نوع نوبت</span><span class="v tag">${t.label}</span></div>
          <div><span class="k">پزشک</span><span class="v">${d.short}</span></div>
          <div><span class="k">تاریخ</span><span class="v">${S.JDOW[S.wIdx(day)]}، ${S.jFull(day)}</span></div>
          <div><span class="k">ساعت</span><span class="v">${rangeTxt(B.start, B.start + B.dur)}</span></div>
          <div><span class="k">مدت</span><span class="v">${fa(B.dur)} دقیقه</span></div>
          <div><span class="k">هزینه تخمینی</span><span class="v">${price} تومان</span></div>
        </div>
        ${!p ? ''
          : conf.length
          ? `<div class="bk-warn">${I.alert}<span><b>تداخل با ${fa(conf.length)} نوبت</b>
              <small>${conf.map((c) => `${c.pet} (${hhmm(c.start)})`).join('، ')}</small></span></div>`
          : `<div class="bk-ok">${I.check}<span>این بازه برای ${d.short} آزاد است.</span></div>`}
      </div>
      <div class="bk-actions">
        <button class="btn btn-primary bk-submit" id="bkSubmit" onclick="SCH.submitBook()" ${p ? '' : 'disabled title="ابتدا بیمار را انتخاب کنید"'}>
          ${I.check} ${B.mode === 'edit' ? 'ذخیره تغییرات' : 'ثبت نوبت'}</button>
        <button class="btn btn-ghost" onclick="SCH.closeBook()">انصراف</button>
      </div>
      <div class="bk-tip">${I.bulb}<span>روی تقویم کلیک کنید یا بکشید تا بازهٔ دلخواه مستقیم انتخاب شود.</span></div>`;
  }

  /* ---------------- painters ---------------- */
  const sec = (n, title, hint, id, inner) => `<section class="bk-sec">
      <h4><span class="step">${fa(n)}</span><span class="ttl">${title}</span><span class="hint">${hint}</span></h4>
      <div id="${id}">${inner}</div>
    </section>`;

  function paintHead() {
    const day = dkey(B.date), t = S.typeOf(B.type);
    el('bkTitle').textContent = B.mode === 'edit' ? 'ویرایش نوبت' : 'ثبت نوبت جدید';
    el('bkHeadMeta').innerHTML = `<span class="hm ${t.cls}"><span class="dot"></span>${t.label}</span>
      <span class="hm">${I.calendar}${S.jDate(day)}</span>
      <span class="hm">${I.clock}${rangeTxt(B.start, B.start + B.dur)}</span>`;
  }
  function paintPatient() { const n = el('bkPatWrap'); if (n) n.innerHTML = patientInner(); }
  function paintList() { const n = el('bkPatList'); if (n) n.innerHTML = listInner(); }
  function paintType() { const n = el('bkTypeWrap'); if (n) n.innerHTML = typeInner(); }
  function paintDoc() { const n = el('bkDocWrap'); if (n) n.innerHTML = docInner(); }
  function paintDate() { const n = el('bkDateWrap'); if (n) n.innerHTML = dateInner(); }
  function paintSide() { el('bkSide').innerHTML = sideInner(); paintHead(); }

  function renderBook() {
    el('bkBody').innerHTML =
      sec(1, 'نوع نوبت', 'مدت پیش‌فرض هر نوع به‌صورت خودکار اعمال می‌شود', 'bkTypeWrap', typeInner()) +
      sec(2, 'بیمار و مالک', 'جستجو بر اساس نام حیوان، مالک، نژاد یا شمارهٔ تماس', 'bkPatWrap', patientInner()) +
      sec(3, 'پزشک معالج', 'ظرفیت هر پزشک در تاریخ انتخاب‌شده نمایش داده می‌شود', 'bkDocWrap', docInner()) +
      sec(4, 'تاریخ و ساعت', 'ابتدا روز، سپس مدت و ساعت شروع را انتخاب کنید', 'bkDateWrap', dateInner()) +
      sec(5, 'جزئیات تکمیلی', 'اختیاری', 'bkExtraWrap', extraInner());
    paintSide();
  }

  /* ---------------- open / close ---------------- */
  let lastFocus = null;
  let closeTimer = null;
  S.book = function (opt) {
    opt = opt || {};
    if (opt.edit) {
      const a = opt.edit;
      Object.assign(B, {
        mode: 'edit', id: a.id, type: a.type, doc: a.doc, date: a.date, start: a.start, dur: a.dur,
        note: a.note, sms: true, remind: true, q: '', kindF: 'all', picking: false, adding: false, hi: 0,
        pet: Math.max(0, S.PETS.findIndex((p) => p.name === a.pet)),
      });
      S.closeDetail();
    } else {
      Object.assign(B, {
        mode: 'new', id: null, type: 'visit', pet: null, sms: true, remind: true, note: '', q: '',
        kindF: 'all', picking: true, adding: false, hi: 0,
        date: opt.date || S.key(S.S.cursor),
        start: opt.start != null ? opt.start : 570,
        dur: opt.dur || S.typeOf('visit').dur,
        doc: opt.doc || [...S.S.docs][0] || 'd1',
      });
    }
    renderBook();
    const w = el('bkWrap');
    lastFocus = document.activeElement;
    w.hidden = false;
    w.classList.add('open');
    document.body.classList.add('bk-lock');
    requestAnimationFrame(() => {
      w.classList.add('show');
      w.querySelector('.bk-main').scrollTop = 0;
      /* new bookings open straight into patient search — focus it so typing works immediately */
      const f = !selectedPet() ? el('bkQ') : el('bkSubmit');
      if (f) f.focus({ preventScroll: true });
    });
  };
  S.isBookOpen = function () {
    const w = el('bkWrap');
    return !!w && w.classList.contains('open');
  };
  S.closeBook = function () {
    const w = el('bkWrap');
    if (!w.classList.contains('open')) return;
    w.classList.remove('show');
    document.body.classList.remove('bk-lock');
    clearTimeout(closeTimer);
    closeTimer = setTimeout(() => { w.classList.remove('open'); w.hidden = true; }, 220);
    if (lastFocus && lastFocus.focus) { try { lastFocus.focus({ preventScroll: true }); } catch (_) {} }
    lastFocus = null;
  };

  /* ---------------- interactions ---------------- */
  document.addEventListener('click', (e) => {
    const h = (sel) => e.target.closest(sel);
    let n;
    /* click on the dimmed area closes the sheet */
    if (e.target.id === 'bkWrap') { S.closeBook(); return; }
    if ((n = h('[data-bk-type]'))) {
      B.type = n.dataset.bkType; B.dur = S.typeOf(B.type).dur;
      paintType(); paintDoc(); paintDate(); paintSide();
    } else if ((n = h('[data-bk-pet]'))) {
      B.pet = +n.dataset.bkPet; B.picking = false; B.adding = false; B.q = '';
      paintPatient(); paintSide();
    } else if ((n = h('[data-bk-pick]'))) {
      B.picking = true; B.hi = 0; paintPatient();
      const i = el('bkQ'); if (i) i.focus();
    } else if ((n = h('[data-bk-clearq]'))) {
      B.q = ''; B.hi = 0; paintPatient();
      const i = el('bkQ'); if (i) i.focus();
    } else if ((n = h('[data-bk-kind]'))) {
      B.kindF = n.dataset.bkKind; B.hi = 0; paintPatient();
    } else if ((n = h('[data-bk-add]'))) {
      B.adding = true; B.picking = true;
      B.nu.name = B.q && !/^\d/.test(B.q) ? B.q : B.nu.name;
      paintPatient();
      const i = el('nuName'); if (i) i.focus();
    } else if ((n = h('[data-bk-addcancel]'))) {
      B.adding = false; paintPatient();
    } else if ((n = h('[data-bk-addsave]'))) {
      savePet();
    } else if ((n = h('[data-bk-doc]'))) {
      B.doc = n.dataset.bkDoc; paintDoc(); paintDate(); paintSide();
    } else if ((n = h('[data-bk-dur]'))) {
      B.dur = +n.dataset.bkDur; paintDate(); paintDoc(); paintSide();
    } else if ((n = h('[data-bk-slot]'))) {
      if (n.disabled) return;
      B.start = +n.dataset.bkSlot; paintDate(); paintSide();
    } else if ((n = h('[data-bk-date]'))) {
      B.date = n.dataset.bkDate; paintDate(); paintDoc(); paintSide();
    } else if ((n = h('[data-bk-month]'))) {
      const dir = +n.dataset.bkMonth;
      const ms = S.jMonthStart(dkey(B.date));
      const next = dir > 0 ? S.jMonthStart(S.addDays(ms, 35)) : S.jMonthStart(S.addDays(ms, -1));
      const want = S.jp(dkey(B.date)).d;
      let d = next, i = 1;
      while (i < want && S.jp(S.addDays(d, 1)).m === S.jp(next).m) { d = S.addDays(d, 1); i++; }
      B.date = S.key(d); paintDate(); paintDoc(); paintSide();
    }
  });

  function savePet() {
    const g = (id) => (el(id) ? el(id).value.trim() : '');
    const name = g('nuName'), owner = g('nuOwner'), phone = g('nuPhone'), kind = g('nuKind') || 'نامشخص';
    if (!name || !owner) { S.toast('نام حیوان و نام مالک الزامی است.', 'alert'); return; }
    const ic = /گربه/.test(kind) ? 'cat' : /پرنده|طوطی|کاسکو/.test(kind) ? 'bird' : /خرگوش/.test(kind) ? 'rabbit' : 'dog';
    const av = 'abcdef'[S.PETS.length % 6];
    S.PETS.push({ name, kind, owner, phone: phone || '—', av, ic });
    B.pet = S.PETS.length - 1; B.adding = false; B.picking = false; B.q = '';
    B.nu = { name: '', owner: '', phone: '', kind: 'سگ' };
    paintPatient(); paintSide();
    S.toast(`پروندهٔ «${name}» ساخته و برای این نوبت انتخاب شد.`, 'paw');
  }

  document.addEventListener('input', (e) => {
    if (e.target.id === 'bkQ') { B.q = e.target.value; B.hi = 0; paintList(); updCount(); }
    else if (e.target.id === 'bkNote') B.note = e.target.value;
    else if (e.target.id === 'bkSms') B.sms = e.target.checked;
    else if (e.target.id === 'bkRemind') B.remind = e.target.checked;
    else if (/^nu(Name|Owner|Phone|Kind)$/.test(e.target.id || '')) {
      B.nu[e.target.id.slice(2).toLowerCase()] = e.target.value;
    }
  });
  function updCount() {
    const c = document.querySelector('.pat-kinds .cnt');
    if (c) c.textContent = fa(petList().length) + ' نتیجه';
  }

  /* keyboard navigation inside the patient search */
  document.addEventListener('keydown', (e) => {
    if (e.target.id !== 'bkQ') return;
    const list = petList();
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!list.length) return;
      B.hi = (B.hi + (e.key === 'ArrowDown' ? 1 : -1) + list.length) % list.length;
      paintList();
      const node = document.querySelector('.pat-opt.hi');
      if (node) node.scrollIntoView({ block: 'nearest' });
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const pick = list[B.hi] || list[0];
      if (pick) { B.pet = pick.i; B.picking = false; B.q = ''; paintPatient(); paintSide(); }
    }
  });

  S.submitBook = function () {
    const p = selectedPet();
    if (!p) {
      S.toast('لطفاً ابتدا بیمار را انتخاب کنید.', 'alert');
      B.picking = true; paintPatient();
      const wrap = el('bkPatWrap');
      if (wrap && wrap.scrollIntoView) wrap.scrollIntoView({ behavior: 'smooth', block: 'center' });
      const i = el('bkQ'); if (i) i.focus({ preventScroll: true });
      return;
    }
    if (B.mode === 'edit') {
      const a = S.get(B.id);
      Object.assign(a, { type: B.type, doc: B.doc, date: B.date, start: B.start, dur: B.dur, note: B.note,
        pet: p.name, kind: p.kind, owner: p.owner, phone: p.phone, av: p.av, ic: p.ic });
      S.toast('تغییرات نوبت ذخیره شد.');
    } else {
      const a = S.add({
        date: B.date, start: B.start, dur: B.dur, type: B.type, doc: B.doc,
        pet: p.name, kind: p.kind, owner: p.owner, phone: p.phone, av: p.av, ic: p.ic,
        status: 'confirmed', note: B.note, created: 'ثبت توسط شما', code: 'AP-' + fa(14059000 + S.APPTS.length),
      });
      B.id = a.id;
      S.toast(B.sms ? `نوبت ${p.name} ثبت و پیامک تأیید ارسال شد.` : `نوبت ${p.name} ثبت شد.`);
    }
    S.closeBook();
    S.S.cursor = dkey(B.date);
    if (S.S.view === 'month') S.S.view = 'day';
    S.S.selected = B.id;
    S.render();
    S.openDetail(B.id);
  };

  /* Ctrl/⌘ + Enter submits the sheet from anywhere inside it */
  document.addEventListener('keydown', (e) => {
    if (!S.isBookOpen() || e.key !== 'Enter' || !(e.ctrlKey || e.metaKey)) return;
    e.preventDefault();
    S.submitBook();
  });

  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    if (S.isBookOpen()) {
      if (B.adding) { B.adding = false; paintPatient(); return; }
      S.closeBook();
    } else if (el('dtl') && el('dtl').classList.contains('open')) S.closeDetail();
  });
})();
