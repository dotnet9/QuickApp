/* ============================================================================
   QuickApp 原型共享脚本（零依赖、离线可用）
   提供：SVG 图标、演示数据、Dock 渲染、悬停气泡、Toast、右键菜单、更新提示。
   index.html 为唯一页面；assets/app.js 管理各界面的共享状态与入口。
   ========================================================================== */
window.QA = (function(){
  'use strict';

  /* ---------------- 1. 图标 ---------------- */
  const GLYPHS = {
    globe:'<circle cx="12" cy="12" r="9"/><path d="M3.6 9h16.8M3.6 15h16.8"/><path d="M12 3c2.4 2.4 3.6 5.4 3.6 9s-1.2 6.6-3.6 9c-2.4-2.4-3.6-5.4-3.6-9S9.6 5.4 12 3z"/>',
    chat:'<path d="M21 11.5a8.4 8.4 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.4 8.4 0 0 1-3.8-.9L3 21l1.9-5.7a8.4 8.4 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.4 8.4 0 0 1 3.8-.9h.5a8.5 8.5 0 0 1 8 8z"/>',
    play:'<circle cx="12" cy="12" r="9"/><path d="M10 8.5l6 3.5-6 3.5z"/>',
    music:'<path d="M9 18V6l10-2v12"/><circle cx="6.5" cy="18" r="2.5"/><circle cx="16.5" cy="16" r="2.5"/>',
    camera:'<rect x="3" y="6" width="13" height="12" rx="2"/><path d="M16 11l5-3v8l-5-3z"/>',
    monitor:'<rect x="2.5" y="4" width="19" height="13" rx="2"/><path d="M8 20h8M12 17v3"/><path d="M9 10.5h6M12 7.5v6"/>',
    'monitor-alt':'<rect x="2.5" y="4" width="19" height="13" rx="2"/><path d="M8 20h8M12 17v3"/>',
    note:'<path d="M14 3H6.5A1.5 1.5 0 0 0 5 4.5v15A1.5 1.5 0 0 0 6.5 21h11a1.5 1.5 0 0 0 1.5-1.5V8z"/><path d="M14 3v5h5"/><path d="M8.5 13h7M8.5 16.5h4.5"/>',
    cloud:'<path d="M7 18a4.5 4.5 0 0 1-.4-9A6 6 0 0 1 18 9.5a4 4 0 0 1 .5 8z"/>',
    code:'<path d="M9 7l-5 5 5 5"/><path d="M15 7l5 5-5 5"/>',
    rocket:'<path d="M12 2.5c2.8 2.2 4.2 5.2 4.2 8.5L12 15l-4.2-4c0-3.3 1.4-6.3 4.2-8.5z"/><path d="M7.8 11L5 18l4-1.5"/><path d="M16.2 11L19 18l-4-1.5"/>',
    database:'<ellipse cx="12" cy="6" rx="7" ry="3"/><path d="M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6"/><path d="M5 12c0 1.7 3.1 3 7 3s7-1.3 7-3"/>',
    shield:'<path d="M12 3l7 3v5.5c0 4.3-2.9 7.6-7 9.5-4.1-1.9-7-5.2-7-9.5V6z"/><path d="M9.5 12l1.8 1.8 3.5-3.6"/>',
    record:'<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="3.5"/>',
    app:'<rect x="3.5" y="3.5" width="17" height="17" rx="4"/><path d="M3.5 8.5h17"/>',
    ding:'<path d="M5 11.5a7 7 0 0 1 14 0v4l2 2H3l2-2z"/><path d="M9.5 20h5"/>',
    terminal:'<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7.5 9.5l2.5 2.5-2.5 2.5"/><path d="M12.5 15h4"/>',
    search:'<circle cx="11" cy="11" r="6.5"/><path d="M16 16l4.5 4.5"/>',
    pencil:'<path d="M4 20l4-1 10-10-3-3L5 16z"/><path d="M14.5 5.5l3 3"/>',
    plus:'<path d="M12 5v14M5 12h14"/>',
    more:'<circle cx="5" cy="12" r="1.5" fill="currentColor" stroke="none"/><circle cx="12" cy="12" r="1.5" fill="currentColor" stroke="none"/><circle cx="19" cy="12" r="1.5" fill="currentColor" stroke="none"/>',
    grid:'<rect x="4" y="4" width="7" height="7" rx="2"/><rect x="13" y="4" width="7" height="7" rx="2"/><rect x="4" y="13" width="7" height="7" rx="2"/><rect x="13" y="13" width="7" height="7" rx="2"/>',
    pin:'<path d="M9.5 3h5l-.8 5.2 3.3 3.3H7L10.3 8.2z"/><path d="M12 11.5V21"/>',
    'chevron-up':'<path d="M6 14l6-6 6 6"/>',
    'chevron-down':'<path d="M6 10l6 6 6-6"/>',
    'chevron-left':'<path d="M14 6l-6 6 6 6"/>',
    'chevron-right':'<path d="M10 6l6 6-6 6"/>',
    x:'<path d="M6 6l12 12M18 6L6 18"/>',
    check:'<path d="M5 12.5l4.5 4.5L19 7"/>',
    upload:'<path d="M12 15.5V4"/><path d="M8 8l4-4 4 4"/><path d="M5 15v3.5A1.5 1.5 0 0 0 6.5 20h11a1.5 1.5 0 0 0 1.5-1.5V15"/>',
    download:'<path d="M12 4v11.5"/><path d="M8 12l4 4 4-4"/><path d="M5 19h14"/>',
    undo:'<path d="M5 9h9a5 5 0 0 1 0 10H8"/><path d="M8.5 5.5L5 9l3.5 3.5"/>',
    gear:'<circle cx="12" cy="12" r="3.2"/><path d="M12 3v2.4M12 18.6V21M4.6 7.6l2 1.2M17.4 15.2l2 1.2M4.6 16.4l2-1.2M17.4 8.8l2-1.2"/>',
    info:'<circle cx="12" cy="12" r="9"/><path d="M12 11.2v5"/><path d="M12 7.6v.2"/>',
    trash:'<path d="M4 7h16"/><path d="M9.5 7V5h5v2"/><path d="M6.5 7l1 13h9l1-13"/><path d="M10.5 11v6M13.5 11v6"/>',
    copy:'<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M15 9V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h3"/>',
    folder:'<path d="M3 7.5A1.5 1.5 0 0 1 4.5 6h4L10.5 8.5H18A1.5 1.5 0 0 1 19.5 10v1"/><path d="M3 10h17.2l-2.1 8H5z"/>',
    power:'<path d="M12 3v9"/><path d="M7.6 6.6a7.5 7.5 0 1 0 8.8 0"/>',
    list:'<path d="M4 6.5h16M4 12h16M4 17.5h16"/>',
    refresh:'<path d="M20 11a8 8 0 1 0-2.3 6.3"/><path d="M20 5v6h-6"/>',
    link:'<path d="M10 14a5 5 0 0 0 7.5.5l2-2a5 5 0 0 0-7-7l-1.2 1.1"/><path d="M14 10a5 5 0 0 0-7.5-.5l-2 2a5 5 0 0 0 7 7l1.2-1.1"/>'
  };
  /* 收起按钮箭头：指向 Dock 所在的边（点击即收进那条边） */
  function collapseGlyph(edge){
    return edge === 'bottom' ? 'chevron-down'
         : edge === 'left' ? 'chevron-left'
         : edge === 'right' ? 'chevron-right'
         : 'chevron-up';
  }
  function setCollapseBtn(btn, edge){
    if(btn) btn.innerHTML = svg(collapseGlyph(edge), 20);
  }

  function svg(name, size = 20, stroke = 1.75){
    return '<svg viewBox="0 0 24 24" width="' + size + '" height="' + size + '" fill="none" stroke="currentColor"' +
           ' stroke-width="' + stroke + '" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
           (GLYPHS[name] || GLYPHS.app) + '</svg>';
  }
  /* 把 data-icon 占位灌成 SVG */
  function hydrateIcons(root = document){
    root.querySelectorAll('[data-icon]').forEach(n => {
      if(!n.innerHTML.trim()) n.innerHTML = svg(n.dataset.icon, n.classList.contains('chip-icon') ? 15 : 20);
    });
  }

  /* ---------------- 2. 演示数据（取自 src/QuickApp/menu.json） ---------------- */
  const SEED = [
    {name:'Google Chrome',type:'app',target:'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',icon:'globe'},
    {name:'Dotnet9',type:'web',target:'https://dotnet9.com/',icon:'globe'},
    {name:'微信公众号',type:'web',target:'https://mp.weixin.qq.com/advanced/homepage',icon:'chat'},
    {name:'json格式化',type:'web',target:'https://tool.lu/json/',icon:'globe'},
    {name:'192.168.1.133',type:'cmd',target:'mstsc /v:192.168.1.133',icon:'monitor'},
    {name:'KuLi',type:'app',target:'C:\\Program Files (x86)\\KuLi\\KuLi.exe',icon:'app'},
    {name:'微信',type:'app',target:'I:\\Program Files (x86)\\Tencent\\WeChat\\WeChat.exe',icon:'chat'},
    {name:'钉钉',type:'app',target:'I:\\Program Files (x86)\\DingDing\\DingtalkLauncher.exe',icon:'ding'},
    {name:'腾讯QQ',type:'app',target:'I:\\Program Files (x86)\\Tencent\\QQ\\Bin\\QQScLauncher.exe',icon:'chat'},
    {name:'腾讯视频',type:'app',target:'I:\\Program Files (x86)\\Tencent\\QQLive\\QQLive.exe',icon:'play'}
  ];
  /* Windows 开始菜单索引的静态样例（搜索页用） */
  const INSTALLED = [
    {id:'installed-typora',name:'Typora',type:'app',target:'C:\\Program Files\\Typora\\Typora.exe',icon:'note'},
    {id:'installed-obsidian',name:'Obsidian',type:'app',target:'C:\\Users\\Public\\Obsidian\\Obsidian.exe',icon:'note'},
    {id:'installed-vscode',name:'Visual Studio Code',type:'app',target:'I:\\Users\\Administrator\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe',icon:'code'}
  ];
  /* 原型拼音词表只覆盖样例名称；正式实现使用通用拼音转换 */
  const PINYIN = {微:'wei',信:'xin',公:'gong',众:'zhong',号:'hao',钉:'ding',腾:'teng',讯:'xun',视:'shi',频:'pin',格:'ge',式:'shi',化:'hua'};
  const ALIAS = {'微信':'weixin wx','钉钉':'dingding dd','微信公众号':'weixin gongzhonghao wxgzh','腾讯视频':'tengxunshipin txsp'};
  function pinyinForms(value){
    const text = String(value).toLowerCase();
    const full = [...text].map(ch => PINYIN[ch] || '').join('');
    const initials = [...text].map(ch => PINYIN[ch] ? PINYIN[ch][0] : '').join('');
    return [full, initials, ALIAS[value] || ''];
  }
  /* 搜索打分：名称前缀 > 拼音/别名 > 目标路径 */
  function matchRank(it, token){
    const name = it.name.toLowerCase();
    if(name === token) return 0;
    if(name.startsWith(token)) return 1;
    if(pinyinForms(it.name).some(f => f && (f === token || f.startsWith(token)))) return 2;
    if(pinyinForms(it.name).some(f => f && f.includes(token))) return 3;
    if(it.target.toLowerCase().includes(token)) return 4;
    return Infinity;
  }
  function searchItems(items, query){
    const tokens = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    if(!tokens.length) return items.slice();
    return items.map((item, index) => ({item, index, rank:Math.min(...tokens.map(t => matchRank(item, t)))}))
      .filter(row => Number.isFinite(row.rank))
      .sort((a, b) => a.rank - b.rank || a.index - b.index)
      .map(row => row.item);
  }

  /* ---------------- 3. 小工具 ---------------- */
  function h(tag, attrs, ...kids){
    const node = document.createElement(tag);
    for(const [k, v] of Object.entries(attrs || {})){
      if(v === null || v === false || v === undefined) continue;
      if(k === 'class') node.className = v;
      else if(k === 'text') node.textContent = v;
      else if(k === 'html') node.innerHTML = v;
      else if(k.startsWith('on')) node.addEventListener(k.slice(2), v);
      else node.setAttribute(k, String(v));
    }
    for(const kid of kids.flat()) if(kid) node.append(kid);
    return node;
  }
  function hueOf(str){
    let hash = 0;
    for(const ch of str) hash = (hash * 31 + ch.codePointAt(0)) % 360;
    return hash;
  }
  const TYPE_LABEL = {app:'应用',web:'网页',cmd:'命令'};
  const EDGE_LABEL = {top:'上',bottom:'下',left:'左',right:'右'};
  function typeLabel(type){ return TYPE_LABEL[type] || '应用'; }
  function targetSummary(it){
    if(it.type === 'web'){
      try{ return new URL(it.target).host; }catch(e){ return it.target; }
    }
    if(it.type === 'cmd') return it.target;
    const parts = it.target.split(/[\\/]/).filter(Boolean);
    return '…\\' + parts.slice(-2).join('\\');
  }
  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

  /* ---------------- 4. 外观 token 应用 ---------------- */
  /* st: {themePref,wall,style,label,tileSize,radius,alpha,edge,searchOpen,edit,pinned} */
  let lastSt = null;
  const mqDark = window.matchMedia('(prefers-color-scheme: dark)');
  mqDark.addEventListener('change', () => {
    if(lastSt && lastSt.themePref === 'system') applyTokens(lastSt);
  });

  function applyTokens(st){
    const root = document.documentElement;
    root.dataset.theme = st.themePref === 'system'
      ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
      : st.themePref;
    if(st.wall && st.wall !== 'auto') root.dataset.wall = st.wall; else delete root.dataset.wall;
    root.dataset.style = st.style;
    root.dataset.label = st.label;
    root.dataset.searchOpen = String(!!st.searchOpen);
    root.dataset.edit = String(!!st.edit);
    if(st.edge) root.dataset.edge = st.edge;
    if(st.tileSize) root.style.setProperty('--tile-size', st.tileSize + 'px');
    if(st.tileSize) root.style.setProperty('--items-window', (st.tileSize * 10 + 90) + 'px');
    if(st.radius) root.style.setProperty('--dock-radius', st.radius + 'px');
    if(st.alpha !== undefined) root.style.setProperty('--dock-alpha', String(st.alpha));
    lastSt = st;
  }

  /* ---------------- 5. Dock 图标渲染 ---------------- */
  /* 图标来源优先级：用户选择的本地图片(it.customUrl) > 类型占位图形。
     程序不内置图标库——应用千奇百怪，内置列表永远填不满。 */
  function tileNode(it, st, opts = {}){
    const tile = h('button', {
      class:'tile' + (it.type === 'app' && !it.customUrl ? ' app-icon' : ''), type:'button', 'data-id':it.id, title:it.name,
      'aria-label':it.name + '，' + typeLabel(it.type)
    });
    if(it.customUrl){
      tile.append(h('img', {class:'tile-img', src:it.customUrl, alt:'', draggable:'false'}));
    }else{
      const hue = hueOf(it.name);
      if(it.type !== 'app') tile.style.background = 'linear-gradient(145deg,hsl(' + hue + ' 52% 46%),hsl(' + ((hue + 18) % 360) + ' 52% 34%))';
      tile.append(h('span', {class:'tile-icon', html:svg(it.icon, Math.round((st.tileSize || 44) * (it.type === 'app' ? 0.9 : 0.5)))}));
    }

    const item = h('div', {class:'dock-item', role:'listitem', 'data-id':it.id, draggable:opts.edit ? 'true' : null}, tile);
    if(st.label === 'iconText') item.append(h('span', {class:'tile-name', text:it.name, title:it.name}));
    if(opts.edit) item.append(h('button', {
      class:'remove', type:'button', 'data-id':it.id,
      title:'移除 ' + it.name, 'aria-label':'移除 ' + it.name, html:svg('x', 12, 2.6)
    }));
    return item;
  }
  function renderTiles(container, items, st, opts = {}){
    const frag = document.createDocumentFragment();
    if(!items.length){
      frag.append(h('div', {class:'dock-empty', role:'status', text:opts.emptyText || '右键空白处添加应用'}));
    }else{
      for(const it of items) frag.append(tileNode(it, st, opts));
    }
    container.replaceChildren(frag);
  }

  /* 滚动外观：两端渐隐 + 指示条（轴向随停靠边） */
  function updateScrollChrome(itemsEl, fades){
    const vertical = itemsEl.classList.contains('searching') || document.documentElement.dataset.edge === 'left' || document.documentElement.dataset.edge === 'right';
    const sc = itemsEl;
    const total = vertical ? sc.scrollHeight : sc.scrollWidth;
    const view = vertical ? sc.clientHeight : sc.clientWidth;
    const pos = vertical ? sc.scrollTop : sc.scrollLeft;
    const maxScroll = Math.max(0, total - view);
    const indicator = fades ? fades.indicator : null;
    if(indicator){
      indicator.hidden = !(maxScroll > 2);
      if(maxScroll > 2){
        const thumb = fades.thumb;
        if(vertical){
          const trackH = indicator.clientHeight;
          const thumbH = Math.max(14, trackH * (view / total));
          thumb.style.height = thumbH + 'px';thumb.style.width = 'auto';
          thumb.style.transform = 'translateY(' + ((pos / maxScroll) * (trackH - thumbH)) + 'px)';
        }else{
          const trackW = indicator.clientWidth;
          const thumbW = Math.max(14, trackW * (view / total));
          thumb.style.width = thumbW + 'px';thumb.style.height = 'auto';
          thumb.style.transform = 'translateX(' + ((pos / maxScroll) * (trackW - thumbW)) + 'px)';
        }
      }
    }
    if(fades){
      fades.left.classList.toggle('show', pos > 2);
      fades.right.classList.toggle('show', pos < maxScroll - 2);
    }
  }

  /* ---------------- 6. 悬停气泡 ---------------- */
  /* showName: label=iconText 时不弹气泡；返回 hide() */
  function attachBubble(dockEl, itemsEl, bubbleEl, st, nameFn, targetFn){
    function show(id){
      if(st.label !== 'icon') return;
      const it = (nameFn(id));
      const itemNode = $('.dock-item[data-id="' + id + '"]', itemsEl);
      if(!it || !itemNode) return;
      const tile = $('.tile', itemNode);
      const r = tile.getBoundingClientRect();
      if(r.width === 0) return;
      $('.bubble-name', bubbleEl).textContent = it.name + ' · ' + typeLabel(it.type);
      $('.bubble-target', bubbleEl).textContent = targetFn ? targetFn(it) : targetSummary(it);
      bubbleEl.hidden = false;
      const w = bubbleEl.offsetWidth, bh = bubbleEl.offsetHeight;
      const gap = 8, margin = 14;
      const edge = document.documentElement.dataset.edge;
      let pos = edge === 'top' ? 'below' : edge === 'bottom' ? 'above' : edge === 'left' ? 'right' : 'left';
      if(pos === 'above' && r.top < bh + margin) pos = 'below';
      else if(pos === 'below' && window.innerHeight - r.bottom < bh + margin) pos = 'above';
      else if(pos === 'right' && window.innerWidth - r.right < w + margin) pos = 'left';
      else if(pos === 'left' && r.left < w + margin) pos = 'right';
      bubbleEl.dataset.pos = pos;
      if(pos === 'above' || pos === 'below'){
        const center = Math.min(Math.max(r.left + r.width / 2, w / 2 + 8), window.innerWidth - w / 2 - 8);
        bubbleEl.style.left = center + 'px';
        bubbleEl.style.top = (pos === 'above' ? r.top - gap : r.bottom + gap) + 'px';
      }else{
        const cy = Math.min(Math.max(r.top + r.height / 2, bh / 2 + 8), window.innerHeight - bh / 2 - 8);
        bubbleEl.style.left = (pos === 'right' ? r.right + gap : r.left - gap - w) + 'px';
        bubbleEl.style.top = cy + 'px';
      }
    }
    function hide(){ bubbleEl.hidden = true; }
    itemsEl.addEventListener('mouseover', e => {
      const item = e.target.closest('.dock-item');
      if(item) show(item.dataset.id);
    });
    itemsEl.addEventListener('mouseout', e => {
      if(!e.relatedTarget || !e.relatedTarget.closest('.dock-item')) hide();
    });
    itemsEl.addEventListener('focusin', e => {
      const tile = e.target.closest('.tile');
      if(tile) show(tile.dataset.id);
    });
    itemsEl.addEventListener('focusout', hide);
    window.addEventListener('resize', hide);
    return hide;
  }

  /* ---------------- 7. Toast ---------------- */
  let toastLayerOverride = null;
  function setToastLayer(el){ toastLayerOverride = el; }
  function toastLayer(){
    if(toastLayerOverride && toastLayerOverride.isConnected) return toastLayerOverride;
    let layer = $('.toast-layer');
    if(!layer){
      layer = h('div', {class:'toast-layer'});
      document.body.append(layer);
    }
    return layer;
  }
  function toast(text, opts = {}){
    const layer = toastLayer();
    $$('.toast', layer).forEach(t => {
      if(t.dataset.text === text && !t.classList.contains('hide')) t.remove();
    });
    const node = h('div', {class:'toast', role:'status', 'data-text':text}, h('span', {text}));
    if(opts.progress !== undefined){
      const progress = h('div', {class:'toast-progress'}, h('i'));
      progress.firstElementChild.style.width = Math.max(0, Math.min(100, opts.progress)) + '%';
      node.classList.add('toast-downloading');
      node.append(progress);
    }
    if(opts.actionLabel){
      node.append(h('button', {class:'toast-action', type:'button', text:opts.actionLabel, onclick:() => {
        if(opts.onAction) opts.onAction();
        dismiss();
      }}));
    }
    layer.append(node);
    const timer = setTimeout(dismiss, opts.duration || (opts.actionLabel ? 5000 : 2200));
    function dismiss(){
      if(!node.isConnected) return;
      clearTimeout(timer);
      node.classList.add('hide');
      setTimeout(() => node.remove(), 200);
    }
    return dismiss;
  }

  /* ---------------- 8. 右键菜单（单例） ---------------- */
  let menuEl = null;
  function closeMenu(){
    if(menuEl){ menuEl.remove(); menuEl = null; }
    const btn = $('#btnMenu');
    if(btn) btn.setAttribute('aria-expanded', 'false');
  }
  function menuRow(row){
    if(row.sep) return h('div', {class:'menu-sep'});
    if(row.title) return h('div', {class:'menu-title', text:row.title});
    if(row.custom) return row.custom();
    return h('button', {
      class:'menu-item' + (row.danger ? ' danger' : ''), type:'button', role:'menuitem',
      onclick:() => { closeMenu(); if(row.action) row.action(); }
    },
      h('span', {class:'mi-icon', html:svg(row.icon || 'app', 16)}),
      h('span', {text:row.label}),
      row.note ? h('span', {class:'mi-note', text:row.note}) : null
    );
  }
  function openMenu(spec, x, y){
    closeMenu();
    menuEl = h('div', {class:'menu', role:'menu', 'aria-label':'菜单'});
    for(const row of spec) menuEl.append(menuRow(row));
    document.body.append(menuEl);
    menuEl.addEventListener('keydown', e => {
      if(!['ArrowUp','ArrowDown','Home','End'].includes(e.key)) return;
      const entries = $$('button', menuEl);
      const index = entries.indexOf(document.activeElement);
      const next = e.key === 'Home' ? 0 : e.key === 'End' ? entries.length - 1
        : (index + (e.key === 'ArrowUp' ? -1 : 1) + entries.length) % entries.length;
      e.preventDefault(); entries[next]?.focus();
    });
    const rect = menuEl.getBoundingClientRect();
    menuEl.style.left = Math.max(8, Math.min(x, window.innerWidth - rect.width - 8)) + 'px';
    menuEl.style.top = Math.max(8, Math.min(y, window.innerHeight - rect.height - 8)) + 'px';
    const btn = $('#btnMenu');
    if(btn) btn.setAttribute('aria-expanded', 'true');
    const opened = menuEl;
    requestAnimationFrame(() => opened.isConnected && $('button', opened)?.focus());
    return menuEl;
  }
  /* 菜单贴着 Dock 操作按钮弹出（方向随停靠边翻转） */
  function openMenuAtButton(spec, anchorEl){
    const menu = openMenu(spec, 8, 8);
    const a = anchorEl.getBoundingClientRect();
    const m = menu.getBoundingClientRect();
    const edge = document.documentElement.dataset.edge;
    const gap = 8;
    let left, top;
    if(edge === 'left'){ left = a.right + gap; top = a.top; }
    else if(edge === 'right'){ left = a.left - m.width - gap; top = a.top; }
    else if(edge === 'top'){ left = a.right - m.width; top = a.bottom + gap; }
    else{ left = a.right - m.width; top = a.top - m.height - gap; }
    menu.style.left = Math.max(8, Math.min(left, window.innerWidth - m.width - 8)) + 'px';
    menu.style.top = Math.max(8, Math.min(top, window.innerHeight - m.height - 8)) + 'px';
    return menu;
  }
  document.addEventListener('mousedown', e => {
    if(menuEl && !e.target.closest('.menu')) closeMenu();
  });
  document.addEventListener('keydown', e => {
    if(e.key === 'Escape' && menuEl){ closeMenu(); e.preventDefault(); }
  });

  /* 停靠位置四向选择器（桌面示意框） */
  function dockEdgePicker(current, onPick){
    const wrap = h('div', {class:'dock-pos', role:'group', 'aria-label':'停靠位置'});
    ['top','right','bottom','left'].forEach(edge => {
      wrap.append(h('button', {
        class:'edge e-' + edge + (current === edge ? ' active' : ''), type:'button',
        'aria-label':EDGE_LABEL[edge] + '边缘', title:EDGE_LABEL[edge] + '边缘',
        onclick:() => onPick(edge)
      }));
    });
    return wrap;
  }

  /* ---------------- 9. 更新提示构建器 ---------------- */
  /* u: {phase:'found'|'noasset'|'downloading'|'ready', tag:'v0.2.5', progress:45, installer:boolean}
     actions: {download,dismiss,install,cancel,openPage} */
  function updateBarNode(u, actions){
    const icon = h('span', {class:'update-icon' + (u.phase === 'ready' ? ' done' : ''),
      html:svg(u.phase === 'ready' ? 'check' : u.phase === 'downloading' ? 'download' : 'upload', 16)});
    const main = h('div', {class:'update-main'});
    const line = h('div', {class:'update-line'});
    if(u.phase === 'found'){
      line.append(h('span', {class:'update-text', html:'发现新版本 <b>' + u.tag + '</b>'}));
      line.append(h('span', {class:'update-actions'},
        h('button', {class:'chip primary', type:'button', onclick:actions.download}, h('span', {class:'chip-icon', html:svg('download', 14, 2)}), h('span', {text:'下载'})),
        h('button', {class:'chip', type:'button', onclick:actions.dismiss}, h('span', {text:'忽略'}))
      ));
    }else if(u.phase === 'noasset'){
      line.append(h('span', {class:'update-text', html:'发现新版本 <b>' + u.tag + '</b> <span class="update-tag">· 暂无法获取当前系统的安装包</span>'}));
      line.append(h('span', {class:'update-actions'},
        h('button', {class:'chip primary', type:'button', onclick:actions.openPage}, h('span', {class:'chip-icon', html:svg('link', 14, 2)}), h('span', {text:'打开发布页'})),
        h('button', {class:'chip', type:'button', onclick:actions.dismiss}, h('span', {text:'忽略'}))
      ));
    }else if(u.phase === 'downloading'){
      line.append(h('span', {class:'update-text', html:'正在下载 <b>' + u.tag + '</b> <span class="update-tag">' + u.progress + '%</span>'}));
      line.append(h('span', {class:'update-actions'},
        h('button', {class:'chip', type:'button', onclick:actions.cancel}, h('span', {text:'取消'}))
      ));
    }else{
      line.append(h('span', {class:'update-text', html:'<b>' + u.tag + '</b> 下载完成'}));
      line.append(h('span', {class:'update-actions'},
        h('button', {class:'chip primary', type:'button', onclick:actions.install}, h('span', {class:'chip-icon', html:svg('check', 14, 2.4)}), h('span', {text:u.installer === false ? '打开安装包' : '安装'})),
        h('button', {class:'chip', type:'button', onclick:actions.dismiss}, h('span', {text:'忽略'}))
      ));
    }
    main.append(line);
    if(u.phase === 'downloading'){
      const bar = h('div', {class:'update-progress' + (u.indeterminate ? ' indeterminate' : '')}, h('i'));
      bar.firstElementChild.style.width = (u.indeterminate ? '' : u.progress + '%');
      main.append(bar);
    }
    return h('div', {class:'update-bar', role:'status', 'aria-label':'软件更新'}, icon, main);
  }

  /* 竖排 Dock 的紧凑胶囊 */
  function updatePillNode(onClick){
    return h('button', {class:'update-pill', type:'button', title:'发现新版本，点击查看', onclick:onClick},
      h('span', {class:'pill-dot'}), h('span', {text:'新版本'}));
  }

  /* 更新卡片（竖排弹出 / 通用悬浮详情） */
  function updateCardNode(u, actions){
    const titles = {found:'发现新版本', noasset:'发现新版本', downloading:'正在下载更新', ready:'更新已就绪'};
    const descs = {
      found:'已按当前系统与架构选定安装包，下载完成后需再次点击安装，程序不会静默替换。',
      noasset:'暂无法获取当前系统的安装包，可以前往 Release 页面手动选择资产。',
      downloading:'下载在后台进行，可随时取消；完成后 Dock 会提示安装。',
      ready:'安装包已校验存放，点击安装后由系统安装器接管，程序随后退出。'
    };
    const card = h('div', {class:'update-card', role:'dialog', 'aria-label':'软件更新'},
      h('div', {class:'update-head'},
        h('span', {class:'update-head-icon' + (u.phase === 'ready' ? ' done' : ''), html:svg(u.phase === 'ready' ? 'check' : 'download', 17)}),
        h('span', {text:titles[u.phase]}),
        h('span', {class:'update-ver', text:u.tag})
      ),
      h('div', {class:'update-desc', text:descs[u.phase]})
    );
    const actionsRow = h('div', {class:'update-actions'});
    if(u.phase === 'found'){
      actionsRow.append(
        h('button', {class:'chip primary', type:'button', onclick:actions.download}, h('span', {text:'下载'})),
        h('button', {class:'chip', type:'button', onclick:actions.dismiss}, h('span', {text:'忽略'}))
      );
    }else if(u.phase === 'noasset'){
      actionsRow.append(
        h('button', {class:'chip primary', type:'button', onclick:actions.openPage}, h('span', {text:'打开发布页'})),
        h('button', {class:'chip', type:'button', onclick:actions.dismiss}, h('span', {text:'忽略'}))
      );
    }else if(u.phase === 'downloading'){
      actionsRow.append(h('button', {class:'chip', type:'button', onclick:actions.cancel}, h('span', {text:'取消'})));
    }else{
      actionsRow.append(
        h('button', {class:'chip primary', type:'button', onclick:actions.install}, h('span', {text:u.installer === false ? '打开安装包' : '安装'})),
        h('button', {class:'chip', type:'button', onclick:actions.dismiss}, h('span', {text:'忽略'}))
      );
    }
    card.append(actionsRow);
    if(u.phase === 'downloading'){
      const bar = h('div', {class:'update-progress' + (u.indeterminate ? ' indeterminate' : '')}, h('i'));
      bar.firstElementChild.style.width = (u.indeterminate ? '' : u.progress + '%');
      card.append(bar);
    }
    return card;
  }

  /* ---------------- 10. 设置控件绑定 ---------------- */
  /* [data-control] 分段按钮：state[key] = data-value，回调通知 */
  function bindControls(root, state, onChange){
    root.addEventListener('click', e => {
      const sw = e.target.closest('[data-switch]');
      if(sw){
        const key = sw.dataset.switch;
        state[key] = !state[key];
        sw.setAttribute('aria-checked', String(state[key]));
        if(onChange) onChange(key);
        return;
      }
      const value = e.target.closest('[data-control] [data-value]');
      if(value){
        const key = value.closest('[data-control]').dataset.control;
        const raw = value.dataset.value;
        state[key] = raw === 'true' ? true : raw === 'false' ? false : raw;
        syncControls(root, state);
        if(onChange) onChange(key, state[key]);
      }
    });
    syncControls(root, state);
  }
  function syncControls(root, state){
    $$('[data-switch]', root).forEach(b => b.setAttribute('aria-checked', String(!!state[b.dataset.switch])));
    $$('[data-control]', root).forEach(group => {
      const key = group.dataset.control;
      $$('[data-value]', group).forEach(b => b.classList.toggle('active', String(state[key]) === b.dataset.value));
    });
  }

  return {
    GLYPHS, svg, hydrateIcons,
    SEED, INSTALLED, searchItems,
    h, $, $$, hueOf, typeLabel, targetSummary, EDGE_LABEL,
    applyTokens, tileNode, renderTiles, updateScrollChrome,
    attachBubble, toast, setToastLayer,
    closeMenu, openMenu, openMenuAtButton, menuRow, dockEdgePicker,
    updateBarNode, updateCardNode, updatePillNode,
    collapseGlyph, setCollapseBtn,
    bindControls, syncControls
  };
})();
