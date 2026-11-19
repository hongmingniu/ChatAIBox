const fs=require('node:fs');const {randomBytes,createHash}=require('node:crypto');
const filename=process.argv[2];
if(!filename){console.error('Usage: node generate-key.cjs /etc/chataibox-relay.env');process.exit(1);}
const key=randomBytes(32).toString('hex');
fs.writeFileSync(filename,'RELAY_ACCESS_KEY_HASH='+createHash('sha256').update(key).digest('hex')+'\n',{flag:'wx',mode:0o600});
console.log('Server configuration created. Save this access key privately; enter it on both clients:');
console.log(key);
console.log('The server stores only its SHA-256 hash. This is separate from the chat pairing key.');
