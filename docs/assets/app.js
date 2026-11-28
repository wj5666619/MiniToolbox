(function(){
  document.documentElement.classList.add('js');

  // Optional privacy endpoint. Leave blank: events stay in window.__mtEvents only.
  const MT_ANALYTICS_ENDPOINT = '';
  window.__mtEvents = window.__mtEvents || [];
  function track(name, detail){
    const ev = Object.assign({event:name, ts:Date.now()}, detail || {});
    window.__mtEvents.push(ev);
    if(!MT_ANALYTICS_ENDPOINT) return;
    try{
      const body = JSON.stringify(ev);
      if(navigator.sendBeacon){
        navigator.sendBeacon(MT_ANALYTICS_ENDPOINT, new Blob([body], {type:'application/json'}));
      } else {
        fetch(MT_ANALYTICS_ENDPOINT, {method:'POST', keepalive:true, headers:{'Content-Type':'application/json'}, body:body});
      }
    }catch(e){}
  }

  // ---------- optional interface sound ----------
  let sfxOn = false;
  try{ sfxOn = localStorage.getItem('mt-sfx') === '1'; }catch(e){}
  let audioCtx = null;
  function ensureAudio(){
    const C = window.AudioContext || window.webkitAudioContext;
    if(!C) return null;
    if(!audioCtx) audioCtx = new C();
    if(audioCtx.state === 'suspended') audioCtx.resume();
    return audioCtx;
  }
  function sound(kind){
    if(!sfxOn) return;
    const c = ensureAudio(); if(!c) return;
    const cfg = kind === 'whoosh'
      ? {type:'sawtooth', f0:230, f1:64, dur:.17, gain:.022}
      : kind === 'tick'
        ? {type:'sine', f0:940, f1:760, dur:.045, gain:.013}
        : {type:'square', f0:1180, f1:1180, dur:.024, gain:.007};
    try{
      const t = c.currentTime + .001;
      const o = c.createOscillator(), g = c.createGain();
      o.type = cfg.type;
      o.frequency.setValueAtTime(cfg.f0, t);
      if(cfg.f1 !== cfg.f0) o.frequency.exponentialRampToValueAtTime(cfg.f1, t + cfg.dur);
      g.gain.setValueAtTime(.0001, t);
      g.gain.linearRampToValueAtTime(cfg.gain, t + .006);
      g.gain.exponentialRampToValueAtTime(.0001, t + cfg.dur);
      o.connect(g); g.connect(c.destination);
      o.start(t); o.stop(t + cfg.dur + .012);
    }catch(e){}
  }
  function haptic(){
    if(!sfxOn || !navigator.vibrate) return;
    try{ navigator.vibrate(8); }catch(e){}
  }
  const sfxBtn = document.getElementById('sfxBtn');
  function syncSfx(){
    if(!sfxBtn) return;
    sfxBtn.setAttribute('aria-pressed', sfxOn ? 'true' : 'false');
    sfxBtn.setAttribute('aria-label', sfxOn ? '关闭界面音效' : '开启界面音效');
  }
  if(sfxBtn){
    syncSfx();
    sfxBtn.addEventListener('click', function(){
      sfxOn = !sfxOn;
      try{ localStorage.setItem('mt-sfx', sfxOn ? '1' : '0'); }catch(e){}
      syncSfx();
      if(sfxOn) sound('tick');
      track('sfx_toggle', {on:sfxOn});
    });
  }

  // one delegated layer for outbound links and small UI sounds
  document.addEventListener('click', function(e){
    const el = e.target && e.target.closest ? e.target.closest('[data-track]') : null;
    if(el){
      track(el.dataset.track, {href:el.href || ''});
      if(el.dataset.track === 'download'){ sound('whoosh'); haptic(); }
      else sound('click');
      return;
    }
    const snd = e.target && e.target.closest ? e.target.closest('[data-sound]') : null;
    if(snd){ sound(snd.dataset.sound || 'click'); return; }
    const anchor = e.target && e.target.closest ? e.target.closest('a[href^="#"]') : null;
    if(anchor && !anchor.classList.contains('skip-link')){
      sound('click');
      track('nav', {target:anchor.getAttribute('href')});
    }
  }, true);

  // ---------- mobile nav + sticky download ----------
  const menuBtn = document.getElementById('menuBtn');
  const mobileNav = document.getElementById('mobileNav');
  const dock = document.getElementById('mobileDock');
  const dockClose = document.getElementById('dockClose');
  const downloadSec = document.getElementById('download');
  let dockDismissed = false;
  let dockHold = false;
  let dockLastY = window.scrollY || 0;
  try{ dockDismissed = sessionStorage.getItem('mt-dock-dismissed') === '1'; }catch(e){}
  function updateDock(){
    if(!dock) return;
    const y = window.scrollY || 0;
    if(y < dockLastY - 6) dockHold = true;
    if(y > dockLastY + 6) dockHold = false;
    dockLastY = y;
    const navOpen = document.body.classList.contains('menu-open');
    const lbOpen = document.getElementById('lightbox') && document.getElementById('lightbox').classList.contains('open');
    const nearDownload = downloadSec ? downloadSec.getBoundingClientRect().top < window.innerHeight * .9 : false;
    const show = window.innerWidth <= 860 && !dockDismissed && dockHold && !navOpen && !lbOpen && !nearDownload && y > window.innerHeight * .8;
    dock.classList.toggle('show', show);
  }
  function closeMenu(){
    mobileNav.classList.remove('open');
    menuBtn.setAttribute('aria-expanded','false');
    document.body.style.overflow='';
    document.body.classList.remove('menu-open');
    updateDock();
  }
  menuBtn.addEventListener('click', function(){
    const open = mobileNav.classList.toggle('open');
    menuBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    document.body.style.overflow = open ? 'hidden' : '';
    document.body.classList.toggle('menu-open', open);
    if(open && dock) dock.classList.remove('show');
  });
  mobileNav.querySelectorAll('a').forEach(a => a.addEventListener('click', closeMenu));
  if(dockClose){
    dockClose.addEventListener('click', function(){
      dockDismissed = true;
      try{ sessionStorage.setItem('mt-dock-dismissed','1'); }catch(e){}
      updateDock();
      track('dock_dismiss');
    });
  }
  window.addEventListener('keydown', e => { if(e.key === 'Escape') closeMenu(); });

  // reveal
  const reveals = document.querySelectorAll('.reveal');
  if('IntersectionObserver' in window){
    const io = new IntersectionObserver(entries => {
      entries.forEach(en => { if(en.isIntersecting){ en.target.classList.add('in'); io.unobserve(en.target); } });
    }, {rootMargin:'0px 0px -8% 0px', threshold:.12});
    reveals.forEach(el => io.observe(el));
  } else { reveals.forEach(el => el.classList.add('in')); }

  // theme + rail + progress
  const header = document.getElementById('siteHeader');
  const progress = document.getElementById('progress');
  const sections = Array.from(document.querySelectorAll('[data-theme]'));
  const railLinks = Array.from(document.querySelectorAll('.rail a'));
  const navLinks = Array.from(document.querySelectorAll('.navlinks a'));
  function onScroll(){
    const y = window.scrollY || 0;
    header.classList.toggle('scrolled', y > 40);
    let cur = sections[0];
    const probe = window.innerHeight * .38;
    sections.forEach(s => { if(s.getBoundingClientRect().top <= probe) cur = s; });
    document.body.dataset.theme = cur.dataset.theme || 'light';
    const id = cur.id;
    railLinks.forEach(a => a.classList.toggle('active', a.dataset.target === id));
    navLinks.forEach(a => a.classList.toggle('active', a.getAttribute('href') === '#' + id));
    const max = document.documentElement.scrollHeight - window.innerHeight;
    progress.style.width = (max > 0 ? (y / max * 100) : 0) + '%';
    updateDock();
  }
  window.addEventListener('scroll', onScroll, {passive:true});
  window.addEventListener('resize', onScroll);
  onScroll();

  // ---------- copy + policy download ----------
  function flashLabel(btn, label){
    if(!btn.__label) btn.__label = btn.textContent.trim();
    if(btn.__timer) clearTimeout(btn.__timer);
    btn.textContent = label;
    btn.classList.add('ok');
    btn.__timer = setTimeout(function(){ btn.textContent = btn.__label; btn.classList.remove('ok'); }, 1600);
  }
  function selectText(node){
    if(!node || !window.getSelection) return;
    const r = document.createRange(); r.selectNodeContents(node);
    const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(r);
  }
  document.querySelectorAll('[data-copy],[data-copy-source]').forEach(function(btn){
    btn.__label = btn.textContent.trim();
    btn.addEventListener('click', async function(){
      const source = btn.dataset.copySource;
      const node = source ? document.querySelector(source) : null;
      const text = (btn.dataset.copy || (node ? node.textContent : '') || '').trim();
      if(!text) return;
      let done = false;
      try{ await navigator.clipboard.writeText(text); done = true; }catch(e){}
      if(!done){
        const ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select();
        try{ done = document.execCommand('copy'); }catch(e){}
        ta.remove();
      }
      if(!done){
        const visible = btn.closest('.hash-wrap') ? btn.closest('.hash-wrap').querySelector('.hash') : (source ? btn.closest('.policy') && document.getElementById('policyCode') : null);
        selectText(visible);
        flashLabel(btn, 'Ctrl+C');
        return;
      }
      flashLabel(btn, '已复制');
      sound('tick'); haptic();
      track('copy', {kind: source ? 'policy' : (btn.closest('.verify-box') ? 'command' : 'sha')});
    });
  });
  document.querySelectorAll('[data-download-source]').forEach(function(btn){
    btn.__label = btn.textContent.trim();
    btn.addEventListener('click', function(){
      const source = btn.dataset.downloadSource;
      const node = source ? document.querySelector(source) : null;
      const text = node ? node.textContent.trim() : '';
      if(!text) return;
      const blob = new Blob([text], {type:'application/json;charset=utf-8'});
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = btn.dataset.filename || 'MiniToolbox-policy.json';
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(function(){ URL.revokeObjectURL(url); }, 500);
      flashLabel(btn, '已下载');
      sound('whoosh'); haptic();
      track('policy_download');
    });
  });

  // tabs
  const tabs = Array.from(document.querySelectorAll('.mtab'));
  const panels = Array.from(document.querySelectorAll('.mpanel'));
  function activateTab(name, focus){
    const btn = tabs.find(b => b.dataset.tab === name);
    if(!btn) return;
    tabs.forEach(b => { b.classList.toggle('active', b === btn); b.setAttribute('aria-selected', b === btn ? 'true' : 'false'); });
    panels.forEach(p => p.classList.toggle('active', p.dataset.panel === name));
    if(focus) btn.focus();
  }
  tabs.forEach((btn, i) => {
    btn.addEventListener('click', () => {
      activateTab(btn.dataset.tab, false);
      sound('click');
      track('tab', {tab:btn.dataset.tab});
    });
    btn.addEventListener('keydown', e => {
      const dir = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
      if(!dir) return;
      e.preventDefault();
      const next = tabs[(i + dir + tabs.length) % tabs.length];
      next.focus(); next.click();
    });
  });

  // hero hotspots
  document.querySelectorAll('.hotspot').forEach(function(btn){
    btn.addEventListener('click', function(){
      if(btn.dataset.jumpTab) activateTab(btn.dataset.jumpTab, false);
      const target = document.querySelector(btn.dataset.jump);
      if(target) target.scrollIntoView({behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block:'start'});
      track('hotspot', {target:btn.dataset.jump, tab:btn.dataset.jumpTab || ''});
    });
  });

  // matrix chip tooltip
  const chipTip = document.getElementById('chipTip');
  function showChipTip(chip){
    if(!chipTip || !chip.dataset.tip) return;
    chipTip.replaceChildren();
    const label = document.createElement('b'); label.textContent = 'TIP / 作用';
    const body = document.createElement('div'); body.textContent = chip.dataset.tip;
    chipTip.appendChild(label); chipTip.appendChild(body);
    if(chip.dataset.key){
      const key = document.createElement('em'); key.textContent = '快捷键 ' + chip.dataset.key;
      chipTip.appendChild(key);
    }
    chipTip.classList.add('show');
    chipTip.style.left = '0px'; chipTip.style.top = '0px';
    const r = chip.getBoundingClientRect();
    const tw = chipTip.offsetWidth, th = chipTip.offsetHeight;
    let left = Math.min(Math.max(12, r.left + r.width / 2 - tw / 2), window.innerWidth - tw - 12);
    let top = r.top - th - 9;
    if(top < 82) top = r.bottom + 9;
    chipTip.style.left = left + 'px'; chipTip.style.top = top + 'px';
  }
  document.querySelectorAll('.chip').forEach(function(chip){
    chip.addEventListener('mouseenter', () => showChipTip(chip));
    chip.addEventListener('mouseleave', () => { if(chipTip) chipTip.classList.remove('show'); });
  });
  window.addEventListener('scroll', () => { if(chipTip) chipTip.classList.remove('show'); }, {passive:true});

  // skin demo
  const skinDemo = document.getElementById('skinDemo');
  const demoName = document.getElementById('demoName');
  const skinCards = Array.from(document.querySelectorAll('.skin-card'));
  function applySkin(card){
    if(!skinDemo || !card) return;
    skinDemo.style.setProperty('--demo-bg', card.dataset.bg);
    skinDemo.style.setProperty('--demo-fg', card.dataset.fg);
    skinDemo.style.setProperty('--demo-accent', card.dataset.accent);
    skinDemo.style.setProperty('--demo-ink', card.dataset.ink);
    skinDemo.style.setProperty('--demo-radius', card.dataset.radius + 'px');
    if(demoName) demoName.textContent = card.dataset.name;
    skinCards.forEach(c => { c.classList.toggle('active', c === card); c.setAttribute('aria-pressed', c === card ? 'true' : 'false'); });
  }
  skinCards.forEach(function(card){
    card.addEventListener('click', function(){
      applySkin(card);
      sound('click');
      track('skin_preview', {skin:card.dataset.name});
    });
  });

  // lightbox
  const lightbox = document.getElementById('lightbox');
  const lbImg = document.getElementById('lbImg');
  const lbClose = document.getElementById('lbClose');
  let lastFocus = null;
  function openLb(src, alt){
    lastFocus = document.activeElement;
    lbImg.src = src; lbImg.alt = alt || ''; lightbox.classList.add('open'); document.body.style.overflow='hidden';
    updateDock();
    lbClose.focus();
    sound('click');
  }
  function closeLb(){ lightbox.classList.remove('open'); lbImg.removeAttribute('src'); document.body.style.overflow=''; if(lastFocus) lastFocus.focus(); updateDock(); }
  document.querySelectorAll('[data-zoom]').forEach(el => {
    el.addEventListener('click', () => openLb(el.dataset.zoom, el.querySelector('img')?.alt));
    el.addEventListener('keydown', e => { if(e.key === 'Enter' || e.key === ' '){ e.preventDefault(); openLb(el.dataset.zoom, el.querySelector('img')?.alt); }});
  });
  lbClose.addEventListener('click', closeLb);
  lightbox.addEventListener('keydown', e => { if(e.key === 'Tab'){ e.preventDefault(); lbClose.focus(); } });
  lightbox.addEventListener('click', e => { if(e.target === lightbox) closeLb(); });
  window.addEventListener('keydown', e => { if(e.key === 'Escape' && lightbox.classList.contains('open')) closeLb(); });

  // ---------- version / hash sync from update.json ----------
  function nfmt(n){ return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','); }
  function shortSha(sha){ return sha ? sha.slice(0, 8) + '…' + sha.slice(-6) : ''; }
  async function syncUpdate(){
    try{
      const res = await fetch('update.json', {cache:'no-store'});
      if(!res.ok) return;
      const u = await res.json();
      if(!u || !u.version) return;
      const ver = String(u.version).replace(/^v/i, '');
      const size = Number(u.size) || 0;
      const sha = String(u.sha256 || '');
      document.querySelectorAll('[data-ver]').forEach(el => { el.textContent = ver; });
      if(size){
        document.querySelectorAll('[data-size]').forEach(el => { el.textContent = nfmt(size) + ' 字节'; });
        document.querySelectorAll('[data-size-bytes]').forEach(el => { el.textContent = nfmt(size) + ' BYTES'; });
        document.querySelectorAll('[data-size-mb]').forEach(el => { el.textContent = (size / 1000000).toFixed(1) + ' MB'; });
      }
      if(sha){
        document.querySelectorAll('[data-sha]').forEach(el => { el.textContent = sha; });
        document.querySelectorAll('[data-sha-short]').forEach(el => { el.textContent = shortSha(sha); });
        document.querySelectorAll('[data-copy]').forEach(btn => {
          if(/^[0-9a-f]{64}$/i.test(btn.dataset.copy || '')) btn.dataset.copy = sha;
        });
      }
      if(u.url){
        document.querySelectorAll('[data-download-link]').forEach(a => { a.href = u.url; });
      }
      const notes = document.getElementById('latestNotes');
      if(notes && Array.isArray(u.notes) && u.notes.length){
        notes.replaceChildren();
        u.notes.slice(0, 3).forEach(function(text){
          const li = document.createElement('li'); li.textContent = text; notes.appendChild(li);
        });
      }
      track('version_sync', {version:ver});
    }catch(e){}
  }
  syncUpdate();

  // particle 88
  const canvas = document.getElementById('dust');
  const ctx = canvas.getContext('2d');
  let W=0,H=0,DPR=1, particles=[], raf=0, last=0;
  const mouse = {x:-9999,y:-9999};
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function buildParticles(){
    const count = W < 1500 ? 300 : 1150;
    if(W < 1500){
      particles = Array.from({length:300}, () => {
        const x = Math.random()*W, y = Math.random()*H;
        return {x, y, tx:x, ty:y, vx:0, vy:0, s:Math.random()<.07?1.8:1, o:Math.random()<.055, d:Math.random()*Math.PI*2};
      });
      return;
    }
    const wide = W >= 1200;
    const tw = wide ? Math.min(W * .33, 520) : Math.min(W * .54, 700);
    const th = tw * .56;
    const off = document.createElement('canvas');
    off.width = Math.ceil(tw); off.height = Math.ceil(th);
    const octx = off.getContext('2d');
    octx.clearRect(0,0,off.width,off.height);
    octx.fillStyle = '#000';
    octx.textAlign = 'center';
    octx.textBaseline = 'middle';
    octx.font = '900 ' + Math.floor(th * .92) + 'px "Bahnschrift Condensed","Arial Narrow",Impact,sans-serif';
    octx.fillText('88', off.width/2, off.height/2 + th * .02);
    const data = octx.getImageData(0,0,off.width,off.height).data;
    const pts = [];
    const step = W < 700 ? 4 : 3;
    for(let y=0; y<off.height; y+=step){
      for(let x=0; x<off.width; x+=step){
        if(data[(y*off.width + x)*4 + 3] > 120) pts.push([x,y]);
      }
    }
    const cx = wide ? W * .45 : W * .64, cy = wide ? H * .66 : H * .47;
    const chosen = [];
    const stride = Math.max(1, Math.floor(pts.length / count));
    for(let i=0; i<pts.length && chosen.length<count; i+=stride){
      const p = pts[(i * 37 + 11) % pts.length];
      chosen.push(p);
    }
    particles = chosen.map((p, i) => {
      const tx = cx - tw/2 + p[0];
      const ty = cy - th/2 + p[1];
      return {
        x: Math.random()*W, y: Math.random()*H, tx, ty,
        vx: 0, vy: 0,
        s: Math.random() < .07 ? 2.1 : 1.35,
        o: Math.random() < .055,
        d: Math.random()*Math.PI*2
      };
    });
  }

  function resizeCanvas(){
    W = canvas.clientWidth; H = canvas.clientHeight;
    DPR = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.floor(W * DPR); canvas.height = Math.floor(H * DPR);
    ctx.setTransform(DPR,0,0,DPR,0,0);
    buildParticles();
    if(reduce){ draw(0, true); }
  }

  function draw(t, once){
    if(!W || !H) return;
    ctx.clearRect(0,0,W,H);
    const active = !once;
    for(const p of particles){
      if(active){
        const dx = p.x - p.tx, dy = p.y - p.ty;
        p.vx += (-dx * .012) + Math.cos(t*.001 + p.d) * .03;
        p.vy += (-dy * .012) + Math.sin(t*.0012 + p.d) * .03;
        const mx = p.x - mouse.x, my = p.y - mouse.y;
        const md = mx*mx + my*my;
        if(md < 5200 && md > 1){
          const f = (5200 - md) / 5200 * .9;
          p.vx += (mx / Math.sqrt(md)) * f;
          p.vy += (my / Math.sqrt(md)) * f;
        }
        p.vx *= .93; p.vy *= .93;
        p.x += p.vx; p.y += p.vy;
      } else { p.x = p.tx; p.y = p.ty; }
      ctx.fillStyle = p.o ? 'rgba(11,12,11,.78)' : 'rgba(11,12,11,.50)';
      if(p.s > 1.5){
        ctx.beginPath(); ctx.arc(p.x,p.y,p.s,0,6.283); ctx.fill();
      } else {
        ctx.fillRect(p.x,p.y,p.s,p.s);
      }
    }
  }
  function loop(t){
    if(t - last > 32){
      draw(t,false); last = t;
    }
    if((window.scrollY || 0) < window.innerHeight * 1.3) raf = requestAnimationFrame(loop);
    else raf = 0;
  }
  function start(){ if(!raf) raf = requestAnimationFrame(loop); }
  window.addEventListener('resize', () => { resizeCanvas(); if(reduce) draw(0,true); });
  window.addEventListener('mousemove', e => {
    const r = canvas.getBoundingClientRect();
    mouse.x = e.clientX - r.left; mouse.y = e.clientY - r.top;
  }, {passive:true});
  window.addEventListener('mouseout', () => { mouse.x=-9999; mouse.y=-9999; });
  resizeCanvas();
  if(reduce){ draw(0,true); } else { start(); }

  // dot lab
  (function initDotLab(){
    const dc = document.getElementById('dotCanvas');
    if(!dc) return;
    const dctx = dc.getContext('2d');
    const stage = dc.parentElement;
    const dotForm = document.getElementById('dotLabForm');
    const dotInput = document.getElementById('dotLabInput');
    const dotStatus = document.getElementById('dotLabStatus');
    const dotCount = document.getElementById('dotLabCount');
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const mouse = {x:-9999,y:-9999};
    let W=0, H=0, DPR=1, parts=[], raf=0, dlast=0, text='', visible=false;
    let timer=0, composing=false;

    function cleanValue(value){
      return [...String(value || '').replace(/\s+/g,' ').trim()].slice(0,8).join('');
    }
    function currentLabel(){ return text || '88'; }
    function setStatus(value){
      if(dotStatus) dotStatus.textContent = value;
      if(dotCount) dotCount.textContent = text ? 'DOT / TEXT' : 'DOT / 88';
    }

    function rebuild(){
      const old = parts;
      if(!W || !H){ parts=[]; return; }
      const label = currentLabel();
      const maxW = W * .76;
      const maxH = H * .70;
      const pad = 6;
      const off = document.createElement('canvas');
      off.width = Math.ceil(maxW) + pad*2;
      off.height = Math.ceil(maxH) + pad*2;
      const octx = off.getContext('2d');
      octx.clearRect(0,0,off.width,off.height);
      octx.fillStyle = '#000';
      octx.textAlign = 'center';
      octx.textBaseline = 'middle';
      const cjk = /[\u2E80-\u9FFF\uF900-\uFAFF]/.test(label);
      const family = cjk
        ? '"Microsoft YaHei","PingFang SC","Noto Sans SC",sans-serif'
        : '"Arial Black","Segoe UI Black","Bahnschrift Condensed",sans-serif';
      const weight = cjk ? 600 : 900;
      const chars = [...label];
      let fontSize = maxH * .92;
      function setFont(size){ octx.font = weight + ' ' + Math.floor(size) + 'px ' + family; }
      function layout(size){
        setFont(size);
        const widths = chars.map(ch => octx.measureText(ch).width);
        const tracking = size * (cjk ? .04 : .04);
        return {widths, tracking, total: widths.reduce((a,b) => a+b, 0) + tracking * (chars.length - 1)};
      }
      let metrics = layout(fontSize);
      if(metrics.total > maxW){
        fontSize *= maxW / metrics.total;
        metrics = layout(fontSize);
      }
      setFont(fontSize);
      let penX = off.width/2 - metrics.total/2;
      for(let i=0; i<chars.length; i++){
        const w = metrics.widths[i] || octx.measureText(chars[i]).width;
        octx.fillText(chars[i], penX + w/2, off.height/2 + fontSize*.03);
        penX += w + metrics.tracking;
      }
      const data = octx.getImageData(0,0,off.width,off.height).data;
      const edge = [], fill = [];
      const iw = off.width, ih = off.height;
      for(let y=0; y<ih; y+=1){
        for(let x=0; x<iw; x+=1){
          if(data[(y*iw+x)*4+3] < 90) continue;
          const aL = x > 0 ? data[(y*iw+x-1)*4+3] : 0;
          const aR = x < iw-1 ? data[(y*iw+x+1)*4+3] : 0;
          const aU = y > 0 ? data[((y-1)*iw+x)*4+3] : 0;
          const aD = y < ih-1 ? data[((y+1)*iw+x)*4+3] : 0;
          if(aL < 40 || aR < 40 || aU < 40 || aD < 40) edge.push([x,y,1]);
          else fill.push([x,y,0]);
        }
      }
      const pts = edge.length ? edge : fill;
      if(!pts.length){ parts=[]; return; }
      const natural = cjk ? W*H/105 : W*H/170;
      const baseCount = cjk
        ? Math.min(3000, Math.max(1500, Math.round(natural)))
        : Math.min(2200, Math.max(800, Math.round(natural)));
      const growth = cjk ? .28 : .16;
      const count = Math.min(
        cjk ? 4600 : 3800,
        Math.round(baseCount * (1 + Math.max(0, chars.length - 1) * growth))
      );
      const chosen = [];
      const edgeN = Math.min(Math.round(count * (cjk ? .48 : .30)), edge.length);
      const fillN = Math.min(count - edgeN, fill.length);
      function pickEven(list, n){
        const out = [];
        if(!list.length || n <= 0) return out;
        const step = list.length / n;
        for(let i=0; i<n; i++){
          const idx = Math.min(list.length - 1, Math.floor((i + .12 + Math.random() * .76) * step));
          out.push(list[idx]);
        }
        return out;
      }
      chosen.push(...pickEven(edge, edgeN), ...pickEven(fill, fillN));
      if(chosen.length < count){
        const all = pickEven(pts, count - chosen.length);
        chosen.push(...all);
      }
      const cx = W * .5, cy = H * .53;
      parts = chosen.map((p, i) => {
        const prev = old.length ? old[i % old.length] : null;
        const tx = cx - off.width/2 + p[0] + (Math.random()-.5)*(p[2] ? 1.2 : 2.2);
        const ty = cy - off.height/2 + p[1] + (Math.random()-.5)*(p[2] ? 1.2 : 2.2);
        return {
          x: prev ? prev.x : tx, y: prev ? prev.y : ty,
          tx, ty,
          vx: prev ? prev.vx : 0,
          vy: prev ? prev.vy : 0,
          s: p[2] ? (cjk ? 3.0 : 2.0) : (Math.random() < .10 ? (cjk ? 4.8 : 3.0) : (cjk ? 3.8 : 2.3)),
          o: Math.random() < .04,
          d: Math.random()*Math.PI*2
        };
      });
    }

    function draw(t, once){
      if(!W || !H) return;
      dctx.clearRect(0,0,W,H);
      const active = !once;
      const radius = Math.max(54, Math.min(140, W*.18));
      for(const p of parts){
        if(active){
          const dx = p.x - p.tx, dy = p.y - p.ty;
          p.vx += (-dx*.045) + Math.cos(t*.001 + p.d)*.008;
          p.vy += (-dy*.045) + Math.sin(t*.0012 + p.d)*.008;
          const mx = p.x - mouse.x, my = p.y - mouse.y;
          const md = mx*mx + my*my;
          if(md < radius*radius && md > 1){
            const f = (radius*radius - md) / (radius*radius) * .75;
            p.vx += (mx / Math.sqrt(md)) * f;
            p.vy += (my / Math.sqrt(md)) * f;
          }
          p.vx *= .88; p.vy *= .88;
          p.x += p.vx; p.y += p.vy;
          if(Math.abs(p.x-p.tx) + Math.abs(p.y-p.ty) < 1.2){
            p.x = p.tx; p.y = p.ty; p.vx = 0; p.vy = 0;
          }
        } else { p.x = p.tx; p.y = p.ty; }
        dctx.fillStyle = p.o ? 'rgba(11,12,11,.84)' : 'rgba(11,12,11,.68)';
        if(p.s > 1.8){
          dctx.beginPath(); dctx.arc(p.x,p.y,p.s,0,6.283); dctx.fill();
        } else {
          dctx.fillRect(p.x,p.y,p.s,p.s);
        }
      }
    }

    function loop(t){
      if(!visible){ raf=0; return; }
      if(t - dlast > 32){ draw(t,false); dlast=t; }
      raf = requestAnimationFrame(loop);
    }
    function start(){
      if(reduceMotion) return;
      if(!raf && visible) raf = requestAnimationFrame(loop);
    }
    function resize(){
      const cw = stage.clientWidth, ch = stage.clientHeight;
      if(!cw || !ch || (cw === W && ch === H)) return;
      W = cw; H = ch;
      DPR = Math.min(window.devicePixelRatio || 1, 2);
      dc.width = Math.floor(W*DPR); dc.height = Math.floor(H*DPR);
      dctx.setTransform(DPR,0,0,DPR,0,0);
      rebuild();
      if(reduceMotion) draw(0,true); else start();
    }

    function apply(value, syncInput){
      text = cleanValue(value);
      if(syncInput && dotInput) dotInput.value = text;
      setStatus(text ? '当前：' + text : '当前：88');
      rebuild();
      if(reduceMotion) draw(0,true); else start();
    }

    function scatter(){
      if(!parts.length || reduceMotion) return;
      for(const p of parts){
        const angle = Math.random()*Math.PI*2;
        const force = 5 + Math.random()*10;
        p.vx += Math.cos(angle)*force;
        p.vy += Math.sin(angle)*force;
      }
      start();
      setStatus('抖散 / 正在聚拢');
      window.clearTimeout(timer);
      timer = window.setTimeout(() => setStatus(text ? '当前：' + text : '当前：88'), 1400);
    }

    if(dotForm && dotInput){
      dotInput.addEventListener('compositionstart', () => { composing=true; });
      dotInput.addEventListener('compositionend', () => {
        composing=false;
        window.clearTimeout(timer);
        timer = window.setTimeout(() => apply(dotInput.value, false), 300);
      });
      dotInput.addEventListener('input', () => {
        if(composing) return;
        window.clearTimeout(timer);
        timer = window.setTimeout(() => apply(dotInput.value, false), 300);
      });
      dotInput.addEventListener('keydown', e => {
        if(e.key === 'Escape'){
          e.preventDefault();
          apply('', true);
          dotInput.blur();
        }
      });
      dotForm.addEventListener('submit', e => {
        e.preventDefault();
        apply(dotInput.value, true);
      });
      document.querySelectorAll('[data-dot-quick]').forEach(btn => {
        btn.addEventListener('click', () => {
          const value = btn.getAttribute('data-dot-quick') || '88';
          if(dotInput) dotInput.value = value;
          apply(value, false);
        });
      });
    }
    const scatterBtn = document.getElementById('dotLabScatter');
    if(scatterBtn) scatterBtn.addEventListener('click', scatter);
    const resetBtn = document.getElementById('dotLabReset');
    if(resetBtn) resetBtn.addEventListener('click', () => {
      if(dotInput) dotInput.value = '';
      apply('', false);
      if(dotInput) dotInput.focus();
    });
    dc.addEventListener('click', scatter);
    dc.addEventListener('pointermove', e => {
      const r = dc.getBoundingClientRect();
      mouse.x = e.clientX - r.left;
      mouse.y = e.clientY - r.top;
    }, {passive:true});
    dc.addEventListener('pointerleave', () => { mouse.x=-9999; mouse.y=-9999; });
    if('ResizeObserver' in window){
      new ResizeObserver(resize).observe(stage);
    } else {
      window.addEventListener('resize', resize);
    }
    if('IntersectionObserver' in window){
      new IntersectionObserver(entries => {
        visible = entries.some(entry => entry.isIntersecting);
        if(visible) start();
      }, {rootMargin:'160px 0px'}).observe(stage);
    } else {
      visible = true;
    }
    resize();
  })();

  // dot duel
  (function initDotDuel(){
    const dc = document.getElementById('duelCanvas');
    if(!dc) return;
    const ctx = dc.getContext('2d');
    const stage = dc.parentElement;
    const roundEl = document.getElementById('duelRound');
    const moveEl = document.getElementById('duelMove');
    const skipBtn = document.getElementById('duelSkip');
    const toggleBtn = document.getElementById('duelToggle');
    const soundBtn = document.getElementById('duelSound');
    const bigEl = document.getElementById('duelBig');
    const bigZhEl = document.getElementById('duelBigZh');
    const comboEl = document.getElementById('duelCombo');
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const DURATION = 8.4;
    const HITS = [
      {t:1.18, s:'L', part:'head', power:1, limb:'handF', word:'JAB', zh:'刺拳'},
      {t:2.05, s:'R', part:'head', power:1, limb:'handF', word:'CROSS', zh:'直拳'},
      {t:3.26, s:'L', part:'legF', power:.9, limb:'footF', word:'SWEEP', zh:'扫腿'},
      {t:4.35, s:'L', part:'armB', power:.95, limb:'handF', word:'HOOK', zh:'勾拳'},
      {t:4.95, s:'R', part:'head', power:1.1, limb:'handF', word:'UPPER', zh:'上勾'},
      {t:5.85, s:'both', part:'head', power:1.3, limb:'handF', word:'CLASH', zh:'对撞'},
      {t:6.78, s:'L', part:'legB', power:.9, limb:'footF', word:'LOW', zh:'低扫'},
      {t:7.35, s:'L', part:'head', power:1.15, limb:'footF', word:'KICK', zh:'飞踢'}
    ];
    const MOVES = [
      {t:0.00, label:'GUARD / 对峙'},
      {t:0.82, label:'JAB / 刺拳'},
      {t:1.95, label:'CROSS / 直拳'},
      {t:2.85, label:'SWEEP / 扫腿'},
      {t:4.08, label:'HOOK / 勾拳'},
      {t:4.60, label:'UPPERCUT / 上勾'},
      {t:5.55, label:'CLASH / 对撞'},
      {t:6.68, label:'LOW SWEEP / 低扫'},
      {t:7.05, label:'JUMP KICK / 飞踢'},
      {t:7.90, label:'RESET / 归位'}
    ];
    const basePose = {
      lean:0, crouch:0, shift:0, tilt:0, lift:0,
      handF:[28,-88], handB:[8,-74], footF:[16,80], footB:[-22,80]
    };
    const mk = o => Object.assign({}, basePose, o);
    const POSES = {
      guard: mk({lean:.02, crouch:0, handF:[18,-94], handB:[2,-66]}),
      block: mk({lean:-.08, crouch:.14, shift:-2, handF:[20,-102], handB:[12,-90], footF:[14,80], footB:[-18,80]}),
      jab: mk({lean:.16, crouch:0, shift:16, tilt:.05, handF:[80,-92], handB:[8,-78], footF:[26,80], footB:[-16,80]}),
      cross: mk({lean:.12, crouch:0, shift:14, tilt:.02, handF:[74,-98], handB:[20,-86], footF:[22,80], footB:[-14,80]}),
      kick: mk({lean:-.2, crouch:0, shift:18, tilt:-.06, handF:[18,-96], handB:[0,-80], footF:[72,-42], footB:[-6,80]}),
      duck: mk({lean:-.08, crouch:.75, shift:-4, tilt:.02, handF:[20,-86], handB:[8,-78], footF:[18,80], footB:[-20,80]}),
      recoil: mk({lean:-.22, crouch:.08, shift:-10, tilt:-.04, handF:[14,-98], handB:[2,-84], footF:[8,80], footB:[-28,80]}),
      clash: mk({lean:.10, crouch:.03, shift:10, tilt:.03, handF:[72,-90], handB:[30,-84], footF:[-2,80], footB:[-30,80]}),
      sweep: mk({lean:-.12, crouch:.48, shift:20, tilt:-.04, handF:[30,-70], handB:[-18,-58], footF:[80,66], footB:[-14,80]}),
      sweepReach: mk({lean:-.16, crouch:.55, shift:24, tilt:-.06, handF:[38,-64], handB:[-24,-52], footF:[58,62], footB:[-10,80]}),
      hop: mk({lift:50, crouch:.15, shift:-2, lean:-.05, handF:[20,-86], handB:[2,-72], footF:[18,-16], footB:[-12,-20]}),
      land: mk({lift:6, crouch:.38, shift:4, lean:.02, handF:[26,-78], handB:[8,-68], footF:[24,72], footB:[-18,72]}),
      dash: mk({lean:.32, crouch:.12, shift:26, tilt:.04, handF:[52,-62], handB:[18,-80], footF:[32,80], footB:[-8,80]}),
      hook: mk({lean:.26, crouch:.14, shift:24, tilt:.10, handF:[84,-38], handB:[22,-82], footF:[30,80], footB:[-10,80]}),
      guardLow: mk({lean:.02, crouch:.34, shift:0, handF:[14,-62], handB:[4,-54], footF:[16,80], footB:[-20,80]}),
      uppercutPrep: mk({lean:-.12, crouch:.62, shift:6, handF:[24,-84], handB:[10,-46], footF:[18,80], footB:[-22,80]}),
      uppercut: mk({lean:-.02, crouch:.25, shift:28, tilt:-.10, handF:[84,-104], handB:[18,-76], footF:[26,80], footB:[-12,80]}),
      knee: mk({lean:.18, crouch:.28, shift:20, handF:[40,-104], handB:[8,-76], footF:[46,-26], footB:[-8,80]}),
      guardHigh: mk({lean:.06, crouch:.12, shift:0, handF:[26,-116], handB:[14,-106], footF:[16,80], footB:[-20,80]}),
      launch: mk({lean:-.10, crouch:.64, shift:6, handF:[20,-58], handB:[0,-62], footF:[18,80], footB:[-24,80]}),
      jumpKick: mk({lift:52, lean:-.28, crouch:.18, shift:30, tilt:-.12, handF:[24,-102], handB:[0,-82], footF:[92,-66], footB:[-6,40]}),
      recoilBody: mk({lean:-.32, crouch:.38, shift:-18, tilt:-.05, handF:[18,-70], handB:[0,-62], footF:[10,80], footB:[-30,80]})
    };
    const KEYS = [
      {t:0.00, L:'guard', R:'guard', round:1},
      {t:0.55, L:'guard', R:'guard'},
      {t:0.82, L:'jab',   R:'block'},
      {t:1.14, L:'jab',   R:'block'},
      {t:1.55, L:'guard', R:'guard'},
      {t:1.95, L:'block', R:'cross'},
      {t:2.35, L:'block', R:'cross'},
      {t:2.62, L:'guard', R:'guard'},
      {t:2.85, L:'sweep', R:'hop'},
      {t:3.08, L:'sweepReach', R:'land'},
      {t:3.26, L:'sweepReach', R:'land'},
      {t:3.45, L:'guard', R:'recoil'},
      {t:3.78, L:'guard', R:'guard', round:2},
      {t:4.08, L:'dash',  R:'guardLow'},
      {t:4.28, L:'hook',  R:'guardLow'},
      {t:4.62, L:'guard', R:'uppercutPrep'},
      {t:4.88, L:'block', R:'uppercut'},
      {t:5.18, L:'guard', R:'guard'},
      {t:5.55, L:'guard', R:'guard', round:3},
      {t:5.70, L:'dash',  R:'dash'},
      {t:5.85, L:'clash', R:'clash'},
      {t:6.10, L:'recoil', R:'recoil'},
      {t:6.45, L:'guard', R:'guard'},
      {t:6.68, L:'sweep', R:'land'},
      {t:6.98, L:'guard', R:'recoil'},
      {t:7.05, L:'launch', R:'guardHigh'},
      {t:7.22, L:'jumpKick', R:'guardHigh'},
      {t:7.58, L:'land', R:'recoil'},
      {t:7.90, L:'guard', R:'guard'},
      {t:8.20, L:'guard', R:'guard'},
      {t:8.40, L:'guard', R:'guard'}
    ];
    let W=0, H=0, DPR=1, SCALE=1, GROUND=0;
    const previewT = parseFloat(new URLSearchParams(location.search).get('t'));
    const previewMode = !reduce && Number.isFinite(previewT) && previewT >= 0 && previewT <= DURATION;
    let t0=performance.now(), offset=previewMode ? previewT : (reduce?.65:0), playing=!reduce && !previewMode, visible=false, raf=0;
    let lastRound=0, lastMove='', lastBig=-1, lastTime=offset, bonusUntil=0;
    let soundOn=false, audio=null, dotInvert=false, figAlpha=1, ghostMode=false;
    const sounded=new Set(), whooshed=new Set(), userBursts=[];
    const pointer={x:.5, y:.5, active:false};
    let riserDone=false;
    const REFORM=.78;
    let curFx=null;

    const CAM = (()=>{
      const list=[{t:0, z:1, x:0, y:0}];
      HITS.forEach(hit=>{
        const clash = hit.s === 'both';
        const leg = hit.part === 'legF' || hit.part === 'legB';
        const side = clash ? 0 : (hit.s === 'L' ? 1 : -1);
        const x = side * (leg ? 20 : 28);
        const y = clash ? 0 : (leg ? 18 : -13);
        const z = clash ? .94 : (leg ? 1.08 : 1.15);
        list.push({t:Math.max(0, hit.t-.20), z:1, x:0, y:0});
        list.push({t:hit.t-.07, z, x, y});
        list.push({t:hit.t+.10, z, x, y});
        list.push({t:Math.min(DURATION, hit.t+.32), z:1, x:0, y:0});
      });
      return list.sort((a,b)=>a.t-b.t);
    })();

    const easeOut = u => 1 - Math.pow(1 - u, 3);
    const easeInOut = u => u < .5 ? 4*u*u*u : 1 - Math.pow(-2*u + 2, 3) / 2;

    function poseClock(time){
      const stop = freezeAt(time);
      if(stop) return stop.t;
      for(const hit of HITS){
        const d = signedAge(hit, time);
        if(d >= -.20 && d < 0){
          return hit.t - .20 + (d + .20) * .55;
        }
      }
      return time;
    }

    function cameraAt(time){
      let a = CAM[0], b = CAM[CAM.length - 1];
      for(let i=0; i<CAM.length-1; i++){
        if(CAM[i+1].t <= time){ a = CAM[i+1]; }
        else { b = CAM[i+1]; break; }
      }
      if(!b || b.t === a.t) return a;
      const u = Math.max(0, Math.min(1, (time-a.t)/(b.t-a.t)));
      const e = u*u*(3-2*u);
      return {
        z: a.z + (b.z-a.z)*e,
        x: a.x + (b.x-a.x)*e,
        y: a.y + (b.y-a.y)*e
      };
    }
    function freezeAt(time){
      for(const hit of HITS){
        const age = hitAge(hit, time);
        if(age >= 0 && age < .055 + hit.power * .035) return hit;
      }
      return null;
    }
    function activeFlash(time){
      for(const hit of HITS){
        const age = hitAge(hit, time);
        if(age >= 0 && age < .045) return {clash: true};
      }
      return null;
    }
    function hitUI(time){
      if(performance.now() < bonusUntil) return;
      let current = -1;
      for(let i=0; i<HITS.length; i++){
        const age = hitAge(HITS[i], time);
        if(age >= 0 && age < .50) current = i;
      }
      if(current < 0 || current === lastBig) return;
      lastBig = current;
      const hit = HITS[current];
      if(bigEl) bigEl.textContent = hit.word;
      if(bigZhEl) bigZhEl.textContent = hit.zh;
      if(comboEl) comboEl.textContent = 'HIT / 0' + (current + 1);
      for(const el of [bigEl, bigZhEl, comboEl]){
        if(!el) continue;
        el.classList.remove('hit');
      }
      if(bigEl) void bigEl.offsetWidth;
      for(const el of [bigEl, bigZhEl, comboEl]){
        if(el) el.classList.add('hit');
      }
    }
    function showBonus(){
      lastBig = 99;
      bonusUntil = performance.now() + 760;
      if(bigEl) bigEl.textContent = 'BONUS';
      if(bigZhEl) bigZhEl.textContent = '补一拳';
      if(comboEl) comboEl.textContent = 'BONUS / +01';
      for(const el of [bigEl, bigZhEl, comboEl]){
        if(!el) continue;
        el.classList.remove('hit');
      }
      if(bigEl) void bigEl.offsetWidth;
      for(const el of [bigEl, bigZhEl, comboEl]){
        if(el) el.classList.add('hit');
      }
    }
    function resetDuelUI(){
      lastBig = -1;
      bonusUntil = 0;
      sounded.clear();
      whooshed.clear();
      userBursts.length = 0;
      riserDone = false;
      if(comboEl) comboEl.textContent = 'COMBO / 00';
      for(const el of [bigEl, bigZhEl, comboEl]){
        if(el) el.classList.remove('hit');
      }
    }
    function targetsSide(hit, side){
      const target = hit.s === 'L' ? 'R' : hit.s === 'R' ? 'L' : 'B';
      return target === 'B' || target === side;
    }
    function hitAge(hit, time){
      return time - hit.t;
    }
    function signedAge(hit, time){
      let a = time - hit.t;
      if(a > DURATION * .5) a -= DURATION;
      if(a < -DURATION * .5) a += DURATION;
      return a;
    }
    function beginPart(part, side, cx, cy, time){
      curFx = null;
      for(const hit of HITS){
        if(hit.part !== part || !targetsSide(hit, side)) continue;
        const age = hitAge(hit, time);
        if(age < 0 || age >= REFORM) continue;
        if(!curFx || age < curFx.age){
          curFx = {age, seed: hit.t * 13 + 7, power: hit.power, cx, cy, part, side};
        }
      }
    }

    function hash(i, s){
      const v = Math.sin((i + 1) * 127.1 + (s + 1) * 311.7) * 43758.5453;
      return v - Math.floor(v);
    }
    function mixPose(a, b, u){
      const out = {};
      for(const k of ['lean','crouch','shift','tilt','lift']) out[k] = a[k] + (b[k] - a[k]) * u;
      for(const k of ['handF','handB','footF','footB']){
        out[k] = [a[k][0] + (b[k][0] - a[k][0]) * u, a[k][1] + (b[k][1] - a[k][1]) * u];
      }
      return out;
    }
    function poseAt(side, time){
      let i = 0;
      while(i < KEYS.length - 1 && KEYS[i+1].t <= time) i++;
      const a = KEYS[i], b = KEYS[Math.min(i + 1, KEYS.length - 1)];
      const u = b.t > a.t ? (time - a.t) / (b.t - a.t) : 0;
      return mixPose(POSES[a[side]], POSES[b[side]], u * u * (3 - 2 * u));
    }
    function roundAt(time){
      let r = 1;
      for(const k of KEYS) if(k.round && k.t <= time) r = k.round;
      return r;
    }
    function moveAt(time){
      let label = MOVES[0].label;
      for(const m of MOVES) if(m.t <= time) label = m.label;
      return label;
    }
    function rawDot(x, y, r, strong, alpha){
      if(alpha <= .004) return;
      ctx.fillStyle = dotInvert
        ? (strong ? 'rgba(255,255,255,' + (alpha * .96).toFixed(3) + ')' : 'rgba(255,255,255,' + (alpha * .86).toFixed(3) + ')')
        : (strong ? 'rgba(11,12,11,' + (alpha * .95).toFixed(3) + ')' : 'rgba(11,12,11,' + (alpha * .84).toFixed(3) + ')');
      ctx.beginPath();
      ctx.arc(x, y, r, 0, 6.283);
      ctx.fill();
    }
    function dot(x, y, r, strong, alpha){
      alpha *= figAlpha;
      if(curFx){
        const fx = curFx;
        const d = Math.hypot(x - fx.cx, y - fx.cy);
        const delay = Math.min(.14, d * .0011);
        const a = fx.age - delay;
        if(a > 0){
          const total = Math.max(.2, REFORM - delay);
          const span = Math.max(.14, total - .42);
          const sc = 1 - Math.pow(1 - Math.min(a / .24, 1), 4);
          const ret = a < .42 ? 1 : 1 - easeInOut(Math.min(1, (a - .42) / span));
          const n1 = hash(Math.round(x * 9) + Math.round(y * 7), Math.round(fx.seed));
          const n2 = hash(Math.round(y * 11) - Math.round(x * 5), Math.round(fx.seed) + 17);
          const chunk = hash(Math.round(x * 3) - Math.round(y * 4), Math.round(fx.seed) + 31) > .87;
          const amp = (8 + n1 * 21) * fx.power;
          const swirl = a < .42 ? a * 1.45 : .7 - Math.min(1, (a - .42) / span) * 1.15;
          const ang = Math.atan2(y - fx.cy, x - fx.cx) + (n2 - .5) * .95 + swirl;
          const drift = amp * sc * ret;
          x += Math.cos(ang) * drift - drift * .32;
          y += Math.sin(ang) * drift - drift * .10;
          const fade = a < .10
            ? 1 - .28 * easeOut(a / .10)
            : a < .30
              ? .72 * (1 - easeInOut((a - .10) / .20))
              : a < .42
                ? 0
                : easeOut(Math.min(1, (a - .42) / span));
          alpha *= Math.max(0, fade);
          r *= 1 + (chunk ? .78 : 0) * sc * ret + .30 * (1 - Math.min(1, a / .12)) - .22 * (a > .12 && a < .42 ? 1 : 0);
        }
      }
      rawDot(x, y, r, strong, alpha);
    }
    function bone(a, b, seed, spacing, alpha){
      const dx = b[0] - a[0], dy = b[1] - a[1];
      const d = Math.hypot(dx, dy);
      if(d < .01) return;
      const n = Math.max(2, Math.round(d / spacing));
      const nx = -dy / d, ny = dx / d;
      for(let i=0; i<=n; i++){
        const u = i / n;
        const j = (hash(i, seed) - .5) * 1.5;
        const ox = a[0] + dx * u + nx * j;
        const oy = a[1] + dy * u + ny * j;
        const rr = 1.14 + hash(i, seed + 3) * .92;
        dot(ox, oy, rr, hash(i, seed + 7) > .965, alpha);
      }
    }
    function cluster(p, seed, alpha){
      for(let i=0; i<8; i++){
        const ang = hash(i, seed) * 6.283;
        const rad = hash(i, seed + 2) * 3.4;
        dot(p[0] + Math.cos(ang) * rad, p[1] + Math.sin(ang) * rad, 1.12 + hash(i, seed + 5) * .82, hash(i, seed + 8) > .955, alpha);
      }
    }
    function node(p, seed){
      dot(p[0], p[1], 1.55, true, .96);
      dot(p[0] + (hash(1, seed) - .5) * 1.8, p[1] + (hash(2, seed) - .5) * 1.8, 1.05, false, .82);
    }
    function fist(p, elbow, seed, alpha){
      const dx = p[0] - elbow[0], dy = p[1] - elbow[1];
      const d = Math.max(.01, Math.hypot(dx, dy));
      const ux = dx / d, uy = dy / d;
      const nx = -uy, ny = ux;
      for(let i=0; i<5; i++){
        const ang = (i / 5) * 6.283 + .6;
        dot(p[0] + Math.cos(ang) * 2.2, p[1] + Math.sin(ang) * 2.2, 1.08 + hash(i, seed) * .44, false, alpha * .96);
      }
      for(let i=0; i<3; i++){
        dot(p[0] + ux * 2.7 + nx * (i - 1) * 1.3, p[1] + uy * 2.7 + ny * (i - 1) * 1.3, 1.2, i === 1, alpha);
      }
      dot(p[0] - ux * 2.9, p[1] - uy * 2.9, 1.04, false, alpha * .78);
    }
    function foot(p, seed, alpha){
      for(let i=0; i<4; i++){
        const ang = (i / 4) * 6.283 + .9;
        dot(p[0] + Math.cos(ang) * 2.1, p[1] + Math.sin(ang) * 1.9, 1.06 + hash(i, seed) * .36, false, alpha * .95);
      }
      for(let i=0; i<4; i++){
        dot(p[0] + 2.2 + i * 1.15, p[1] + .75 + (hash(i, seed + 2) - .5) * .45, .98 + hash(i, seed + 3) * .34, false, alpha);
      }
      for(const oy of [-.9, .1, 1.0]){
        dot(p[0] + 7.1, p[1] + oy, 1.05, false, alpha * .9);
      }
      dot(p[0] - 3.1, p[1] + .9, 1.0, false, alpha * .78);
    }
    function joint(a, b, len, pref){
      const dx = b[0] - a[0], dy = b[1] - a[1];
      const d = Math.max(.01, Math.hypot(dx, dy));
      const half = d * .5;
      const h = Math.sqrt(Math.max(0, len * len - half * half)) * .9;
      let px = -dy / d, py = dx / d;
      if(pref === 'down' && py < 0){ px = -px; py = -py; }
      if(pref === 'forward' && px < 0){ px = -px; py = -py; }
      if(pref === 'back' && px > 0){ px = -px; py = -py; }
      return [a[0] + dx * .5 + px * h, a[1] + dy * .5 + py * h];
    }
    function figure(cx, dir, pose, seed, side, time){
      ctx.save();
      ctx.translate(cx + dir * pose.shift * SCALE, GROUND - (80 + pose.lift) * SCALE);
      ctx.scale(dir * SCALE, SCALE);

      const torsoLen = 58 * (1 - pose.crouch * .26);
      const chest = [Math.sin(pose.lean) * torsoLen, -Math.cos(pose.lean) * torsoLen + pose.crouch * 8];
      const neck = [chest[0] + Math.sin(pose.lean) * 12, chest[1] - Math.cos(pose.lean) * 12];
      const tilt = pose.lean + pose.tilt;
      const head = [neck[0] + Math.sin(tilt) * 19, neck[1] - Math.cos(tilt) * 19];
      const shF = [chest[0] + 3, chest[1] - 1];
      const shB = [chest[0] - 3, chest[1]];
      const hipF = [3, 0], hipB = [-3, 0];

      const elbowF = joint(shF, pose.handF, 31, 'down');
      const elbowB = joint(shB, pose.handB, 31, 'back');
      const kneeF = joint(hipF, pose.footF, 46, 'forward');
      const kneeB = joint(hipB, pose.footB, 46, 'forward');

      if(!ghostMode) beginPart('torso', side, chest[0] * .5, chest[1] * .55, time);
      bone([0, 0], chest, seed, 1.85, 1);
      bone(shB, shF, seed + 1, 1.8, 1);
      bone(hipB, hipF, seed + 2, 1.8, 1);
      bone(neck, [head[0], head[1] + 13], seed + 3, 1.7, 1);
      node(shF, seed + 20);
      node(shB, seed + 24);
      node(hipF, seed + 28);
      node(hipB, seed + 32);

      if(!ghostMode) beginPart('armF', side, (shF[0] + pose.handF[0]) * .5, (shF[1] + pose.handF[1]) * .5, time);
      bone(shF, elbowF, seed + 4, 2.0, 1);
      bone(elbowF, pose.handF, seed + 5, 2.0, 1);
      fist(pose.handF, elbowF, seed + 15, 1);

      if(!ghostMode) beginPart('armB', side, (shB[0] + pose.handB[0]) * .5, (shB[1] + pose.handB[1]) * .5, time);
      bone(shB, elbowB, seed + 6, 2.0, 1);
      bone(elbowB, pose.handB, seed + 7, 2.0, 1);
      fist(pose.handB, elbowB, seed + 16, 1);

      if(!ghostMode) beginPart('legF', side, (hipF[0] + pose.footF[0]) * .5, (hipF[1] + pose.footF[1]) * .5, time);
      bone(hipF, kneeF, seed + 8, 2.0, 1);
      bone(kneeF, pose.footF, seed + 9, 2.0, 1);
      foot(pose.footF, seed + 17, 1);
      node(kneeF, seed + 36);
      if(pose.footF[1] < 70) cluster(kneeF, seed + 19, .9);

      if(!ghostMode) beginPart('legB', side, (hipB[0] + pose.footB[0]) * .5, (hipB[1] + pose.footB[1]) * .5, time);
      bone(hipB, kneeB, seed + 10, 2.0, 1);
      bone(kneeB, pose.footB, seed + 11, 2.0, 1);
      foot(pose.footB, seed + 18, 1);
      node(kneeB, seed + 40);

      if(!ghostMode) beginPart('head', side, head[0], head[1], time);
      const hc = Math.cos(tilt), hs = Math.sin(tilt);
      for(let i=0; i<40; i++){
        const ang = (i / 40) * 6.283;
        const ex = Math.cos(ang) * (12.1 + hash(i, seed + 12) * 1.1);
        const ey = Math.sin(ang) * (13.7 + hash(i, seed + 12) * 1.1);
        dot(head[0] + ex * hc - ey * hs, head[1] + ex * hs + ey * hc, 1.24 + hash(i, seed + 13) * .72, hash(i, seed + 14) > .97, 1);
      }
      for(let i=0; i<13; i++){
        const ang = (i / 13) * 6.283 + .35;
        const ex = Math.cos(ang) * 8.2;
        const ey = Math.sin(ang) * 8.9;
        dot(head[0] + ex * hc - ey * hs, head[1] + ex * hs + ey * hc, 1.02, false, .66);
      }
      for(let i=0; i<9; i++){
        const ang = (i / 9) * 6.283 + .45;
        const ex = Math.cos(ang) * 4.7;
        const ey = Math.sin(ang) * 5.2;
        dot(head[0] + ex * hc - ey * hs, head[1] + ex * hs + ey * hc, .98, false, .74);
      }
      for(let i=0; i<3; i++){
        const ang = i * 2.094 - 1.2;
        dot(head[0] + Math.cos(ang) * 2.2, head[1] + Math.sin(ang) * 2.0, .94, false, .78);
      }
      const eyeX = 6.6, eyeY = -2.8;
      dot(head[0] + eyeX * hc - eyeY * hs, head[1] + eyeX * hs + eyeY * hc, 1.55, true, .98);
      dot(head[0] + (eyeX - .15) * hc - (eyeY - 3.6) * hs, head[1] + (eyeX - .15) * hs + (eyeY - 3.6) * hc, 1.02, false, .72);

      ctx.restore();
      curFx = null;
    }
    function ghostFigures(time, sep){
      if(reduce) return;
      for(const hit of HITS){
        const raw = signedAge(hit, time);
        if(raw < -.15 || raw > .035) continue;
        const sides = hit.s === 'both' ? ['L', 'R'] : [hit.s];
        for(const side of sides){
          const cx = W * .5 + (side === 'L' ? -1 : 1) * sep / 2;
          const dir = side === 'L' ? 1 : -1;
          const seed = side === 'L' ? 4 : 9;
          for(const off of [.04, .075, .11]){
            const pt = hit.t - off;
            ghostMode = true;
            figAlpha = .13;
            figure(cx, dir, poseAt(side, pt), seed, side, time);
          }
        }
      }
      ghostMode = false;
      figAlpha = 1;
    }
    function sceneFx(time, sep){
      if(reduce) return;
      for(const hit of HITS){
        const age = hitAge(hit, time);
        if(age < 0 || age > .64) continue;
        const [tx] = hitPoint(hit, time, sep);
        const k = age / .64;
        const a = Math.pow(1 - k, 1.7) * hit.power;
        const baseY = GROUND + 4;
        for(let ring=0; ring<3; ring++){
          const rr = (6 + k * (58 + hit.power * 18)) * (1 + ring * .34);
          const n = 30 + ring * 8;
          for(let i=0; i<n; i++){
            const ang = (i / n) * 6.283 + ring * .14;
            rawDot(tx + Math.cos(ang) * rr * 1.55, baseY + Math.sin(ang) * rr * .30,
              .64 + (1 - k) * .5, false, a * (.42 - ring * .09));
          }
        }
        for(let i=0; i<7; i++){
          const ang = hash(i, hit.t + 21) * 6.283;
          const len = 12 + k * (54 + hit.power * 16);
          for(let j=0; j<5; j++){
            const q = j / 4;
            rawDot(tx + Math.cos(ang) * len * q, baseY + Math.sin(ang) * len * q * .18,
              .58 + (1 - q) * .24, false, a * (.32 - q * .04));
          }
        }
        for(let i=0; i<18; i++){
          const dx = (hash(i, hit.t + 31) - .5) * (26 + k * 60);
          const dy = (5 + hash(i, hit.t + 41) * 34) * k;
          rawDot(tx + dx, baseY - dy, .72 + hash(i, hit.t + 51) * .5, false, a * .38);
        }
      }
    }
    function userFx(){
      const now = performance.now();
      for(let i=userBursts.length-1; i>=0; i--){
        const b = userBursts[i];
        const age = (now - b.start) / 1000;
        if(age > .52){ userBursts.splice(i, 1); continue; }
        const k = age / .52;
        const a = Math.pow(1 - k, 2);
        for(let ring=0; ring<2; ring++){
          const rr = (4 + k * 54) * (1 + ring * .42);
          for(let j=0; j<24; j++){
            const ang = (j / 24) * 6.283;
            rawDot(b.x + Math.cos(ang) * rr, b.y + Math.sin(ang) * rr * .82,
              .8 + (1 - k) * .8, false, a * (.62 - ring * .18));
          }
        }
        for(let j=0; j<14; j++){
          const ang = hash(j, b.seed) * 6.283;
          const len = (8 + k * 46) * (.7 + hash(j, b.seed + 3) * .6);
          rawDot(b.x + Math.cos(ang) * len, b.y + Math.sin(ang) * len * .85,
            .8, false, a * .55);
        }
      }
    }
    function ensureAudio(){
      if(audio) return true;
      const AC = window.AudioContext || window.webkitAudioContext;
      if(!AC) return false;
      try { audio = new AC(); } catch(e){ return false; }
      return true;
    }
    function noiseBurst(dur, freq, gain, q){
      if(!audio) return;
      const sr = audio.sampleRate;
      const len = Math.max(1, Math.floor(sr * dur));
      const buf = audio.createBuffer(1, len, sr);
      const data = buf.getChannelData(0);
      for(let i=0; i<len; i++){
        const env = Math.pow(1 - i / len, 2.2);
        data[i] = (Math.random() * 2 - 1) * env;
      }
      const src = audio.createBufferSource();
      const filter = audio.createBiquadFilter();
      const g = audio.createGain();
      src.buffer = buf;
      filter.type = 'bandpass';
      filter.frequency.value = freq;
      filter.Q.value = q || .8;
      g.gain.value = gain;
      src.connect(filter).connect(g).connect(audio.destination);
      src.start();
    }
    function tone(freq, dur, gain, type, slide){
      if(!audio) return;
      const now = audio.currentTime;
      const osc = audio.createOscillator();
      const g = audio.createGain();
      osc.type = type || 'sine';
      osc.frequency.setValueAtTime(freq, now);
      if(slide) osc.frequency.exponentialRampToValueAtTime(Math.max(30, freq + slide), now + dur);
      g.gain.setValueAtTime(gain, now);
      g.gain.exponentialRampToValueAtTime(.001, now + dur);
      osc.connect(g).connect(audio.destination);
      osc.start(now);
      osc.stop(now + dur + .01);
    }
    function playHitSound(hit){
      if(!soundOn || !audio) return;
      if(hit.s === 'both' || hit.power > 1.2){
        noiseBurst(.26, 240, .38, .55);
        tone(46, .42, .30, 'sine', -18);
        tone(120, .16, .16, 'triangle', -70);
      } else {
        noiseBurst(.11, 520, .34, .7);
        tone(92, .16, .16, 'sine', -34);
        if(hit.part === 'legF' || hit.part === 'legB') noiseBurst(.08, 760, .18, .5);
      }
    }
    function playWhoosh(){
      if(!soundOn || !audio) return;
      noiseBurst(.16, 1250, .12, .45);
    }
    function playBonus(){
      if(!soundOn || !audio) return;
      noiseBurst(.08, 700, .25, .6);
      tone(180, .12, .12, 'triangle', -80);
    }
    function playRiser(){
      if(!soundOn || !audio) return;
      tone(90, .30, .09, 'sawtooth', 520);
      noiseBurst(.22, 900, .06, .4);
    }
    function syncSound(time){
      if(!soundOn || !audio) return;
      if(time >= 5.52 && time < 5.80 && !riserDone){
        riserDone = true;
        playRiser();
      }
      for(let i=0; i<HITS.length; i++){
        const hit = HITS[i];
        if(time >= hit.t - .20 && time < hit.t - .01 && !whooshed.has(i)){
          whooshed.add(i);
          playWhoosh();
        }
        if(time >= hit.t && time < hit.t + .10 && !sounded.has(i)){
          sounded.add(i);
          playHitSound(hit);
        }
      }
    }

    function limbWorld(side, time, limb){
      const pose = poseAt(side, time);
      const dir = side === 'L' ? 1 : -1;
      const cx = W * .5 + (side === 'L' ? -1 : 1) * Math.max(120, 100 * SCALE) / 2;
      const point = pose[limb];
      return [cx + dir * (pose.shift + point[0]) * SCALE, GROUND - (80 + pose.lift) * SCALE + point[1] * SCALE];
    }
    function hitPoint(hit, time, sep){
      if(hit.s === 'both') return [W * .5, GROUND - 168 * SCALE];
      const target = hit.s === 'L' ? 'R' : 'L';
      if(hit.part === 'armF') return limbWorld(target, time, 'handF');
      if(hit.part === 'armB') return limbWorld(target, time, 'handB');
      if(hit.part === 'legF') return limbWorld(target, time, 'footF');
      if(hit.part === 'legB') return limbWorld(target, time, 'footB');
      const dir = target === 'L' ? 1 : -1;
      const pose = poseAt(target, time);
      const cx = W * .5 + (target === 'L' ? -1 : 1) * sep / 2;
      const x = cx + dir * (pose.shift + Math.sin(pose.lean) * 22) * SCALE;
      const y = GROUND - 80 * SCALE + (-89 + pose.lift) * SCALE;
      return [x, y];
    }
    function trailFx(time, sep){
      for(const hit of HITS){
        const raw = signedAge(hit, time);
        if(raw < -.20 || raw > .03) continue;
        const fade = raw < 0 ? 1 + raw / .20 : 1 - raw / .03;
        const attackers = hit.s === 'both' ? ['L', 'R'] : [hit.s];
        for(const side of attackers){
          const limb = hit.limb || (hit.part === 'legF' || hit.part === 'legB' ? 'footF' : 'handF');
          const p1 = limbWorld(side, time, limb);
          const p0 = limbWorld(side, time - .11, limb);
          for(let i=1; i<=10; i++){
            const u = i / 10;
            rawDot(p0[0] + (p1[0] - p0[0]) * u, p0[1] + (p1[1] - p0[1]) * u, .72 + u * .5, false, fade * .22 * u);
          }
          for(let i=0; i<5; i++){
            const ang = hash(i, Math.round(hit.t * 10)) * 6.283;
            rawDot(p0[0] + Math.cos(ang) * 4, p0[1] + Math.sin(ang) * 4, .75, false, fade * .14);
          }
        }
      }
    }
    function impactFx(time, sep){
      for(const hit of HITS){
        const age = hitAge(hit, time);
        if(age < 0 || age > .56) continue;
        const [tx, ty] = hitPoint(hit, time, sep);
        const k = age / .56;
        const a = Math.pow(1 - k, 2);
        for(let i=0; i<10; i++){
          const ang = hash(i, hit.t) * 6.283;
          const rad = 3 + hash(i, hit.t + 1) * 7 * (1 + k * 2);
          rawDot(tx + Math.cos(ang) * rad, ty + Math.sin(ang) * rad * .8, .9 + (1 - k) * 1.4, true, a * .9);
        }
        for(let ring=0; ring<2; ring++){
          const rr = (7 + k * 48) * (1 + ring * .34);
          const ga = a * (.64 - ring * .14);
          const n = 28 + ring * 8;
          for(let i=0; i<n; i++){
            const ang = (i / n) * 6.283 + ring * .12;
            rawDot(tx + Math.cos(ang) * rr * 1.22, ty + Math.sin(ang) * rr * .78, .78 + (1 - k) * .55, false, ga);
          }
        }
        for(let i=0; i<14; i++){
          const ang = hash(i, hit.t + 5) * 6.283;
          const len = (10 + k * 44) * (.75 + hash(i, hit.t + 8) * .6);
          for(let j=0; j<3; j++){
            const q = .55 + j * .22;
            rawDot(tx + Math.cos(ang) * len * q, ty + Math.sin(ang) * len * q * .82, .66 + (1 - k) * .4, false, a * (.55 - j * .12));
          }
        }
      }
    }
    function hitEnergy(time){
      let e = 0;
      for(const hit of HITS){
        const age = hitAge(hit, time);
        if(age >= 0 && age < .30) e = Math.max(e, Math.pow(1 - age / .30, 2) * hit.power);
      }
      return e;
    }
    function drawFrame(time){
      if(!W || !H) return;
      const flash = reduce ? null : activeFlash(time);
      dotInvert = !!flash && flash.clash;
      if(flash){
        ctx.fillStyle = flash.clash ? '#0b0c0b' : '#ffffff';
        ctx.fillRect(0, 0, W, H);
      } else {
        ctx.clearRect(0, 0, W, H);
      }
      const energy = hitEnergy(time);
      const cam = cameraAt(time);
      const px = pointer.active && !reduce ? (pointer.x - .5) * 8 : 0;
      const py = pointer.active && !reduce ? (pointer.y - .5) * 6 : 0;
      ctx.save();
      ctx.translate(W * .5 + cam.x + px, GROUND * .58 + cam.y + py);
      ctx.scale(cam.z, cam.z);
      ctx.translate(-W * .5, -GROUND * .58);
      if(energy > 0){
        ctx.translate(W * .5, GROUND * .58);
        ctx.scale(1 + energy * .014, 1 + energy * .014);
        ctx.translate(-W * .5, -GROUND * .58);
        ctx.translate(Math.sin(time * 137) * 1.8 * energy, Math.cos(time * 113) * 1.1 * energy);
      }
      if(!flash){
        ctx.fillStyle = 'rgba(11,12,11,.13)';
        for(let x=14; x<W; x+=28){
          ctx.beginPath(); ctx.arc(x, GROUND + 6, .85, 0, 6.283); ctx.fill();
        }
        ctx.fillStyle = 'rgba(11,12,11,.14)';
        for(let i=0; i<96; i++){
          const ang = (i / 96) * 6.283;
          ctx.beginPath();
          ctx.arc(W * .5 + Math.cos(ang) * W * .27, GROUND + Math.sin(ang) * 26, 1.0, 0, 6.283);
          ctx.fill();
        }
      }
      const sep = Math.max(120, 100 * SCALE);
      const poseTime = poseClock(time);
      sceneFx(time, sep);
      ghostFigures(poseTime, sep);
      figAlpha = 1;
      ghostMode = false;
      trailFx(poseTime, sep);
      figure(W * .5 - sep / 2, 1, poseAt('L', poseTime), 4, 'L', time);
      figure(W * .5 + sep / 2, -1, poseAt('R', poseTime), 9, 'R', time);
      userFx();
      impactFx(time, sep);
      ctx.restore();
      dotInvert = false;
      figAlpha = 1;
      ghostMode = false;
      if(!flash && time < .12){
        ctx.fillStyle = 'rgba(255,255,255,' + (1 - time / .12).toFixed(3) + ')';
        ctx.fillRect(0, 0, W, H);
      }
      hitUI(time);
      const r = roundAt(time);
      if(roundEl && r !== lastRound){
        roundEl.textContent = 'ROUND / 0' + r;
        roundEl.classList.remove('pop');
        void roundEl.offsetWidth;
        roundEl.classList.add('pop');
        lastRound = r;
      }
      const mv = moveAt(time);
      if(moveEl && mv !== lastMove){
        moveEl.textContent = mv;
        lastMove = mv;
      }
    }
    function timeNow(now){ return ((now - t0) / 1000 + offset) % DURATION; }
    function resize(){
      const cw = stage.clientWidth, ch = stage.clientHeight;
      if(!cw || !ch) return;
      W = cw; H = ch;
      DPR = Math.min(window.devicePixelRatio || 1, 2);
      dc.width = Math.floor(W * DPR);
      dc.height = Math.floor(H * DPR);
      ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
      SCALE = Math.min(H * .66 / 190, W * .34 / 130);
      GROUND = H * .82;
      drawFrame(playing ? timeNow(performance.now()) : offset);
    }
    function loop(now){
      if(!visible || !playing){ raf = 0; return; }
      const t = timeNow(now);
      if(t < lastTime - .25) resetDuelUI();
      lastTime = t;
      drawFrame(t);
      syncSound(t);
      raf = requestAnimationFrame(loop);
    }
    function start(){
      if(reduce || !playing || raf || !visible) return;
      raf = requestAnimationFrame(loop);
    }
    function pause(){
      if(reduce) return;
      if(playing){
        offset = timeNow(performance.now());
        playing = false;
        if(raf){ cancelAnimationFrame(raf); raf = 0; }
        if(toggleBtn) toggleBtn.textContent = '开打';
        sounded.clear();
        whooshed.clear();
        drawFrame(offset);
      } else {
        t0 = performance.now();
        lastTime = offset;
        playing = true;
        if(toggleBtn) toggleBtn.textContent = '停手';
        start();
      }
    }
    function skip(){
      resetDuelUI();
      offset = (timeNow(performance.now()) + 1.35) % DURATION;
      lastTime = offset;
      t0 = performance.now();
      drawFrame(offset);
      start();
    }
    if(toggleBtn) toggleBtn.addEventListener('click', pause);
    if(skipBtn) skipBtn.addEventListener('click', skip);
    if(soundBtn){
      soundBtn.setAttribute('aria-pressed', 'false');
      soundBtn.addEventListener('click', async () => {
        if(!ensureAudio()){
          soundBtn.textContent = '声音不可用';
          soundBtn.disabled = true;
          return;
        }
        if(audio.state === 'suspended'){
          try { await audio.resume(); } catch(e){}
        }
        soundOn = !soundOn;
        soundBtn.textContent = soundOn ? '声音 ON' : '声音 OFF';
        soundBtn.setAttribute('aria-pressed', String(soundOn));
        if(soundOn){
          tone(420, .06, .10, 'triangle', 30);
          resetDuelUI();
        }
      });
    }
    dc.addEventListener('pointermove', e => {
      const rect = dc.getBoundingClientRect();
      pointer.x = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
      pointer.y = Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));
      pointer.active = true;
    });
    dc.addEventListener('pointerleave', () => { pointer.active = false; });
    dc.addEventListener('pointerdown', e => {
      if(reduce) return;
      const rect = dc.getBoundingClientRect();
      const x = e.clientX - rect.left;
      const y = e.clientY - rect.top;
      userBursts.push({x, y, start:performance.now(), seed:Math.random() * 100});
      playBonus();
      showBonus();
      drawFrame(playing ? timeNow(performance.now()) : offset);
    });
    if('ResizeObserver' in window){
      new ResizeObserver(resize).observe(stage);
    } else {
      window.addEventListener('resize', resize);
    }
    if('IntersectionObserver' in window){
      new IntersectionObserver(entries => {
        visible = entries.some(entry => entry.isIntersecting);
        if(visible) start();
      }, {rootMargin:'160px 0px'}).observe(stage);
    } else {
      visible = true;
    }
    resize();
    if(previewMode && toggleBtn) toggleBtn.textContent = '定格';
    if(reduce){
      if(toggleBtn){ toggleBtn.textContent = '静态'; toggleBtn.disabled = true; }
      if(soundBtn){ soundBtn.disabled = true; soundBtn.textContent = '声音 OFF'; }
      drawFrame(.65);
    } else {
      start();
    }
  })();
})();