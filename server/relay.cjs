const http=require('node:http');const {randomUUID,createHash,timingSafeEqual}=require('node:crypto');const {WebSocketServer,WebSocket}=require('ws');
/** Volatile routing only: no chat database, offline queue, payload logs or disk writes. */
function createRelay({maxRooms=2000,maxPeers=8,accessKeyHash,allowUnauthenticated=false}={}){
 if(!allowUnauthenticated&&!/^[a-f0-9]{64}$/.test(accessKeyHash||''))throw Error('RELAY_ACCESS_KEY_HASH must be a SHA-256 hash; refusing to start without authentication');
 const authorized=req=>{if(allowUnauthenticated)return true;const header=req.headers.authorization;if(typeof header!=='string'||!header.startsWith('Bearer ')||header.length>520)return false;const key=header.slice(7);if(!/^[A-Za-z0-9_-]{32,256}$/.test(key))return false;return timingSafeEqual(createHash('sha256').update(key).digest(),Buffer.from(accessKeyHash,'hex'));};
 const rooms=new Map(),pendingUpgrade=new Map();let connections=0;
 const server=http.createServer((req,res)=>{res.setHeader('Cache-Control','no-store');if(req.url==='/health'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({ok:true,mode:'encrypted-memory-relay',offlineStorage:false}));}else{res.writeHead(404).end();}});
 const wss=new WebSocketServer({noServer:true,maxPayload:96*1024,perMessageDeflate:false});
 server.on('upgrade',(req,socket,head)=>{if(req.url!=='/relay'||connections>=64){socket.destroy();return;}if(!authorized(req)){socket.end('HTTP/1.1 401 Unauthorized\r\nConnection: close\r\nContent-Length: 0\r\nCache-Control: no-store\r\n\r\n');return;}const ip=socket.remoteAddress||'',now=Date.now(),entry=pendingUpgrade.get(ip)||{at:now,count:0};if(now-entry.at>60000){entry.at=now;entry.count=0;}if(++entry.count>60){socket.destroy();return;}pendingUpgrade.set(ip,entry);wss.handleUpgrade(req,socket,head,ws=>wss.emit('connection',ws));});
 const send=(ws,value)=>{if(ws.readyState!==WebSocket.OPEN)return false;if(ws.bufferedAmount>2*1024*1024){ws.close(1013,'slow peer');return false;}ws.send(JSON.stringify(value));return true;};
 wss.on('connection',ws=>{connections++;ws.alive=true;ws.id=randomUUID();const helloTimer=setTimeout(()=>ws.close(1008,'join required'),10000);let room,role,lastAt=0,frames=0;
  ws.on('pong',()=>ws.alive=true);
  ws.on('message',raw=>{try{const msg=JSON.parse(raw.toString());if(!room){if(msg.type!=='join'||!['phone','desktop'].includes(msg.role)||!/^[a-f0-9]{64}$/.test(msg.room)||Object.keys(msg).length!==3)throw Error();role=msg.role;room=rooms.get(msg.room);if(!room){if(rooms.size>=maxRooms)throw Error();room={id:msg.room,phone:null,desktops:new Map()};rooms.set(msg.room,room);}if(role==='phone'){const old=room.phone;room.phone=ws;old?.close(4001,'reconnected');}else{if(room.desktops.size>=maxPeers)throw Error();room.desktops.set(ws.id,ws);}clearTimeout(helloTimer);send(ws,{type:'joined',id:ws.id,phoneOnline:!!room.phone});if(role==='phone')for(const peer of room.desktops.values())send(peer,{type:'presence',phoneOnline:true});return;}
   const now=Date.now();if(now-lastAt>1000){lastAt=now;frames=0;}if(++frames>500)throw Error();
   if(msg.type!=='data'||!/^[a-f0-9-]{36}$/.test(msg.id)||typeof msg.data!=='string'||msg.data.length>48000||typeof msg.end!=='boolean'||Object.keys(msg).some(k=>!['type','id','data','end','to'].includes(k)))throw Error();
   if(role==='desktop'){if(!room.phone||!send(room.phone,{type:'data',from:ws.id,id:msg.id,data:msg.data,end:msg.end}))send(ws,{type:'unavailable',id:msg.id});}
   else{if(room.phone!==ws)throw Error();const peer=room.desktops.get(msg.to);if(peer)send(peer,{type:'data',id:msg.id,data:msg.data,end:msg.end});}
  }catch{ws.close(1008,'invalid frame');}});
  ws.on('error',()=>{});ws.on('close',()=>{connections--;clearTimeout(helloTimer);if(!room)return;if(role==='phone'&&room.phone===ws){room.phone=null;for(const peer of room.desktops.values())send(peer,{type:'presence',phoneOnline:false});}else if(role==='desktop')room.desktops.delete(ws.id);if(!room.phone&&!room.desktops.size)rooms.delete(room.id);});
 });
 const heartbeat=setInterval(()=>{for(const ws of wss.clients){if(!ws.alive){ws.terminate();continue;}ws.alive=false;ws.ping();}const now=Date.now();for(const [ip,v]of pendingUpgrade)if(now-v.at>60000)pendingUpgrade.delete(ip);},20000);heartbeat.unref();
 return {server,rooms,close:()=>new Promise(resolve=>{clearInterval(heartbeat);for(const ws of wss.clients)ws.terminate();wss.close(()=>server.close(resolve));})};
}
if(require.main===module){const relay=createRelay({accessKeyHash:process.env.RELAY_ACCESS_KEY_HASH});relay.server.listen(Number(process.env.PORT||39181),'127.0.0.1');for(const signal of ['SIGTERM','SIGINT'])process.on(signal,()=>relay.close().then(()=>process.exit(0)));}
module.exports={createRelay};
