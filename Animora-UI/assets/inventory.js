/* ==========================================================================
   Inventory Hub — page controller
   Depends on: window.Icons, window.INV, window.toFa
   ========================================================================== */
(function () {
  const $  = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

  // Current UI state
  const state = {
    tab: 'products',      // products | settings
    search: '',
    catFilter: 'all',     // 'all' | cat id
    stockFilter: 'all',   // 'all' | 'low' | 'ok'
    expanded: {},         // { [productId]: true } — inline variant sub-rows
    modalTab: 'info',     // info | variants | history — active tab in product modal
    historyFilter: 'all', // all | in | out | adjust — history tab filter
  };

  // -------- Utility ---------------------------------------------------------
  function toast(msg) {
    let t = $('#inv-toast');
    if (!t) {
      t = document.createElement('div');
      t.id = 'inv-toast'; t.className = 'inv-toast';
      document.body.appendChild(t);
    }
    t.innerHTML = `${Icons.check || ''}<span>${msg}</span>`;
    t.classList.add('show');
    clearTimeout(t._to);
    t._to = setTimeout(() => t.classList.remove('show'), 2400);
  }
  const fa = window.toFa;
  const fmt = INV.formatIRR;
  const fmtFull = INV.formatFull;

  // -------- Top-level render -----------------------------------------------
  function render() {
    const view = $('#inv-view');
    if (!view) return;
    const { controls, body } = renderViewParts();
    // Unified toolbar: tier-1 sub-nav + tier-2 per-tab controls, all inside
    // one .pagebar card — matching every other workspace page.
    view.innerHTML = `
      <div class="pagebar">
        <div class="pagebar-row">${renderSubnav()}</div>
        ${controls ? `<div class="pagebar-row tier2 inv-controls">${controls}</div>` : ''}
      </div>
      ${body}`;
    bindSubnav();
    bindViewBody();
    bindHeaderNew();
    convertFa(view);
  }

  // Header "new product" button (lives outside #inv-view, next to the title,
  // top-left of the page). Only relevant on the products tab.
  function bindHeaderNew() {
    const btn = $('#inv-header-new');
    if (!btn) return;
    // Inject the plus icon once (static HTML can't use template literals).
    if (!btn.querySelector('svg')) btn.insertAdjacentHTML('afterbegin', Icons.plus || '');
    btn.style.display = state.tab === 'products' ? '' : 'none';
    if (!btn._bound) {
      btn._bound = true;
      btn.addEventListener('click', () => {
        if (state.tab !== 'products') { state.tab = 'products'; render(); }
        openNewProductModal();
      });
    }
  }

  function convertFa(root) {
    root.querySelectorAll('.num-fa').forEach(el => {
      el.textContent = fa(el.textContent);
    });
  }

  // -------- Sub-nav pills --------------------------------------------------
  function renderSubnav() {
    const tabs = [
      { key: 'products',   label: 'کالاها',        icon: 'box',   count: INV.PRODUCTS.length },
      { key: 'settings',   label: 'تنظیمات کالاها', icon: 'tools', count: INV.CATS.length },
    ];
    return `
      <div class="pb-seg inv-subnav" role="tablist">
        ${tabs.map(t => `
          <button class="${state.tab === t.key ? 'on' : ''}" data-tab="${t.key}" role="tab" aria-selected="${state.tab === t.key}">
            ${Icons[t.icon] || ''}
            <span>${t.label}</span>
            <span class="seg-n num-fa">${t.count}</span>
          </button>
        `).join('')}
      </div>
    `;
  }
  function bindSubnav() {
    $$('.inv-subnav button[data-tab]').forEach(el => el.addEventListener('click', () => {
      state.tab = el.dataset.tab;
      state.search = ''; state.catFilter = 'all'; state.stockFilter = 'all';
      render();
    }));
  }

  // -------- Body dispatch --------------------------------------------------
  // Each view returns { controls, body }: `controls` is the tier-2 toolbar
  // content (search / actions / filter chips), `body` is everything below.
  function renderViewParts() {
    switch (state.tab) {
      case 'products': return renderProducts();
      case 'settings': return renderSettings();
    }
    return { controls: '', body: '' };
  }
  function bindViewBody() {
    switch (state.tab) {
      case 'products': bindProducts(); break;
      case 'settings': bindSettings(); break;
    }
  }

  // ==== Products view ======================================================
  function renderProducts() {
    const q = state.search.trim().toLowerCase();
    let items = INV.PRODUCTS.slice();
    if (state.catFilter !== 'all') items = items.filter(p => p.cat === state.catFilter);
    if (state.stockFilter === 'low') items = items.filter(p => INV.stockStatus(p) !== 'ok');
    if (state.stockFilter === 'ok')  items = items.filter(p => INV.stockStatus(p) === 'ok');
    if (q) {
      items = items.filter(p =>
        p.name.toLowerCase().includes(q) ||
        p.code.toLowerCase().includes(q) ||
        p.variants.some(v => v.sku.toLowerCase().includes(q))
      );
    }

    const controls = `
      <div class="pb-chips">
        <button class="pb-chip ${state.catFilter === 'all' ? 'on' : ''}" data-cat="all">همه دسته‌ها</button>
        ${INV.CATS.map(c => `
          <button class="pb-chip tone-${c.tone} ${state.catFilter === c.id ? 'on' : ''}" data-cat="${c.id}">
            <span class="dot"></span>${c.label}
          </button>
        `).join('')}
        <span class="pb-chip-sep"></span>
        <button class="pb-chip ${state.stockFilter === 'all' ? 'on' : ''}" data-stock="all">همه وضعیت‌ها</button>
        <button class="pb-chip ${state.stockFilter === 'low' ? 'on' : ''}" data-stock="low">کم‌موجود</button>
        <button class="pb-chip ${state.stockFilter === 'ok'  ? 'on' : ''}" data-stock="ok">در دسترس</button>
      </div>
      <div class="pb-search">
        <span class="ic">${Icons.search}</span>
        <input id="inv-search" placeholder="جستجو در نام، کد یا بارکد..." value="${state.search}"/>
      </div>`;

    const body = `
      <div class="inv-card">
        ${items.length === 0 ? emptyState('کالایی پیدا نشد', 'با تغییر فیلترها یا افزودن کالای جدید امتحان کنید.') : `
        <table class="inv-table">
          <thead>
            <tr>
              <th style="width:34px"></th>
              <th style="width:32%">کالا</th>
              <th>کد کالا</th>
              <th>دسته</th>
              <th>تنوع</th>
              <th>موجودی کل</th>
              <th>محدوده قیمت</th>
              <th>وضعیت</th>
              <th style="width:120px"></th>
            </tr>
          </thead>
          <tbody>
            ${items.map(p => renderProductRow(p)).join('')}
          </tbody>
        </table>`}
      </div>`;

    return { controls, body };
  }

  function renderProductRow(p) {
    const cat = INV.catOf(p.cat);
    const stock = INV.totalStock(p);
    const stockPeak = Math.max(stock, p.variants.reduce((s,v) => s + v.reorder * 3, 0));
    const stockPct = Math.min(100, (stock / (stockPeak || 1)) * 100);
    const status = INV.stockStatus(p);
    const { min, max } = INV.minMaxPrice(p);
    const priceClass = (status === 'low' || status === 'out') ? (status === 'out' ? 'low' : 'warn') : '';
    const hasVariants = p.variants.length > 1;
    const isOpen = !!state.expanded[p.id];

    const mainRow = `
      <tr data-product="${p.id}" class="prow ${isOpen ? 'expanded' : ''}">
        <td class="expand-cell">
          ${hasVariants ? `
            <button class="expand-btn ${isOpen ? 'open' : ''}" data-action="toggle-expand" title="${isOpen ? 'بستن تنوع‌ها' : 'مشاهده تنوع‌ها'}" aria-expanded="${isOpen}">
              ${Icons.chevronDown}
            </button>` : ''}
        </td>
        <td>
          <div class="pcell">
            <div class="pthumb ${p.tone}">${p.icon}</div>
            <div class="meta">
              <div class="name">${p.name}</div>
              <div class="sub num-fa">${p.brand}</div>
            </div>
          </div>
        </td>
        <td><span class="code-chip mono num-fa">${p.code}</span></td>
        <td><span class="cat-tag tone-${cat.tone}">${cat.label}</span></td>
        <td>
          ${hasVariants ? `
            <button class="variant-pill as-btn" data-action="toggle-expand" title="مشاهده تنوع‌ها">
              <span class="stack"><i></i><i></i><i></i></span>
              <span class="num-fa">${p.variants.length}</span> نوع
            </button>` : `<span class="muted-txt">تک واحد</span>`}
        </td>
        <td class="stockcell">
          <div class="num ${priceClass} num-fa">${fmtFull(stock)} عدد</div>
          <div class="bar"><i class="${priceClass}" style="width:${stockPct}%"></i></div>
        </td>
        <td class="price-range num-fa">
          ${min === max ? `${fmtFull(min)}` : `${fmtFull(min)}<span class="sep">–</span>${fmtFull(max)}`}
          <span class="cur">تومان</span>
        </td>
        <td>
          ${status === 'ok'   ? '<span class="status-dot ok">در دسترس</span>' :
            status === 'low'  ? '<span class="status-dot warn">هشدار</span>' :
                                '<span class="status-dot low">اتمام</span>'}
        </td>
        <td>
          <div class="row-actions">
            <button class="btn btn-outline btn-sm" data-action="adjust-stock" title="تغییر موجودی">${Icons.refresh}<span>موجودی</span></button>
            <button class="icon-btn edit-ic" data-action="edit-product" title="مشاهده و ویرایش">${Icons.edit}</button>
          </div>
        </td>
      </tr>`;

    if (!hasVariants || !isOpen) return mainRow;

    // Inline sub-rows: one per variant, showing its own SKU / stock / price.
    const subRows = `
      <tr class="subrow-wrap">
        <td colspan="9" class="subrow-cell">
          <div class="subrow-panel">
            <div class="subrow-head">
              <span>${p.attribute ? p.attribute.name : 'تنوع'}</span>
              <span>کد / بارکد</span>
              <span>موجودی</span>
              <span>قیمت خرید</span>
              <span>قیمت فروش</span>
              <span>انقضا</span>
              <span></span>
            </div>
            ${p.variants.map(v => {
              const low = v.stock <= v.reorder;
              return `
              <div class="subrow ${low ? 'low' : ''}">
                <span class="v-name">${v.label}</span>
                <span class="mono num-fa">${v.sku}</span>
                <span class="num-fa v-stock ${low ? 'low' : ''}">${fmtFull(v.stock)} عدد${low ? ' <em>کم‌موجود</em>' : ''}</span>
                <span class="num-fa">${fmtFull(v.cost)} ت</span>
                <span class="num-fa strong">${fmtFull(v.price)} ت</span>
                <span class="num-fa muted-txt">${v.expiry || '—'}</span>
                <span class="v-act"><button class="icon-btn" data-action="edit-variant" data-vid="${v.id}" title="ویرایش این تنوع">${Icons.edit}</button></span>
              </div>`;
            }).join('')}
          </div>
        </td>
      </tr>`;

    return mainRow + subRows;
  }

  function bindProducts() {
    const sr = $('#inv-search');
    if (sr) sr.addEventListener('input', e => { state.search = e.target.value; render(); });

    $$('.inv-controls [data-cat]').forEach(el => el.addEventListener('click', () => {
      state.catFilter = el.dataset.cat; render();
    }));
    $$('.inv-controls [data-stock]').forEach(el => el.addEventListener('click', () => {
      state.stockFilter = el.dataset.stock; render();
    }));

    $$('.inv-table tbody tr[data-product]').forEach(tr => {
      const pid = tr.dataset.product;
      // Row click opens the centered detail/edit modal — unless the click
      // landed on an interactive control (actions, expand toggle, variant pill).
      tr.addEventListener('click', (e) => {
        if (e.target.closest('.row-actions')) return;
        if (e.target.closest('[data-action="toggle-expand"]')) return;
        openProductModal(pid, 'info');
      });
      tr.querySelectorAll('[data-action="toggle-expand"]').forEach(btn =>
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          state.expanded[pid] = !state.expanded[pid];
          render();
        }));
      tr.querySelector('[data-action="edit-product"]')?.addEventListener('click', (e) => {
        e.stopPropagation();
        openProductModal(pid, 'info');
      });
      tr.querySelector('[data-action="adjust-stock"]')?.addEventListener('click', (e) => {
        e.stopPropagation();
        openStockAdjustModal(pid);
      });
    });

    // Inline variant "edit" buttons jump straight into the variants tab.
    $$('.inv-table [data-action="edit-variant"]').forEach(btn =>
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const tr = btn.closest('.subrow-wrap').previousElementSibling;
        openProductModal(tr.dataset.product, 'variants');
      }));

    // "کالای جدید" now lives in the page header (see bindHeaderNew).
  }

  // ==== Stock adjust modal (per product) ===================================
  function findVariant(pid, vid) {
    const p = INV.PRODUCTS.find(x => x.id === pid); if (!p) return null;
    return p.variants.find(x => x.id === vid) || null;
  }

  // ==== Settings view (product catalog settings) ===========================
  function renderSettings() {
    const controls = `
      <span class="pb-count num-fa">${INV.CATS.length} دسته فعال · تنظیمات دسته‌بندی و برچسب کالاها را اینجا مدیریت کنید.</span>
      <div class="pb-actions">
        <button class="btn btn-primary" data-action="new-cat">${Icons.plus}<span>دسته جدید</span></button>
      </div>`;

    const toneOptions = ['teal','peach','lilac','amber','blue','green'];
    const body = `
      <div class="settings-wrap">
        <section class="settings-card">
          <div class="settings-hd">
            <div>
              <h3>دسته‌بندی کالاها</h3>
              <p>نام، رنگ و نماد هر دسته را ویرایش کنید. تعداد کالاهای هر دسته نمایش داده می‌شود.</p>
            </div>
          </div>
          <div class="cat-settings-list">
            ${INV.CATS.map(c => {
              const count = INV.PRODUCTS.filter(p => p.cat === c.id).length;
              return `
              <div class="cat-setting-row" data-cat="${c.id}">
                <div class="ic tone-${c.tone}">${Icons[c.icon] || Icons.box}</div>
                <div class="fields">
                  <div class="field">
                    <label>نام دسته</label>
                    <input class="input" value="${c.label}" data-field="label"/>
                  </div>
                  <div class="field">
                    <label>رنگ برچسب</label>
                    <div class="tone-picker">
                      ${toneOptions.map(t => `
                        <button class="tone-dot tone-${t} ${t===c.tone?'on':''}" data-tone="${t}" title="${t}" aria-label="${t}"></button>
                      `).join('')}
                    </div>
                  </div>
                </div>
                <div class="cat-meta">
                  <span class="count num-fa">${count} کالا</span>
                  <button class="icon-btn del-btn" data-action="del-cat" title="حذف دسته">${Icons.trash}</button>
                </div>
              </div>`;
            }).join('')}
          </div>
        </section>

        <section class="settings-card">
          <div class="settings-hd">
            <div>
              <h3>تنظیمات موجودی</h3>
              <p>رفتار پیش‌فرض کالاها در انبار را تنظیم کنید (نمونه طراحی).</p>
            </div>
          </div>
          <div class="settings-toggles">
            <label class="switch-row">
              <div class="txt"><div class="title">هشدار کم‌موجودی</div><div class="sub">نمایش نشان هشدار وقتی موجودی به نقطه سفارش رسید.</div></div>
              <span class="switch on" data-toggle></span>
            </label>
            <label class="switch-row">
              <div class="txt"><div class="title">ردیابی تاریخ انقضا</div><div class="sub">نمایش ستون انقضا و هشدار نزدیک شدن به تاریخ.</div></div>
              <span class="switch on" data-toggle></span>
            </label>
            <label class="switch-row">
              <div class="txt"><div class="title">کد کالای خودکار</div><div class="sub">تولید خودکار کد هنگام ساخت کالای جدید.</div></div>
              <span class="switch" data-toggle></span>
            </label>
          </div>
          <div class="settings-foot">
            <button class="btn btn-primary" data-action="save-settings">${Icons.check}<span>ذخیره تنظیمات</span></button>
          </div>
        </section>
      </div>`;

    return { controls, body };
  }

  function bindSettings() {
    $$('[data-action="new-cat"]').forEach(b => b.addEventListener('click', () => toast('افزودن دسته — نمونه طراحی')));
    $$('[data-action="save-settings"]').forEach(b => b.addEventListener('click', () => toast('تنظیمات ذخیره شد — نمونه طراحی')));

    // Category rows: tone picker + delete
    $$('.cat-setting-row').forEach(row => {
      const cid = row.dataset.cat;
      const cat = INV.catOf(cid);
      row.querySelectorAll('[data-tone]').forEach(dot => dot.addEventListener('click', () => {
        cat.tone = dot.dataset.tone;
        row.querySelectorAll('[data-tone]').forEach(d => d.classList.toggle('on', d.dataset.tone === cat.tone));
        row.querySelector('.ic').className = 'ic tone-' + cat.tone;
      }));
      row.querySelector('[data-field="label"]')?.addEventListener('input', e => { cat.label = e.target.value; });
      row.querySelector('[data-action="del-cat"]')?.addEventListener('click', () => {
        if (INV.PRODUCTS.some(p => p.cat === cid)) { toast('این دسته دارای کالاست و حذف نمی‌شود'); return; }
        if (!confirm('حذف این دسته؟')) return;
        const idx = INV.CATS.indexOf(cat); if (idx > -1) INV.CATS.splice(idx, 1);
        render(); toast('دسته حذف شد — نمونه طراحی');
      });
    });

    // Toggles
    $$('.switch[data-toggle]').forEach(sw => sw.addEventListener('click', (e) => {
      e.preventDefault();
      sw.classList.toggle('on');
    }));
  }

  // -------- Empty state helper --------------------------------------------
  function emptyState(title, sub) {
    return `
      <div class="inv-empty">
        <div class="ic">${Icons.box}</div>
        <h4>${title}</h4>
        <p>${sub}</p>
      </div>
    `;
  }

  // ==== Product detail / edit modal (centered) =============================
  // Unified view+edit surface. Opens in the centre of the screen (not a side
  // drawer) and is visually consistent with the new-product & stock modals.
  function openProductModal(pid, tab) {
    const p = INV.PRODUCTS.find(x => x.id === pid);
    if (!p) return;
    state.modalTab = tab || 'info';
    renderProductModal(p);
  }

  function renderProductModal(p) {
    const cat = INV.catOf(p.cat);
    const stock = INV.totalStock(p);
    const val = INV.totalValue(p);
    const hasVariants = p.variants.length > 1 || !!p.attribute;

    const html = `
      <div class="inv-modal wide product-modal" role="dialog" aria-modal="true">
        <div class="inv-modal-head">
          <div class="pthumb ${p.tone}">${p.icon}</div>
          <div class="titles">
            <div class="eyebrow">${cat.label} · <span class="num-fa">${p.code}</span></div>
            <h3>${p.name}</h3>
            <div class="sub">${p.brand}${p.description ? ' · ' + p.description : ''}</div>
          </div>
          <button class="icon-btn close-btn" data-action="close">${Icons.x}</button>
        </div>

        <div class="modal-tabs" role="tablist">
          <button class="tab ${state.modalTab==='info'?'active':''}"     data-mtab="info" role="tab">${Icons.clipboard || ''}<span>مشخصات</span></button>
          <button class="tab ${state.modalTab==='variants'?'active':''}" data-mtab="variants" role="tab">${Icons.layers || ''}<span>تنوع‌ها</span> <span class="tab-badge num-fa">${p.variants.length}</span></button>
          <button class="tab ${state.modalTab==='history'?'active':''}"  data-mtab="history" role="tab">${Icons.clock || ''}<span>تاریخچه</span></button>
        </div>

        <div class="inv-modal-body product-modal-body">
          ${state.modalTab === 'info'     ? renderInfoTab(p, stock, val) : ''}
          ${state.modalTab === 'variants' ? renderVariantsTab(p) : ''}
          ${state.modalTab === 'history'  ? renderHistoryTab(p) : ''}
        </div>

        <div class="inv-modal-foot">
          <button class="btn btn-primary" data-action="save-product">${Icons.check}<span>ذخیره تغییرات</span></button>
          <button class="btn btn-ghost" data-action="close">انصراف</button>
          <div style="margin-inline-start:auto">
            <button class="btn btn-danger-ghost" data-action="delete-product">${Icons.trash}<span>حذف کالا</span></button>
          </div>
        </div>
      </div>`;
    openModal(html);

    const bd = document.querySelector('#inv-modal-bd');
    bd.querySelectorAll('[data-action="close"]').forEach(b => b.addEventListener('click', closeModal));
    bd.querySelectorAll('[data-mtab]').forEach(el => el.addEventListener('click', () => {
      state.modalTab = el.dataset.mtab; renderProductModal(p);
    }));
    bd.querySelectorAll('[data-hfilter]').forEach(el => el.addEventListener('click', () => {
      state.historyFilter = el.dataset.hfilter; renderProductModal(p);
    }));
    bd.querySelector('[data-action="save-product"]')?.addEventListener('click', () => {
      closeModal(); render(); toast('تغییرات ذخیره شد — نمونه طراحی');
    });
    bd.querySelector('[data-action="delete-product"]')?.addEventListener('click', () => {
      if (!confirm('حذف کالا؟')) return;
      const idx = INV.PRODUCTS.indexOf(p); if (idx > -1) INV.PRODUCTS.splice(idx, 1);
      delete state.expanded[p.id];
      closeModal(); render(); toast('کالا حذف شد — نمونه طراحی');
    });

    bindVariantsTabInteractions(p);
  }

  function renderInfoTab(p, stock, val) {
    const hasVariants = p.variants.length > 1 || !!p.attribute;
    // For single-unit products, price/stock live on the sole variant and are
    // shown inline here so the user never has to leave the مشخصات tab.
    const single = !hasVariants ? p.variants[0] : null;

    return `
      <div class="inv-kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:18px">
        <div class="inv-kpi"><div class="ic teal">${Icons.box}</div><div><div class="num num-fa">${fmtFull(stock)}</div><div class="lbl">موجودی کل</div></div></div>
        <div class="inv-kpi"><div class="ic info">${Icons.wallet}</div><div><div class="num num-fa">${fmt(val)} ت</div><div class="lbl">ارزش موجودی</div></div></div>
        <div class="inv-kpi"><div class="ic warn">${Icons.layers}</div><div><div class="num num-fa">${hasVariants ? fmtFull(p.variants.length) : '۱'}</div><div class="lbl">${hasVariants ? 'تنوع فعال' : 'تک واحد'}</div></div></div>
      </div>

      <div class="section-hd"><h4>اطلاعات پایه</h4></div>
      <div class="var-grid" style="margin-bottom:${single ? '16px' : '4px'}">
        <div class="field wide">
          <label>نام کالا</label>
          <input class="input" value="${p.name}"/>
        </div>
        <div class="field">
          <label>کد کالا</label>
          <input class="input mono num-fa" value="${p.code}"/>
        </div>
        <div class="field">
          <label>برند</label>
          <input class="input" value="${p.brand}"/>
        </div>
        <div class="field">
          <label>دسته</label>
          <select class="selectbox">
            ${INV.CATS.map(c => `<option ${c.id===p.cat?'selected':''}>${c.label}</option>`).join('')}
          </select>
        </div>
        <div class="field">
          <label>نماد</label>
          <input class="input" value="${p.icon}" style="font-size:20px;text-align:center"/>
        </div>
        <div class="field wide">
          <label>توضیحات</label>
          <textarea class="textarea">${p.description}</textarea>
        </div>
      </div>

      ${single ? `
      <div class="section-hd"><h4>قیمت و موجودی</h4></div>
      <div class="var-grid">
        <div class="field stock-field">
          <label>موجودی فعلی</label>
          <div class="qty-stepper" data-single-stepper>
            <button data-step="-">−</button>
            <input value="${fa(single.stock)}" data-single="stock"/>
            <button data-step="+">+</button>
          </div>
        </div>
        <div class="field">
          <label>نقطه سفارش</label>
          <input class="input num-fa" value="${fa(single.reorder)}" data-single="reorder"/>
        </div>
        <div class="field">
          <label>قیمت خرید (تومان)</label>
          <input class="input num-fa" value="${fa(single.cost.toLocaleString('fa-IR'))}" data-single="cost"/>
        </div>
        <div class="field">
          <label>قیمت فروش (تومان)</label>
          <input class="input num-fa" value="${fa(single.price.toLocaleString('fa-IR'))}" data-single="price"/>
        </div>
        <div class="field wide">
          <label>تاریخ انقضا (اختیاری)</label>
          <input class="input num-fa" value="${single.expiry || ''}" placeholder="۱۴۰۶/۰۱/۰۱" data-single="expiry"/>
        </div>
      </div>
      <div class="convert-hint">
        <div class="txt">این کالا تک‌واحد است. اگر در چند وزن، حجم یا سایز ارائه می‌شود، می‌توانید آن را چند‌تنوعی کنید.</div>
        <button class="btn btn-outline btn-sm" data-mtab-jump="variants">${Icons.layers}<span>افزودن تنوع</span></button>
      </div>
      ` : ''}
    `;
  }

  function renderVariantsTab(p) {
    const attr = p.attribute;
    const isSingle = !attr && p.variants.length <= 1;

    // No-variant state — a clear, guided call-to-action (default for products).
    if (isSingle) {
      return `
        <div class="variant-intro">
          <div class="ic">${Icons.layers}</div>
          <h4>این کالا هنوز تنوع ندارد</h4>
          <p>اگر این کالا در چند حالت مختلف (مثل وزن، حجم، سایز یا دوز) عرضه می‌شود، یک ویژگی تعریف کنید تا برای هر گزینه یک تنوع با قیمت و موجودی مستقل ساخته شود.</p>
          <div class="variant-intro-form">
            <label>نام ویژگی تنوع</label>
            <div class="row">
              <input class="input" id="new-attr-name" placeholder="مثلاً: وزن، سایز، دوز"/>
              <button class="btn btn-primary" data-action="enable-attr">${Icons.plus}<span>ساخت تنوع</span></button>
            </div>
            <div class="quick-attrs">
              <span>پیشنهاد سریع:</span>
              ${['وزن','حجم','سایز','دوز'].map(s => `<button class="opt-chip add" data-quick-attr="${s}">${s}</button>`).join('')}
            </div>
          </div>
        </div>
      `;
    }

    // Has an attribute → option builder + variant list.
    return `
      <div class="attr-builder">
        <div class="attr-builder-top">
          <div class="field" style="margin:0;flex:1">
            <label>نام ویژگی</label>
            <input class="input" data-attr-name value="${attr ? attr.name : ''}" placeholder="مثلاً: وزن، سایز، دوز"/>
          </div>
          <button class="btn btn-danger-ghost btn-sm" data-action="disable-attr" title="حذف تنوع‌بندی">${Icons.trash}<span>حذف تنوع‌بندی</span></button>
        </div>
        <label class="attr-opts-label">گزینه‌ها <span class="num-fa">(${fa(attr ? attr.options.length : 0)})</span></label>
        <div class="opts" id="attr-opts">
          ${(attr ? attr.options : []).map(o => `
            <span class="opt-chip"><span>${o}</span><button data-remove-opt="${o}" title="حذف">${Icons.x.replace('width="24" height="24"','width="10" height="10"')}</button></span>
          `).join('')}
          <button class="opt-chip add" data-add-opt>${Icons.plus.replace('width="24" height="24"','width="12" height="12"')} افزودن گزینه</button>
        </div>
      </div>

      <div class="section-hd">
        <h4>تنوع‌ها <span class="num-fa hd-count">(${fa(p.variants.length)})</span></h4>
        <button class="btn btn-ghost btn-sm" data-action="add-variant">${Icons.plus}<span>تنوع دستی</span></button>
      </div>

      <div id="var-list">
        ${p.variants.map(v => renderVariantCard(p, v)).join('')}
      </div>
    `;
  }

  function renderVariantCard(p, v) {
    const isLow = v.stock <= v.reorder;
    return `
      <div class="var-card ${isLow ? 'low' : ''}" data-var="${v.id}">
        <div class="var-head">
          <input class="label-input" value="${v.label}" placeholder="نام تنوع"/>
          <span class="sku num-fa">${v.sku}</span>
          <button class="icon-btn del-btn" data-del-variant title="حذف تنوع">${Icons.trash}</button>
        </div>
        <div class="var-grid">
          <div class="field stock-field">
            <label>موجودی فعلی</label>
            <div class="qty-stepper" data-stepper>
              <button data-step="-">−</button>
              <input value="${fa(v.stock)}" data-val="stock"/>
              <button data-step="+">+</button>
            </div>
          </div>
          <div class="field">
            <label>نقطه سفارش</label>
            <input class="input num-fa" value="${fa(v.reorder)}" data-val="reorder"/>
          </div>
          <div class="field">
            <label>قیمت خرید (تومان)</label>
            <input class="input num-fa" value="${fa(v.cost.toLocaleString('fa-IR'))}" data-val="cost"/>
          </div>
          <div class="field">
            <label>قیمت فروش (تومان)</label>
            <input class="input num-fa" value="${fa(v.price.toLocaleString('fa-IR'))}" data-val="price"/>
          </div>
          <div class="field wide">
            <label>تاریخ انقضا (اختیاری)</label>
            <input class="input num-fa" value="${v.expiry || ''}" placeholder="۱۴۰۶/۰۱/۰۱" data-val="expiry"/>
          </div>
        </div>
      </div>
    `;
  }

  // Build a variant object from an option label (used when generating from
  // the option builder). SKU derives from the product code.
  function makeVariant(p, label, idx) {
    return {
      id: 'v_' + Math.random().toString(36).slice(2, 8),
      label,
      sku: p.code + '-' + (idx + 1).toString().padStart(2, '0'),
      stock: 0, reorder: 5, cost: 0, price: 0, expiry: null,
    };
  }
  // Sync the product's variant list to its attribute options: keep existing
  // variants whose label matches an option, create new ones for the rest.
  function syncVariantsToOptions(p) {
    if (!p.attribute) return;
    const opts = p.attribute.options;
    const byLabel = {};
    p.variants.forEach(v => { byLabel[v.label] = v; });
    p.variants = opts.map((o, i) => byLabel[o] || makeVariant(p, o, i));
    if (p.variants.length === 0) p.variants = [makeVariant(p, p.attribute.name || 'تنوع ۱', 0)];
  }

  function bindVariantsTabInteractions(p) {
    const scope = document.querySelector('#inv-modal-bd');
    if (!scope) return;
    const rerender = () => renderProductModal(p);

    // --- Info-tab: single-unit stepper + jump-to-variants -----------------
    const single = p.variants.length <= 1 && !p.attribute ? p.variants[0] : null;
    if (single) {
      const stepper = scope.querySelector('[data-single-stepper]');
      if (stepper) {
        const inp = stepper.querySelector('[data-single="stock"]');
        stepper.querySelectorAll('[data-step]').forEach(btn => btn.addEventListener('click', () => {
          const dir = btn.dataset.step === '+' ? 1 : -1;
          single.stock = Math.max(0, single.stock + dir);
          if (inp) inp.value = fa(single.stock);
        }));
      }
    }
    scope.querySelectorAll('[data-mtab-jump]').forEach(b => b.addEventListener('click', () => {
      state.modalTab = b.dataset.mtabJump; rerender();
    }));

    if (state.modalTab !== 'variants') return;

    // --- No-variant intro: enable attribute -------------------------------
    const enableAttr = (name) => {
      const nm = (name || '').trim() || 'ویژگی';
      p.attribute = { name: nm, options: [] };
      // Seed the current single variant as the first option if meaningful.
      rerender();
    };
    scope.querySelector('[data-action="enable-attr"]')?.addEventListener('click', () => {
      const nm = scope.querySelector('#new-attr-name')?.value;
      enableAttr(nm);
    });
    scope.querySelectorAll('[data-quick-attr]').forEach(b =>
      b.addEventListener('click', () => enableAttr(b.dataset.quickAttr)));

    // --- Attribute name + option builder ----------------------------------
    scope.querySelector('[data-attr-name]')?.addEventListener('input', e => {
      if (p.attribute) p.attribute.name = e.target.value;
    });
    scope.querySelector('[data-action="disable-attr"]')?.addEventListener('click', () => {
      if (!confirm('حذف تنوع‌بندی؟ کالا به حالت تک‌واحد برمی‌گردد.')) return;
      p.attribute = null;
      p.variants = [p.variants[0] || makeVariant(p, 'تک واحد', 0)];
      p.variants[0].label = p.variants[0].label || 'تک واحد';
      delete state.expanded[p.id];
      rerender();
    });
    scope.querySelectorAll('#attr-opts [data-remove-opt]').forEach(btn => btn.addEventListener('click', () => {
      if (!p.attribute) return;
      p.attribute.options = p.attribute.options.filter(o => o !== btn.dataset.removeOpt);
      syncVariantsToOptions(p);
      rerender();
    }));
    scope.querySelector('#attr-opts [data-add-opt]')?.addEventListener('click', () => {
      const v = prompt('نام گزینه جدید (مثلاً: ۴ کیلوگرم):');
      if (!v || !p.attribute) return;
      if (!p.attribute.options.includes(v.trim())) p.attribute.options.push(v.trim());
      syncVariantsToOptions(p);
      rerender();
    });

    // --- Variant cards: steppers + delete ---------------------------------
    scope.querySelectorAll('#var-list .var-card').forEach(card => {
      const vid = card.dataset.var;
      const v = p.variants.find(x => x.id === vid);
      if (!v) return;
      card.querySelectorAll('[data-step]').forEach(btn => btn.addEventListener('click', () => {
        const dir = btn.dataset.step === '+' ? 1 : -1;
        v.stock = Math.max(0, v.stock + dir);
        const inp = card.querySelector('[data-val="stock"]');
        if (inp) inp.value = fa(v.stock);
        card.classList.toggle('low', v.stock <= v.reorder);
      }));
      card.querySelector('[data-del-variant]')?.addEventListener('click', () => {
        if (p.variants.length <= 1) { toast('حداقل یک تنوع لازم است'); return; }
        p.variants = p.variants.filter(x => x.id !== vid);
        if (p.attribute) p.attribute.options = p.attribute.options.filter(o => o !== v.label);
        rerender();
      });
    });
    scope.querySelector('[data-action="add-variant"]')?.addEventListener('click', () => {
      const nv = makeVariant(p, 'تنوع جدید', p.variants.length);
      p.variants.push(nv);
      if (p.attribute && !p.attribute.options.includes(nv.label)) p.attribute.options.push(nv.label);
      rerender();
    });
  }

  // ---- History tab: chronological ledger of stock changes ---------------
  function renderHistoryTab(p) {
    const filter = state.historyFilter || 'all';
    let movs = INV.movesOf(p.id);
    if (filter !== 'all') movs = movs.filter(m => m.kind === filter);

    const all = INV.movesOf(p.id);
    const totalIn  = all.filter(m => m.kind === 'in').reduce((s, m) => s + m.qty, 0);
    const totalOut = all.filter(m => m.kind === 'out').reduce((s, m) => s + m.qty, 0);
    const adjustN  = all.filter(m => m.kind === 'adjust').length;

    const kindMeta = {
      in:     { icon: Icons.arrowDownLeft, cls: 'in',     label: 'افزایش', sign: '+' },
      out:    { icon: Icons.arrowUpRight,  cls: 'out',    label: 'کاهش',   sign: '−' },
      adjust: { icon: Icons.refresh,       cls: 'adjust', label: 'اصلاح',  sign: '' },
    };

    return `
      <div class="inv-kpis hist-kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
        <div class="inv-kpi"><div class="ic ok-ic">${Icons.arrowDownLeft}</div><div><div class="num num-fa">+${fmtFull(totalIn)}</div><div class="lbl">مجموع افزایش</div></div></div>
        <div class="inv-kpi"><div class="ic danger">${Icons.arrowUpRight}</div><div><div class="num num-fa">−${fmtFull(totalOut)}</div><div class="lbl">مجموع کاهش</div></div></div>
        <div class="inv-kpi"><div class="ic warn">${Icons.refresh}</div><div><div class="num num-fa">${fmtFull(adjustN)}</div><div class="lbl">اصلاح موجودی</div></div></div>
      </div>

      <div class="hist-filter" role="tablist">
        <button class="hf-chip ${filter==='all'?'on':''}" data-hfilter="all">همه رخدادها</button>
        <button class="hf-chip ${filter==='in'?'on':''}" data-hfilter="in">افزایش</button>
        <button class="hf-chip ${filter==='out'?'on':''}" data-hfilter="out">کاهش</button>
        <button class="hf-chip ${filter==='adjust'?'on':''}" data-hfilter="adjust">اصلاح</button>
      </div>

      ${movs.length === 0 ? emptyState('رخدادی ثبت نشده', 'با خرید، فروش یا اصلاح موجودی، تاریخچه اینجا نمایش داده می‌شود.') : `
      <div class="hist-timeline">
        ${movs.map(m => {
          const meta = kindMeta[m.kind] || kindMeta.in;
          const vlabel = m.variantId ? INV.variantLabel(m.productId, m.variantId) : '';
          const qtyTxt = m.kind === 'adjust'
            ? (m.qty >= 0 ? '+' : '−') + fmtFull(Math.abs(m.qty))
            : meta.sign + fmtFull(m.qty);
          return `
          <div class="hist-item">
            <div class="hist-ic ${meta.cls}">${meta.icon}</div>
            <div class="hist-body">
              <div class="hist-top">
                <span class="hist-title">${m.source}</span>
                <span class="hist-qty ${meta.cls} num-fa">${qtyTxt}</span>
              </div>
              <div class="hist-meta">
                ${vlabel ? `<span class="chip-mini">${vlabel}</span>` : ''}
                <span class="num-fa">${m.date}${m.time && m.time !== '—' ? ' · ' + m.time : ''}</span>
                <span class="dot-sep">•</span>
                <span>${m.by}</span>
              </div>
              ${m.note ? `<div class="hist-note">${m.note}</div>` : ''}
            </div>
          </div>`;
        }).join('')}
      </div>`}
    `;
  }

  // ---- New product modal (centered) -------------------------------------
  // Products are single-unit BY DEFAULT. The variant setup only appears when
  // the user explicitly turns it on, keeping the common path short & clear.
  function openNewProductModal() {
    const draft = { hasVariants: false };
    const html = `
      <div class="inv-modal wide new-product-modal" role="dialog" aria-modal="true">
        <div class="inv-modal-head">
          <div class="pthumb" style="background:linear-gradient(135deg,var(--brand-400),var(--brand-600));color:#fff;font-size:20px">${Icons.plus}</div>
          <div class="titles">
            <div class="eyebrow">افزودن به انبار</div>
            <h3>کالای جدید</h3>
            <div class="sub">ابتدا اطلاعات پایه را وارد کنید. تنوع اختیاری است.</div>
          </div>
          <button class="icon-btn close-btn" data-action="close">${Icons.x}</button>
        </div>
        <div class="inv-modal-body">
          <div class="section-hd"><h4>اطلاعات پایه</h4></div>
          <div class="var-grid">
            <div class="field wide">
              <label>نام کالا</label>
              <input class="input" data-np="name" placeholder="مثلاً: قرص ویتامین"/>
            </div>
            <div class="field">
              <label>کد کالا</label>
              <input class="input mono" data-np="code" placeholder="AN-1900"/>
            </div>
            <div class="field">
              <label>برند</label>
              <input class="input" data-np="brand" placeholder="BioVet"/>
            </div>
            <div class="field">
              <label>دسته</label>
              <select class="selectbox" data-np="cat">${INV.CATS.map(c=>`<option value="${c.id}">${c.label}</option>`).join('')}</select>
            </div>
            <div class="field">
              <label>نماد</label>
              <input class="input" data-np="icon" value="📦" style="font-size:20px;text-align:center"/>
            </div>
            <div class="field wide">
              <label>توضیحات</label>
              <textarea class="textarea" data-np="description" placeholder="توضیح مختصر برای پرسنل صندوق و ویزیت"></textarea>
            </div>
          </div>

          <div class="section-hd"><h4>نوع کالا</h4></div>
          <div class="type-toggle">
            <button class="type-opt active" data-choice="no">
              <div class="ic">${Icons.box}</div>
              <div class="txt"><div class="t">تک‌واحد</div><div class="s">یک قیمت و موجودی واحد</div></div>
              <span class="tick">${Icons.check}</span>
            </button>
            <button class="type-opt" data-choice="yes">
              <div class="ic">${Icons.layers}</div>
              <div class="txt"><div class="t">چند‌تنوعی</div><div class="s">وزن، سایز یا دوز مختلف</div></div>
              <span class="tick">${Icons.check}</span>
            </button>
          </div>

          <!-- Single-unit price/stock (default) -->
          <div data-single-fields>
            <div class="var-grid" style="margin-top:6px">
              <div class="field">
                <label>موجودی اولیه</label>
                <input class="input num-fa" data-np="stock" placeholder="۰"/>
              </div>
              <div class="field">
                <label>نقطه سفارش</label>
                <input class="input num-fa" data-np="reorder" placeholder="۵"/>
              </div>
              <div class="field">
                <label>قیمت خرید (تومان)</label>
                <input class="input num-fa" data-np="cost" placeholder="۰"/>
              </div>
              <div class="field">
                <label>قیمت فروش (تومان)</label>
                <input class="input num-fa" data-np="price" placeholder="۰"/>
              </div>
            </div>
          </div>

          <!-- Variant setup (opt-in) -->
          <div data-variant-fields style="display:none">
            <div class="var-grid" style="margin-top:6px">
              <div class="field wide">
                <label>ویژگی تنوع</label>
                <input class="input" data-np="attrName" placeholder="مثلاً: وزن، سایز، دوز"/>
              </div>
              <div class="field wide">
                <label>گزینه‌ها را با ویرگول جدا کنید</label>
                <input class="input" data-np="attrOpts" placeholder="۲ کیلوگرم، ۴ کیلوگرم، ۸ کیلوگرم"/>
                <div class="muted-txt" style="font-size:11px;margin-top:6px">به‌ازای هر گزینه یک تنوع با قیمت و موجودی مستقل ساخته می‌شود که پس از ساخت قابل ویرایش است.</div>
              </div>
            </div>
          </div>
        </div>
        <div class="inv-modal-foot">
          <button class="btn btn-primary" data-action="create">${Icons.check}<span>ساخت کالا</span></button>
          <button class="btn btn-ghost" data-action="close">انصراف</button>
        </div>
      </div>
    `;
    openModal(html);
    const bd = document.querySelector('#inv-modal-bd');
    bd.querySelectorAll('[data-action="close"]').forEach(b => b.addEventListener('click', closeModal));

    const singleWrap = bd.querySelector('[data-single-fields]');
    const varWrap = bd.querySelector('[data-variant-fields]');
    bd.querySelectorAll('[data-choice]').forEach(el => el.addEventListener('click', () => {
      bd.querySelectorAll('[data-choice]').forEach(x => x.classList.remove('active'));
      el.classList.add('active');
      draft.hasVariants = el.dataset.choice === 'yes';
      singleWrap.style.display = draft.hasVariants ? 'none' : '';
      varWrap.style.display = draft.hasVariants ? '' : 'none';
    }));

    bd.querySelector('[data-action="create"]')?.addEventListener('click', () => {
      const g = k => bd.querySelector(`[data-np="${k}"]`);
      const name = (g('name').value || '').trim();
      if (!name) { g('name').focus(); toast('نام کالا را وارد کنید'); return; }
      const code = (g('code').value || '').trim() || 'NP-' + Math.floor(1000 + Math.random() * 9000);
      const tones = ['a','b','c','d','e','f'];
      const catId = g('cat').value;
      const toNum = s => parseInt((s || '').replace(/[^0-9۰-۹]/g, '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)) || '0', 10);

      const product = {
        id: 'p_' + Math.random().toString(36).slice(2, 7),
        code, name,
        brand: (g('brand').value || '').trim() || '—',
        cat: catId,
        icon: (g('icon').value || '📦').trim() || '📦',
        tone: tones[INV.PRODUCTS.length % tones.length],
        description: (g('description').value || '').trim(),
        attribute: null,
        variants: [],
      };

      if (draft.hasVariants) {
        const attrName = (g('attrName').value || '').trim() || 'ویژگی';
        const opts = (g('attrOpts').value || '').split(/[,،]/).map(s => s.trim()).filter(Boolean);
        product.attribute = { name: attrName, options: opts.length ? opts : ['گزینه ۱'] };
        product.variants = product.attribute.options.map((o, i) => makeVariant(product, o, i));
      } else {
        product.variants = [{
          id: 'v_' + Math.random().toString(36).slice(2, 8),
          label: 'تک واحد',
          sku: code,
          stock: toNum(g('stock').value),
          reorder: toNum(g('reorder').value) || 5,
          cost: toNum(g('cost').value),
          price: toNum(g('price').value),
          expiry: null,
        }];
      }

      INV.PRODUCTS.unshift(product);
      closeModal(); render(); toast('کالا ساخته شد — نمونه طراحی');
    });
  }

  // ---- Stock adjust modal (centered) ------------------------------------
  function ensureModal() {
    let bd = document.querySelector('#inv-modal-bd');
    if (!bd) {
      bd = document.createElement('div');
      bd.id = 'inv-modal-bd'; bd.className = 'inv-modal-bd';
      bd.addEventListener('click', (e) => { if (e.target === bd) closeModal(); });
      document.body.appendChild(bd);
    }
    return bd;
  }
  function openModal(html) {
    const bd = ensureModal();
    bd.innerHTML = html;
    requestAnimationFrame(() => bd.classList.add('open'));
    convertFa(bd);
    document.body.style.overflow = 'hidden';
  }
  function closeModal() {
    const bd = document.querySelector('#inv-modal-bd');
    if (bd) { bd.classList.remove('open'); bd.innerHTML = ''; }
    document.body.style.overflow = '';
  }
  document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); });

  function openStockAdjustModal(pid) {
    const p = INV.PRODUCTS.find(x => x.id === pid);
    if (!p) return;
    renderStockAdjustModal(p);
  }

  function renderStockAdjustModal(p) {
    const cat = INV.catOf(p.cat);
    // Snapshot starting stock so we can log the delta as a history event.
    const startStock = {};
    p.variants.forEach(v => { startStock[v.id] = v.stock; });
    const html = `
      <div class="inv-modal" role="dialog" aria-modal="true">
        <div class="inv-modal-head">
          <div class="pthumb ${p.tone}">${p.icon}</div>
          <div class="titles">
            <div class="eyebrow">${cat.label} · <span class="num-fa">${p.code}</span></div>
            <h3>تغییر موجودی</h3>
            <div class="sub">${p.name}</div>
          </div>
          <button class="icon-btn close-btn" data-action="close">${Icons.x}</button>
        </div>
        <div class="inv-modal-body">
          <div class="adjust-list">
            ${p.variants.map(v => {
              const isLow = v.stock <= v.reorder;
              return `
              <div class="adjust-row ${isLow ? 'low' : ''}" data-variant="${v.id}">
                <div class="info">
                  <div class="label">${v.label}</div>
                  <div class="sku mono num-fa">${v.sku} · نقطه سفارش ${fa(v.reorder)}</div>
                </div>
                <div class="qty-stepper" data-stepper>
                  <button data-step="-" aria-label="کاهش">−</button>
                  <input value="${fa(v.stock)}" data-val="stock" inputmode="numeric"/>
                  <button data-step="+" aria-label="افزایش">+</button>
                </div>
              </div>`;
            }).join('')}
          </div>
        </div>
        <div class="inv-modal-foot">
          <button class="btn btn-primary" data-action="save">${Icons.check}<span>ثبت موجودی</span></button>
          <button class="btn btn-ghost" data-action="close">انصراف</button>
        </div>
      </div>`;
    openModal(html);

    const bd = document.querySelector('#inv-modal-bd');
    bd.querySelectorAll('[data-action="close"]').forEach(b => b.addEventListener('click', closeModal));

    bd.querySelectorAll('.adjust-row').forEach(row => {
      const v = p.variants.find(x => x.id === row.dataset.variant);
      if (!v) return;
      const input = row.querySelector('[data-val="stock"]');
      const syncLow = () => row.classList.toggle('low', v.stock <= v.reorder);
      row.querySelectorAll('[data-step]').forEach(btn => btn.addEventListener('click', () => {
        const dir = btn.dataset.step === '+' ? 1 : -1;
        v.stock = Math.max(0, v.stock + dir);
        input.value = fa(v.stock);
        syncLow();
      }));
      input.addEventListener('input', () => {
        const digits = input.value.replace(/[^0-9۰-۹]/g, '')
          .replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
        v.stock = Math.max(0, parseInt(digits || '0', 10));
        syncLow();
      });
      input.addEventListener('blur', () => { input.value = fa(v.stock); });
    });

    bd.querySelector('[data-action="save"]')?.addEventListener('click', () => {
      // Record a history entry for every variant whose stock changed.
      p.variants.forEach(v => {
        const delta = v.stock - (startStock[v.id] || 0);
        if (delta === 0) return;
        INV.logMove(p.id, {
          variantId: v.id,
          kind: 'adjust',
          qty: delta,
          source: 'اصلاح موجودی دستی',
          note: 'ثبت از طریق «تغییر موجودی»',
        });
      });
      closeModal(); render(); toast('موجودی به‌روزرسانی شد — نمونه طراحی');
    });
  }

  // ==== Init ==============================================================
  document.addEventListener('DOMContentLoaded', render);
  if (document.readyState !== 'loading') render();
})();
