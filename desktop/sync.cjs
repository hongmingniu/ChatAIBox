const http=require('node:http');
const {randomBytes,createCipheriv,createDecipheriv}=require('node:crypto');
const {isIP}=require('node:net');
const MAX=96*1024*1024;
function seal(value,key) { const iv=randomBytes(12),c=createCipheriv('aes-256-gcm',Buffer.from(key,'hex'),iv);return {v:1,nonce:iv.toString('base64'),ciphertext:Buffer.concat([c.update(JSON.stringify(value),'utf8'),c.final(),c.getAuthTag()]).toString('base64')}; }
function open(value,key) { if(value.v!==1) throw Error('协议版本不匹配'); const iv=Buffer.from(value.nonce,'base64'),data=Buffer.from(value.ciphertext,'base64'); if(iv.length!==12||data.length<16||data.length>MAX)throw Error('加密信封无效'); const d=createDecipheriv('aes-256-gcm',Buffer.from(key,'hex'),iv);d.setAuthTag(data.subarray(-16));return JSON.parse(Buffer.concat([d.update(data.subarray(0,-16)),d.final()]).toString('utf8')); }
function peerUrl(address,port) { if(isIP(address)!==6||address.includes('%')) throw Error('请输入对端的完整 IPv6 地址（不带方括号）');if(!Number.isInteger(port)||port<1024||port>65535)throw Error('端口必须在 1024–65535 之间');return `http://[${address}]:${port}/sync`; }
function startSync({port,key,read,merge,onStatus}) {
  if(!/^[a-f0-9]{64}$/.test(key))throw Error('配对密钥无效');
  peerUrl('::1',port);
  const seen=new Map();let active=0;
  const server=http.createServer(async(req,res)=>{
    if(req.method!=='POST'||req.url!=='/sync'){res.writeHead(404).end();return;}
    if(active>=4){res.writeHead(429).end();return;} active++;
    try {
      const chunks=[];let size=0;
      for await(const part of req){size+=part.length;if(size>MAX)throw Error('数据超过 96 MiB');chunks.push(part);}
      const msg=open(JSON.parse(Buffer.concat(chunks).toString('utf8')),key);
      const now=Date.now();for(const [id,at]of seen)if(now-at>300000)seen.delete(id);
      if(msg.kind!=='request'||typeof msg.id!=='string'||msg.id.length>80||Math.abs(now-msg.at)>300000||seen.has(msg.id))throw Error('过期或重复的请求');
      seen.set(msg.id,now);if(seen.size>10000)throw Error('请求过于频繁');
      await merge(msg.records);
      const known=Array.isArray(msg.known)?new Map(msg.known):null;
      const reply=JSON.stringify(seal({kind:'response',id:msg.id,at:Date.now(),records:known?read().filter(r=>known.get(r.id)!==r.rev):read()},key));
      res.writeHead(200,{'Content-Type':'application/json','Cache-Control':'no-store'}).end(reply);onStatus({lastSync:Date.now(),error:''});
    }catch{res.writeHead(400).end('Invalid sync request');}finally{active--;}
  });
  server.requestTimeout=15000;server.headersTimeout=10000;server.maxConnections=12;
  server.on('error',e=>onStatus({error:`同步监听失败：${e.code||e.message}`}));
  server.listen({port,host:'::',ipv6Only:true},()=>onStatus({listening:true,error:''}));return server;
}
async function syncOnce({address,port,key,records,known}) {
  const url=peerUrl(address,port);if(!/^[a-f0-9]{64}$/.test(key))throw Error('配对密钥无效');
  const id=require('node:crypto').randomUUID();const payload=JSON.stringify(seal({kind:'request',id,at:Date.now(),records,...(known?{known}:{})},key));
  const raw=await new Promise((resolve,reject)=>{
    const req=http.request(url,{method:'POST',agent:false,headers:{'Content-Type':'application/json','Content-Length':Buffer.byteLength(payload)},timeout:12000},res=>{
      const parts=[];let size=0;res.on('data',p=>{size+=p.length;if(size>MAX){res.destroy();reject(Error('同步数据过大'));}else parts.push(p);});
      res.on('end',()=>res.statusCode===200?resolve(Buffer.concat(parts).toString('utf8')):reject(Error(`手机拒绝同步（${res.statusCode}），请检查密钥与系统时间`)));res.on('error',reject);
    });req.on('timeout',()=>req.destroy(Error('连接手机超时，请检查 IPv6 地址和防火墙')));req.on('error',reject);req.end(payload);
  });
  const msg=open(JSON.parse(raw),key);if(msg.kind!=='response'||msg.id!==id||Math.abs(Date.now()-msg.at)>300000)throw Error('同步响应验证失败');return msg.records;
}
module.exports={seal,open,peerUrl,startSync,syncOnce};
