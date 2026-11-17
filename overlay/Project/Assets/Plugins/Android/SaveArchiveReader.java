package local.bnsm.bridge;

import java.io.*;
import java.nio.ByteBuffer;
import java.nio.charset.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.zip.*;
import org.json.*;

/** Reads the original archive without serializing any player/resident JSON through org.json. */
public final class SaveArchiveReader {
    private static final Set<String> DATA=new HashSet<>(Arrays.asList("players.json","world-chat.json","save-format.json","bot-world.json"));
    public static final int LIMIT=64*1024*1024;
    public static String json(byte[] bytes)throws Exception {
        String text=StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();
        if(text.startsWith("\ufeff"))text=text.substring(1);
        JsonDocumentPatch.validate(text);return text;
    }
    public static String hash(byte[] bytes)throws Exception {
        StringBuilder out=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(bytes))out.append(String.format(Locale.US,"%02x",b&255));return out.toString();
    }
    public static String hash(File file)throws Exception {
        MessageDigest digest=MessageDigest.getInstance("SHA-256");
        try(InputStream in=new FileInputStream(file)){byte[] buffer=new byte[32768];int n;while((n=in.read(buffer))!=-1)digest.update(buffer,0,n);}
        StringBuilder out=new StringBuilder();for(byte b:digest.digest())out.append(String.format(Locale.US,"%02x",b&255));return out.toString();
    }
    public static void copyVerified(InputStream source,File target,long expected,String checksum)throws Exception {
        boolean ok=false;
        try(InputStream in=source){
            if(expected<=0||expected>128L*1024*1024||checksum==null||!checksum.matches("[a-f0-9]{64}"))throw new IOException("导入文件的长度或校验信息无效");
            try(FileOutputStream out=new FileOutputStream(target)){
                if(in==null)throw new IOException("所选存档不可读");
                byte[] buffer=new byte[32768];int n;long total=0;
                while((n=in.read(buffer))!=-1){if((total+=n)>expected)throw new IOException("导入文件长度变化，请重新选择");out.write(buffer,0,n);}
                if(total!=expected)throw new IOException("存档传输不完整");out.getFD().sync();
            }
            if(!hash(target).equals(checksum))throw new IOException("存档传输校验失败，未导入");ok=true;
        }finally{if(!ok)target.delete();}
    }
    private static byte[] bytes(InputStream input,int maximum)throws IOException {
        try(InputStream in=input;ByteArrayOutputStream out=new ByteArrayOutputStream()){
            byte[] buffer=new byte[16384];int n,total=0;while((n=in.read(buffer))!=-1){if((total+=n)>maximum)throw new IOException("存档解压后超过64 MiB限制，请保留原包并联系维护者");out.write(buffer,0,n);}return out.toByteArray();
        }
    }
    private static void safe(String name)throws IOException {
        if(name.isEmpty()||name.startsWith("/")||name.contains("\\")||name.contains(":")||Arrays.asList(name.split("/",-1)).contains(".."))throw new IOException("压缩包包含不安全路径，未导入");
    }
    private static Map<String,byte[]> unpack(ZipFile zip)throws Exception {
        if(zip.size()>5)throw new IOException("存档包包含多余文件；请选择原导出的 .bnsm-save.zip");
        Map<String,byte[]> entries=new LinkedHashMap<>();long total=0;
        Enumeration<? extends ZipEntry> it=zip.entries();
        while(it.hasMoreElements()){
            ZipEntry e=it.nextElement();String name=e.getName();safe(name);
            if(e.isDirectory()||(!DATA.contains(name)&&!name.equals("archive.json"))||entries.containsKey(name))throw new IOException("存档包包含重复或未知文件："+name);
            if(e.getSize()>LIMIT||(e.getSize()>0&&(total+e.getSize())>LIMIT))throw new IOException("存档解压后超过64 MiB限制");
            byte[] data=bytes(zip.getInputStream(e),name.equals("archive.json")?Math.min(1024*1024,(int)(LIMIT-total)):(int)(LIMIT-total));total+=data.length;entries.put(name,data);
        }
        if(!entries.containsKey("archive.json"))throw new IOException("不是原版存档包：缺少 archive.json");
        JSONObject manifest=new JSONObject(json(entries.remove("archive.json")));
        Object version=manifest.get("ArchiveVersion");
        if(!"BnsmLocalGame".equals(manifest.getString("Product"))||!(version instanceof Integer||version instanceof Long)||((Number)version).longValue()!=1)throw new IOException("存档备份格式或版本不兼容");
        JSONObject hashes=manifest.getJSONObject("Files");
        if(hashes.length()!=entries.size()||!entries.containsKey("players.json"))throw new IOException("存档内容不完整，请重新导出");
        Iterator<String> names=hashes.keys();while(names.hasNext()){
            String name=names.next();if(!DATA.contains(name)||!entries.containsKey(name))throw new IOException("存档文件清单与内容不一致");
            String expected=hashes.getString(name);if(!expected.matches("[a-fA-F0-9]{64}")||!hash(entries.get(name)).equalsIgnoreCase(expected))throw new IOException("存档校验失败："+name+"，原存档未覆盖");
            try{json(entries.get(name));}catch(Exception e){throw new IOException("存档 JSON 无效："+name,e);}
        }
        return entries;
    }
    public static Map<String,byte[]> read(File source)throws Exception {
        if(!source.isFile()||source.length()==0||source.length()>128L*1024*1024)throw new IOException("请选择不超过128 MiB的存档 ZIP");
        try(ZipFile zip=new ZipFile(source)){
            ZipEntry nested=zip.getEntry("save.bnsm-save.zip");
            if(nested==null){
                if(zip.getEntry("diagnostics.json")!=null&&zip.getEntry("players.json")==null)throw new IOException("这是日志包，不包含存档；请选择存档备份或“存档＋日志”总包");
                return unpack(zip);
            }
            if(zip.size()>64)throw new IOException("存档与日志总包包含过多项目");
            Set<String> names=new HashSet<>();long total=0;Enumeration<? extends ZipEntry> it=zip.entries();
            while(it.hasMoreElements()){
                ZipEntry e=it.nextElement();String name=e.getName();safe(name);
                if(!names.add(name))throw new IOException("总包包含重复文件");
                boolean known=name.equals("save.bnsm-save.zip")||name.equals("diagnostics.json")||name.equals("host-state.json")||name.equals("说明.txt")||name.matches("logs/(client|server)/(client|server)(?:\\.[1-3])?\\.log");
                if(!known||e.isDirectory()||e.getSize()<0||(total+=e.getSize())>128L*1024*1024)throw new IOException("不是受支持的存档与日志总包");
            }
            File extracted=File.createTempFile("nested-save-",".zip",source.getParentFile());
            try{
                try(InputStream in=zip.getInputStream(nested);FileOutputStream out=new FileOutputStream(extracted)){
                    byte[] buffer=new byte[16384];int n;long size=0;while((n=in.read(buffer))!=-1){if((size+=n)>128L*1024*1024)throw new IOException("嵌套存档超过大小限制");out.write(buffer,0,n);}out.getFD().sync();
                }
                try(ZipFile inner=new ZipFile(extracted)){return unpack(inner);}
            }finally{extracted.delete();}
        }catch(ZipException e){throw new IOException("文件不是完整的 ZIP 存档，请重新导出或下载",e);}
    }
    private SaveArchiveReader(){}
}
