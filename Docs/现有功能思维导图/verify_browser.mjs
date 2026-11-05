// Documentation-only browser QA; never launches or connects to Unity.
import {spawn} from 'node:child_process';
import {mkdtemp,writeFile,readFile,unlink,stat} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,dirname} from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
const dir=dirname(fileURLToPath(import.meta.url));
const chrome='C:/Program Files/Google/Chrome/Application/chrome.exe';
async function browser(file,width,height,screenshot){
 const profile=await mkdtemp(join(tmpdir(),'magic-cave-map-'));
 const args=['--headless=new','--no-sandbox','--disable-gpu','--no-first-run','--no-default-browser-check','--disable-background-networking','--hide-scrollbars','--user-data-dir='+profile,'--allow-file-access-from-files','--window-size='+width+','+height,'--virtual-time-budget=1600','--dump-dom'];
 if(screenshot)args.push('--screenshot='+join(dir,screenshot));args.push(pathToFileURL(file).href);
 return new Promise((resolve,reject)=>{let out='',err='';const child=spawn(chrome,args,{windowsHide:true,stdio:['ignore','pipe','pipe']});const timer=setTimeout(()=>{child.kill();reject(Error('Browser render timeout: '+err))},45000);child.stdout.on('data',c=>out+=c);child.stderr.on('data',c=>err+=c);child.on('error',reject);child.on('exit',code=>{clearTimeout(timer);code===0?resolve(out):reject(Error('Browser exit '+code+': '+err))})});
}
const qa=String.raw`
const check=(ok,why)=>{if(!ok)throw Error(why)};
const result={date:'2026-11-04',checks:[],unityEditorOperations:0,unityTestsRun:false};
try{
check(document.body.dataset.ready==='true','Initialization');
result.counts={systems:Mindmap.data.systems.length,features:Mindmap.data.systems.flatMap(s=>s.features).length,relations:Mindmap.data.relations.length,nodes:document.querySelectorAll('#mind .node').length};
check(result.counts.nodes===60,'Node count');result.checks.push('HTML initialization and all 60 system/feature nodes');
const overflow=Array.from(document.querySelectorAll('#mind .feature')).flatMap(g=>Array.from(g.querySelectorAll('text')).filter(t=>t.getComputedTextLength()>329).map(t=>({text:t.textContent,width:t.getComputedTextLength()})));
check(!overflow.length,'Text overflow: '+JSON.stringify(overflow));result.checks.push('Chinese SVG text bounds');
Mindmap.select('nav-keys',true);const details=document.querySelector('#details').innerText;
check(details.includes('InputSystem.onEvent')&&details.includes('PrototypeGame.cs:'),'Unity/source details');result.checks.push('Unity API, implementation and source evidence');
const search=document.querySelector('#search');search.value='JsonUtility';search.dispatchEvent(new Event('input'));check(document.querySelectorAll('#searchResults button').length>5,'Search');search.value='ZZZ_no_match';search.dispatchEvent(new Event('input'));check(document.querySelector('#searchResults').innerText.includes('没有匹配'),'Empty results');search.value='';search.dispatchEvent(new Event('input'));result.checks.push('API search and empty results');
Mindmap.setMode('relations');Mindmap.select('items');check(document.querySelectorAll('.edge.chosen').length>=3,'Relation focus');result.checks.push('Directed system relation focus');
const before=Mindmap.getView()[2];document.querySelector('#zoomIn').click();check(Mindmap.getView()[2]<before,'Zoom');document.querySelector('#fitBtn').click();result.checks.push('Zoom and fit');
document.querySelector('#boundsBtn').click();check(document.querySelector('#details').innerText.includes('R01/R03'),'Scope');result.checks.push('Scope and known limitations');
check(document.documentElement.scrollWidth<=innerWidth+1,'Horizontal layout overflow');result.checks.push('Responsive layout '+innerWidth+'px');
Mindmap.setMode('mind');Mindmap.select('nav-keys');Mindmap.fit();
check(Mindmap.data.unityGuides.length===20,'Guide coverage');
document.querySelector('#mind .tech-link[data-guide="input"]').dispatchEvent(new MouseEvent('click',{bubbles:true}));
check(document.querySelector('#unityDialog').open,'Map Unity link');
check(document.querySelector('#guideBody').innerText.includes('如何使用')&&document.querySelector('#guideBody').innerText.includes('在本项目中的体现'),'Guide sections');
check(document.querySelector('#guideBody pre').textContent.includes('Keyboard.current'),'Code sample');
document.querySelector('#guideBody .jump').click();check(!document.querySelector('#unityDialog').open,'Return to function');
document.querySelector('#unityBtn').click();check(document.querySelectorAll('#guideBody .tech-chip').length===20,'Guide index');
for(const g of Mindmap.data.unityGuides){Mindmap.showGuide(g.id);check(document.querySelector('#guideBody pre')&&document.querySelector('#guideBody .source a'),'Complete guide '+g.id)}
document.querySelector('#guideClose').click();check(!document.querySelector('#unityDialog').open,'Guide close');
document.querySelector('#details .tech-chip').click();check(document.querySelector('#unityDialog').open,'Sidebar Unity link');
document.querySelector('#guideClose').click();result.checks.push('20 Unity guides, map links, sidebar links, usage examples, source links, return to map and close');
result.passed=true;
}catch(error){result.passed=false;result.error=error.message}
document.body.setAttribute('data-qa-report',btoa(unescape(encodeURIComponent(JSON.stringify(result)))));
`;
const html=await readFile(join(dir,'现有功能思维导图.html'),'utf8');
const testfile=join(dir,'.mindmap-qa.html');
try{
 await writeFile(testfile,html.replace('</body>','<script>'+qa+'</script></body>'));
 const output=await browser(testfile,1600,1100,'交互版预览.png');
 const parse=out=>{const match=out.match(/data-qa-report="([^"]+)"/);if(!match)throw Error('Browser did not return QA report');return JSON.parse(Buffer.from(match[1],'base64').toString('utf8'))};
 const desktop=parse(output);if(!desktop.passed)throw Error(JSON.stringify(desktop));
 const mobile=parse(await browser(testfile,390,844));if(!mobile.passed)throw Error(JSON.stringify(mobile));
 await browser(join(dir,'现有功能思维导图.svg'),2500,2200,'现有功能思维导图.png');
 const png=await stat(join(dir,'现有功能思维导图.png'));if(png.size<50000)throw Error('PNG export looks empty');
 await writeFile(testfile,html.replace('</body>',`<script>Mindmap.setMode('relations');Mindmap.select('items');</script></body>`));
 await browser(testfile,1600,1100,'系统关系图预览.png');
 await writeFile(testfile,html.replace('</body>',`<script>Mindmap.showGuide('input');</script></body>`));
 await browser(testfile,1600,1100,'Unity功能介绍预览.png');
 const report={desktop,mobile,pngBytes:png.size};await writeFile(join(dir,'导图验证记录.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}finally{await unlink(testfile).catch(()=>{})}
