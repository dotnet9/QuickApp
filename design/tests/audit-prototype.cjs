const { chromium } = require('playwright');
const path = require('node:path');
const fs = require('node:fs');
const { pathToFileURL } = require('node:url');
(async()=>{
  const output=path.resolve(process.env.QUICKAPP_AUDIT_OUTPUT||'artifacts/prototype-verification');fs.mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({headless:true,...(process.env.CI?{}:{channel:'msedge'})});
  const page=await browser.newPage({viewport:{width:1280,height:900},deviceScaleFactor:1});
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  const isolated=path.join(output,'standalone');fs.mkdirSync(isolated,{recursive:true});
  const entry=path.join(isolated,'QuickApp.html');fs.copyFileSync(path.resolve(__dirname,'../index.html'),entry);
  const dependencies=[];page.on('request',request=>{if(!request.url().startsWith('data:')&&request.url()!==pathToFileURL(entry).href)dependencies.push(request.url());});
  await page.goto(pathToFileURL(entry).href);
  await page.locator('#btnMenu').click();await page.getByRole('menuitem',{name:'设置',exact:true}).click();
  const frame=await page.locator('.settings-card').evaluate(element=>({border:parseFloat(getComputedStyle(element).borderTopWidth),title:element.querySelector('.modal-head').getBoundingClientRect().height,buttons:element.querySelector('.window-controls').children.length}));
  if(frame.border<1||Math.abs(frame.title-32)>1||frame.buttons!==3)throw new Error('Desktop settings frame is incomplete: '+JSON.stringify(frame));
  await page.locator('.settings-card [data-window-action="maximize"]').click();
  if(!(await page.locator('.settings-card').evaluate(element=>element.classList.contains('is-maximized'))))throw new Error('Window maximize control is inactive');
  await page.locator('.settings-card [data-window-action="maximize"]').click();
  let routes=0;
  for(const language of ['zh-CN','en-US']){
    await page.locator('[data-tab="appearance"]').click();await page.selectOption('#qaLanguage',language);
    for(const palette of ['Default','Desert','Aquatic','Dusk','NightSky','DotNet','Aurora','Ember']){
      await page.selectOption('#qaPalette',palette);
      for(const mode of ['light','dark']){
        await page.locator('[data-control="themePref"] [data-value="'+mode+'"]').click();
        const properties=await page.locator('.settings-card').evaluate(element=>({width:element.getBoundingClientRect().width,height:element.getBoundingClientRect().height,text:getComputedStyle(element).color}));
        if(Math.abs(properties.width-720)>2||Math.abs(properties.height-620)>2)throw new Error('Settings viewport differs: '+JSON.stringify(properties));
        if(await page.locator('#qaPalette option').count()!==8)throw new Error('Palette catalog differs');
        await page.locator('.settings-card').screenshot({path:path.join(output,`appearance-${language}-${palette}-${mode}.png`),animations:'disabled'});routes++;
      }
    }
    for(const tab of ['general','appearance','data','recommend','about']){
      await page.locator('[data-tab="'+tab+'"]').click();
      if(!(await page.locator('[data-panel="'+tab+'"]').isVisible()))throw new Error('Empty route '+tab);
      await page.locator('.settings-card').screenshot({path:path.join(output,`settings-${tab}-${language}.png`),animations:'disabled'});routes++;
    }
  }
  await page.locator('[data-tab="appearance"]').click();await page.locator('[data-control="themePref"] [data-value="system"]').click();
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
