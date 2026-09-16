// Shared layout: sidebar + topbar
// Optional per-role identity for personalized surfaces (dashboard variants).
// Default = manager (every page keeps its incumbent identity when unset).
window.ROLES = {
  manager:  { name: 'دکتر آریا نیک',   role: 'مدیر کلینیک',   ini: 'آ' },
  cashier:  { name: 'سمیرا کاویانی',  role: 'مسئول صندوق',   ini: 'س' },
  doctor:   { name: 'دکتر مینا همتی', role: 'پزشک حیوانات',  ini: 'م' },
};
window.roleOf = function (key) { return window.ROLES[key] || window.ROLES.manager; };

window.NAV_ITEMS = [
  { group: 'مرکز فرمان', items: [
    { key: 'dashboard', label: 'داشبورد', icon: 'dashboard', href: 'dashboard.html' },
    { key: 'appointments', label: 'نوبت‌ها', icon: 'calendar', href: 'appointments.html', badge: 4 },
  ]},
  { group: 'مدیریت کلینیک', items: [
    { key: 'patients', label: 'بیماران و مالکین', icon: 'users', href: 'patients.html' },
    { key: 'visits', label: 'ویزیت و پرونده', icon: 'clipboard', href: 'visits.html' },
    { key: 'vaccine', label: 'واکسیناسیون', icon: 'syringe', href: 'vaccination.html' },
    { key: 'boarding', label: 'پانسیون', icon: 'home', href: 'boarding.html', badge: 3 },
    { key: 'sale', label: 'فروش و صندوق', icon: 'cart', href: 'sale.html' },
  ]},
  { group: 'کسب و کار', items: [
    { key: 'inventory', label: 'انبار و کالا', icon: 'box', href: 'inventory.html', badge: 2 },
    { key: 'expense', label: 'هزینه‌ها', icon: 'receipt', href: 'expense.html' },
    { key: 'reports', label: 'آمار و گزارش فروش', icon: 'chart', href: 'reports.html' },
  ]},
  { group: 'مالی و خزانه', items: [
    { key: 'cheques', label: 'مدیریت چک', icon: 'cheque', href: 'cheques.html', badge: 3 },
  ]},
  { group: 'سیستم', items: [
    { key: 'history', label: 'تاریخچه', icon: 'clock', href: 'history.html' },
    { key: 'profile', label: 'پروفایل و تنظیمات', icon: 'shield', href: 'profile.html' },
  ]},
];

window.renderSidebar = function(activeKey, roleKey) {
  const R = window.roleOf(roleKey);
  const html = `
    <aside class="sidebar">
      <div class="brand">
        <div class="brand-text">
          <div class="brand-name">وت‌پلاس</div>
          <div class="brand-tag">کلینیک هوشمند</div>
        </div>
        <div class="brand-logo" style="margin-inline-start:auto">${Icons.paw}</div>
      </div>

      ${NAV_ITEMS.map(section => `
        <div class="nav-section-title">${section.group}</div>
        <div class="nav-list">
          ${section.items.map(it => `
            <a class="nav-item ${it.key === activeKey ? 'active' : ''}" href="${it.href}" title="${it.label}">
              ${Icons[it.icon] || ''}
              <span>${it.label}</span>
              ${it.badge ? `<span class="badge">${toFa(it.badge)}</span>` : ''}
            </a>
          `).join('')}
        </div>
      `).join('')}

      <div class="side-cta">
        <h4>نسخه حرفه‌ای</h4>
        <p>همه‌چیز برای یک کلینیک درخشان</p>
        <button>مشاهده امکانات</button>
      </div>
      <div class="side-status">
        <span class="dot"></span>
        <div>
          <div style="color:var(--ink);font-weight:700">همگام‌سازی فعال</div>
          <div>آخرین بروزرسانی: چند ثانیه پیش</div>
        </div>
      </div>
      <div class="side-user" title="حساب کاربری">
        <div class="avatar">${R.ini}</div>
        <div class="su-text">
          <div class="name">${R.name}</div>
          <div class="role">${R.role}</div>
        </div>
        <span class="chev">${Icons.chevronLeft}</span>
      </div>
    </aside>
  `;
  return html;
};

