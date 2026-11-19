package app.chataibox.mobile;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import android.util.Base64;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.util.*;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;

/** Phone is the durable owner. Settings never enter replication payloads. */
public final class Store {
    private static Store instance;
    private final Context context;
    private JSONObject state,settings;
    private long version=0;
    private final SecretKey key;
    public static synchronized Store get(Context context) throws Exception {
        if(instance==null)instance=new Store(context.getApplicationContext());
        return instance;
    }
    private Store(Context context) throws Exception {
        this.context=context;
        KeyStore keys=KeyStore.getInstance("AndroidKeyStore");keys.load(null);
        if(!keys.containsAlias("chataibox.local")){
            KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder("chataibox.local",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());generator.generateKey();
        }
        key=(SecretKey)keys.getKey("chataibox.local",null);
        state=read("records.enc");settings=read("settings.enc");
        if(state==null){state=new JSONObject().put("schema",1).put("deviceId",UUID.randomUUID().toString()).put("records",new JSONArray());persist("records.enc",state);}
        if(settings==null){byte[] secret=new byte[32];new SecureRandom().nextBytes(secret);StringBuilder hex=new StringBuilder();for(byte b:secret)hex.append(String.format(Locale.ROOT,"%02x",b));settings=new JSONObject().put("apiKey","").put("model","deepseek-flash").put("proxy","").put("syncEnabled",false).put("port",39281).put("syncKey",hex.toString());persist("settings.enc",settings);}
    }
    private JSONObject read(String name) throws Exception {
        AtomicFile file=new AtomicFile(new File(context.getFilesDir(),name));
        if(!file.getBaseFile().exists()&&!new File(file.getBaseFile()+".bak").exists())return null;
        byte[] bytes=file.readFully();if(bytes.length<28)throw new IOException("本机数据文件损坏");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key,new GCMParameterSpec(128,Arrays.copyOfRange(bytes,0,12)));
        return new JSONObject(new String(cipher.doFinal(Arrays.copyOfRange(bytes,12,bytes.length)),StandardCharsets.UTF_8));
    }
    private void persist(String name,JSONObject value) throws Exception {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key);
        byte[] encrypted=cipher.doFinal(value.toString().getBytes(StandardCharsets.UTF_8));
        AtomicFile file=new AtomicFile(new File(context.getFilesDir(),name));FileOutputStream stream=null;
        try{stream=file.startWrite();stream.write(cipher.getIV());stream.write(encrypted);file.finishWrite(stream);}catch(Exception e){if(stream!=null)file.failWrite(stream);throw e;}
    }
    public synchronized JSONObject state() throws Exception{return new JSONObject(state.toString());}
    public synchronized long version(){return version;}
    public synchronized JSONObject apply(JSONArray incoming,boolean returnState) throws Exception {long before=version;mergeCore(incoming);JSONObject result=new JSONObject().put("baseVersion",before).put("stateVersion",version);if(returnState)result.put("state",state());return result;}
    public synchronized JSONObject settings() throws Exception{return new JSONObject(settings.toString());}
    public synchronized void saveSettings(JSONObject next) throws Exception {
        if(!next.optString("syncKey").matches("[a-f0-9]{64}"))throw new IOException("配对密钥必须为 64 位十六进制字符");
        int port=next.optInt("port",0);if(port<1024||port>65535)throw new IOException("端口应在 1024–65535 之间");
        if(next.optString("model").isEmpty()||next.optString("model").length()>160)throw new IOException("模型无效");JSONArray providers=next.optJSONArray("providers");if(providers!=null){if(providers.length()>30)throw new IOException("提供方过多");Set<String> ids=new HashSet<>();for(int i=0;i<providers.length();i++){JSONObject p=providers.getJSONObject(i);Providers.validate(p);if(!ids.add(p.getString("id")))throw new IOException("提供方重复");}}
        if(!Arrays.asList("relay","direct").contains(next.optString("syncMode","relay")))throw new IOException("同步模式无效");next.put("relayUrl",RelayAgent.baseUrl(next.optString("relayUrl","https://relay.example.com/chataibox")));
        String accessKey=next.optString("relayAccessKey","");if(!accessKey.isEmpty()&&!accessKey.matches("[A-Za-z0-9_-]{32,256}"))throw new IOException("服务器访问密钥格式无效");
        String proxy=next.optString("proxy","");if(!proxy.isEmpty()&&!proxy.matches("http://(127\\.0\\.0\\.1|localhost):[0-9]{1,5}/?"))throw new IOException("代理格式应为 http://127.0.0.1:端口");
        persist("settings.enc",next);settings=new JSONObject(next.toString());
    }
    public synchronized JSONObject merge(JSONArray incoming) throws Exception {
        mergeCore(incoming);return state();
    }
    public synchronized JSONArray replicate(JSONArray incoming) throws Exception {mergeCore(incoming);return state.getJSONArray("records");}
    private void mergeCore(JSONArray incoming) throws Exception {
        if(incoming!=null&&incoming.length()==0)return;
        TreeMap<String,JSONObject> rows=new TreeMap<>();
        JSONArray original=state.getJSONArray("records");for(int i=0;i<original.length();i++){JSONObject row=original.getJSONObject(i);rows.put(row.getString("id"),row);}add(rows,incoming);
        boolean changed=rows.size()!=original.length();if(!changed)for(int i=0;i<original.length();i++){JSONObject row=original.getJSONObject(i);if(!row.getString("rev").equals(rows.get(row.getString("id")).getString("rev"))){changed=true;break;}}
        if(!changed)return;
        if(rows.size()>30000)throw new IOException("记录超过同步容量，请先导出备份");
        JSONArray merged=new JSONArray();for(JSONObject row:rows.values())merged.put(row);
        JSONObject next=new JSONObject().put("schema",1).put("deviceId",state.getString("deviceId")).put("records",merged);
        if(next.toString().getBytes(StandardCharsets.UTF_8).length>64*1024*1024)throw new IOException("聊天库超过 64 MiB，请导出备份后减少记录");
        persist("records.enc",next);state=next;version++;
    }
    private static String cut(String s,int max){return s.length()>max?s.substring(0,max):s;}
    private static void add(Map<String,JSONObject> rows,JSONArray input) throws Exception {
        if(input==null||input.length()>30000)throw new IOException("同步记录格式无效");
        for(int i=0;i<input.length();i++){
            JSONObject r=input.getJSONObject(i);String id=r.getString("id"),rev=r.getString("rev"),type=r.getString("type");
            if(id.length()>160||!rev.matches("[0-9]{16}:[a-f0-9-]{36}")||!Arrays.asList("chat","message").contains(type))throw new IOException("同步记录格式无效");
            JSONObject existing=rows.get(id);if(existing!=null&&rev.compareTo(existing.getString("rev"))<=0)continue;
            JSONObject clean=new JSONObject().put("id",id).put("rev",rev).put("type",type).put("deleted",r.optBoolean("deleted",false));
            if(type.equals("chat")){
                clean.put("title",cut(r.optString("title","新对话"),300)).put("created",r.optLong("created",0));
                JSONObject summary=cleanSummary(r.optJSONObject("contextSummary"));if(summary!=null)clean.put("contextSummary",summary);
            }
            else{
                String role=r.getString("role"),content=r.getString("content");if(!Arrays.asList("user","assistant").contains(role)||content.length()>2000000)throw new IOException("同步消息格式无效");
                JSONArray sources=new JSONArray(),raw=r.optJSONArray("sources");
                if(raw!=null)for(int j=0;j<Math.min(raw.length(),100);j++){JSONObject s=raw.optJSONObject(j);if(s!=null&&s.optString("url").matches("https?://.*"))sources.put(new JSONObject().put("title",cut(s.optString("title",s.optString("url")),1000)).put("url",cut(s.optString("url"),4000)));}
                clean.put("chatId",r.getString("chatId")).put("role",role).put("content",content).put("reasoning",cut(r.optString("reasoning",""),2000000)).put("created",r.optLong("created",0)).put("sources",sources);
                if(r.optBoolean("interrupted"))clean.put("interrupted",true);
            }
            for(String field:new String[]{"providerId","model"})if(r.has(field))clean.put(field,cut(r.optString(field),160));
            if(type.equals("message")){JSONArray images=new JSONArray(),rawImages=r.optJSONArray("images");if(rawImages!=null)for(int j=0;j<Math.min(4,rawImages.length());j++){JSONObject img=rawImages.optJSONObject(j);if(img==null)continue;String data=img.optString("dataUrl");if(data.length()<=1500000&&data.matches("(?s)^data:image/(png|jpeg|gif|webp);base64,[A-Za-z0-9+/=\\r\\n]+$"))images.put(new JSONObject().put("id",cut(img.optString("id"),160)).put("name",cut(img.optString("name"),256)).put("mime",cut(img.optString("mime"),80)).put("dataUrl",data));}clean.put("images",images);JSONArray traces=new JSONArray(),rawTraces=r.optJSONArray("searches");if(rawTraces!=null)for(int j=0;j<Math.min(100,rawTraces.length());j++){JSONObject t=rawTraces.optJSONObject(j);if(t==null)continue;JSONArray queries=new JSONArray(),rawQ=t.optJSONArray("queries");if(rawQ!=null)for(int k=0;k<Math.min(20,rawQ.length());k++)queries.put(cut(rawQ.optString(k),1024));traces.put(new JSONObject().put("id",cut(t.optString("id"),160)).put("kind",cut(t.optString("kind"),40)).put("status",cut(t.optString("status"),40)).put("url",cut(t.optString("url"),4000)).put("queries",queries));}clean.put("searches",traces);}
            JSONObject old=rows.get(id);
            if(old==null||rev.compareTo(old.getString("rev"))>0)rows.put(id,clean);
        }
    }
    private static JSONObject cleanSummary(JSONObject s)throws Exception{
        if(s==null||!(s.opt("text") instanceof String)||s.optString("text").length()>1000000||s.optInt("covered",-1)<0||s.optInt("covered")>30000||!s.optString("prefixHash").matches("[a-f0-9]{1,8}"))return null;
        JSONObject partial=s.optJSONObject("partial");if(s.has("partial")&&(partial==null||partial.optString("id").length()>160||!partial.optString("rev").matches("[0-9]{16}:[a-f0-9-]{36}")||partial.optInt("offset")<=0||partial.optInt("offset")>2000000))return null;
        if(s.optInt("covered")==0&&partial==null)return null;
        JSONObject out=new JSONObject().put("text",s.getString("text")).put("covered",s.getInt("covered")).put("prefixHash",s.getString("prefixHash"));
        if(partial!=null)out.put("partial",new JSONObject().put("id",partial.getString("id")).put("rev",partial.getString("rev")).put("offset",partial.getInt("offset")));return out;
    }
}
