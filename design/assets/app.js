/* 单一原型入口：界面共用状态；系统启动、文件选择和安装以本地演示替代。 */
(() => {
  'use strict';
  const {$, $$, h} = QA;
  const storageKey = 'quickapp-prototype-v1';
  const defaults = {themePref:'system', wall:'auto', style:'glass', label:'icon', tileSize:44, radius:18, alpha:1,
    edge:'top', pinned:false, autoStart:true, edgeReveal:true, autoCollapse:true, checkUpdates:true,
    autoHide:700, monitor:'display-1', portable:false};
  let stored;
  try { stored = JSON.parse(localStorage.getItem(storageKey)); } catch (_) { /* 首次运行或存储不可用 */ }
  const st = {...defaults, ...(stored?.settings || {}), dockVisible:true, searchOpen:false, edit:false, exited:false};
  let items = stored?.items || QA.SEED.map((it, i) => ({...it, id:'i' + i}));
  let savedConfig = {settings:{...st}, items:items.map(it => ({...it}))};
  let configError = null;
  const list = $('#dockItems'), fades = {left:$('#fadeLeft'), right:$('#fadeRight'), indicator:$('#scrollIndicator'), thumb:$('#scrollThumb')};
  let draft = null, pendingIcon = null, dragId = null, picker = null;
  const returnFocus = new Map();
  let revealTimer, hideTimer, updateTimer, update = null, popoverOpen = false;
  const rec = {id:'vex', name:'Vex · 维刻', version:'v1.5.1', phase:stored?.vexInstalled ? 'installed' : 'idle', progress:0};

  function persist() {
    if (configError) { QA.toast('配置读取失败，请先重新加载配置。'); return false; }
    const settings = Object.fromEntries(Object.keys(defaults).map(key => [key, st[key]]));
    savedConfig = {settings, items:items.map(it => ({...it}))};
    try { localStorage.setItem(storageKey, JSON.stringify({...savedConfig, vexInstalled:rec.phase === 'installed'})); }
    catch (_) { QA.toast('浏览器存储不可用，本次修改仅保留到关闭页面。'); }
    return true;
  }
  function systemRows(query) {
    if (!query.trim()) return [];
    return QA.searchItems(QA.INSTALLED, query).filter(it => !items.some(x => x.target.toLowerCase() === it.target.toLowerCase()));
  }
  function renderSearch() {
    const query = $('#searchInput').value, configured = QA.searchItems(items, query), installed = systemRows(query);
    $('#searchCount').textContent = String(configured.length + installed.length);
    const groups = [];
    if (configured.length) groups.push(h('section', {class:'search-group'},
      h('div', {class:'search-group-title'}, h('strong', {text:'已配置'}), h('span', {class:'search-group-count', text:configured.length})),
      h('div', {class:'search-result-row'}, configured.map(it => QA.tileNode(it, st, {edit:st.edit})))));
    if (installed.length) groups.push(h('section', {class:'search-group'},
      h('div', {class:'search-group-title'}, h('strong', {text:'系统应用'}), h('span', {class:'search-group-count', text:installed.length})),
      h('div', {class:'search-installed-list'}, installed.map(it => h('div', {class:'search-installed-item', 'data-system':it.id, tabindex:0, role:'button', 'aria-label':'运行 ' + it.name},
        QA.tileNode(it, {...st, tileSize:36}).firstElementChild,
        h('div', {class:'search-installed-copy'}, h('strong', {text:it.name}), h('span', {text:'开始菜单 · ' + QA.typeLabel(it.type)})),
        h('button', {class:'search-result-add', type:'button', 'data-add':it.id, title:'添加到 Dock', 'aria-label':'添加 ' + it.name + ' 到 Dock', html:QA.svg('plus', 14)}))))));
    if (!groups.length) groups.push(h('div', {class:'dock-empty', role:'status', text:configError ? '配置读取失败 · 设置中可重试' : '没有匹配的应用'}));
    list.replaceChildren(...groups);
  }
  function refresh() {
    QA.applyTokens(st);
    document.body.classList.toggle('dock-visible', st.dockVisible && !st.exited);
    document.body.classList.toggle('dock-pinned', st.pinned);
    $('#dockHandle').hidden = st.exited;
    $('#trayButton').hidden = st.exited;
    $('#searchRow').hidden = !st.searchOpen;
    $('#dockEditbar').hidden = !st.edit;
    list.classList.toggle('searching', st.searchOpen);
    $('#btnPin').classList.toggle('active', st.pinned);
    $('#btnPin').setAttribute('aria-pressed', String(st.pinned));
    $('#btnSearch').classList.toggle('active', st.searchOpen);
    QA.setCollapseBtn($('#btnHide'), st.edge);
    if (st.searchOpen) renderSearch();
    else QA.renderTiles(list, items, st, {edit:st.edit, emptyText:configError ? '配置读取失败 · 设置中可重试' : st.edit ? '右键空白处添加应用' : '空空如也'});
    renderUpdate();
    renderRec();
    syncSettings();
    requestAnimationFrame(() => QA.updateScrollChrome(list, fades));
  }
  function showDock() { clearTimeout(hideTimer); st.exited = false; st.dockVisible = true; refresh(); }
  function setEdge(edge) { st.edge = edge; list.scrollLeft = list.scrollTop = 0; persist(); refresh(); }
  function search(open = !st.searchOpen) {
    st.searchOpen = open; st.dockVisible = true;
    if (!open) $('#searchInput').value = '';
    refresh(); if (open) $('#searchInput').focus();
  }
  function edit(open = !st.edit) { st.edit = open; st.dockVisible = true; refresh(); }
  function run(it) {
    if (!it) return;
    if (st.edit) { QA.toast('编辑中，点击「完成」后运行'); return; }
    QA.toast('正在' + (it.type === 'cmd' ? '执行：' : '打开：') + it.name);
    if (st.autoCollapse && !st.pinned) { st.dockVisible = false; refresh(); }
  }
  function remove(it) {
    const index = items.indexOf(it); if (index < 0 || configError) return;
    items.splice(index, 1); persist(); refresh();
    QA.toast('已移除 ' + it.name, {actionLabel:'撤销', onAction:() => {
      if (!items.some(x => x.id === it.id)) items.splice(Math.min(index, items.length), 0, it);
      persist(); refresh();
    }});
  }
  function addPaths(paths, folder = false) {
    if (configError) { QA.toast('配置读取失败，请先重新加载配置。'); return; }
    let count = 0;
    for (const target of paths) {
      if (items.some(it => it.target.toLowerCase() === target.toLowerCase())) continue;
      const name = target.replace(/[\\/]+$/, '').split(/[\\/]/).pop();
      items.push({id:'i' + crypto.randomUUID(), type:'app', target, name:folder ? name : name.replace(/\.[^.]+$/, ''), icon:folder ? 'folder' : 'app'}); count++;
    }
    if (count) { persist(); refresh(); QA.toast('已添加 ' + count + ' 项'); }
    else QA.toast('已在 Dock 中');
  }
  function clearItems() {
    if (configError) return;
    const backup = items; items = []; persist(); refresh();
    QA.toast('已清空 ' + backup.length + ' 项', {actionLabel:'撤销', onAction:() => { items = [...backup, ...items.filter(it => !backup.some(x => x.id === it.id))]; persist(); refresh(); }});
  }
  function copy(text) {
    navigator.clipboard?.writeText(text).then(() => QA.toast('已复制'), () => QA.toast('复制失败，请检查浏览器剪贴板权限'));
  }
  function commandText(it) { return it.type === 'cmd' ? it.target : (it.type === 'web' ? 'start "" ' : '') + '"' + it.target + '"' + (it.args ? ' ' + it.args : ''); }
  function rename(it) {
    st.edit = true; refresh();
    const node = [...list.querySelectorAll('.dock-item')].find(el => el.dataset.id === it.id);
    if (!node) return;
    const input = h('input', {class:'rename-input', value:it.name, 'aria-label':'重命名 ' + it.name});
    node.classList.add('renaming'); node.append(input); input.focus(); input.select();
    let done = false;
    function finish(save) { if (done) return; done = true; if (save && input.value.trim()) { it.name = input.value.trim(); persist(); } refresh(); }
    input.addEventListener('keydown', e => { e.stopPropagation(); if (e.key === 'Enter' || e.key === 'Escape') { e.preventDefault(); finish(e.key === 'Enter'); } });
    input.addEventListener('blur', () => finish(true));
  }
  function itemMenu(it) {
    return [{label:'运行', icon:'play', action:() => run(it)}, {sep:true},
      {label:'编辑…', icon:'pencil', action:() => openEditor(it)}, {label:'重命名', icon:'pencil', action:() => rename(it)},
      {label:'更换图标…', icon:'upload', action:() => { pendingIcon = it; $('#iconFile').click(); }},
      {label:'复制路径', icon:'copy', action:() => copy(it.target)}, {label:'复制命令', icon:'terminal', action:() => copy(commandText(it))},
      {label:'打开位置', icon:'folder', action:() => QA.toast('正在打开位置：' + it.target)}, {sep:true},
      {label:'移除', icon:'trash', danger:true, action:() => remove(it)}];
  }
  function dockMenu() {
    return [{title:'添加'}, {label:'文件…', icon:'upload', action:() => choosePath(false, path => addPaths([path]))},
      {label:'目录…', icon:'folder', action:() => choosePath(true, path => addPaths([path], true))},
      {label:'网址…', icon:'globe', action:() => openEditor(null, 'web')}, {label:'命令行…', icon:'terminal', action:() => openEditor(null, 'cmd')},
      {sep:true}, {title:'管理'}, {label:'编辑模式', icon:'pencil', action:() => edit()}, {label:'清空', icon:'trash', danger:true, action:clearItems},
      {sep:true}, {title:'停靠'}, {custom:() => QA.dockEdgePicker(st.edge, edge => { QA.closeMenu(); setEdge(edge); })},
      {sep:true}, {label:'设置', icon:'gear', action:openSettings}, {sep:true}, {label:'退出', icon:'power', danger:true, action:exit}];
  }
  function exit() {
    persist(); clearTimeout(hideTimer); clearInterval(updateTimer); clearInterval(rec.timer); popoverOpen = false;
    st.exited = true; st.dockVisible = false; closeLayer('settingsLayer'); refresh(); QA.toast('QuickApp 已退出');
  }
  function openLayer(id) {
    QA.closeMenu(); clearTimeout(hideTimer);
    returnFocus.set(id, document.activeElement); $('#' + id).hidden = false;
    requestAnimationFrame(() => $('#' + id).querySelector('input,button,select')?.focus());
  }
  function closeLayer(id) { $('#' + id).hidden = true; returnFocus.get(id)?.focus(); }
  function dialog(title, content, buttons) {
    $('#dialogTitle').textContent = title;
    $('#dialogBody').replaceChildren(...content, h('div', {class:'row dialog-actions'}, h('span', {class:'spacer'}), ...buttons));
    openLayer('dialogLayer');
  }
  const chip = (label, action, primary = false) => h('button', {class:'chip' + (primary ? ' primary' : ''), type:'button', text:label, onclick:action});
  function choosePath(folder, onChoose, initial = '') {
    picker = onChoose;
    const paths = folder ? ['C:\\Work', 'E:\\github\\Apps\\QuickApp', 'C:\\Users\\Public\\Documents'] : ['C:\\Program Files\\GitHub Desktop\\GitHubDesktop.exe', 'C:\\Windows\\System32\\notepad.exe', 'C:\\Work\\notes.txt'];
    const input = h('input', {id:'pickerPath', value:initial || paths[0], 'aria-label':folder ? '目录路径' : '文件路径'});
    dialog(folder ? '选择目录' : '选择程序或文件', [
      h('div', {class:'picker-list'}, paths.map(path => h('button', {type:'button', onclick:() => input.value = path}, h('span', {html:QA.svg(folder ? 'folder' : 'app', 16)}), h('span', {text:path})))),
      h('label', {for:'pickerPath', text:folder ? '目录' : '文件名'}), input],
      [chip('取消', () => closeLayer('dialogLayer')), chip('选择', () => { if (!input.value.trim()) return; closeLayer('dialogLayer'); picker(input.value.trim().replace(/^"|"$/g, '')); }, true)]);
  }
  function inferName(target, type) {
    if (type === 'web') { try { return new URL(target).host; } catch (_) { return target; } }
    if (type === 'cmd') return target;
    const name = target.replace(/[\\/]+$/, '').split(/[\\/]/).pop();
    return name.replace(/\.(exe|lnk|app|bat|cmd)$/i, '') || target;
  }
  function openEditor(it, type = 'app') {
    draft = it ? {...it} : {id:null, type, name:'', target:'', args:'', cwd:'', hotkey:'', shell:'cmd', terminal:type === 'cmd'};
    $('#editorTitle').textContent = it ? '编辑快捷项' : '添加快捷项'; $('#save').textContent = it ? '保存' : '添加';
    for (const key of ['name', 'target', 'args', 'cwd', 'hotkey']) $('#' + key).value = draft[key] || '';
    $('#kind').value = draft.type; $('#shell').value = draft.shell || 'cmd'; $('#terminal').checked = !!draft.terminal;
    $('#error').hidden = true; syncEditor(); openLayer('editorLayer'); $('#name').focus();
  }
  function syncEditor() {
    const type = $('#kind').value;
    $('#argsRow').hidden = $('#browse').hidden = $('#folder').hidden = type !== 'app';
    $('#cwdRow').hidden = type === 'web'; $('#commandOptions').hidden = type !== 'cmd';
    $('#targetLabel').textContent = type === 'cmd' ? '命令' : type === 'web' ? '网址' : '路径或程序名';
    $('#target').placeholder = type === 'cmd' ? '例如：git status' : type === 'web' ? '例如：https://dotnet9.com' : '例如：C:\\Work 或 explorer.exe';
  }
  function error(message) { $('#error').textContent = message; $('#error').hidden = false; }
  function gesture(e) {
    return [e.ctrlKey ? 'Ctrl' : '', e.altKey ? 'Alt' : '', e.shiftKey ? 'Shift' : '', e.metaKey ? 'Win' : '',
      e.key === ' ' ? 'Space' : e.key.length === 1 ? e.key.toUpperCase() : e.key].filter(Boolean).join('+');
  }
  function saveEditor(e) {
    e.preventDefault();
    if (configError) { error('配置读取失败，请先重新加载配置。'); return; }
    const next = {...draft, type:$('#kind').value, target:$('#target').value.trim(), name:$('#name').value.trim(),
      args:$('#args').value.trim(), cwd:$('#cwd').value.trim(), hotkey:$('#hotkey').value, shell:$('#shell').value, terminal:$('#terminal').checked};
    if (!next.target) { error('请填写目标。'); $('#target').focus(); return; }
    if (next.type === 'web') {
      if (!next.target.includes('://')) next.target = 'https://' + next.target;
      try { const url = new URL(next.target); if (!['http:', 'https:'].includes(url.protocol) || !url.hostname) throw Error(); }
      catch (_) { error('请填写有效的 HTTP 或 HTTPS 网址。'); return; }
      next.args = next.cwd = '';
    } else if (next.type === 'app') next.target = next.target.replace(/^"|"$/g, '');
    if (next.type !== 'app') next.args = '';
    if (next.type !== 'cmd') { next.shell = 'cmd'; next.terminal = false; }
    if (next.hotkey === 'Ctrl+Alt+Space') { error('该快捷键已用于唤出 QuickApp。'); return; }
    const conflict = next.hotkey && items.find(it => it.id !== next.id && it.hotkey === next.hotkey);
    if (conflict) { error('该快捷键已用于「' + conflict.name + '」。'); return; }
    next.name ||= inferName(next.target, next.type); next.icon ||= next.type === 'cmd' ? 'terminal' : next.type === 'web' ? 'globe' : 'app';
    if (draft.target !== next.target || draft.type !== next.type) delete next.recId;
    const index = items.findIndex(it => it.id === next.id);
    if (index >= 0) items[index] = next; else { next.id = 'i' + crypto.randomUUID(); items.push(next); }
    persist(); closeLayer('editorLayer'); refresh(); QA.toast((index >= 0 ? '已更新：' : '已添加：') + next.name);
  }
  function syncSettings() {
    QA.syncControls($('.settings-card'), st);
    for (const [id, key] of [['rgTile', 'tileSize'], ['rgRadius', 'radius'], ['rgAutoHide', 'autoHide']]) {
      $('#' + id).value = st[key]; $('#out' + id.slice(2)).textContent = key === 'autoHide' ? st[key] ? st[key] + ' ms' : '不自动隐藏' : st[key];
    }
    $('#selMonitor').value = st.monitor;
    $('#storageMode').textContent = st.portable ? '便携版 · 配置随程序目录' : '安装版 · 用户配置目录';
    $('#configPath').textContent = st.portable ? 'QuickApp\\config.json' : '%LOCALAPPDATA%\\QuickApp\\config.json';
    $('#switchStorage').textContent = st.portable ? '切换为安装版' : '切换为便携版';
    $('#configError').hidden = !configError; $('#configError').textContent = configError || '';
  }
  function selectTab(key) {
    $$('[data-tab]').forEach(tab => { const active = tab.dataset.tab === key; tab.classList.toggle('active', active); tab.setAttribute('aria-selected', String(active)); tab.tabIndex = active ? 0 : -1; });
    $$('[data-panel]').forEach(panel => panel.hidden = panel.dataset.panel !== key);
  }
  function openSettings() { syncSettings(); openLayer('settingsLayer'); }
  function exportConfig() {
    if (configError) { QA.toast('配置读取失败，请先重新加载配置。'); return; }
    const kinds = {app:0, web:1, cmd:2}, edges = {top:0, bottom:1, left:2, right:3};
    const config = {schemaVersion:1, settings:{theme:st.themePref, style:st.style, showLabels:st.label === 'iconText', edge:edges[st.edge], tileSize:st.tileSize, cornerRadius:st.radius, pinned:st.pinned, checkUpdates:st.checkUpdates,
      autoStart:st.autoStart, revealOnEdgeTouch:st.edgeReveal, collapseAfterLaunch:st.autoCollapse, autoHideDelayMs:st.autoHide, hotkey:'Ctrl+Alt+Space'},
      items:items.map(it => ({id:it.id, name:it.name, kind:kinds[it.type], target:it.target, arguments:it.args || null, workingDirectory:it.cwd || null, hotkey:it.hotkey || null, usePowerShell:it.shell === 'powershell', runInTerminal:!!it.terminal}))};
    const url = URL.createObjectURL(new Blob([JSON.stringify(config, null, 2)], {type:'application/json'}));
    const anchor = h('a', {href:url, download:'QuickApp-config.json'}); anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); QA.toast('已导出配置');
  }
  function importConfig(config) {
    if (!config || !Array.isArray(config.items) || config.items.some(it => !it || typeof it.target !== 'string')) throw Error('不是有效的 QuickApp 配置文件');
    const imported = config.items.map(it => ({id:it.id || 'i' + crypto.randomUUID(), name:it.name || inferName(it.target, 'app'), type:it.type || ['app', 'web', 'cmd'][it.kind] || 'app', target:it.target,
      args:it.arguments || '', cwd:it.workingDirectory || '', hotkey:it.hotkey || '', shell:it.usePowerShell ? 'powershell' : 'cmd', terminal:!!it.runInTerminal, icon:it.kind === 2 ? 'terminal' : it.kind === 1 ? 'globe' : 'app'}));
    dialog('导入配置', [h('p', {text:'导入将替换当前快捷项与设置。当前 ' + items.length + ' 项，导入后 ' + imported.length + ' 项。'})],
      [chip('取消', () => closeLayer('dialogLayer')), chip('导入并替换', () => {
        items = imported; configError = null;
        const settings = config.settings || {};
        Object.assign(st, defaults, {themePref:settings.theme || 'system', style:settings.style || 'glass', label:settings.showLabels ? 'iconText' : 'icon',
          edge:['top', 'bottom', 'left', 'right'][settings.edge] || 'top', tileSize:settings.tileSize || 44, radius:settings.cornerRadius || 18, pinned:!!settings.pinned, checkUpdates:settings.checkUpdates !== false,
          autoStart:!!settings.autoStart, edgeReveal:settings.revealOnEdgeTouch !== false, autoCollapse:settings.collapseAfterLaunch !== false, autoHide:settings.autoHideDelayMs ?? 700});
        st.edit = false; st.searchOpen = false; persist(); closeLayer('dialogLayer'); refresh(); QA.toast('已导入 ' + items.length + ' 个快捷项');
      }, true)]);
  }
  function reloadConfig() {
    try {
      const config = JSON.parse(localStorage.getItem(storageKey)) || savedConfig;
      if (!Array.isArray(config.items)) throw Error('快捷项列表无效');
      items = config.items; Object.assign(st, defaults, config.settings); configError = null; refresh(); QA.toast('已重新加载 ' + items.length + ' 个快捷项');
    } catch (e) { configError = '配置读取失败：' + e.message; refresh(); QA.toast(configError); }
  }
  function renderRec() {
    const inDock = items.some(it => it.recId === rec.id);
    const card = h('div', {class:'rec-card', 'data-rec':rec.id}, h('div', {class:'rec-head'},
      h('span', {class:'rec-icon'}, h('img', {src:'assets/vex-logo.svg', alt:'Vex 图标'})),
      h('div', {class:'rec-info'}, h('div', {class:'rec-title'}, h('strong', {text:rec.name}), h('span', {class:'rec-ver', text:rec.version})),
        h('div', {class:'rec-desc', text:'跨平台 Markdown 编辑器：实时预览、大纲导航、文档统计及导出。'}), h('div', {class:'rec-meta', text:'Vex-v1.5.1-win-x64-setup.exe · 40.7 MB'}))));
    if (['downloading', 'installing'].includes(rec.phase)) card.append(h('div', {class:'rec-download'},
      h('div', {class:'update-progress' + (rec.phase === 'installing' ? ' indeterminate' : '')}, h('i', {style:'width:' + rec.progress + '%'})),
      h('span', {class:'rec-pct', text:rec.phase === 'installing' ? '安装中…' : rec.progress + '%'}),
      rec.phase === 'downloading' ? chip('取消', () => { clearInterval(rec.timer); rec.phase = 'idle'; renderRec(); }) : null));
    else card.append(h('div', {class:'rec-foot'}, h('div', {class:'rec-actions'},
      rec.phase === 'installed' ? h('span', {class:'rec-badge', text:'已安装'}) : null,
      chip(rec.phase === 'idle' ? '安装' : inDock ? '移出 Dock' : '加入 Dock', () => {
        if (rec.phase === 'idle') installRec();
        else if (inDock) remove(items.find(it => it.recId === rec.id)); else addRec();
      }, rec.phase === 'idle'), chip('发布页', () => window.open('https://github.com/dotnet9/Vex/releases/latest', '_blank', 'noopener')))));
    $('#recList').replaceChildren(card);
  }
  function addRec() {
    if (configError) { QA.toast('配置读取失败，请先重新加载配置。'); return; }
    if (!items.some(it => it.recId === rec.id)) items.push({id:'rec-vex', recId:rec.id, name:'Vex', type:'app', target:'C:\\Program Files\\Vex\\Vex.exe', customUrl:'assets/vex-logo.svg'});
    persist(); refresh(); QA.toast('Vex 快捷方式已加入 Dock');
  }
  function installRec() {
    rec.phase = 'downloading'; rec.progress = 0; renderRec();
    rec.timer = setInterval(() => {
      rec.progress = Math.min(100, rec.progress + 10); renderRec();
      if (rec.progress === 100) {
        clearInterval(rec.timer); rec.phase = 'installing'; renderRec();
        rec.timer = setTimeout(() => { rec.phase = 'installed'; addRec(); }, 900);
      }
    }, 180);
  }
  const updateActions = {
    download() { update.phase = 'downloading'; update.progress = 0; renderUpdate();
      updateTimer = setInterval(() => { update.progress += 10; if (update.progress >= 100) { clearInterval(updateTimer); update.phase = 'ready'; } renderUpdate(); }, 180); },
    cancel() { clearInterval(updateTimer); update.phase = 'found'; renderUpdate(); QA.toast('已取消下载'); },
    dismiss() { clearInterval(updateTimer); update = null; popoverOpen = false; renderUpdate(); },
    install() { QA.toast('正在打开安装程序'); updateActions.dismiss(); exit(); },
    openPage() { window.open('https://github.com/dotnet9/QuickApp/releases/latest', '_blank', 'noopener'); }
  };
  function renderUpdate() {
    const slot = $('#updateSlot'), popover = $('#updatePopover'); slot.replaceChildren(); popover.replaceChildren(); popover.hidden = !popoverOpen || !update || st.exited;
    if (!update || st.exited) return;
    if (st.edge === 'left' || st.edge === 'right') slot.append(QA.updatePillNode(() => { popoverOpen = !popoverOpen; renderUpdate(); }));
    else { slot.append(QA.updateBarNode(update, updateActions)); popoverOpen = false; popover.hidden = true; }
    if (popoverOpen) {
      const card = QA.updateCardNode(update, updateActions); popover.append(card);
      requestAnimationFrame(() => { const rect = $('#dock').getBoundingClientRect(); popover.style.left = Math.max(8, Math.min(st.edge === 'left' ? rect.right + 8 : rect.left - 338, innerWidth - 338)) + 'px'; popover.style.top = Math.max(8, Math.min(rect.bottom - 140, innerHeight - card.offsetHeight - 10)) + 'px'; });
    }
  }

  QA.hydrateIcons();
  QA.attachBubble($('#dock'), list, $('#bubble'), st, id => items.find(it => it.id === id) || QA.INSTALLED.find(it => it.id === id), it => QA.targetSummary(it) + (it.hotkey ? ' · ' + it.hotkey : ''));
  $('#btnMenu').onclick = e => QA.openMenuAtButton(dockMenu(), e.currentTarget);
  $('#dock').addEventListener('contextmenu', e => {
    e.preventDefault(); const node = e.target.closest('[data-id]'), it = items.find(it => it.id === node?.dataset.id);
    QA.openMenu(it ? itemMenu(it) : dockMenu(), e.clientX, e.clientY);
  });
  $('#btnSearch').onclick = () => search(); $('#searchInput').oninput = refresh;
  $('#searchClear').onclick = () => { $('#searchInput').value = ''; refresh(); $('#searchInput').focus(); };
  $('#btnPin').onclick = () => { st.pinned = !st.pinned; persist(); refresh(); };
  $('#btnHide').onclick = () => { st.dockVisible = false; refresh(); };
  $('#editDone').onclick = () => edit(false);
  $('#launchApp').onclick = showDock; $('#dockHandle').onclick = showDock;
  $('#dockHandle').onpointerenter = () => { if (st.edgeReveal) revealTimer = setTimeout(showDock, 300); };
  $('#dockHandle').onpointerleave = () => clearTimeout(revealTimer);
  $('#dock').onpointerenter = () => clearTimeout(hideTimer);
  let dockDrag = null;
  $('#dock').addEventListener('pointerdown', e => {
    if (e.button !== 0 || e.target.closest('button,input,select,.dock-item,[data-system]')) return;
    dockDrag = {x:e.clientX, y:e.clientY, moved:false};
    $('#dock').setPointerCapture(e.pointerId); clearTimeout(hideTimer);
  });
  $('#dock').addEventListener('pointermove', e => {
    if (!dockDrag) return;
    const dx = e.clientX - dockDrag.x, dy = e.clientY - dockDrag.y;
    if (Math.abs(dx) + Math.abs(dy) < 6) return;
    dockDrag.moved = true; $('#dock').style.transform = 'translate(' + dx + 'px,' + dy + 'px)';
  });
  $('#dock').addEventListener('pointerup', e => {
    if (!dockDrag) return;
    const moved = dockDrag.moved; dockDrag = null; $('#dock').style.transform = '';
    if ($('#dock').hasPointerCapture(e.pointerId)) $('#dock').releasePointerCapture(e.pointerId);
    if (moved) setEdge(Object.entries({top:e.clientY, bottom:innerHeight - e.clientY, left:e.clientX, right:innerWidth - e.clientX}).sort((a,b) => a[1] - b[1])[0][0]);
  });
  $('#dock').addEventListener('pointercancel', () => { dockDrag = null; $('#dock').style.transform = ''; });
  $('#dock').onpointerleave = () => {
    if (st.pinned || st.edit || st.searchOpen || !st.autoHide || $('.menu') || $$('.modal-layer').some(el => !el.hidden)) return;
    hideTimer = setTimeout(() => { st.dockVisible = false; refresh(); }, st.autoHide);
  };
  list.onclick = e => {
    const add = e.target.closest('[data-add]');
    if (add) {
      if (configError) { QA.toast('配置读取失败，请先重新加载配置。'); return; }
      const it = QA.INSTALLED.find(it => it.id === add.dataset.add);
      items.push({...it, id:'i' + crypto.randomUUID()}); persist(); refresh(); QA.toast('已添加：' + it.name); return;
    }
    const system = e.target.closest('[data-system]');
    if (system) { run(QA.INSTALLED.find(it => it.id === system.dataset.system)); return; }
    const node = e.target.closest('[data-id]'); if (!node) return;
    const it = items.find(it => it.id === node.dataset.id) || QA.INSTALLED.find(it => it.id === node.dataset.id);
    if (e.target.closest('.remove')) remove(it); else if (!e.target.closest('input')) run(it);
  };
  list.ondblclick = e => { if (st.edit) { const it = items.find(it => it.id === e.target.closest('[data-id]')?.dataset.id); if (it) rename(it); } };
  list.addEventListener('wheel', e => { const vertical = st.searchOpen || ['left', 'right'].includes(st.edge); list.scrollBy({[vertical ? 'top' : 'left']:Math.abs(e.deltaY) > Math.abs(e.deltaX) ? e.deltaY : e.deltaX}); e.preventDefault(); }, {passive:false});
  list.onscroll = () => QA.updateScrollChrome(list, fades);
  list.ondragstart = e => { const node = e.target.closest('.dock-item'); if (!st.edit || !node) return; dragId = node.dataset.id; e.dataTransfer.setData('text/plain', dragId); node.classList.add('dragging'); };
  list.ondragover = e => {
    if (!st.edit || !dragId) return; e.preventDefault();
    const node = e.target.closest('.dock-item'); if (!node) return;
    const rect = node.getBoundingClientRect(), vertical = ['left', 'right'].includes(st.edge), line = $('#dropLine');
    line.hidden = false; line.style.left = (vertical ? rect.left : rect.left - 4) + 'px'; line.style.top = (vertical ? rect.top - 4 : rect.top) + 'px'; line.style.width = (vertical ? rect.width : 2) + 'px'; line.style.height = (vertical ? 2 : rect.height) + 'px';
  };
  list.ondrop = e => {
    e.preventDefault(); const targetId = e.target.closest('.dock-item')?.dataset.id;
    const from = items.findIndex(it => it.id === dragId), to = items.findIndex(it => it.id === targetId);
    if (from >= 0 && to >= 0 && from !== to) { const [it] = items.splice(from, 1); items.splice(to, 0, it); persist(); }
    dragId = null; $('#dropLine').hidden = true; refresh();
  };
  list.ondragend = () => { dragId = null; $('#dropLine').hidden = true; $$('.dragging').forEach(el => el.classList.remove('dragging')); };
  $('#iconFile').onchange = async e => { const file = e.target.files[0]; if (file && pendingIcon) {
    const reader = new FileReader(); reader.onload = () => { pendingIcon.customUrl = reader.result; persist(); refresh(); }; reader.readAsDataURL(file);
  } e.target.value = ''; };
  $('#kind').onchange = syncEditor; $('#editor').onsubmit = saveEditor;
  $('#name').onkeydown = e => { if (e.key === 'Enter') { e.preventDefault(); $('#target').focus(); } };
  $('#hotkey').onkeydown = e => {
    if (e.key === 'Tab' || e.key === 'Escape') return;
    e.preventDefault(); e.stopPropagation();
    if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return;
    if (!e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey) { if (['Backspace', 'Delete'].includes(e.key)) $('#hotkey').value = ''; else error('快捷键需要 Ctrl、Alt、Shift 或 Win 修饰键。'); return; }
    $('#hotkey').value = gesture(e); $('#error').hidden = true;
  };
  $('#clearKey').onclick = () => $('#hotkey').value = '';
  $('#browse').onclick = () => choosePath(false, path => $('#target').value = path, $('#target').value);
  $('#folder').onclick = () => choosePath(true, path => $('#target').value = path, $('#target').value);
  $('#browseCwd').onclick = () => choosePath(true, path => $('#cwd').value = path, $('#cwd').value);
  $('#cancel').onclick = $('#close').onclick = () => closeLayer('editorLayer');
  $('#settingsClose').onclick = () => closeLayer('settingsLayer'); $('#dialogClose').onclick = () => closeLayer('dialogLayer');
  $$('[data-dismiss]').forEach(el => el.onclick = () => closeLayer(el.dataset.dismiss));
  $$('[data-tab]').forEach((tab, i, tabs) => {
    tab.onclick = () => selectTab(tab.dataset.tab);
    tab.onkeydown = e => { if (!['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.key)) return; e.preventDefault(); const next = tabs[(i + (['ArrowUp', 'ArrowLeft'].includes(e.key) ? -1 : 1) + tabs.length) % tabs.length]; selectTab(next.dataset.tab); next.focus(); };
  });
  QA.bindControls($('.settings-card'), st, () => { persist(); refresh(); });
  for (const [id, key] of [['rgTile', 'tileSize'], ['rgRadius', 'radius'], ['rgAutoHide', 'autoHide']]) $('#' + id).oninput = e => { st[key] = Number(e.target.value); persist(); refresh(); };
  $('#selMonitor').onchange = e => { st.monitor = e.target.value; persist(); QA.toast('Dock 已迁移至' + e.target.selectedOptions[0].textContent); };
  $('#switchStorage').onclick = () => { st.portable = !st.portable; persist(); syncSettings(); QA.toast('已切换并迁移配置'); };
  $('#btnExport').onclick = exportConfig; $('#btnReloadConfig').onclick = reloadConfig;
  $('#btnOpenConfig').onclick = () => QA.toast('正在打开配置文件夹');
  $('#btnImport').onclick = () => $('#importFile').click();
  $('#importFile').onchange = async e => { try { if (e.target.files[0]) importConfig(JSON.parse(await e.target.files[0].text())); } catch (err) { QA.toast('导入失败：' + err.message); } e.target.value = ''; };
  $('#aboutLicense').onclick = () => dialog('许可证', [h('h3', {text:'MIT License'}), h('p', {text:'Copyright (c) QuickApp contributors'}), h('p', {text:'Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files, to deal in the Software without restriction.'}), h('p', {text:'THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND.'})], [chip('关闭', () => closeLayer('dialogLayer'), true)]);
  $('#aboutCheckUpdate').onclick = () => {
    $('#aboutCheckResult').textContent = '正在检查…'; $('#aboutCheckUpdate').disabled = true;
    setTimeout(() => { update = {phase:'found', tag:'v0.6.0', progress:0, installer:true}; $('#aboutCheckResult').textContent = '发现新版本 v0.6.0'; $('#aboutCheckUpdate').disabled = false; st.dockVisible = true; renderUpdate(); QA.toast('发现新版本 v0.6.0', {actionLabel:'查看', onAction:() => { closeLayer('settingsLayer'); showDock(); if (['left', 'right'].includes(st.edge)) { popoverOpen = true; renderUpdate(); } }}); }, 500);
  };
  $('#trayButton').onclick = e => QA.openMenuAtButton([
    {label:st.dockVisible ? '收起 Dock' : '显示 Dock', icon:'grid', action:() => { st.dockVisible = !st.dockVisible; refresh(); }},
    {label:'开机启动', icon:'power', note:st.autoStart ? '✓' : '', action:() => { st.autoStart = !st.autoStart; persist(); refresh(); }},
    {label:'钉住 Dock', icon:'pin', note:st.pinned ? '✓' : '', action:() => { st.pinned = !st.pinned; persist(); refresh(); }},
    {sep:true}, {label:'设置', icon:'gear', action:openSettings}, {sep:true}, {label:'退出', icon:'power', danger:true, action:exit}], e.currentTarget);
  document.addEventListener('keydown', e => {
    if (e.defaultPrevented) return;
    const layers = ['dialogLayer', 'editorLayer', 'settingsLayer'], active = layers.find(id => !$('#' + id).hidden);
    if (active) {
      if (e.key === 'Escape') { e.preventDefault(); closeLayer(active); }
      if (e.key === 'Enter' && e.ctrlKey && active === 'editorLayer') saveEditor(e);
      if (e.key === 'Tab') {
        const focusable = $$('button,input,select,a[href]', $('#' + active)).filter(el => el.getClientRects().length && !el.disabled);
        if (e.shiftKey && document.activeElement === focusable[0]) { e.preventDefault(); focusable.at(-1).focus(); }
        else if (!e.shiftKey && document.activeElement === focusable.at(-1)) { e.preventDefault(); focusable[0].focus(); }
      }
      return;
    }
    if (st.exited) return;
    if (e.ctrlKey && e.altKey && e.code === 'Space') { e.preventDefault(); st.dockVisible = !(st.dockVisible && !st.pinned); refresh(); return; }
    const bound = items.find(it => it.hotkey && it.hotkey === gesture(e));
    if (bound && !st.exited) { e.preventDefault(); run(bound); return; }
    if (e.key === 'Escape') { if (popoverOpen) { popoverOpen = false; renderUpdate(); } else if (st.searchOpen) search(false); else if (st.edit) edit(false); else { st.dockVisible = false; refresh(); } return; }
    if (e.target.closest('input,select,textarea')) { if (e.key === 'Enter' && e.target.id === 'searchInput') { e.preventDefault(); run(QA.searchItems(items, e.target.value)[0] || systemRows(e.target.value)[0]); } return; }
    if (e.key === 'Enter' && e.target.closest('[data-system]')) { e.preventDefault(); run(QA.INSTALLED.find(it => it.id === e.target.closest('[data-system]').dataset.system)); return; }
    if (e.key.toLowerCase() === 's' || e.ctrlKey && e.key.toLowerCase() === 'k') { e.preventDefault(); search(); }
    else if (e.key.toLowerCase() === 'e') edit();
    else if (/^[1-9]$/.test(e.key)) run(items[Number(e.key) - 1]);
    else if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) {
      const tiles = $$('.tile', list), index = tiles.indexOf(document.activeElement), step = ['ArrowLeft', 'ArrowUp'].includes(e.key) ? -1 : 1;
      e.preventDefault(); tiles[(index + step + tiles.length) % tiles.length]?.focus();
    }
  });
  document.addEventListener('pointerdown', e => { if (popoverOpen && !e.target.closest('#updatePopover,.update-pill')) { popoverOpen = false; renderUpdate(); } });
  window.addEventListener('resize', () => { QA.closeMenu(); QA.updateScrollChrome(list, fades); renderUpdate(); });
  $('#clock').textContent = new Date().toLocaleTimeString('zh-CN', {hour:'2-digit', minute:'2-digit'});
  selectTab('general'); refresh();
})();
