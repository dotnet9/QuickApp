const { chromium } = require('playwright');
const path = require('node:path');
const fs = require('node:fs');
const { pathToFileURL } = require('node:url');
(async()=>{
  const output=path.resolve(process.env.QUICKAPP_AUDIT_OUTPUT||'artifacts/prototype-verification');fs.mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({headless:true,...(process.env.CI?{}:{channel:'msedge'})});
  const page=await browser.newPage({viewport:{width:1280,height:900},deviceScaleFactor:1});
  async function checkAppearanceLanguage(language){
    const matches=await page.evaluate(expected=>{
      const keys={Default:'DefaultPalette',Desert:'DesertPalette',Aquatic:'AquaticPalette',Dusk:'DuskPalette',NightSky:'NightSkyPalette',DotNet:'DotNetPalette',Aurora:'AuroraPalette',Ember:'EmberPalette'};
      const words=QUICKAPP_RESOURCES[expected].QuickApp.Text,mode=document.getElementById('qaTheme'),languageButton=document.getElementById('qaLanguage');
      const modeKey=mode.dataset.mode==='light'?'LightTheme':mode.dataset.mode==='dark'?'DarkTheme':'SystemTheme';
      return document.documentElement.lang===expected && mode.title===words[modeKey] && mode.getAttribute('aria-label')===mode.title && languageButton.title===words.InterfaceLanguage && languageButton.getAttribute('aria-label')===languageButton.title && [...document.querySelectorAll('#qaPalette button[data-palette]')].every(button=>button.title===words[keys[button.dataset.palette]] && button.getAttribute('aria-label')===button.title);
    },language);
    if(!matches)throw Error('Title-bar language, tooltips and accessibility labels did not switch immediately: '+language);
  }
  const setLanguage=async value=>{await page.locator('#qaLanguage').click();await page.locator('#qaLanguageMenu [data-language="'+value+'"]').click();await checkAppearanceLanguage(value);};
  const setMode=async value=>{for(let index=0;index<3;index++){if(await page.locator('#qaTheme').getAttribute('data-mode')===value){await checkAppearanceLanguage(await page.locator('html').getAttribute('lang'));return;}await page.locator('#qaTheme').click();}if(await page.locator('#qaTheme').getAttribute('data-mode')!==value)throw Error('Theme cycle failed');await checkAppearanceLanguage(await page.locator('html').getAttribute('lang'));};
  const setPalette=async value=>{await page.locator('button[data-palette="'+value+'"]').click();};
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  const isolated=path.join(output,'standalone');fs.mkdirSync(isolated,{recursive:true});
  const entry=path.join(isolated,'QuickApp.html');fs.copyFileSync(path.resolve(__dirname,'../index.html'),entry);
  const dependencies=[];page.on('request',request=>{if(!request.url().startsWith('data:')&&request.url()!==pathToFileURL(entry).href)dependencies.push(request.url());});
  await page.goto(pathToFileURL(entry).href);
  await page.locator('#btnMenu').click();await page.getByRole('menuitem',{name:'设置',exact:true}).click();
  // 入场动画会缩放渲染边界，设计尺寸应检查布局值。
  const frame=await page.locator('.settings-card').evaluate(element=>({border:parseFloat(getComputedStyle(element).borderTopWidth),title:parseFloat(getComputedStyle(element.querySelector('.modal-head')).height),buttons:element.querySelector('.window-controls').children.length}));
  if(frame.border<1||frame.title!==32||frame.buttons!==3)throw new Error('Desktop settings frame is incomplete: '+JSON.stringify(frame));
  if(await page.locator('.settings-card .window-controls > button:last-child').getAttribute('id')!=='settingsClose' || await page.locator('.settings-card .title-appearance > #qaTheme').count()!==1)throw new Error('Theme toggle must stay separate from the standard caption buttons');
  const paletteOrder=await page.locator('#qaPalette button[data-palette]').evaluateAll(buttons=>buttons.map(button=>button.dataset.palette));
  if(JSON.stringify(paletteOrder)!==JSON.stringify(['DotNet','Aurora','Ember','Default','Aquatic','Desert','Dusk','NightSky']))throw new Error('Palette order differs from the native title bar');
  const swatches=await page.locator('#qaPalette button i').evaluateAll(dots=>dots.map(dot=>[getComputedStyle(dot).backgroundColor,getComputedStyle(dot).backgroundImage]));
  if(swatches[0][0]!=='rgb(81, 43, 212)' || !swatches[1][1].startsWith('linear-gradient('))throw new Error('Fixed .NET and Aurora swatches differ from the native title bar');
  await page.locator('.settings-card [data-window-action="minimize"]').click();
  if(!(await page.locator('.settings-card').evaluate(element=>element.classList.contains('is-minimized'))) || await page.locator('.settings-layout').isVisible())throw new Error('Minimizing settings must hide its content');
  await page.locator('.settings-card [data-window-action="minimize"]').click();
  if(!(await page.locator('.settings-layout').isVisible()))throw new Error('Minimized settings cannot be restored');
  await page.locator('.settings-card [data-window-action="maximize"]').click();
  if(!(await page.locator('.settings-card').evaluate(element=>element.classList.contains('is-maximized') && element.getBoundingClientRect().width>720)))throw new Error('Window maximize control does not change the layout');
  await page.locator('.settings-card [data-window-action="maximize"]').click();
  if(Math.abs(await page.locator('.settings-card').evaluate(element=>element.getBoundingClientRect().width)-720)>2)throw new Error('Window restore does not recover the settings layout');
  await page.locator('#settingsClose').click();
  if(await page.locator('#settingsLayer').isVisible())throw new Error('Settings close button does not close its window');
  await page.locator('#btnMenu').click();await page.getByRole('menuitem',{name:'设置',exact:true}).click();
  if(!(await page.locator('.settings-card').isVisible()))throw new Error('Closed settings cannot be reopened');
  let routes=0;
  for(const language of ['zh-CN','en-US']){
    await page.locator('[data-tab="appearance"]').click();await setLanguage(language);
    await setMode('system');
    for(const palette of ['Default','Desert','Aquatic','Dusk','NightSky','DotNet','Aurora','Ember']){
      await setPalette(palette);
      for(const mode of ['light','dark']){
        await setMode(mode);
        if(JSON.stringify(await page.locator('#qaPalette button i').evaluateAll(dots=>dots.map(dot=>[getComputedStyle(dot).backgroundColor,getComputedStyle(dot).backgroundImage])))!==JSON.stringify(swatches))throw new Error('Palette swatches must remain stable across theme modes');
        const properties=await page.locator('.settings-card').evaluate(element=>({width:element.getBoundingClientRect().width,height:element.getBoundingClientRect().height,text:getComputedStyle(element).color}));
        if(Math.abs(properties.width-720)>2||Math.abs(properties.height-620)>2)throw new Error('Settings viewport differs: '+JSON.stringify(properties));
        if(await page.locator('#qaPalette [data-palette]').count()!==8)throw new Error('Palette catalog differs');
        await page.locator('.settings-card').screenshot({path:path.join(output,`appearance-${language}-${palette}-${mode}.png`),animations:'disabled'});routes++;
      }
    }
    for(const tab of ['general','appearance','data','recommend','about']){
      await page.locator('[data-tab="'+tab+'"]').click();
      if(!(await page.locator('[data-panel="'+tab+'"]').isVisible()))throw new Error('Empty route '+tab);
      await page.locator('.settings-card').screenshot({path:path.join(output,`settings-${tab}-${language}.png`),animations:'disabled'});routes++;
    }
  }
  await page.locator('[data-tab="appearance"]').click();await setMode('system');
  await page.locator('#settingsClose').click();await page.locator('#btnMenu').click();
  await page.getByRole('menuitem',{name:'About',exact:true}).click();
  if(!(await page.locator('#aboutLayer').isVisible()))throw new Error('Independent about entry is missing');
  if(!(await page.locator('.standalone-about-info').textContent()).includes('2026-10-15 00:00:00'))throw new Error('About build-time sample differs');
  await page.locator('.standalone-about [data-window-action="minimize"]').click();
  if(!(await page.locator('.standalone-about').evaluate(element=>element.classList.contains('is-minimized'))))throw new Error('Window minimize control is inactive');
  await page.locator('.standalone-about [data-window-action="minimize"]').click();
  await page.locator('.standalone-about').screenshot({path:path.join(output,'standalone-about.png'),animations:'disabled'});routes++;
  await page.locator('#standaloneCheckUpdate').click();await page.locator('#dialogLayer').waitFor({state:'visible'});
  await page.locator('#dialogLayer .modal-card').screenshot({path:path.join(output,'release-notes.png'),animations:'disabled'});routes++;
  if(dependencies.length)throw new Error('Unexpected external dependency: '+dependencies.join(', '));
  if(errors.length)throw new Error(errors.join('\n'));
  await browser.close();console.log(`PASS: ${routes} rendered states; zero JavaScript errors`);
})().catch(error=>{console.error(error);process.exit(1)});
