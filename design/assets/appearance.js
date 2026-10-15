/* 原型复用宿主资源与共享令牌，系统调用继续使用受控演示。 */
(() => {
  const root=document.documentElement, dictionary=window.QUICKAPP_RESOURCES;
  const stored=JSON.parse(localStorage.getItem('quickapp-appearance')||'{}');
  let language=stored.language||'zh-CN',palette=stored.palette||'DotNet';
  const source=new WeakMap(),last=new WeakMap();
  const paletteNames={Default:'DefaultPalette',Desert:'DesertPalette',Aquatic:'AquaticPalette',Dusk:'DuskPalette',NightSky:'NightSkyPalette',DotNet:'DotNetPalette',Aurora:'AuroraPalette',Ember:'EmberPalette'};
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
    const picker=document.getElementById('qaPalette');
    for(const option of picker.options)option.textContent=t(paletteNames[option.value]);
    localize();
  }
  const palettePicker=document.getElementById('qaPalette');
  for(const key of Object.keys(paletteNames))palettePicker.add(new Option(t(paletteNames[key]),key));
  palettePicker.value=palette;palettePicker.onchange=()=>{palette=palettePicker.value;apply();};
  const languagePicker=document.getElementById('qaLanguage');languagePicker.value=language;
  languagePicker.onchange=()=>{language=languagePicker.value;apply();};
  new MutationObserver(()=>localize()).observe(document.body,{childList:true,subtree:true,characterData:true});
  new MutationObserver(apply).observe(root,{attributes:true,attributeFilter:['data-theme']});
  apply();
})();
