package app.chataibox.mobile;
import org.json.*;import java.util.*;
public class RelayHarness{
 public static void main(String[] args)throws Exception{JSONObject settings=new JSONObject().put("syncKey",args[0]).put("relayUrl",args[1]).put("proxy",args[2]).put("relayAccessKey",System.getenv("RELAY_ACCESS_KEY")==null?"":System.getenv("RELAY_ACCESS_KEY"));Map<String,JSONObject> rows=new TreeMap<>();RelayAgent agent=new RelayAgent(settings,incoming->{synchronized(rows){for(int i=0;i<incoming.length();i++){JSONObject row=incoming.getJSONObject(i),old=rows.get(row.getString("id"));if(old==null||row.getString("rev").compareTo(old.getString("rev"))>0)rows.put(row.getString("id"),row);}JSONArray result=new JSONArray();for(JSONObject row:rows.values())result.put(row);return result;}},(connected,error)->{if(connected)System.out.println("READY");},()->System.out.println("SYNCED"));Runtime.getRuntime().addShutdownHook(new Thread(agent::close));agent.start();Thread.sleep(300000);agent.close();}
}
