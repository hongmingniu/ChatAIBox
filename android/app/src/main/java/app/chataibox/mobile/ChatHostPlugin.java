package app.chataibox.mobile;

import com.getcapacitor.*;
import com.getcapacitor.annotation.CapacitorPlugin;
import android.content.*;
import android.os.Build;
import android.provider.MediaStore;
import org.json.*;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

@CapacitorPlugin(name="ChatHost",permissions={@com.getcapacitor.annotation.Permission(alias="microphone",strings={android.Manifest.permission.RECORD_AUDIO})})
public class ChatHostPlugin extends Plugin {
    private final ExecutorService io=Executors.newFixedThreadPool(8);
    private final ChatNetwork network=new ChatNetwork();
    private final MediaNetwork media=new MediaNetwork();
    @PluginMethod public void execute(PluginCall call){
        if("cancelRequest".equals(call.getString("method",""))){network.cancel(call.getObject("args",new JSObject()).optString("requestId"));media.cancel(call.getObject("args",new JSObject()).optString("requestId"));call.resolve();return;}
        String requested=call.getString("method","");
        if(requested.equals("microphone")){if(getPermissionState("microphone")==PermissionState.GRANTED)call.resolve();else requestPermissionForAlias("microphone",call,"microphoneResult");return;}
        if(requested.startsWith("clipboard")){getActivity().runOnUiThread(()->{try{ClipboardManager manager=(ClipboardManager)getContext().getSystemService(Context.CLIPBOARD_SERVICE);JSObject args=call.getObject("args",new JSObject()),result=new JSObject();if(requested.equals("clipboardWrite"))manager.setPrimaryClip(ClipData.newPlainText("ChatAIBox",args.optString("text")));else{ClipData data=manager.getPrimaryClip();result.put("text",data!=null&&data.getItemCount()>0?String.valueOf(data.getItemAt(0).coerceToText(getContext())):"");}call.resolve(result);}catch(Exception e){call.reject("无法访问剪贴板，请在应用前台重试");}});return;}
        if(requested.equals("download")&&Build.VERSION.SDK_INT<29){getActivity().runOnUiThread(()->{Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("text/plain").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,call.getObject("args",new JSObject()).optString("filename","code.txt"));startActivityForResult(call,intent,"downloadResult");});return;}
        io.execute(()->{try{
            String method=call.getString("method","");JSObject args=call.getObject("args",new JSObject());Store store=Store.get(getContext());JSONObject result=new JSONObject();
            switch(method){
                case "read":
                    JSONArray addresses=new JSONArray();Enumeration<NetworkInterface> interfaces=NetworkInterface.getNetworkInterfaces();
                    while(interfaces!=null&&interfaces.hasMoreElements()){NetworkInterface net=interfaces.nextElement();if(!net.isUp()||net.isLoopback())continue;Enumeration<InetAddress> ips=net.getInetAddresses();while(ips.hasMoreElements()){InetAddress ip=ips.nextElement();if(ip instanceof Inet6Address&&!ip.isLoopbackAddress()&&!ip.isLinkLocalAddress())addresses.put(ip.getHostAddress().split("%")[0]);}}
                    result.put("stateVersion",store.version());if(args.optLong("knownVersion",-1)!=store.version())result.put("state",store.state());
                    result.put("settings",store.settings()).put("platform","android").put("status",new JSONObject().put("listening",SyncService.listening).put("relayConnected",store.settings().optString("syncMode","relay").equals("relay")&&SyncService.listening).put("lastSync",SyncService.lastSync).put("error",SyncService.error).put("addresses",addresses));break;
                case "write":result=store.apply(args.getJSONArray("records"),args.optBoolean("returnState",true));break;
                case "settings":
                    store.saveSettings(args);getContext().stopService(new Intent(getContext(),SyncService.class));
                    if(args.optBoolean("syncEnabled"))getActivity().runOnUiThread(()->{try{if(Build.VERSION.SDK_INT>=33&&getActivity().checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)getActivity().requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},8);Intent service=new Intent(getContext(),SyncService.class);if(Build.VERSION.SDK_INT>=26)getContext().startForegroundService(service);else getContext().startService(service);}catch(Exception e){SyncService.error="后台服务无法启动，请重新打开应用后启用同步";}});
                    result.put("settings",store.settings());break;
                case "request":result=network.request(args,store.settings(),(id,text)->{JSObject chunk=new JSObject();chunk.put("requestId",id);chunk.put("chunk",text);notifyListeners("streamChunk",chunk);});break;
                case "edgeTts":result=media.edge(args,store.settings());break;
                case "asr":case "fetchImage":result=media.request(method,args,store.settings());break;
                case "download":
                    ContentValues download=new ContentValues();download.put(MediaStore.Downloads.DISPLAY_NAME,new File(args.optString("filename","code.txt")).getName());download.put(MediaStore.Downloads.MIME_TYPE,"text/plain");download.put(MediaStore.Downloads.IS_PENDING,1);android.net.Uri target=getContext().getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,download);if(target==null)throw new IOException("无法创建下载文件");try(OutputStream out=getContext().getContentResolver().openOutputStream(target)){out.write(args.optString("text").getBytes(StandardCharsets.UTF_8));}download.clear();download.put(MediaStore.Downloads.IS_PENDING,0);getContext().getContentResolver().update(target,download,null,null);result.put("saved",true);break;
                case "openLink":
                    URI link=new URI(args.getString("url"));if(!Arrays.asList("https","http").contains(link.getScheme()))throw new IOException("链接协议无效");
                    Intent browser=new Intent(Intent.ACTION_VIEW,android.net.Uri.parse(link.toString()));browser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);getContext().startActivity(browser);break;
                case "export":
                    if(Build.VERSION.SDK_INT<29)throw new IOException("系统低于 Android 10，无法保存下载备份");
                    ContentValues values=new ContentValues();values.put(MediaStore.Downloads.DISPLAY_NAME,"ChatAIBox-"+System.currentTimeMillis()+".json");values.put(MediaStore.Downloads.MIME_TYPE,"application/json");values.put(MediaStore.Downloads.IS_PENDING,1);
                    android.net.Uri uri=getContext().getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);if(uri==null)throw new IOException("无法创建备份");
                    try(OutputStream out=getContext().getContentResolver().openOutputStream(uri)){if(out==null)throw new IOException("无法写入备份");out.write(new JSONObject().put("schema",1).put("records",store.state().getJSONArray("records")).toString(2).getBytes(StandardCharsets.UTF_8));}
                    values.clear();values.put(MediaStore.Downloads.IS_PENDING,0);getContext().getContentResolver().update(uri,values,null,null);result.put("saved",true);break;
                default:throw new IOException("未知操作");
            }
            call.resolve(JSObject.fromJSONObject(result));
        }catch(Exception e){call.reject(e.getMessage()==null?"操作失败":e.getMessage());}});
    }
    @com.getcapacitor.annotation.PermissionCallback private void microphoneResult(PluginCall call){if(getPermissionState("microphone")==PermissionState.GRANTED)call.resolve();else call.reject("麦克风权限未授予，可使用音频文件输入");}
    @com.getcapacitor.annotation.ActivityCallback private void downloadResult(PluginCall call,androidx.activity.result.ActivityResult result){if(call==null)return;if(result.getResultCode()!=android.app.Activity.RESULT_OK||result.getData()==null){call.resolve(new JSObject().put("saved",false));return;}io.execute(()->{try(OutputStream out=getContext().getContentResolver().openOutputStream(result.getData().getData())){out.write(call.getObject("args",new JSObject()).optString("text").getBytes(StandardCharsets.UTF_8));call.resolve(new JSObject().put("saved",true));}catch(Exception e){call.reject("保存失败");}});}
}