window.renderTopbar = function(title, crumbs=[]) {
  // crumbs may arrive as ['a', 'b'] or as a single 'a / b' string
  const parts = crumbs.flatMap(c => String(c).split('/')).map(c => c.trim()).filter(Boolean);
  const crumbHtml = parts.map((c, i) =>
    `<span class="${i === parts.length - 1 ? 'last' : ''}">${c}</span>` +
    (i < parts.length - 1 ? '<span class="sep">›</span>' : '')
  ).join('');
  return `
    <div class="topbar">
      <button class="icon-btn" id="sidebarToggleBtn" onclick="toggleSidebar()" title="جمع کردن منو">${Icons.fold}</button>
      <div class="topbar-title">
        <div class="crumbs">${crumbHtml}</div>
        <h1>${title}</h1>
      </div>
      <div class="top-actions">
        <div class="search search-trigger" onclick="openCmdk()" title="جستجو و دستورات سریع (⌘K)">
          <span class="icon">${Icons.search}</span>
          <input placeholder="جستجو در بیماران، نوبت‌ها، فاکتورها..." readonly />
          <span class="kbd">⌘K</span>
        </div>
        <button class="btn btn-primary topbar-cta" onclick="openCmdk()" title="ایجاد سریع (⌘K)">${Icons.plus}<span>ایجاد جدید</span></button>
        <button class="icon-btn" title="تم روشن/تیره">${Icons.moon}</button>
        <button class="icon-btn" onclick="toggleNotifs()" title="اعلان‌ها">
          ${Icons.bell}
          <span class="dot-red"></span>
        </button>
      </div>
    </div>
  `;
};

window.renderNotifPanel = function() {
  return `
    <div class="notif-panel" id="notif-panel">
      <div class="notif-head">
        <div class="notif-head-text">
          <h3>اعلان‌ها</h3>
          <span class="count">۵ اعلان تازه</span>
        </div>
        <button class="icon-btn close" onclick="toggleNotifs()" title="بستن">${Icons.x}</button>
      </div>
      <div class="notif-list">
        <div class="notif-item">
          <div class="ic" style="background:var(--danger-bg);color:var(--danger)">${Icons.alert}</div>
          <div>
            <div class="title">موجودی قرص ضد انگل کم است</div>
            <div class="desc">فقط ۳ عدد باقی مانده، نقطه سفارش‌گذاری ۱۰ عدد.</div>
            <div class="time">۸ دقیقه پیش</div>
          </div>
        </div>
        <div class="notif-item">
          <div class="ic" style="background:var(--brand-50);color:var(--brand-700)">${Icons.calendar}</div>
          <div>
            <div class="title">نوبت جدید تأیید شد</div>
            <div class="desc">اَنو برای ساعت ۱۰:۳۰ امروز.</div>
            <div class="time">۱۵ دقیقه پیش</div>
          </div>
        </div>
        <div class="notif-item">
          <div class="ic" style="background:var(--lilac-bg);color:#7b5cc4">${Icons.edit}</div>
          <div>
            <div class="title">یادآوری واکسن ارسال شد</div>
            <div class="desc">پیامک یادآوری برای ۶ مالک ارسال شد.</div>
            <div class="time">۱ ساعت پیش</div>
          </div>
        </div>
        <div class="notif-item">
          <div class="ic" style="background:var(--info-bg);color:var(--info)">${Icons.wallet}</div>
          <div>
            <div class="title">فاکتور INV-۱۴۰۵۸۲۱۷ پرداخت شد</div>
            <div class="desc">مبلغ ۵٬۸۸۹٬۰۰۰ تومان توسط کارت‌خوان تسویه شد.</div>
            <div class="time">۲ ساعت پیش</div>
          </div>
        </div>
        <div class="notif-item">
          <div class="ic" style="background:var(--warn-bg);color:var(--warn)">${Icons.truck}</div>
          <div>
            <div class="title">فاکتور خرید در انتظار تسویه</div>
            <div class="desc">پخش دارویی سپهر — سررسید فردا.</div>
            <div class="time">دیروز</div>
          </div>
        </div>
      </div>
      <div class="notif-foot">
        <a href="#">مشاهده همه اعلان‌ها →</a>
      </div>
    </div>
    <div id="notif-backdrop" class="panel-backdrop" onclick="toggleNotifs()"></div>
  `;
};

window.toggleNotifs = function() {
  const p = document.getElementById('notif-panel');
  const b = document.getElementById('notif-backdrop');
  if (!p) return;
  const open = p.classList.toggle('open');
  if (b) b.classList.toggle('show', open);
};

