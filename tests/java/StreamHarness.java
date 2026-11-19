import app.chataibox.mobile.ChatNetwork;
import java.io.*;
import java.nio.charset.StandardCharsets;
public class StreamHarness {
 public static void main(String[]args)throws Exception{
  String expected="data: {\"text\":\"中文🙂\"}\r\n\r\ndata: [DONE]\n\n";
  byte[] bytes=expected.getBytes(StandardCharsets.UTF_8);
  InputStream input=new ByteArrayInputStream(bytes){@Override public synchronized int read(byte[] buffer,int offset,int length){return super.read(buffer,offset,Math.min(length,1));}};
  StringBuilder actual=new StringBuilder();ChatNetwork.readStream(new InputStreamReader(input,StandardCharsets.UTF_8),text->actual.append(text));
  if(!expected.equals(actual.toString()))throw new AssertionError("UTF-8 SSE data changed");
  String longText=new String(new char[8191]).replace('\0','x')+"🙂尾部";StringBuilder joined=new StringBuilder();
  ChatNetwork.readStream(new StringReader(longText),text->{if(Character.isHighSurrogate(text.charAt(text.length()-1)))throw new AssertionError("surrogate split across bridge events");joined.append(text);});
  if(!longText.equals(joined.toString()))throw new AssertionError("surrogate pair changed");System.out.println("PASS: Android native incremental UTF-8 SSE reader");
 }
}
