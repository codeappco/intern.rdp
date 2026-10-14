const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(require('node:path').join(__dirname,'../stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/WebRtc.html'),'utf8');
const handler=html.slice(html.indexOf('share.onclick='),html.indexOf('\nasync function signal'));
(async()=>{
 const ctx={ready:true,stopped:false,share:{disabled:false,hidden:false},config:{fps:30},statusText:{},navigator:{mediaDevices:{}},published:0,failures:0};
 ctx.publishStream=async()=>ctx.published++;ctx.fail=()=>ctx.failures++;
 vm.createContext(ctx);vm.runInContext(handler,ctx);
 let calls=0;ctx.navigator.mediaDevices.getDisplayMedia=async()=>{calls++;throw {name:'NotAllowedError'};};
 await ctx.share.onclick();await ctx.share.onclick();
 assert.equal(calls,2);assert.equal(ctx.share.disabled,false);assert.equal(ctx.failures,0);assert.equal(ctx.stopped,false);
 let stoppedTracks=0;const track={stop(){stoppedTracks++;},getSettings(){return {displaySurface:'window'};}};
 ctx.navigator.mediaDevices.getDisplayMedia=async()=>({getTracks:()=>[track],getVideoTracks:()=>[track]});
 await ctx.share.onclick();assert.equal(stoppedTracks,1);assert.equal(ctx.published,0);assert.equal(ctx.share.disabled,false);
 track.getSettings=()=>({displaySurface:'monitor'});await ctx.share.onclick();assert.equal(ctx.published,1);
 console.log('PASS cancel/retry, rejected window releases capture, subsequent monitor selection publishes');
 ctx.share.disabled=false;ctx.navigator.mediaDevices.getDisplayMedia=async()=>{ctx.stopped=true;return {getTracks:()=>[track]};};
 await ctx.share.onclick();assert.equal(stoppedTracks,2);assert.equal(ctx.published,1);
 console.log('PASS closing session during picker releases late capture without publishing');
})().catch(e=>{console.error(e);process.exitCode=1;});
