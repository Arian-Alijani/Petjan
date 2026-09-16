/* =========================================================
   Vetplus — Patients & Owners directory  (page controller)
   ========================================================= */
(function () {
  const S = {
    view: 'owners',      // owners | pets
    layout: 'list',      // list (row) | grid (box) — applies to both owners & pets views
    q: '',
    species: 'all',
    vax: 'all',          // all | has | none — vaccination filter
    sort: 'recent',
  };

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

  /* ---------------- search matching ---------------- */
  const norm = (s) => String(s || '')
    .replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d))
    .replace(/[ي]/g, 'ی').replace(/[ك]/g, 'ک').toLowerCase().trim();

  function petMatches(p) {
    const o = ownerById(p.ownerId);
    if (S.species !== 'all' && p.species !== S.species) return false;
    if (S.vax === 'has' && !petHasVaccine(p)) return false;
    if (S.vax === 'none' && petHasVaccine(p)) return false;
    if (!S.q) return true;
    const hay = norm([p.name, p.code, p.breed, speciesLabel(p.species), p.color,
      o && ownerName(o), o && o.file, o && o.mobile1, o && o.mobile2, o && o.phone,
      o && o.nationalId, p.microchip && p.microchip.code].join(' '));
    return hay.includes(norm(S.q));
  }
  function ownerMatches(o) {
    const pets = petsOfOwner(o.id);
    const hasPet = pets.some(petMatches);
    if (S.species !== 'all' || S.vax !== 'all') return hasPet;
    if (!S.q) return true;
    const hay = norm([ownerName(o), o.file, o.mobile1, o.mobile2, o.phone, o.nationalId, o.address, o.referral].join(' '));
    return hay.includes(norm(S.q)) || hasPet;
  }

  function sortPets(list) {
    const c = [...list];
    if (S.sort === 'name') c.sort((a, b) => a.name.localeCompare(b.name, 'fa'));
    else if (S.sort === 'age') c.sort((a, b) => (jalaliToDate(a.birth) || 0) - (jalaliToDate(b.birth) || 0));
    else c.sort((a, b) => {
      const av = lastVisit(a), bv = lastVisit(b);
      return (jalaliToDate(bv ? bv.date : '1390/01/01')) - (jalaliToDate(av ? av.date : '1390/01/01'));
    });
    return c;
  }

  /* ---------------- toolbar ---------------- */
  function renderSpeciesChips() {
    const counts = {};
    PETS.forEach(p => counts[p.species] = (counts[p.species] || 0) + 1);
    const chips = [{ k: 'all', label: 'همه گونه‌ها', icon: 'paw', n: PETS.length }]
      .concat(Object.keys(SPECIES).filter(k => counts[k]).map(k => ({ k, label: SPECIES[k].label, icon: SPECIES[k].icon, n: counts[k] })));
    $('#speciesChips').innerHTML = chips.map(c => `
      <button class="pb-chip ${S.species === c.k ? 'on' : ''}" data-species="${c.k}">
        ${Icons[c.icon] || ''}<span>${c.label}</span><span class="cnt">${fa(c.n)}</span>
      </button>`).join('');
    $$('#speciesChips .pb-chip').forEach(b => b.onclick = () => { S.species = b.dataset.species; render(); });
  }

  /* ---------------- owner card ---------------- */
  function ownerCard(o) {
    const pets = petsOfOwner(o.id).filter(p => (S.species === 'all' && S.vax === 'all') ? true : petMatches(p));
    const shown = pets.length ? pets : petsOfOwner(o.id);
    const tagCls = { 'VIP': 'vip', 'بدهکار': 'debt', 'مشتری وفادار': 'loyal' };
    return `
      <article class="owner-card rv" data-owner="${o.id}">
        <div class="oc-top">
          <div class="oc-avatar ${o.tone}">${initials(o)}</div>
          <div class="oc-id">
            <div class="oc-name">
              ${ownerName(o)}
              <span class="g-pill">${OWNER_GENDER[o.gender] || '—'}</span>
            </div>
            <div class="oc-meta">
              <span class="oc-file">پرونده ${o.file}</span>
              ${(o.tags || []).map(t => `<span class="oc-tag ${tagCls[t] || ''}">${t}</span>`).join('')}
            </div>
          </div>
          <button class="icon-btn oc-more" title="اطلاعات کامل مالک" data-open-owner="${o.id}">${Icons.more}</button>
        </div>

        <div class="oc-contact">
          <div class="oc-line" title="موبایل اول">${Icons.mobile}<span>${fa(o.mobile1)}</span></div>
          <div class="oc-line" title="موبایل دوم">${Icons.mobile}<span class="${o.mobile2 ? '' : 'muted'}">${o.mobile2 ? fa(o.mobile2) : 'ثبت نشده'}</span></div>
          <div class="oc-line" title="تلفن ثابت">${Icons.phone}<span class="${o.phone ? '' : 'muted'}">${o.phone ? fa(o.phone) : 'ثبت نشده'}</span></div>
          <div class="oc-line" title="کد ملی">${Icons.idcard}<span>${fa(o.nationalId)}</span></div>
          <div class="oc-line full" title="آدرس">${Icons.pin}<span>${o.address}</span></div>
        </div>

        <div class="oc-pets">
          <div class="oc-pets-head">
            <span>${fa(petsOfOwner(o.id).length)} حیوان ثبت‌شده</span>
            <button class="add" data-add-pet="${o.id}">${Icons.plus} افزودن حیوان</button>
          </div>
          <div class="pet-chips">
            ${shown.map(p => {
              const a = ageFromJalali(p.birth);
              const lv = lastVisit(p);
              return `
              <a class="pet-chip" href="patient-file.html?id=${p.id}">
                <span class="pc-av tone-${p.tone}">${speciesIcon(p.species)}</span>
                <span>
                  <span class="pc-name">${p.name} ${sexDot(p.gender)}</span>
                  <span class="pc-sub">${speciesLabel(p.species)} · ${p.breed} · ${a.label}</span>
                </span>
                <span>${statusPill(p.status)}</span>
                <span class="pc-go">${Icons.chevronLeft}</span>
              </a>`;
            }).join('')}
          </div>
        </div>

        <div class="oc-foot">
          <button class="btn-tiny" data-open-owner="${o.id}">${Icons.file} پرونده مالک</button>
          <button class="btn-tiny" data-call="${o.mobile1}">${Icons.phone} تماس</button>
          <span class="since">مشتری از ${fa(o.since)}</span>
        </div>
      </article>`;
  }

  /* ---------------- pet card ---------------- */
  const COVERS = {
    cat:    ['#ece5fb', '#f8f5ff'], dog: ['#fbe7d6', '#fff7f0'],
    bird:   ['#fffaeb', '#fffaf0'], rabbit: ['#fbe4f1', '#fff5fb'],
    rodent: ['#ddf0e9', '#f3fbf8'], other: ['#eff8ff', '#f4f8ff'],
  };
  function petCard(p) {
    const o = ownerById(p.ownerId);
    const a = ageFromJalali(p.birth);
    const lv = lastVisit(p);
    const w = currentWeight(p);
    const c = COVERS[p.species] || COVERS.other;
    return `
      <a class="pet-card rv" href="patient-file.html?id=${p.id}">
        <div class="pk-cover" style="--c1:${c[0]};--c2:${c[1]}">
          <span class="pk-status">${statusPill(p.status)}</span>
          <span class="pk-av tone-${p.tone}">${speciesIcon(p.species)}</span>
        </div>
        <div class="pk-body">
          <div class="pk-name">${p.name} ${sexDot(p.gender)}</div>
          <div class="pk-code">کد بیمار ${p.code}</div>
          <span class="pk-breed">${speciesIcon(p.species)} ${speciesLabel(p.species)} · ${p.breed}</span>
          <div class="pk-facts">
            <div class="pk-fact"><div class="fv">${a.short || a.label}</div><div class="fl">سن</div></div>
            <div class="pk-fact"><div class="fv">${w ? fa(w) : '—'}<span style="font-size:9px"> کیلو</span></div><div class="fl">وزن فعلی</div></div>
            <div class="pk-fact"><div class="fv" style="font-size:11.5px">${lv ? sinceJalali(lv.date) : '—'}</div><div class="fl">آخرین ویزیت</div></div>
          </div>
        </div>
        <div class="pk-owner">
          <span class="oa tone-${o.tone}">${initials(o)}</span>
          <span class="nm">${ownerName(o)}</span>
          <span class="go">${Icons.chevronLeft}</span>
        </div>
      </a>`;
  }

  function petsTable(list) {
    return `
    <div class="pt-table-card rv">
      <table class="pt-table">
        <thead><tr>
          <th>بیمار</th><th>مالک</th><th>گونه / نژاد</th><th>سن</th><th>وزن</th>
          <th>آخرین ویزیت</th><th>واکسن بعدی</th><th>وضعیت</th><th></th>
        </tr></thead>
        <tbody>
        ${list.map(p => {
          const o = ownerById(p.ownerId), a = ageFromJalali(p.birth), lv = lastVisit(p), nv = nextVaccine(p);
          const due = nv && nv.u.days < 0;
          return `<tr data-go="patient-file.html?id=${p.id}">
            <td><div class="cell-pet">
              <span class="av tone-${p.tone}">${speciesIcon(p.species)}</span>
              <span><span class="nm">${p.name} ${sexDot(p.gender)}</span><div class="sb">${p.code}</div></span>
            </div></td>
            <td><div class="nm" style="font-size:12.5px">${ownerName(o)}</div><div class="sb">${fa(o.mobile1)}</div></td>
            <td>${speciesLabel(p.species)}<div class="sb">${p.breed}</div></td>
            <td class="sb" style="font-size:12.5px;color:var(--ink-2)">${a.label}</td>
            <td style="font-variant-numeric:tabular-nums">${currentWeight(p) ? fa(currentWeight(p)) + ' کیلو' : '—'}</td>
            <td>${lv ? sinceJalali(lv.date) : '—'}<div class="sb">${lv ? lv.type : ''}</div></td>
            <td>${nv ? `<span class="${due ? 'stock-low' : ''}">${fa(nv.next)}</span><div class="sb">${nv.name}</div>` : '<span class="sb">—</span>'}</td>
            <td>${statusPill(p.status)}</td>
            <td><span style="color:var(--muted-2)">${Icons.chevronLeft.replace('<svg', '<svg width="16" height="16"')}</span></td>
          </tr>`;
        }).join('')}
        </tbody>
      </table>
    </div>`;
  }

  function ownersTable(list) {
    return `
    <div class="pt-table-card rv">
      <table class="pt-table">
        <thead><tr>
          <th>مالک</th><th>تماس</th><th>پرونده</th><th>حیوانات</th>
          <th>آخرین ویزیت</th><th>واکسن</th><th>مانده حساب</th><th></th>
        </tr></thead>
        <tbody>
        ${list.map(o => {
          const pets = petsOfOwner(o.id);
          let last = null;
          pets.forEach(p => {
            const lv = lastVisit(p);
            if (!lv) return;
            const d = jalaliToDate(lv.date);
            if (!last || (d && d > last.d)) last = { d, date: lv.date };
          });
          const vax = ownerHasVaccine(o.id);
          return `<tr data-open-owner="${o.id}">
            <td><div class="cell-pet">
              <span class="av tone-${o.tone}">${initials(o)}</span>
              <span><span class="nm">${ownerName(o)}</span><div class="sb">${o.file}</div></span>
            </div></td>
            <td><div class="nm" style="font-size:12.5px">${fa(o.mobile1)}</div><div class="sb">${o.phone ? fa(o.phone) : 'بدون تلفن ثابت'}</div></td>
            <td class="sb" style="font-size:12.5px;color:var(--ink-2);font-variant-numeric:tabular-nums">${o.file}</td>
            <td>${fa(pets.length)} حیوان<div class="sb">${pets.slice(0, 2).map(p => p.name).join('، ')}${pets.length > 2 ? '…' : ''}</div></td>
            <td>${last ? sinceJalali(last.date) : '—'}</td>
            <td>${vax ? '<span class="pill pill-ok">دارد</span>' : '<span class="pill pill-mute">ندارد</span>'}</td>
            <td style="font-variant-numeric:tabular-nums">${o.balance ? faNum(o.balance) + ' ت' : '۰'}</td>
            <td><span style="color:var(--muted-2)">${Icons.chevronLeft.replace('<svg', '<svg width="16" height="16"')}</span></td>
          </tr>`;
        }).join('')}
        </tbody>
      </table>
    </div>`;
  }

  /* ---------------- main render ---------------- */
  function render() {
    renderSpeciesChips();
    $$('#viewSeg button').forEach(b => b.classList.toggle('on', b.dataset.view === S.view));
    $$('#layoutSeg button').forEach(b => b.classList.toggle('on', b.dataset.layout === S.layout));
    $('#sortSelect').value = S.sort;
    $('#vaxSelect').value = S.vax;
    $('#ptSearch').parentElement.classList.toggle('has-value', !!S.q);

    const host = $('#ptContent');
    if (S.view === 'owners') {
      const list = OWNERS.filter(ownerMatches);
      $('#toolCount').textContent = `${fa(list.length)} مالک · ${fa(PETS.filter(petMatches).length)} بیمار`;
      if (S.layout === 'grid') {
        host.className = 'owner-grid';
        host.innerHTML = list.length ? list.map(ownerCard).join('') : emptyState('مالکی پیدا نشد');
      } else {
        host.className = '';
        host.innerHTML = list.length ? ownersTable(list) : `<div class="owner-grid">${emptyState('مالکی پیدا نشد')}</div>`;
      }
    } else {
      const list = sortPets(PETS.filter(petMatches));
      $('#toolCount').textContent = `${fa(list.length)} بیمار`;
      if (S.layout === 'grid') {
        host.className = 'pets-grid';
        host.innerHTML = list.length ? list.map(petCard).join('') : emptyState('بیماری پیدا نشد');
      } else {
        host.className = '';
        host.innerHTML = list.length ? petsTable(list) : `<div class="pets-grid">${emptyState('بیماری پیدا نشد')}</div>`;
      }
    }
    bindContent();
    mountReveal('.rv:not(.in)');
  }

  function emptyState(t) {
    return `<div class="pt-empty">
      <div class="ic">${Icons.search}</div>
      <h4>${t}</h4>
      <p>عبارت جستجو یا فیلترها را تغییر دهید.</p>
      <button class="btn btn-outline" id="resetFilters">${Icons.x} پاک کردن فیلترها</button>
    </div>`;
  }

  function bindContent() {
    $$('[data-open-owner]').forEach(b => b.onclick = (e) => { e.preventDefault(); e.stopPropagation(); openOwnerDrawer(b.dataset.openOwner); });
    $$('[data-add-pet]').forEach(b => b.onclick = (e) => { e.preventDefault(); e.stopPropagation(); openPetModal(b.dataset.addPet); });
    $$('[data-call]').forEach(b => b.onclick = (e) => { e.preventDefault(); e.stopPropagation(); toast(`در حال برقراری تماس با ${fa(b.dataset.call)}`, 'phone'); });
    /* pet cards / chips link to patient-file.html?id=… — intercept and open the centered pet-file sheet instead */
    $$('#ptContent a[href^="patient-file.html?id="]').forEach(a => a.onclick = (e) => {
      e.preventDefault(); e.stopPropagation();
      openPetFile(a.getAttribute('href').split('id=')[1]);
    });
    $$('#ptContent tr[data-go]').forEach(tr => tr.onclick = () => {
      const id = (tr.dataset.go.split('id=')[1]) || '';
      if (tr.dataset.go.startsWith('patient-file.html')) openPetFile(id);
      else navigateTo(tr.dataset.go);
    });
    const rf = $('#resetFilters');
    if (rf) rf.onclick = () => { S.q = ''; S.species = 'all'; S.vax = 'all'; $('#ptSearch').value = ''; render(); };
  }

  /* ---------------- owner file (centered sheet) ----------------
     Opens in the middle of the screen. Information-first:
     identity → contact → pets → activity. Full inline edit mode. */
  let ownerEditing = false;
  let currentOwnerId = null;

  window.openOwnerDrawer = function (id) {
    const o = ownerById(id);
    if (!o) return;
    currentOwnerId = id;
    ownerEditing = false;
    renderOwnerSheet();
    $('#drawerRoot').classList.add('open');
    document.body.style.overflow = 'hidden';
  };

  function renderOwnerSheet() {
    const o = ownerById(currentOwnerId);
    if (!o) return;
    const pets = petsOfOwner(o.id);
    const visits = pets.reduce((n, p) => n + p.visits.length, 0);

    /* newest visit across all pets of this owner */
    let last = null;
    pets.forEach(p => {
      const lv = lastVisit(p);
      if (!lv) return;
      const d = jalaliToDate(lv.date);
      if (!last || (d && d > last.d)) last = { d, date: lv.date };
    });

    /* editable row (`field` maps to an owner property) vs. static row */
    const ed = ownerEditing;
    const inputRow = (k, field, val, opts = {}) => {
      const disp = val || 'ثبت نشده';
      const view = `<span class="v ${val ? '' : 'empty'} ${opts.wrap ? 'wrap' : ''}">${disp}</span>`;
      let edit;
      if (opts.stack) {
        edit = `<span class="v"><textarea class="od-input" data-field="${field}" placeholder="${opts.ph || ''}">${opts.raw ?? ''}</textarea></span>`;
      } else {
        edit = `<span class="v"><input class="od-input" data-field="${field}" value="${(opts.raw ?? '')}" placeholder="${opts.ph || ''}" ${opts.numeric ? 'inputmode="numeric"' : ''}/></span>`;
      }
      return `<div class="od-row ${opts.stack ? 'stack' : ''}"><span class="k">${k}</span>${ed && field ? edit : view}</div>`;
    };
    const staticRow = (k, v, opts = {}) =>
      `<div class="od-row ${opts.stack ? 'stack' : ''}"><span class="k">${k}</span><span class="v ${v ? '' : 'empty'} ${opts.wrap ? 'wrap' : ''}">${v || 'ثبت نشده'}</span></div>`;

    /* gender editor uses radio pills when editing */
    const genderRow = ed
      ? `<div class="od-row"><span class="k">جنسیت</span><span class="v">
          <span class="radio-pills" style="justify-content:flex-end">
            <label><input type="radio" name="oGenderEdit" value="female" ${o.gender === 'female' ? 'checked' : ''}/><span>خانم</span></label>
            <label><input type="radio" name="oGenderEdit" value="male" ${o.gender === 'male' ? 'checked' : ''}/><span>آقا</span></label>
          </span></span></div>`
      : staticRow('جنسیت', OWNER_GENDER[o.gender]);

    $('#drawerTitle').textContent = ownerName(o);
    $('#drawerEyebrow').innerHTML = `
      <span class="od-file">پرونده ${o.file}</span>
      <span class="od-dot"></span>
      <span>${OWNER_GENDER[o.gender] || '—'}</span>
      <span class="od-dot"></span>
      <span>مشتری از ${fa(o.since)}</span>
      ${ed ? '<span class="od-editflag">' + (Icons.edit || '') + ' حالت ویرایش</span>' : ''}`;
    const av = $('#drawerAv');
    av.className = `drawer-av tone-${o.tone}`;
    av.textContent = initials(o);

    $('#drawerBody').innerHTML = `
      <div class="od">

        ${ed ? '' : `<div class="od-cta">
          <button class="od-btn primary" data-call="${o.mobile1}">${Icons.phone}<span>تماس</span></button>
          <button class="od-btn" data-sms="${o.mobile1}">${Icons.sms}<span>پیامک</span></button>
          <button class="od-btn" data-remind="${o.id}">${Icons.bell || Icons.history}<span>یادآوری</span></button>
        </div>`}

        <div class="od-grid">
          <!-- identity -->
          <section class="od-sec ${ed ? 'editing' : ''}">
            <div class="od-sec-h"><h5>اطلاعات هویتی</h5></div>
            <div class="od-rows">
              ${inputRow('نام', 'first', o.first, { raw: o.first })}
              ${inputRow('نام خانوادگی', 'last', o.last, { raw: o.last })}
              ${inputRow('کد ملی', 'nationalId', fa(o.nationalId), { raw: o.nationalId, numeric: true, ph: 'کد ملی' })}
              ${genderRow}
              ${staticRow('شماره پرونده', o.file)}
            </div>
          </section>

          <!-- contact -->
          <section class="od-sec ${ed ? 'editing' : ''}">
            <div class="od-sec-h"><h5>راه‌های ارتباطی</h5></div>
            <div class="od-rows">
              ${inputRow('موبایل اول', 'mobile1', o.mobile1 ? fa(o.mobile1) : '', { raw: o.mobile1, numeric: true, ph: '۰۹۱۲…' })}
              ${inputRow('موبایل دوم', 'mobile2', o.mobile2 ? fa(o.mobile2) : '', { raw: o.mobile2, numeric: true, ph: 'اختیاری' })}
              ${inputRow('تلفن ثابت', 'phone', o.phone ? fa(o.phone) : '', { raw: o.phone, numeric: true, ph: 'اختیاری' })}
            </div>
          </section>

          <!-- address (full width) -->
          <section class="od-sec span ${ed ? 'editing' : ''}">
            <div class="od-sec-h"><h5>نشانی و نحوه آشنایی</h5></div>
            <div class="od-rows">
              ${inputRow('آدرس', 'address', o.address, { stack: true, wrap: true, raw: o.address, ph: 'استان، شهر، خیابان…' })}
              ${ed
                ? `<div class="od-row"><span class="k">نحوه آشنایی</span><span class="v"><select class="od-input" data-field="referral">${REFERRALS.map(r => `<option ${r === o.referral ? 'selected' : ''}>${r}</option>`).join('')}</select></span></div>`
                : staticRow('نحوه آشنایی', o.referral)}
            </div>
          </section>

          <!-- pets -->
          <section class="od-sec span">
            <div class="od-sec-h">
              <h5>حیوانات <span class="od-num">${fa(pets.length)}</span></h5>
              <button class="od-btn sm" data-add-pet="${o.id}">${Icons.plus}<span>افزودن حیوان</span></button>
            </div>
            <div class="od-pets">
              ${pets.length ? pets.map(p => {
                const a = ageFromJalali(p.birth);
                return `<a class="od-pet" href="javascript:void(0)" data-open-pet="${p.id}">
                  <span class="pv tone-${p.tone}">${speciesIcon(p.species)}</span>
                  <span class="pt">
                    <span class="pn">${p.name} ${sexDot(p.gender)}</span>
                    <span class="ps">${speciesLabel(p.species)} · ${p.breed} · ${a.label}</span>
                  </span>
                  ${statusPill(p.status)}
                  <span class="pg">${Icons.chevronLeft}</span>
                </a>`;
              }).join('') : `<div class="od-empty">حیوانی برای این مالک ثبت نشده است</div>`}
            </div>
          </section>

          <!-- activity -->
          <section class="od-sec span">
            <div class="od-sec-h"><h5>خلاصه فعالیت</h5></div>
            <div class="od-stats">
              <div class="od-stat"><b>${fa(visits)}</b><span>ویزیت ثبت‌شده</span></div>
              <div class="od-stat"><b>${fa(pets.length)}</b><span>حیوان فعال</span></div>
              <div class="od-stat"><b>${last ? sinceJalali(last.date) : '—'}</b><span>آخرین مراجعه</span></div>
            </div>
          </section>
        </div>

      </div>`;

    $('#drawerFoot').innerHTML = ed
      ? `<button class="od-btn primary" id="ownerSaveBtn">${Icons.check}<span>ذخیره تغییرات</span></button>
         <button class="od-btn" id="ownerCancelBtn"><span>انصراف</span></button>`
      : `<button class="od-btn primary" id="ownerEditBtn">${Icons.edit}<span>ویرایش اطلاعات</span></button>
         <button class="od-btn" onclick="closeDrawer()"><span>بستن</span></button>`;

    /* bind actions */
    $$('#drawerBody [data-call]').forEach(b => b.onclick = () => toast(`در حال برقراری تماس با ${fa(b.dataset.call)}`, 'phone'));
    $$('#drawerBody [data-sms]').forEach(b => b.onclick = () => toast('پیامک برای مالک ارسال شد (نمونه طراحی)', 'sms'));
    $$('#drawerBody [data-remind]').forEach(b => b.onclick = () => toast('یادآوری برای مالک ثبت شد (نمونه طراحی)', 'sms'));
    $$('#drawerBody [data-add-pet]').forEach(b => b.onclick = () => { closeDrawer(); openPetModal(b.dataset.addPet); });
    $$('#drawerBody [data-open-pet]').forEach(b => b.onclick = () => { closeDrawer(); openPetFile(b.dataset.openPet); });

    const editBtn = $('#ownerEditBtn');
    if (editBtn) editBtn.onclick = () => { ownerEditing = true; renderOwnerSheet(); };
    const cancelBtn = $('#ownerCancelBtn');
    if (cancelBtn) cancelBtn.onclick = () => { ownerEditing = false; renderOwnerSheet(); };
    const saveBtn = $('#ownerSaveBtn');
    if (saveBtn) saveBtn.onclick = saveOwnerEdits;
  }

  /* commit inline edits back to the in-memory owner object */
  function saveOwnerEdits() {
    const o = ownerById(currentOwnerId);
    if (!o) return;
    $$('#drawerBody .od-input[data-field]').forEach(el => {
      const f = el.dataset.field;
      o[f] = el.value.trim();
    });
    const g = $('#drawerBody input[name="oGenderEdit"]:checked');
    if (g) o.gender = g.value;
    ownerEditing = false;
    renderOwnerSheet();
    render();                       // refresh the directory behind the sheet
    toast('اطلاعات مالک به‌روزرسانی شد (نمونه طراحی)', 'check');
  }

  window.closeDrawer = function () {
    $('#drawerRoot').classList.remove('open');
    ownerEditing = false;
    document.body.style.overflow = '';
  };

  /* =========================================================
     PATIENT (PET) FILE — centered sheet
     Full record: summary, visits, weight/growth chart, vaccines,
     documents. Supports inline editing of core info + adding a
     new weight measurement (in-memory, design prototype).
     ========================================================= */
  let currentPetId = null;
  let petTab = 'summary';
  let petEditing = false;

  const HERO_TONE = {
    healthy:  { cls: 'ok',    label: 'وضعیت پایدار' },
    followup: { cls: 'warn',  label: 'در حال پیگیری' },
    critical: { cls: 'alert', label: 'بیمار مزمن' },
  };

  window.openPetFile = function (id) {
    const p = petById(id);
    if (!p) return;
    currentPetId = id;
    petTab = 'summary';
    petEditing = false;
    renderPetFile();
    $('#petFileRoot').classList.add('open');
    document.body.style.overflow = 'hidden';
  };
  window.closePetFile = function () {
    $('#petFileRoot').classList.remove('open');
    petEditing = false;
    document.body.style.overflow = '';
  };

  /* ---- weight helpers ---- */
  function weightSeries(p) {
    return [{
      name: 'وزن', unit: 'کیلوگرم', color: 'var(--brand-600)',
      points: (p.weights || []).map(w => ({ label: w.d, short: w.d.slice(2), value: w.w })),
    }];
  }
  function weightRows(p) {
    const ws = [...(p.weights || [])];
    return ws.map((w, i) => {
      const prev = i > 0 ? ws[i - 1].w : null;
      const d = prev == null ? null : +(w.w - prev).toFixed(2);
      let cls = 'delta-flat', txt = '—';
      if (d != null) {
        if (d > 0) { cls = 'delta-up'; txt = '▲ ' + fa(Math.abs(d)); }
        else if (d < 0) { cls = 'delta-down'; txt = '▼ ' + fa(Math.abs(d)); }
        else { cls = 'delta-flat'; txt = '۰'; }
      }
      return { d: w.d, w: w.w, cls, txt };
    }).reverse();
  }

  /* ---- tab bodies ---- */
  function petSummary(p, o) {
    const a = ageFromJalali(p.birth);
    const w = currentWeight(p);
    const dw = weightDelta(p);
    const lv = lastVisit(p);
    const nv = nextVaccine(p);
    const dwCls = dw > 0 ? 'up' : dw < 0 ? 'down' : 'flat';
    const dwTxt = dw === 0 ? 'بدون تغییر' : `${dw > 0 ? '+' : '−'}${fa(Math.abs(dw))} کیلو`;

    const alerts = [];
    (p.chronic || []).forEach(c => alerts.push({ cls: 'danger', ic: Icons.pulse || Icons.alert, t: 'بیماری مزمن', d: c }));
    (p.allergies || []).forEach(al => alerts.push({ cls: 'warn', ic: Icons.alert, t: 'حساسیت', d: al }));
    if (nv && nv.u.days < 0) alerts.push({ cls: 'warn', ic: Icons.syringe, t: 'واکسن عقب‌افتاده', d: `${nv.name} — سررسید ${fa(nv.next)}` });
    if (!alerts.length) alerts.push({ cls: 'ok', ic: Icons.check, t: 'بدون هشدار فعال', d: 'سابقه‌ای از بیماری مزمن یا حساسیت ثبت نشده است.' });

    return `
      <div class="kpi-strip">
        <div class="kpi-cell"><div class="kl">${Icons.scale} وزن فعلی</div>
          <div class="kv">${w ? fa(w) : '—'}<span class="u">کیلو</span></div>
          <div class="kd ${dwCls}">${dwTxt}</div></div>
        <div class="kpi-cell"><div class="kl">${Icons.cake || Icons.calendar} سن</div>
          <div class="kv" style="font-size:16px">${a.label}</div>
          <div class="kd flat">${p.birth ? 'تولد ' + fa(p.birth) : '—'}</div></div>
        <div class="kpi-cell"><div class="kl">${Icons.history} آخرین ویزیت</div>
          <div class="kv" style="font-size:16px">${lv ? sinceJalali(lv.date) : '—'}</div>
          <div class="kd flat">${lv ? lv.type : 'بدون سابقه'}</div></div>
        <div class="kpi-cell"><div class="kl">${Icons.syringe} واکسن بعدی</div>
          <div class="kv" style="font-size:16px">${nv ? fa(nv.next) : '—'}</div>
          <div class="kd ${nv && nv.u.days < 0 ? 'down' : 'flat'}">${nv ? nv.u.label : 'ثبت نشده'}</div></div>
      </div>

      <div class="pf-layout">
        <div>
          <div class="panel">
            <div class="panel-head"><h3>${Icons.trend} روند وزن</h3>
              <div class="end"><span class="legend-mini"><i style="background:var(--brand-600)"></i> کیلوگرم</span></div></div>
            <div class="panel-body"><div class="chart-wrap" id="pfWeightMini"></div></div>
          </div>

          <div class="panel">
            <div class="panel-head"><h3>${Icons.clipboard || Icons.file} علائم حیاتی آخرین معاینه</h3></div>
            <div class="panel-body">
              <div class="vgrid">
                <div class="vcell"><div class="vl">${Icons.thermo} دما</div><div class="vv">${fa(p.vitals.temp)}<span class="u">°C</span></div></div>
                <div class="vcell"><div class="vl">${Icons.pulse} ضربان</div><div class="vv">${fa(p.vitals.pulse)}<span class="u">bpm</span></div></div>
                <div class="vcell"><div class="vl">${Icons.lungs} تنفس</div><div class="vv">${fa(p.vitals.resp)}<span class="u">/min</span></div></div>
                <div class="vcell"><div class="vl">${Icons.scale} BCS</div><div class="vv" style="font-size:15px">${p.vitals.bcs}</div></div>
              </div>
            </div>
          </div>
        </div>

        <div>
          <div class="panel">
            <div class="panel-head"><h3>${Icons.shield} هشدارهای بالینی</h3></div>
            <div class="panel-body">
              ${alerts.map(al => `<div class="alert-row ${al.cls}"><span class="ai">${al.ic || Icons.info}</span>
                <span><span class="at">${al.t}</span><div class="ad">${al.d}</div></span></div>`).join('')}
            </div>
          </div>

          <div class="aside-card">
            <div class="ac-head">${Icons.user} <h4>مالک</h4></div>
            <div class="ac-body">
              <a class="owner-mini" href="javascript:void(0)" data-back-owner="${o.id}" style="text-decoration:none;color:inherit">
                <div class="om-av tone-${o.tone}">${initials(o)}</div>
                <div><div class="om-name">${ownerName(o)}</div><div class="om-sub">پرونده ${o.file} · ${fa(o.mobile1)}</div></div>
              </a>
              <div class="contact-actions">
                <button class="od-btn sm" data-call="${o.mobile1}">${Icons.phone}<span>تماس</span></button>
                <button class="od-btn sm" data-back-owner="${o.id}">${Icons.file}<span>پرونده مالک</span></button>
              </div>
            </div>
          </div>

          ${p.notes ? `<div class="aside-card"><div class="ac-head">${Icons.msg || Icons.clipboard} <h4>یادداشت</h4></div>
            <div class="ac-body" style="font-size:12.5px;line-height:1.9;color:var(--ink-2)">${p.notes}</div></div>` : ''}
        </div>
      </div>`;
  }

  function petInfoTab(p) {
    const ed = petEditing;
    const a = ageFromJalali(p.birth);
    const dl = (dt, dd, icon, opts = {}) => `
      <div class="dl-item ${opts.full ? 'full' : ''}">
        <div class="dt">${icon || ''} ${dt}</div>
        <div class="dd ${dd ? '' : 'empty'} ${opts.line ? 'line' : ''}">${dd || 'ثبت نشده'}</div>
      </div>`;
    const edItem = (dt, field, val, icon, opts = {}) => {
      if (!ed) return dl(dt, val, icon, opts);
      const ctrl = opts.select
        ? `<select class="od-input" data-pfield="${field}" style="max-width:100%;text-align:start">${opts.select.map(x => `<option ${x === (opts.raw ?? '') ? 'selected' : ''}>${x}</option>`).join('')}</select>`
        : `<input class="od-input" data-pfield="${field}" value="${opts.raw ?? ''}" placeholder="${opts.ph || ''}" style="max-width:100%;text-align:start"/>`;
      return `<div class="dl-item ${opts.full ? 'full' : ''}"><div class="dt">${icon || ''} ${dt}</div><div class="dd">${ctrl}</div></div>`;
    };

    return `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.paw} مشخصات حیوان</h3>
          <div class="end">${ed
            ? `<button class="mini-act" id="petInfoSave">${Icons.check} ذخیره</button><button class="mini-act" id="petInfoCancel">${Icons.x} انصراف</button>`
            : `<button class="mini-act" id="petInfoEdit">${Icons.edit} ویرایش</button>`}</div>
        </div>
        <div class="panel-body">
          <div class="dl">
            ${edItem('نام حیوان', 'name', p.name, Icons.paw, { raw: p.name })}
            ${edItem('نژاد', 'breed', p.breed, Icons.tag, { raw: p.breed })}
            ${edItem('رنگ', 'color', p.color, Icons.palette, { raw: p.color })}
            ${dl('گونه', speciesLabel(p.species), speciesIcon(p.species))}
            ${dl('جنسیت', (GENDER_MAP[p.gender] || {}).label, Icons.info)}
            ${edItem('تاریخ تولد', 'birth', p.birth ? fa(p.birth) : '', Icons.cake, { raw: p.birth, ph: '۱۴۰۲/۰۴/۲۱' })}
            ${dl('سن', a.label, Icons.clock)}
            ${edItem('محل زندگی', 'habitat', p.habitat, Icons.home2, { raw: p.habitat, select: HABITATS })}
            ${edItem('رفتار', 'behavior', p.behavior, Icons.smile, { raw: p.behavior, select: BEHAVIORS })}
            ${edItem('جیره غذایی', 'diet', p.diet, Icons.bowl, { raw: p.diet, select: DIETS })}
            ${dl('گروه خونی', p.blood, Icons.flask)}
            ${dl('وضعیت عقیم‌سازی', p.neutered ? `عقیم شده${p.neuteredDate ? ' · ' + fa(p.neuteredDate) : ''}` : 'عقیم نشده', Icons.scalpel)}
            ${dl('بیمه', p.insurance, Icons.shield, { full: true })}
          </div>
        </div>
      </div>

      ${(p.microchip && p.microchip.code) ? `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.cpu} میکروچیپ</h3></div>
        <div class="panel-body">
          <div class="chip-card">
            <div class="cc-top">
              <div class="cc-ic">${Icons.cpu}</div>
              <div><div class="cc-code">${fa(p.microchip.code)}</div><div class="cc-lbl">شناسه میکروچیپ</div></div>
              <button class="od-btn sm cc-copy" data-copy="${p.microchip.code}">${Icons.copy}<span>کپی</span></button>
            </div>
            <div class="cc-bar">${barcodeSvg(p.microchip.code)}<div class="digits">${fa(p.microchip.code)}</div></div>
            <div class="cc-meta">
              <div><div class="k">تاریخ کاشت</div><div class="v">${fa(p.microchip.date)}</div></div>
              <div><div class="k">سازنده</div><div class="v">${p.microchip.vendor || '—'}</div></div>
              <div><div class="k">محل کاشت</div><div class="v">${p.microchip.location || '—'}</div></div>
            </div>
          </div>
        </div>
      </div>` : ''}

      ${(p.surgeries && p.surgeries.length) ? `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.scalpel} سابقه جراحی</h3></div>
        <div class="panel-body">
          ${p.surgeries.map(s => `<div class="alert-row info"><span class="ai">${Icons.scalpel}</span>
            <span><span class="at">${s.title} · ${fa(s.date)}</span><div class="ad">${s.surgeon}${s.note && s.note !== '—' ? ' — ' + s.note : ''}</div></span></div>`).join('')}
        </div>
      </div>` : ''}`;
  }

  function petVisitsTab(p) {
    if (!p.visits || !p.visits.length)
      return `<div class="panel"><div class="panel-body"><div class="od-empty">ویزیتی برای این بیمار ثبت نشده است</div></div></div>`;
    const dotTone = { 'واکسیناسیون': 'violet', 'جراحی': 'danger', 'درمانی': 'amber', 'پیگیری': 'info' };
    return `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.history} تاریخچه ویزیت‌ها <span class="pf-tab-n"></span></h3>
          <div class="end"><button class="mini-act" data-add-visit="${p.id}">${Icons.plus} ثبت ویزیت</button></div></div>
        <div class="panel-body">
          <div class="tl">
            ${p.visits.map((v, i) => `
              <div class="tl-item">
                <span class="tl-dot ${dotTone[v.type] || ''}">${Icons.check}</span>
                <div class="visit ${i === 0 ? 'open' : ''}" data-visit>
                  <div class="visit-head" data-visit-toggle>
                    <div><div class="vt">${v.type}</div><div class="vr">${v.reason}</div></div>
                    <div class="vmeta">
                      <span class="vdate">${fa(v.date)}</span>
                      <span class="vdate">${v.vet}</span>
                      <span class="chev">${Icons.chevronDown}</span>
                    </div>
                  </div>
                  <div class="visit-body"><div class="inner"><div class="pad">
                    <div class="vfield"><div class="k">${Icons.stetho} معاینه</div><div class="v">${v.exam}</div></div>
                    <div class="vfield"><div class="k">${Icons.microscope} تشخیص</div><div class="v">${v.dx}</div></div>
                    <div class="vfield"><div class="k">${Icons.clipboard} دستور دارویی</div>
                      <ul class="rx-list">${(v.rx || []).map(r => `<li>${r}</li>`).join('')}</ul></div>
                    <div class="vstats">
                      <span class="vstat">وزن <b>${fa(v.weight)} کیلو</b></span>
                      <span class="vstat">دما <b>${fa(v.temp)}°</b></span>
                      <span class="vstat">هزینه <b>${faNum(v.cost)} ت</b></span>
                    </div>
                  </div></div></div>
                </div>
              </div>`).join('')}
          </div>
        </div>
      </div>`;
  }

  function petWeightTab(p) {
    const rows = weightRows(p);
    return `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.trend} نمودار تغییرات وزن</h3>
          <div class="end"><span class="legend-mini"><i style="background:var(--brand-600)"></i> کیلوگرم</span></div></div>
        <div class="panel-body"><div class="chart-wrap" id="pfWeightChart"></div></div>
      </div>

      <div class="panel">
        <div class="panel-head"><h3>${Icons.scale} ثبت وزن جدید</h3></div>
        <div class="panel-body">
          <div class="wt-form">
            <div class="field"><label>تاریخ (شمسی)</label><input class="input" id="wtDate" placeholder="${fa(TODAY_J)}" inputmode="numeric"/></div>
            <div class="field"><label>وزن (کیلوگرم)</label><input class="input" id="wtVal" placeholder="مثال: ۴٫۵" inputmode="decimal"/></div>
            <button class="btn btn-primary" id="wtAdd">${Icons.plus} افزودن</button>
          </div>
        </div>
      </div>

      <div class="panel">
        <div class="panel-head"><h3>${Icons.rows || Icons.list} سابقه اندازه‌گیری وزن</h3>
          <div class="end"><span class="tag-soft">${fa((p.weights || []).length)} رکورد</span></div></div>
        <div class="panel-body flush">
          <table class="mtable">
            <thead><tr><th>تاریخ</th><th>وزن (کیلو)</th><th>تغییر نسبت به قبل</th></tr></thead>
            <tbody>
              ${rows.map(r => `<tr><td>${fa(r.d)}</td><td><b>${fa(r.w)}</b></td><td class="${r.cls}">${r.txt}</td></tr>`).join('')}
            </tbody>
          </table>
        </div>
      </div>`;
  }

  function petVaxTab(p) {
    if (!p.vaccines || !p.vaccines.length)
      return `<div class="panel"><div class="panel-body"><div class="od-empty">واکسنی برای این بیمار ثبت نشده است</div></div></div>`;
    return `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.syringe} سابقه واکسیناسیون</h3>
          <div class="end"><button class="mini-act" data-add-vax="${p.id}">${Icons.plus} ثبت واکسن</button></div></div>
        <div class="panel-body flush">
          <div class="vax-list">
            ${p.vaccines.map(v => {
              const u = untilJalali(v.next);
              const cls = u.days < 0 ? 'over' : (u.days < 30 ? 'due' : '');
              const badge = u.days < 0 ? '<span class="pill pill-danger">عقب‌افتاده</span>'
                : u.days < 30 ? '<span class="pill pill-warn">نزدیک</span>' : '<span class="pill pill-ok">به‌روز</span>';
              return `<div class="vax-item">
                <span class="vax-ic ${cls}">${Icons.syringe}</span>
                <div><div class="vax-name">${v.name}</div>
                  <div class="vax-sub"><span>تزریق ${fa(v.date)}</span><span>${v.vet}</span><span>سری ${v.lot}</span><span>${v.site}</span></div></div>
                <div class="vax-right"><div class="vax-next">یادآور بعدی: ${fa(v.next)}</div><div class="vax-badge">${badge}</div></div>
              </div>`;
            }).join('')}
          </div>
        </div>
      </div>`;
  }

  function petDocsTab(p) {
    return `
      ${(p.labs && p.labs.length) ? `
      <div class="panel">
        <div class="panel-head"><h3>${Icons.flask} آزمایش‌ها و تصویربرداری</h3></div>
        <div class="panel-body">
          ${p.labs.map(l => {
            const map = { normal: 'ok', warn: 'warn', danger: 'danger', pending: 'info' };
            return `<div class="alert-row ${map[l.status] || 'info'}"><span class="ai">${Icons.microscope}</span>
              <span><span class="at">${l.name} · ${fa(l.date)}</span><div class="ad">${l.result}</div></span></div>`;
          }).join('')}
        </div>
      </div>` : ''}

      <div class="panel">
        <div class="panel-head"><h3>${Icons.folder} مدارک و فایل‌ها</h3></div>
        <div class="panel-body flush">
          ${(p.docs && p.docs.length) ? p.docs.map(d => `
            <div class="doc-row"><span class="di">${Icons.file}</span>
              <div><div class="dn">${d.name}</div><div class="ds">${d.size} · ${fa(d.date)}</div></div>
              <button class="od-btn sm" onclick="toast('دانلود فایل (نمونه طراحی)','download')">${Icons.download}</button>
            </div>`).join('') : '<div class="od-empty">مدرکی بارگذاری نشده است</div>'}
        </div>
        <div class="panel-body">
          <div class="dropzone" onclick="toast('بارگذاری فایل (نمونه طراحی)','upload')">
            ${Icons.upload}<div class="t">فایلی را اینجا رها کنید</div><div class="s">تصویر، PDF یا نتیجه آزمایش</div>
          </div>
        </div>
      </div>`;
  }

  const PET_TABS = [
    { k: 'summary', label: 'خلاصه',        icon: 'dashboard' },
    { k: 'info',    label: 'مشخصات',       icon: 'paw' },
    { k: 'visits',  label: 'ویزیت‌ها',     icon: 'history' },
    { k: 'weight',  label: 'رشد و وزن',    icon: 'trend' },
    { k: 'vax',     label: 'واکسیناسیون',  icon: 'syringe' },
    { k: 'docs',    label: 'مدارک',        icon: 'folder' },
  ];

  function renderPetFile() {
    const p = petById(currentPetId);
    if (!p) return;
    const o = ownerById(p.ownerId);
    const a = ageFromJalali(p.birth);
    const ht = HERO_TONE[p.status] || HERO_TONE.healthy;
    const w = currentWeight(p);
    const nv = nextVaccine(p);
    const counts = { visits: (p.visits || []).length, vax: (p.vaccines || []).length, weight: (p.weights || []).length };

    const body = $('#petFileBody');
    body.innerHTML = `
      <div class="pf-hero">
        <button class="pf-close" onclick="closePetFile()" aria-label="بستن">${Icons.x}</button>
        <div class="pf-hero-in">
          <div class="pf-av tone-${p.tone}">${speciesIcon(p.species)}
            <span class="badge-sex">${(GENDER_MAP[p.gender] || {}).icon || '?'}</span></div>
          <div class="pf-id">
            <h1>${p.name}</h1>
            <div class="code">کد بیمار ${p.code} · ${speciesLabel(p.species)} · ${p.breed}</div>
            <div class="pf-chips">
              <span class="hchip ${ht.cls}">${Icons.pulse || Icons.info} ${ht.label}</span>
              <span class="hchip">${Icons.cake || Icons.calendar} ${a.label}</span>
              <span class="hchip">${Icons.scale} ${w ? fa(w) + ' کیلو' : '—'}</span>
              ${nv ? `<span class="hchip ${nv.u.days < 0 ? 'alert' : ''}">${Icons.syringe} واکسن ${nv.u.label}</span>` : ''}
            </div>
          </div>
          <div class="pf-actions">
            <button class="btn-glass" data-add-visit="${p.id}">${Icons.plus} ثبت ویزیت</button>
            <button class="btn-glass solid" onclick="toast('چاپ پرونده (نمونه طراحی)','printer')">${Icons.printer} چاپ</button>
          </div>
        </div>
        <div class="pf-ownerbar">
          <span class="oa tone-${o.tone}">${initials(o)}</span>
          <b>${ownerName(o)}</b><span class="dotsep">·</span>
          <span>پرونده ${o.file}</span><span class="dotsep">·</span>
          <span>${fa(o.mobile1)}</span>
          <span class="sib"><span class="lbl">مشاهده پرونده مالک</span>
            <a href="javascript:void(0)" data-back-owner="${o.id}"><span class="m tone-${o.tone}">${initials(o)}</span>${Icons.chevronLeft}</a></span>
        </div>
      </div>

      <div class="pf-tabs" id="pfTabs">
        ${PET_TABS.map(t => `<button class="pf-tab ${t.k === petTab ? 'on' : ''}" data-tab="${t.k}">
          ${Icons[t.icon] || ''}<span>${t.label}</span>${counts[t.k] != null ? `<span class="n">${fa(counts[t.k])}</span>` : ''}</button>`).join('')}
      </div>

      <div id="pfPane">
        ${petTab === 'summary' ? petSummary(p, o)
          : petTab === 'info'  ? petInfoTab(p)
          : petTab === 'visits'? petVisitsTab(p)
          : petTab === 'weight'? petWeightTab(p)
          : petTab === 'vax'   ? petVaxTab(p)
          : petDocsTab(p)}
      </div>`;

    bindPetFile(p, o);
    /* draw charts after layout so clientWidth is correct */
    if (petTab === 'summary') { const h = $('#pfWeightMini'); if (h) lineChart(h, weightSeries(p), { height: 180 }); }
    if (petTab === 'weight')  { const h = $('#pfWeightChart'); if (h) lineChart(h, weightSeries(p), { height: 240 }); }
  }

  function bindPetFile(p, o) {
    $$('#pfTabs .pf-tab').forEach(b => b.onclick = () => { petTab = b.dataset.tab; petEditing = false; renderPetFile(); });

    /* accordion visits */
    $$('#pfPane [data-visit-toggle]').forEach(h => h.onclick = () => h.closest('.visit').classList.toggle('open'));

    /* owner links -> reopen owner sheet */
    $$('#petFileBody [data-back-owner]').forEach(b => b.onclick = () => { closePetFile(); openOwnerDrawer(b.dataset.backOwner); });
    $$('#petFileBody [data-call]').forEach(b => b.onclick = () => toast(`در حال برقراری تماس با ${fa(b.dataset.call)}`, 'phone'));
    $$('#petFileBody [data-copy]').forEach(b => b.onclick = () => copyText(b.dataset.copy, 'شناسه میکروچیپ کپی شد'));

    /* design-only add actions */
    $$('#petFileBody [data-add-visit]').forEach(b => b.onclick = () => toast('فرم ثبت ویزیت جدید (نمونه طراحی)', 'plus'));
    $$('#pfPane [data-add-vax]').forEach(b => b.onclick = () => toast('فرم ثبت واکسن جدید (نمونه طراحی)', 'plus'));

    /* info edit */
    const iEdit = $('#petInfoEdit'); if (iEdit) iEdit.onclick = () => { petEditing = true; renderPetFile(); };
    const iCancel = $('#petInfoCancel'); if (iCancel) iCancel.onclick = () => { petEditing = false; renderPetFile(); };
    const iSave = $('#petInfoSave'); if (iSave) iSave.onclick = () => savePetInfo(p);

    /* add weight */
    const wtAdd = $('#wtAdd');
    if (wtAdd) wtAdd.onclick = () => addWeight(p);
  }

  /* convert Persian digits + optional separators to a JS number */
  function parseFaNum(s) {
    const en = String(s || '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٫،]/g, '.').replace(/[^\d.]/g, '');
    const n = parseFloat(en);
    return isNaN(n) ? null : n;
  }
  function normalizeJalaliInput(s) {
    return String(s || '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[^\d/]/g, '').trim();
  }

  function addWeight(p) {
    const val = parseFaNum($('#wtVal').value);
    let date = normalizeJalaliInput($('#wtDate').value) || TODAY_J;
    if (val == null || val <= 0) { toast('لطفاً وزن معتبر وارد کنید', 'info'); return; }
    if (!/^\d{4}\/\d{1,2}\/\d{1,2}$/.test(date)) { toast('تاریخ را به شکل ۱۴۰۴/۰۵/۱۱ وارد کنید', 'info'); return; }
    p.weights = p.weights || [];
    p.weights.push({ d: date, w: +val });
    /* keep chronological order so the chart & deltas stay correct */
    p.weights.sort((x, y) => (jalaliToDate(x.d) || 0) - (jalaliToDate(y.d) || 0));
    renderPetFile();
    render();                       // refresh directory cards behind the sheet
    toast('وزن جدید ثبت شد (نمونه طراحی)', 'check');
  }

  function savePetInfo(p) {
    $$('#pfPane [data-pfield]').forEach(el => {
      const f = el.dataset.pfield;
      let v = el.value.trim();
      if (f === 'birth') v = normalizeJalaliInput(v);
      p[f] = v;
    });
    petEditing = false;
    renderPetFile();
    render();
    toast('مشخصات بیمار به‌روزرسانی شد (نمونه طراحی)', 'check');
  }

  /* ---------------- new owner / existing owner wizard ---------------- */
  let wizStep = 1;
  let wizMode = 'new';           // 'new' | 'existing'
  let wizSelectedOwnerId = null;

  window.openOwnerModal = function () {
    wizStep = 1;
    setWizMode('new');
    $('#wizOwnerSearch').value = '';
    renderWizOwnerList('');
    $('#ownerModal').style.display = 'grid';
    paintWiz();
    document.body.style.overflow = 'hidden';
  };
  window.closeOwnerModal = function () {
    $('#ownerModal').style.display = 'none';
    document.body.style.overflow = '';
  };

  function setWizMode(mode) {
    wizMode = mode;
    if (mode === 'new') wizSelectedOwnerId = null;
    $$('#ownerModeSeg button').forEach(b => b.classList.toggle('on', b.dataset.mode === mode));
    $('#wizNewOwnerFields').style.display = mode === 'new' ? '' : 'none';
    $('#wizExistingOwnerPick').style.display = mode === 'existing' ? '' : 'none';
    $('#wizModeHint').textContent = mode === 'new'
      ? 'اگر این مالک برای اولین‌بار مراجعه می‌کند، اطلاعات هویتی و تماس او را کامل کنید.'
      : 'مالک موردنظر را از فهرست زیر جستجو و انتخاب کنید تا حیوان جدید به همان پرونده اضافه شود.';
  }

  function renderWizOwnerList(q) {
    const host = $('#wizOwnerList');
    if (!host) return;
    const nq = norm(q || '');
    const list = OWNERS.filter(o => !nq || norm([ownerName(o), o.file, o.mobile1, o.nationalId].join(' ')).includes(nq));
    host.innerHTML = list.length ? list.map(o => `
      <a class="sib-row selectable ${wizSelectedOwnerId === o.id ? 'picked' : ''}" href="javascript:void(0)" data-pick-owner="${o.id}">
        <span class="sv tone-${o.tone}">${initials(o)}</span>
        <span><span class="sn">${ownerName(o)}</span><div class="ss">پرونده ${o.file} · ${fa(o.mobile1)} · ${fa(petsOfOwner(o.id).length)} حیوان</div></span>
        <span class="pick-check">${Icons.check}</span>
      </a>`).join('') : `<div style="padding:22px;text-align:center;color:var(--muted);font-size:12px">مالکی با این مشخصات پیدا نشد</div>`;
    $$('#wizOwnerList [data-pick-owner]').forEach(el => el.onclick = () => {
      wizSelectedOwnerId = el.dataset.pickOwner;
      renderWizOwnerList($('#wizOwnerSearch').value);
    });
  }

  window.wizGo = function (n) {
    if (n < 1) return;
    if (n > wizStep && wizStep === 1 && wizMode === 'existing' && !wizSelectedOwnerId) {
      toast('لطفاً ابتدا یک مالک را از فهرست انتخاب کنید', 'info');
      return;
    }
    if (n > 2) {
      const owner = wizMode === 'existing' ? ownerById(wizSelectedOwnerId) : null;
      closeOwnerModal();
      toast(wizMode === 'existing'
        ? `حیوان جدید به پرونده ${owner ? ownerName(owner) : 'مالک'} اضافه شد (نمونه طراحی)`
        : 'پرونده مالک و حیوان ثبت شد (نمونه طراحی)');
      return;
    }
    wizStep = Math.max(1, Math.min(2, n));
    paintWiz();
  };

  function paintWiz() {
    $$('#wizSteps .wiz-step').forEach((el, i) => {
      el.classList.toggle('on', i + 1 === wizStep);
      el.classList.toggle('done', i + 1 < wizStep);
    });
    $$('.wiz-pane').forEach((el, i) => el.style.display = (i + 1 === wizStep) ? '' : 'none');

    const preview = $('#wizOwnerPreview');
    if (wizStep === 2 && wizMode === 'existing') {
      const o = ownerById(wizSelectedOwnerId);
      preview.style.display = o ? '' : 'none';
      if (o) preview.innerHTML = `
        <div class="owner-mini">
          <div class="om-av tone-${o.tone}">${initials(o)}</div>
          <div><div class="om-name">${ownerName(o)}</div><div class="om-sub">پرونده ${o.file} · ${fa(o.mobile1)}</div></div>
          <span class="tag-soft" style="margin-inline-start:auto">${Icons.paw}${fa(petsOfOwner(o.id).length)} حیوان فعلی</span>
        </div>`;
    } else {
      preview.style.display = 'none';
    }

    const back = $('#wizBack');
    back.style.display = wizStep === 1 ? 'none' : '';
    back.onclick = () => wizGo(wizStep - 1);

    $('#wizNext').innerHTML = wizStep === 1
      ? `ادامه — اطلاعات حیوان ${Icons.chevronLeft}`
      : `${Icons.check} ثبت پرونده`;
    $('#wizNext').onclick = () => wizGo(wizStep + 1);
  }

  /* ---------------- add pet modal ---------------- */
  window.openPetModal = function (ownerId) {
    const o = ownerById(ownerId);
    $('#petModalOwner').innerHTML = o ? `
      <div class="owner-mini">
        <div class="om-av tone-${o.tone}">${initials(o)}</div>
        <div><div class="om-name">${ownerName(o)}</div><div class="om-sub">پرونده ${o.file} · ${fa(o.mobile1)}</div></div>
        <span class="tag-soft" style="margin-inline-start:auto">${Icons.paw}${fa(petsOfOwner(o.id).length)} حیوان فعلی</span>
      </div>` : '';
    $('#petModal').style.display = 'grid';
    document.body.style.overflow = 'hidden';
  };
  window.closePetModal = function () {
    $('#petModal').style.display = 'none';
    document.body.style.overflow = '';
  };

  /* species tiles inside forms */
  window.bindTypePicker = function (rootSel, selectSel) {
    const root = $(rootSel);
    if (!root) return;
    root.innerHTML = Object.entries(SPECIES).map(([k, v], i) => `
      <div class="type-tile ${i === 0 ? 'active' : ''}" data-sp="${k}">
        <div class="icon-wrap">${Icons[v.icon]}</div><span>${v.label}</span>
      </div>`).join('');
    const sel = selectSel ? $(selectSel) : null;
    const fill = (k) => { if (sel) sel.innerHTML = SPECIES[k].breeds.map(b => `<option>${b}</option>`).join(''); };
    fill('cat');
    root.querySelectorAll('.type-tile').forEach(t => t.onclick = () => {
      root.querySelectorAll('.type-tile').forEach(x => x.classList.remove('active'));
      t.classList.add('active'); fill(t.dataset.sp);
    });
  };

  /* fill selects from taxonomy */
  window.fillOptions = function (sel, arr, placeholder) {
    const el = $(sel); if (!el) return;
    el.innerHTML = (placeholder ? `<option value="">${placeholder}</option>` : '') + arr.map(a => `<option>${a}</option>`).join('');
  };

  /* ---------------- boot ---------------- */
  window.initPatientsPage = function () {
    $('#ptSearch').addEventListener('input', (e) => { S.q = e.target.value; render(); });
    $('#ptSearch').parentElement.querySelector('.clear').onclick = () => { S.q = ''; $('#ptSearch').value = ''; render(); };
    $$('#viewSeg button').forEach(b => b.onclick = () => { S.view = b.dataset.view; render(); });
    $$('#layoutSeg button').forEach(b => b.onclick = () => { S.layout = b.dataset.layout; render(); });
    $('#sortSelect').onchange = (e) => { S.sort = e.target.value; render(); };
    $('#vaxSelect').onchange = (e) => { S.vax = e.target.value; render(); };
    $('#drawerBd').onclick = closeDrawer;
    $('#petFileBd').onclick = closePetFile;

    $$('#ownerModeSeg button').forEach(b => b.onclick = () => setWizMode(b.dataset.mode));
    $('#wizOwnerSearch').addEventListener('input', (e) => renderWizOwnerList(e.target.value));

    bindTypePicker('#wizTypes', '#wizBreed');
    bindTypePicker('#petTypes', '#petBreed');
    fillOptions('#wizReferral', REFERRALS);
    fillOptions('#wizHabitat', HABITATS);
    fillOptions('#wizBehavior', BEHAVIORS);
    fillOptions('#wizDiet', DIETS);
    fillOptions('#petHabitat', HABITATS);
    fillOptions('#petBehavior', BEHAVIORS);
    fillOptions('#petDiet', DIETS);

    $$('.switch-row').forEach(r => r.onclick = () => r.classList.toggle('on'));

    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { closeDrawer(); closePetFile(); closeOwnerModal(); closePetModal(); }
    });

    render();

    /* deep links:
       patients.html#owner-ow-1  → open that owner file
       patients.html#pet-pt-1    → open that patient file */
    const dOwner = (location.hash || '').match(/^#owner-(.+)$/);
    if (dOwner && ownerById(dOwner[1])) openOwnerDrawer(dOwner[1]);
    const dPet = (location.hash || '').match(/^#pet-(.+)$/);
    if (dPet && petById(dPet[1])) openPetFile(dPet[1]);
  };
})();
