package app.chataibox.mobile;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override public void onCreate(android.os.Bundle savedInstanceState){
        registerPlugin(ChatHostPlugin.class);super.onCreate(savedInstanceState);
        try{if(Store.get(this).settings().optBoolean("syncEnabled")){android.content.Intent service=new android.content.Intent(this,SyncService.class);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(service);else startService(service);}}catch(Exception ignored){}
    }
}
