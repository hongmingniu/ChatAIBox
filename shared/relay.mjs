export const DEFAULT_RELAY_URL='https://relay.example.com/chataibox';
export function relayUrl(value=DEFAULT_RELAY_URL){const url=new URL(value);if(url.protocol!=='https:'||url.username||url.password||url.search||url.hash)throw Error('转发服务地址必须是 HTTPS，不含账号、参数或片段');return url.href.replace(/\/$/,'');}
