package app.chataibox.mobile;
import org.json.JSONObject;
import android.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.spec.SecretKeySpec;
import javax.crypto.spec.GCMParameterSpec;
import java.security.SecureRandom;
import java.nio.charset.StandardCharsets;

public final class SyncCrypto {
    private static byte[] key(String hex){if(!hex.matches("[a-f0-9]{64}"))throw new IllegalArgumentException("密钥格式错误");byte[] out=new byte[32];for(int i=0;i<32;i++)out[i]=(byte)Integer.parseInt(hex.substring(i*2,i*2+2),16);return out;}
    public static JSONObject seal(JSONObject value,String secret)throws Exception{
        byte[] iv=new byte[12];new SecureRandom().nextBytes(iv);Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,new SecretKeySpec(key(secret),"AES"),new GCMParameterSpec(128,iv));
        return new JSONObject().put("v",1).put("nonce",Base64.encodeToString(iv,Base64.NO_WRAP)).put("ciphertext",Base64.encodeToString(cipher.doFinal(value.toString().getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP));
    }
    public static JSONObject open(JSONObject value,String secret)throws Exception{
        if(value.getInt("v")!=1)throw new IllegalArgumentException("协议版本不匹配");
        byte[] iv=Base64.decode(value.getString("nonce"),Base64.NO_WRAP),data=Base64.decode(value.getString("ciphertext"),Base64.NO_WRAP);
        if(iv.length!=12||data.length<16||data.length>96*1024*1024)throw new IllegalArgumentException("加密信封无效");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,new SecretKeySpec(key(secret),"AES"),new GCMParameterSpec(128,iv));
        return new JSONObject(new String(cipher.doFinal(data),StandardCharsets.UTF_8));
    }
}