window.toggleSidebar = function() {
  const btn = document.getElementById('sidebarToggleBtn');
  // narrow viewports: the rail is an off-canvas drawer — open/close it instead
  if (window.matchMedia('(max-width: 1024px)').matches) {
    const open = document.body.classList.toggle('sidebar-mobile-open');
    const bd = document.getElementById('side-backdrop');
    if (bd) bd.classList.toggle('show', open);
    if (btn) btn.title = open ? 'بستن منو' : 'باز کردن منو';
    return;
  }
  const collapsed = document.body.classList.toggle('sidebar-collapsed');
  try { localStorage.setItem('vp_sidebar_collapsed', collapsed ? '1' : '0'); } catch (_) {}
  if (btn) btn.title = collapsed ? 'باز کردن منو' : 'جمع کردن منو';
};

window.closeMobileSidebar = function() {
  document.body.classList.remove('sidebar-mobile-open');
  const bd = document.getElementById('side-backdrop');
  if (bd) bd.classList.remove('show');
};

/* =========================================================
   Command palette (⌘K / click on the search field)
   ========================================================= */
window.CMDK_ACTIONS = [
  { t: 'جستجوی بیمار',       s: 'پرونده‌ها و مالکین',   href: 'patients.html',     icon: 'users',    tone: 'teal',   kw: 'بیمار مالک پرونده سگ گربه patient' },
  { t: 'ثبت نوبت جدید',      s: 'برنامه‌ریزی مراجعه',   href: 'appointments.html', icon: 'calendar', tone: 'blue',   kw: 'نوبت تقویم رزرو appointment' },
  { t: 'صدور فاکتور',        s: 'فروش و صندوق',         href: 'sale.html',         icon: 'receipt',  tone: 'teal',   kw: 'فاکتور فروش صندوق پرداخت invoice' },
  { t: 'مشاهده گزارش فروش',  s: 'هوش کسب‌وکار',         href: 'reports.html',      icon: 'chart',    tone: 'blue',   kw: 'گزارش آمار تحلیل report' },
  { t: 'شروع ویزیت',         s: 'ثبت معاینه و نسخه',    href: 'visits.html',       icon: 'clipboard',tone: 'violet', kw: 'ویزیت معاینه نسخه پرونده پزشکی' },
  { t: 'واکسیناسیون',        s: 'یادآورها و برنامه',    href: 'vaccination.html',  icon: 'syringe',  tone: 'green',  kw: 'واکسن یادآور ایمن‌سازی' },
  { t: 'پانسیون',            s: 'اقامت و نگهداری حیوانات', href: 'boarding.html',     icon: 'home',     tone: 'violet', kw: 'پانسیون اقامت نگهداری اتاق تخلیه boarding' },
  { t: 'ثبت اقامت جدید',     s: 'پذیرش پانسیون',        href: 'boarding.html',     icon: 'plus',     tone: 'teal',   kw: 'ثبت اقامت پذیرش پانسیون ورود' },
  { t: 'انبار و کالا',       s: 'کنترل موجودی و خرید',  href: 'inventory.html',    icon: 'box',      tone: 'amber',  kw: 'انبار کالا موجودی دارو خرید تأمین سفارش' },
  { t: 'ثبت هزینه',          s: 'مدیریت هزینه‌ها',      href: 'expense.html',      icon: 'wallet',   tone: 'amber',  kw: 'هزینه مالی پرداخت' },
  { t: 'مدیریت چک',          s: 'خزانه‌داری و سررسیدها', href: 'cheques.html',      icon: 'cheque',   tone: 'teal',   kw: 'چک صیاد سررسید وصول برگشتی بانک cheque' },
  { t: 'ثبت چک جدید',        s: 'دریافتی یا پرداختی',   href: 'cheques.html',      icon: 'plus',     tone: 'green',  kw: 'ثبت چک جدید دریافتی پرداختی' },
  { t: 'داشبورد',            s: 'نمای کلی کلینیک',       href: 'dashboard.html',    icon: 'dashboard',tone: 'teal',   kw: 'داشبورد خانه نمای کلی' },
  { t: 'پروفایل و تنظیمات',  s: 'حساب کاربری و تنظیمات', href: 'profile.html',       icon: 'shield',    tone: 'violet', kw: 'پروفایل تنظیمات حساب کاربری امنیت notification اعلان تم' },
];

