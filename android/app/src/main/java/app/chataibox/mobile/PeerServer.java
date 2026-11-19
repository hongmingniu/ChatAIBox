package app.chataibox.mobile;
import org.json.*;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** IPv6 transport shared by the Android service and JVM interoperability tests. */
public final class PeerServer implements AutoCloseable {
    public interface Repository { JSONArray merge(JSONArray incoming) throws Exception; }
    private final ServerSocket server;
    private final String secret;
    private final Repository repository;
    private final Runnable onSynced;
    private volatile boolean closed=false;
    private final ConcurrentHashMap<String,Long> seen=new ConcurrentHashMap<>();
    private final ExecutorService workers=new ThreadPoolExecutor(2,4,30,TimeUnit.SECONDS,new ArrayBlockingQueue<>(4),new ThreadPoolExecutor.AbortPolicy());
    public PeerServer(int port,String secret,Repository repository,Runnable onSynced)throws Exception{
        this.secret=secret;this.repository=repository;this.onSynced=onSynced;
        server=new ServerSocket();server.setReuseAddress(true);server.bind(new InetSocketAddress(InetAddress.getByName("::"),port),8);
    }
    public void listen()throws Exception{
        while(!closed){Socket client=server.accept();client.setSoTimeout(12000);try{workers.execute(()->handle(client));}catch(Exception e){client.close();}}
    }
    private static String line(InputStream in)throws Exception{
        ByteArrayOutputStream out=new ByteArrayOutputStream();int c;while((c=in.read())!=-1){if(c=='\n')break;if(c!='\r')out.write(c);if(out.size()>4096)throw new IOException("header too long");}if(c==-1&&out.size()==0)return null;return out.toString("US-ASCII");
    }
    private void handle(Socket socket){
        try(Socket client=socket){
            InputStream in=client.getInputStream();if(!"POST /sync HTTP/1.1".equals(line(in)))throw new IOException("invalid route");
            String header;int length=-1,headers=0;while((header=line(in))!=null&&!header.isEmpty()){
                if(++headers>40)throw new IOException("headers");String lower=header.toLowerCase(Locale.ROOT);
                if(lower.startsWith("content-length:")){if(length!=-1)throw new IOException("duplicate length");length=Integer.parseInt(header.substring(15).trim());}
                if(lower.startsWith("transfer-encoding:"))throw new IOException("chunked not supported");
            }
            if(length<=0||length>96*1024*1024)throw new IOException("body size");byte[] bytes=new byte[length];int read=0,n;while(read<length&&(n=in.read(bytes,read,length-read))>0)read+=n;if(read!=length)throw new IOException("truncated");
            JSONObject msg=SyncCrypto.open(new JSONObject(new String(bytes,StandardCharsets.UTF_8)),secret);
            long now=System.currentTimeMillis();String id=msg.getString("id");for(Map.Entry<String,Long> entry:seen.entrySet())if(now-entry.getValue()>300000)seen.remove(entry.getKey(),entry.getValue());
            if(!msg.optString("kind").equals("request")||id.length()>80||Math.abs(now-msg.getLong("at"))>300000||seen.putIfAbsent(id,now)!=null||seen.size()>10000)throw new IOException("expired or repeated request");
            JSONArray records=repository.merge(msg.getJSONArray("records"));
            JSONArray inventory=msg.optJSONArray("known");if(inventory!=null){
                if(inventory.length()>30000)throw new IOException("inventory too large");Map<String,String> known=new HashMap<>();for(int i=0;i<inventory.length();i++){JSONArray pair=inventory.getJSONArray(i);known.put(pair.getString(0),pair.getString(1));}
                JSONArray delta=new JSONArray();for(int i=0;i<records.length();i++){JSONObject row=records.getJSONObject(i);if(!row.getString("rev").equals(known.get(row.getString("id"))))delta.put(row);}records=delta;
            }
            JSONObject reply=SyncCrypto.seal(new JSONObject().put("kind","response").put("id",id).put("at",System.currentTimeMillis()).put("records",records),secret);
            byte[] body=reply.toString().getBytes(StandardCharsets.UTF_8);OutputStream out=client.getOutputStream();out.write(("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: "+body.length+"\r\n\r\n").getBytes(StandardCharsets.US_ASCII));out.write(body);out.flush();onSynced.run();
        }catch(Exception ignored){/* Do not log unauthenticated payloads or credentials. */}
    }
    @Override public void close(){closed=true;try{server.close();}catch(Exception ignored){}workers.shutdownNow();}
}
