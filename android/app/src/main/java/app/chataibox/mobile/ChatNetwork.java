package app.chataibox.mobile;
import org.json.*;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.Collections;
import java.util.UUID;
import java.util.concurrent.*;

/** Native HTTP/SSE transport; no conversation data is written to disk here. */
public final class ChatNetwork {
    public interface Sink {void chunk(String requestId,String text);}
    private static class Job {volatile boolean cancelled;volatile HttpURLConnection connection;}
    private final ConcurrentHashMap<String,Job> active=new ConcurrentHashMap<>();
    private final Set<String> preCancelled=Collections.newSetFromMap(new ConcurrentHashMap<String,Boolean>());
    public void cancel(String id){Job job=active.get(id);if(job!=null){job.cancelled=true;if(job.connection!=null)job.connection.disconnect();}else{if(preCancelled.size()>1000)preCancelled.clear();preCancelled.add(id);}}
    public JSONObject request(JSONObject args,JSONObject settings,Sink sink)throws Exception{
        String id=args.optString("requestId",UUID.randomUUID().toString());Job job=new Job();job.cancelled=preCancelled.remove(id);
        if(active.putIfAbsent(id,job)!=null)throw new IOException("请求 ID 重复");
        try{
            if(job.cancelled)return new JSONObject().put("cancelled",true);
            String kind=args.getString("kind"),endpoint;JSONObject provider=kind.equals("chat")?Providers.resolve(settings,args.optString("providerId")):null;
            if(kind.equals("chat"))endpoint=provider.getString("baseUrl")+"/chat/completions";
            else if(kind.equals("share")&&args.optString("shareId").matches("[a-zA-Z0-9_-]{5,128}"))endpoint="https://chat.deepseek.com/api/v0/share/content?share_id="+args.getString("shareId");
            else throw new IOException("无效请求");
            String address=settings.optString("proxy","");Proxy proxy=Proxy.NO_PROXY;
            if(!address.isEmpty()){URI uri=new URI(address);proxy=new Proxy(Proxy.Type.HTTP,new InetSocketAddress(uri.getHost(),uri.getPort()));}
            HttpURLConnection connection=(HttpURLConnection)new URL(endpoint).openConnection(proxy);job.connection=connection;
            if(job.cancelled)return new JSONObject().put("cancelled",true);
            connection.setConnectTimeout(15000);connection.setReadTimeout(30*60*1000);connection.setInstanceFollowRedirects(false);connection.setRequestProperty("Accept",args.optBoolean("stream")?"text/event-stream":"application/json");
            if(kind.equals("chat")){connection.setRequestMethod("POST");connection.setRequestProperty("Authorization","Bearer "+provider.optString("apiKey"));connection.setRequestProperty("Content-Type","application/json");connection.setDoOutput(true);JSONObject body=Providers.body(args.getJSONObject("body"),provider);body.put("stream",args.optBoolean("stream",false));try(OutputStream out=connection.getOutputStream()){out.write(body.toString().getBytes(StandardCharsets.UTF_8));}}
            int code=connection.getResponseCode();if(code!=200){
                if(code==400||code==413){try(InputStream error=connection.getErrorStream()){if(error!=null){ByteArrayOutputStream detail=new ByteArrayOutputStream();byte[] part=new byte[2048];int n;while(detail.size()<8192&&(n=error.read(part))!=-1)detail.write(part,0,n);String message=detail.toString("UTF-8");if(java.util.regex.Pattern.compile("context_length|context.{0,80}(length|window|limit)|maximum context",java.util.regex.Pattern.CASE_INSENSITIVE|java.util.regex.Pattern.DOTALL).matcher(message).find())throw new ContextLimitException();}}}
                throw new IOException(code==401?"API Key 无效":code==402?"DeepSeek 余额不足":code==429?"请求过于频繁，请稍后再试":"请求失败（HTTP "+code+"）");
            }
            if(args.optBoolean("stream")){
                if(connection.getContentType()==null||!connection.getContentType().contains("text/event-stream"))throw new IOException("服务器没有返回 SSE 流式数据");
                try(Reader reader=new InputStreamReader(connection.getInputStream(),StandardCharsets.UTF_8)){readStream(reader,text->{if(!job.cancelled)sink.chunk(id,text);});}
                return new JSONObject().put("streamed",true).put("cancelled",job.cancelled);
            }
            ByteArrayOutputStream out=new ByteArrayOutputStream();try(InputStream in=connection.getInputStream()){byte[] buffer=new byte[8192];int count;while((count=in.read(buffer))!=-1){out.write(buffer,0,count);if(out.size()>16000000)throw new IOException("返回内容过大");}}return new JSONObject().put("data",out.toString("UTF-8"));
        }catch(Exception e){if(job.cancelled)return new JSONObject().put("cancelled",true);if(e instanceof SocketTimeoutException)throw new IOException("请求超时，请稍后重试");String message=e.getMessage();if(message!=null&&message.matches("^(请求|API Key|DeepSeek|服务器|返回|无效|CONTEXT_LIMIT:).*"))throw e;throw new IOException("网络请求失败，请检查网络或本机代理");}
        finally{active.remove(id);preCancelled.remove(id);if(job.connection!=null)job.connection.disconnect();}
    }
    public interface TextSink {void accept(String text);}
    private static class ContextLimitException extends IOException {ContextLimitException(){super("CONTEXT_LIMIT: 模型上下文已达到上限");}}
    public static void readStream(Reader reader,TextSink sink)throws Exception{
        char[] buffer=new char[4096];StringBuilder pending=new StringBuilder();long last=System.currentTimeMillis();int size=0,count;
        while((count=reader.read(buffer))!=-1){size+=count;if(size>16000000)throw new IOException("返回内容过大");pending.append(buffer,0,count);if(System.currentTimeMillis()-last>=40||pending.length()>=8192){int end=pending.length();if(end>0&&Character.isHighSurrogate(pending.charAt(end-1)))end--;if(end>0){sink.accept(pending.substring(0,end));pending.delete(0,end);}last=System.currentTimeMillis();}}
        if(pending.length()>0)sink.accept(pending.toString());
    }
}
