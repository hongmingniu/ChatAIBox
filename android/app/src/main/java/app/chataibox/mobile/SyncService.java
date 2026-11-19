package app.chataibox.mobile;
import android.app.*;
import android.content.*;
import android.os.*;
import android.content.pm.ServiceInfo;
import org.json.*;

public class SyncService extends Service {
    public static volatile boolean listening=false;
    public static volatile long lastSync=0;
    public static volatile String error="";
    private volatile PeerServer server;
    private volatile RelayAgent relay;
    private volatile boolean stopping=false,starting=false;
    private PowerManager.WakeLock wakeLock;
    @Override public IBinder onBind(Intent intent){return null;}
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        try{
            JSONObject settings=Store.get(this).settings();if(!settings.optBoolean("syncEnabled")){stopSelf();return START_NOT_STICKY;}
            NotificationManager manager=getSystemService(NotificationManager.class);
            if(Build.VERSION.SDK_INT>=26)manager.createNotificationChannel(new NotificationChannel("device-sync","设备消息同步",NotificationManager.IMPORTANCE_LOW));
            PendingIntent pi=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            Notification.Builder builder=Build.VERSION.SDK_INT>=26?new Notification.Builder(this,"device-sync"):new Notification.Builder(this);
            Notification notification=builder.setContentTitle("ChatAIBox 手机主机运行中").setContentText(settings.optString("syncMode","relay").equals("relay")?"加密服务器转发 · 点击打开":"IPv6 直连 · 点击打开").setSmallIcon(android.R.drawable.stat_notify_sync).setOngoing(true).setContentIntent(pi).build();
            if(Build.VERSION.SDK_INT>=34)startForeground(20,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_REMOTE_MESSAGING);
            else if(Build.VERSION.SDK_INT>=29)startForeground(20,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);else startForeground(20,notification);
            if(server!=null||relay!=null||starting)return START_STICKY;starting=true;stopping=false;
            PowerManager power=(PowerManager)getSystemService(POWER_SERVICE);wakeLock=power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"ChatAIBox:SyncHost");wakeLock.acquire();
            if(settings.optString("syncMode","relay").equals("relay")){relay=new RelayAgent(settings,rows->Store.get(this).replicate(rows),(connected,message)->{listening=connected;error=message;},()->{lastSync=System.currentTimeMillis();error="";});relay.start();return START_STICKY;}
            new Thread(()->{
                try{
                    PeerServer peer=new PeerServer(settings.getInt("port"),settings.getString("syncKey"),rows->Store.get(this).replicate(rows),()->{lastSync=System.currentTimeMillis();error="";});
                    server=peer;if(stopping){peer.close();return;}listening=true;error="";peer.listen();
                }catch(Exception e){if(!stopping)error="IPv6 监听失败，请检查端口是否占用";}
                finally{listening=false;if(!stopping)stopSelf();}
            },"ChatAIBox-IPv6").start();return START_STICKY;
        }catch(Exception e){error="后台服务启动失败，请重新开启同步";stopSelf();return START_NOT_STICKY;}
    }
    @Override public void onDestroy(){stopping=true;listening=false;if(server!=null)server.close();server=null;if(relay!=null)relay.close();relay=null;if(wakeLock!=null&&wakeLock.isHeld())wakeLock.release();super.onDestroy();}
}