window.renderCmdk = function() {
  return `
    <div class="cmdk-root" id="cmdk-root" aria-hidden="true">
      <div class="cmdk-backdrop" onclick="closeCmdk()"></div>
      <div class="cmdk-panel" role="dialog" aria-modal="true" aria-label="جستجو و دستورات">
        <div class="cmdk-input-row">
          <span class="cmdk-ic">${Icons.search}</span>
          <input id="cmdk-input" type="text" autocomplete="off" spellcheck="false"
                 placeholder="چه کاری می‌خواهید انجام دهید؟" />
          <button class="cmdk-esc" onclick="closeCmdk()" title="بستن">Esc</button>
        </div>
        <div class="cmdk-body" id="cmdk-body"></div>
        <div class="cmdk-foot">
          <span><kbd>↑</kbd><kbd>↓</kbd> جابجایی</span>
          <span><kbd>↵</kbd> انتخاب</span>
          <span><kbd>Esc</kbd> بستن</span>
          <span class="cmdk-brand">وت‌پلاس</span>
        </div>
      </div>
    </div>
  `;
};

(function () {
  let idx = 0, results = [];

  function itemHtml(a, i) {
    return `
      <a class="cmdk-item ${i === idx ? 'on' : ''}" data-i="${i}" href="${a.href}">
        <span class="ci-ic tone-${a.tone}">${Icons[a.icon] || ''}</span>
        <span class="ci-text">
          <span class="ci-title">${a.t}</span>
          <span class="ci-sub">${a.s}</span>
        </span>
        <span class="ci-go">${Icons.chevronLeft || ''}</span>
      </a>`;
  }

  function paint(q) {
    const body = document.getElementById('cmdk-body');
    if (!body) return;
    const query = (q || '').trim();
    results = !query ? CMDK_ACTIONS.slice(0, 4)
      : CMDK_ACTIONS.filter(a => (a.t + ' ' + a.s + ' ' + a.kw).toLowerCase().includes(query.toLowerCase()));
    if (idx >= results.length) idx = 0;
    if (!results.length) {
      body.innerHTML = `<div class="cmdk-empty">${Icons.search}<p>نتیجه‌ای برای «${query}» پیدا نشد</p></div>`;
      return;
    }
    body.innerHTML =
      `<div class="cmdk-section">${query ? 'نتایج' : 'پیشنهادها'}</div>
       <div class="cmdk-list">${results.map(itemHtml).join('')}</div>`;
    body.querySelectorAll('.cmdk-item').forEach(el => {
      el.addEventListener('mousemove', () => { idx = +el.dataset.i; mark(); });
      el.addEventListener('click', e => { e.preventDefault(); go(+el.dataset.i); });
    });
  }

  function mark() {
    document.querySelectorAll('.cmdk-item').forEach(el => el.classList.toggle('on', +el.dataset.i === idx));
  }

  function go(i) {
    const a = results[i];
    if (!a) return;
    closeCmdk();
    setTimeout(() => navigateTo(a.href), 120);
  }

  window.openCmdk = function () {
    const r = document.getElementById('cmdk-root');
    if (!r || r.classList.contains('open')) return;
    idx = 0;
    r.classList.add('open');
    r.setAttribute('aria-hidden', 'false');
    document.body.classList.add('cmdk-lock');
    const inp = document.getElementById('cmdk-input');
    if (inp) { inp.value = ''; setTimeout(() => inp.focus(), 60); }
    paint('');
  };

  window.closeCmdk = function () {
    const r = document.getElementById('cmdk-root');
    if (!r) return;
    r.classList.remove('open');
    r.setAttribute('aria-hidden', 'true');
    document.body.classList.remove('cmdk-lock');
  };

  document.addEventListener('keydown', function (e) {
    const root = document.getElementById('cmdk-root');
    const open = root && root.classList.contains('open');
    if ((e.metaKey || e.ctrlKey) && (e.key === 'k' || e.key === 'K')) {
      e.preventDefault(); open ? closeCmdk() : openCmdk(); return;
    }
    if (!open) return;
    if (e.key === 'Escape') { e.preventDefault(); closeCmdk(); }
    else if (e.key === 'ArrowDown') { e.preventDefault(); idx = (idx + 1) % results.length; mark(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); idx = (idx - 1 + results.length) % results.length; mark(); }
    else if (e.key === 'Enter') { e.preventDefault(); go(idx); }
  });

  document.addEventListener('input', function (e) {
    if (e.target && e.target.id === 'cmdk-input') { idx = 0; paint(e.target.value); }
  });
})();

