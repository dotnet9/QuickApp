/* 原型复用宿主资源与共享令牌，系统调用继续使用受控演示。 */
(() => {
  const root=document.documentElement, dictionary=window.QUICKAPP_RESOURCES;
  const stored=JSON.parse(localStorage.getItem('quickapp-appearance')||'{}');
  let language=stored.language||'zh-CN',palette=stored.palette||'DotNet';
  const source=new WeakMap(),last=new WeakMap();
  const paletteNames={Default:'DefaultPalette',Desert:'DesertPalette',Aquatic:'AquaticPalette',Dusk:'DuskPalette',NightSky:'NightSkyPalette',DotNet:'DotNetPalette',Aurora:'AuroraPalette',Ember:'EmberPalette'};
  for(const head of document.querySelectorAll('.modal-head')){
    const card=head.closest('.modal-card'),close=head.querySelector('button');
    const controls=document.createElement('div');controls.className='window-controls';
    for(const [action,symbol,label] of [['minimize','−','Minimize'],['maximize','□','Maximize']]){
      const button=document.createElement('button');button.type='button';button.className='caption-button';button.dataset.windowAction=action;button.textContent=symbol;button.setAttribute('aria-label',label);
      button.onclick=()=>{card.classList.toggle(action==='minimize'?'is-minimized':'is-maximized');};controls.append(button);
    }
    close.classList.add('caption-button','caption-close');controls.append(close);head.append(controls);
  }
  const t=key=>dictionary[language].QuickApp.Text[key];
  const translations=Object.entries(dictionary['zh-CN'].QuickApp.Text).filter(([,value])=>value.length>1);
  function translate(value){
    if(language==='zh-CN')return value;
    const match=translations.find(([,original])=>original.trim()===value.trim());
    if(match)return value.replace(value.trim(),t(match[0]).trim());
    return value.replace(/^显示器 (\d+) · 左侧$/,'Monitor $1 · Left').replace(/^显示器 (\d+) · 右侧$/,'Monitor $1 · Right').replace('Vex · 维刻','Vex');
  }
  function localize(){
    root.lang=language;
    for(const button of document.querySelectorAll('[data-window-action]')){
      const label=language==='zh-CN'?(button.dataset.windowAction==='minimize'?'最小化或还原':'最大化或还原'):(button.dataset.windowAction==='minimize'?'Minimize or restore':'Maximize or restore');
      button.setAttribute('aria-label',label);button.title=label;
    }
    const walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT);
    for(let node=walker.nextNode();node;node=walker.nextNode()){
      if(['SCRIPT','STYLE'].includes(node.parentElement?.tagName))continue;
      if(!source.has(node)||node.nodeValue!==last.get(node))source.set(node,node.nodeValue);
      const next=translate(source.get(node));last.set(node,next);if(node.nodeValue!==next)node.nodeValue=next;
    }
    for(const element of document.querySelectorAll('[placeholder],[title],[aria-label]')){
      for(const attr of ['placeholder','title','aria-label'])if(element.hasAttribute(attr)){
        const key='source'+attr.replace('-','');element.dataset[key]||=element.getAttribute(attr);
        const next=translate(element.dataset[key]);if(element.getAttribute(attr)!==next)element.setAttribute(attr,next);
      }
    }
  }
  function apply(){
    const tokens=QUICKAPP_PALETTES[palette][root.dataset.theme==='light'?'light':'dark'];
    for(const [css,key] of Object.entries({'--accent':'StudioAccentColor','--text':'StudioTextColor','--text-dim':'StudioMutedColor','--line':'StudioBorderColor','--hover':'StudioRaisedColor','--active':'StudioRaisedColor','--menu-bg':'StudioSurfaceColor','--bubble-bg':'StudioSurfaceColor','--menu-hover':'StudioRaisedColor'}))root.style.setProperty(css,tokens[key]);
    const hex=tokens.StudioSurfaceColor.replace('#','');root.style.setProperty('--surface',[0,2,4].map(i=>parseInt(hex.slice(i,i+2),16)).join(','));
    root.style.setProperty('--accent-ink','#ffffff');
    localStorage.setItem('quickapp-appearance',JSON.stringify({language,palette}));
    document.getElementById('qaPalette').innerHTML=Object.keys(paletteNames).map(key=>`<button class="palette-dot" role="option" data-palette="${key}" aria-selected="${key===palette}" title="${t(paletteNames[key])}" aria-label="${t(paletteNames[key])}"><i style="background:${QUICKAPP_PALETTES[key][root.dataset.theme==='light'?'light':'dark'].StudioAccentColor}"></i></button>`).join('');
    const mode=QUICKAPP_THEME.get(),toggle=document.getElementById('qaTheme');toggle.dataset.mode=mode;toggle.textContent=mode==='light'?'☀':mode==='dark'?'☾':'◐';toggle.title=t(mode==='light'?'LightTheme':mode==='dark'?'DarkTheme':'SystemTheme');toggle.setAttribute('aria-label',toggle.title);
    document.getElementById('qaLanguage').title=t('InterfaceLanguage');
    localize();
  }
  document.getElementById('qaPalette').onclick=event=>{const button=event.target.closest('[data-palette]');if(button){palette=button.dataset.palette;apply();}};
  document.getElementById('qaTheme').onclick=()=>{QUICKAPP_THEME.set(({system:'light',light:'dark',dark:'system'})[QUICKAPP_THEME.get()]);apply();};
  document.getElementById('qaLanguage').onclick=()=>{const menu=document.getElementById('qaLanguageMenu');menu.hidden=!menu.hidden;};
  document.getElementById('qaLanguageMenu').onclick=event=>{const button=event.target.closest('[data-language]');if(button){language=button.dataset.language;document.getElementById('qaLanguageMenu').hidden=true;apply();}};
  document.addEventListener('click',event=>{if(!event.target.closest('.language-picker'))document.getElementById('qaLanguageMenu').hidden=true;});
  new MutationObserver(()=>localize()).observe(document.body,{childList:true,subtree:true,characterData:true});
  new MutationObserver(apply).observe(root,{attributes:true,attributeFilter:['data-theme']});
  apply();
})();
