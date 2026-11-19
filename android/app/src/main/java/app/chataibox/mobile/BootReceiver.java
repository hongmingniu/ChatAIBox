package app.chataibox.mobile;
import android.content.*;
import android.os.Build;
public class BootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent intent){
        if(!Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction()))return;
        try{if(Store.get(context).settings().optBoolean("syncEnabled")){Intent service=new Intent(context,SyncService.class);if(Build.VERSION.SDK_INT>=26)context.startForegroundService(service);else context.startService(service);}}catch(Exception ignored){SyncService.error="开机启动受系统限制，请打开应用启动同步";}
    }
}
