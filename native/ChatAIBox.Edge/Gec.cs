// SPDX-License-Identifier: LGPL-3.0-or-later
// C# adaptation of the Edge v2 codec in shared/edge-gec.mjs.
// Original protocol implementation: rany2/edge-tts drm.py.
// See shared/THIRD_PARTY_NOTICES.md and shared/EDGE-TTS-LICENSE.txt.
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
namespace ChatAIBox.Edge;
public static class Gec
{
    private static readonly JsonObject C=Load();
    private static JsonObject Load(){using var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("ChatAIBox.Edge.edge-constants.mjs")??throw new InvalidDataException("Missing Edge codec constants");using var reader=new StreamReader(input);var text=reader.ReadToEnd();return JsonNode.Parse(text[(text.IndexOf("GEC=",StringComparison.Ordinal)+4)..].Trim().TrimEnd(';'))!.AsObject();}
    private static uint[] Array(string name)=>C[name]!.AsArray().Select(v=>v!.GetValue<uint>()).ToArray();
    private static uint Value(string name)=>C[name]!.GetValue<uint>();
    private static uint Rol(uint x)=>BitOperations.RotateLeft(x,16);
    private static uint S0(uint x)=>BitOperations.RotateRight(x,7)^BitOperations.RotateRight(x,18)^(x>>3);
    private static uint S1(uint x)=>BitOperations.RotateRight(x,17)^BitOperations.RotateRight(x,19)^(x>>10);
    private static void Expand(uint[] w,int start=16){unchecked{for(var i=start;i<64;i++)w[i]=S1(w[i-2])+w[i-7]+S0(w[i-15])+w[i-16];}}
    private static uint[] Rounds(uint[] state,uint[] w,int start=0)
    {
        var a=state[0];var b=state[1];var c=state[2];var d=state[3];var e=state[4];var f=state[5];var g=state[6];var h=state[7];var k=Array("k");
        unchecked{for(var i=start;i<64;i++){var constant=start==14&&i==14?Value("adjust"):k[i];var t=h+(BitOperations.RotateRight(e,6)^BitOperations.RotateRight(e,11)^BitOperations.RotateRight(e,25))+(g^(e&(f^g)))+constant+w[i];var q=(BitOperations.RotateRight(a,2)^BitOperations.RotateRight(a,13)^BitOperations.RotateRight(a,22))+((a&b)|(c&(a|b)));(a,b,c,d,e,f,g,h)=(t+q,a,b,c,d+t,e,f,g);}}
        return[a,b,c,d,e,f,g,h];
    }
    private static byte[] Sha(byte[] data)
    {
        unchecked{
            uint Read(int a,int b){uint n=0;for(var i=a;i<b;i++)n=n*256+data[i];return n;}
            var w=new uint[64];w[14]=Read(0,3);w[15]=Read(3,7);var schedule=Array("schedule");var k=Array("k");
            for(var i=16;i<30;i++){var v=schedule[i-16];if(i-2>=14)v+=S1(w[i-2])^(i==16?Value("sig1"):0);if(i-7>=14)v+=w[i-7];if(i-15>=14)v+=S0(w[i-15])^(i==29?Value("sig0"):0);w[i]=v;}
            w[30]=S1(w[28])+w[23]+S0(w[15])+w[14]+Value("adjust")-k[14];Expand(w,31);
            var initial=Array("initial");var state=Rounds(Array("state"),w,14).Select((v,i)=>v+initial[i]).ToArray();var length=57+data.Length;var pad=(55-length%64+64)%64;var bytes=new byte[data.Length-7+1+pad+8];data.AsSpan(7).CopyTo(bytes);bytes[data.Length-7]=128;BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length-4),(uint)(length*8));
            for(var offset=0;offset<bytes.Length;offset+=64){w=new uint[64];for(var i=0;i<16;i++)w[i]=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+i*4,4));Expand(w);var next=Rounds(state,w);for(var i=0;i<8;i++)state[i]+=next[i];}
            var digest=new byte[32];for(var i=0;i<8;i++)BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i*4),state[i]);return digest;
        }
    }
    private static uint[] Words(byte[] data){var padded=new byte[(data.Length+7)/8*8];data.CopyTo(padded,0);var result=new uint[padded.Length/4];for(var i=0;i<result.Length;i++)result[i]=BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(i*4));return result;}
    public static string Generate(long? unixMilliseconds=null)
    {
        unchecked{
            var seconds=(unixMilliseconds??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())/1000+11644473600L;seconds-=seconds%300;var data=Encoding.ASCII.GetBytes((seconds*10000000L).ToString(CultureInfo.InvariantCulture)+"6A5AA1D4EAFF4E9FB37E23D68491D6F4");var digest=Sha(data);var words=Words(digest);uint[] keys=[words[0]|1,words[1]|1];var factors=keys.Select(v=>v*Value("factor")).ToArray();var coefficients=C["coefficients"]!.AsArray();var terms=coefficients.Select((v,i)=>v![0]!.GetValue<uint>()*factors[i%2]).ToList();var offsets=coefficients.Select(v=>v![1]!.GetValue<uint>()).ToList();var p=new byte[data.Length+1];data.CopyTo(p,1);var input=Words(p);
            for(var i=0;i<input.Length;i++){var v=input[i];terms.Add(v*keys[i%2]+(i==0?Value("adj0")*factors[0]:0));offsets.Add(Rol(v)*(i%2!=0?0xc31713dbu:0xef0569fbu)+(i==0?Value("adj1"):0));}
            uint acc=0,last=0,state=0,total=0;
            for(var i=0;i<terms.Count;i+=2){var x=terms[i];var y=terms[i+1];var q=Rol(offsets[i]+x)*0x76f50f34;acc+=Rol(q)*0xc3efea97+q;q=Rol(offsets[i+1]+y)*0x1881ce3c;last=Rol(q)*0x35bd1ec9+q;acc+=last;state=state*keys[0]+x;foreach(var factor in new uint[]{0xcf98b111,0x87085b9f,0x12ceb96d,0x257e1d83})state=Rol(state)*factor;total+=state;var t=state*keys[1]+y;t=Rol(t)*0xa27416f5;t=Rol(t)*0xd38396ff;state=Rol(Rol(t)*0x7c932b89)*0xbfa49f69;total+=state;}
            var checksum=new byte[8];BinaryPrimitives.WriteUInt32LittleEndian(checksum,last^state);BinaryPrimitives.WriteUInt32LittleEndian(checksum.AsSpan(4),acc^total);return Convert.ToHexString(checksum)+"-"+Convert.ToHexString(digest);
        }
    }
}