/* =========================================================
   Soft page transitions (minimal fade + lift, MPA friendly)
   ========================================================= */
window.navigateTo = function (href) {
  if (!href || href === '#' ) return;
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reduce) { location.href = href; return; }
  document.body.classList.add('page-leaving');
  const bar = document.getElementById('route-bar');
  if (bar) bar.classList.add('go');
  setTimeout(() => { location.href = href; }, 260);
};

(function () {
  // top progress bar element
  function ensureBar() {
    if (document.getElementById('route-bar')) return;
    const b = document.createElement('div');
    b.id = 'route-bar';
    document.body.appendChild(b);
  }

  function isInternal(a) {
    if (!a) return false;
    const href = a.getAttribute('href') || '';
    if (!href || href.startsWith('#') || href.startsWith('http') || href.startsWith('mailto:')) return false;
    if (a.target && a.target !== '_self') return false;
    return /\.html($|[?#])/.test(href) || !href.includes(':');
  }

  document.addEventListener('click', function (e) {
    const a = e.target.closest && e.target.closest('a');
    if (!isInternal(a)) return;
    if (a.closest('.sidebar')) closeMobileSidebar();
    if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
    const href = a.getAttribute('href');
    // same page → let it be
    if (href === location.pathname.split('/').pop()) { e.preventDefault(); return; }
    e.preventDefault();
    navigateTo(href);
  });

  window.addEventListener('pageshow', function (e) {
    // reset when coming back through bfcache
    document.body.classList.remove('page-leaving');
    const bar = document.getElementById('route-bar');
    if (bar) bar.classList.remove('go');
  });

  document.addEventListener('DOMContentLoaded', ensureBar);
  if (document.readyState !== 'loading') ensureBar();
})();

// Persian digit converter
window.toFa = function(v) {
  const map = ['۰','۱','۲','۳','۴','۵','۶','۷','۸','۹'];
  return String(v).replace(/\d/g, d => map[d]);
};

// Boot: render sidebar into element#sidebar-slot and topbar into #topbar-slot
window.mountLayout = function(cfg) {
  // Restore the sidebar collapsed/expanded preference *before* the sidebar is
  // painted, so it never flashes open and then snaps shut (or vice versa)
  // while moving between pages.
  try {
    if (localStorage.getItem('vp_sidebar_collapsed') === '1') document.body.classList.add('sidebar-collapsed');
  } catch (_) {}
  // The sidebar's nav list only plays its entrance animation once per browser
  // session — after that, navigating between pages keeps it visually stable
  // instead of re-animating (feeling like a "refresh") on every click.
  try {
    if (sessionStorage.getItem('vp_nav_seen')) document.body.classList.add('nav-no-anim');
    else sessionStorage.setItem('vp_nav_seen', '1');
  } catch (_) {}

  const s = document.getElementById('sidebar-slot');
  if (s) s.outerHTML = renderSidebar(cfg.active, cfg.role) + '<div id="side-backdrop" class="side-backdrop" onclick="closeMobileSidebar()"></div>';
  const t = document.getElementById('topbar-slot');
  if (t) t.outerHTML = renderTopbar(cfg.title, cfg.crumbs || []);
  const n = document.getElementById('notif-slot');
  if (n) n.outerHTML = renderNotifPanel();

  // sync the fold button's tooltip with the restored state
  const foldBtn = document.getElementById('sidebarToggleBtn');
  if (foldBtn) foldBtn.title = document.body.classList.contains('sidebar-collapsed') ? 'باز کردن منو' : 'جمع کردن منو';

  // command palette lives at the end of <body> on every page
  if (!document.getElementById('cmdk-root')) {
    const holder = document.createElement('div');
    holder.innerHTML = renderCmdk();
    document.body.appendChild(holder.firstElementChild);
  }

  // convert numerals
  document.querySelectorAll('.num-fa').forEach(el => {
    el.textContent = toFa(el.textContent);
  });

  // elevate the topbar once the body scrolls (clear separation from content)
  const onScroll = () => document.body.classList.toggle('is-scrolled', window.scrollY > 6);
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  // mark the shell as ready → plays the page-enter animation
  requestAnimationFrame(() => document.body.classList.add('page-ready'));
};
