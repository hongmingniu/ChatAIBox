import app.chataibox.mobile.PeerServer;
import org.json.*;
import java.util.*;
public class PeerHarness {
    public static void main(String[] args)throws Exception{
        TreeMap<String,JSONObject> rows=new TreeMap<>();
        PeerServer server=new PeerServer(Integer.parseInt(args[0]),args[1],incoming->{synchronized(rows){for(int i=0;i<incoming.length();i++){JSONObject row=incoming.getJSONObject(i),old=rows.get(row.getString("id"));if(old==null||row.getString("rev").compareTo(old.getString("rev"))>0)rows.put(row.getString("id"),row);}JSONArray out=new JSONArray();for(JSONObject row:rows.values())out.put(row);return out;}},()->{});
        System.out.println("READY");System.out.flush();server.listen();
    }
}
