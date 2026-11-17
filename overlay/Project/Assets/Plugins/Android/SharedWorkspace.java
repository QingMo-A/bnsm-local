package local.bnsm.bridge;

import org.json.*;
import java.io.*;
import java.nio.channels.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.nio.file.attribute.BasicFileAttributes;
import java.security.MessageDigest;
import java.text.SimpleDateFormat;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;

/** Android file bridge, compatible with the authoritative SaveWorkspace v1.
 * No gameplay state is edited here. A cross-process lease spans the whole game.
 */
public final class SharedWorkspace {
    private static final String[] DATA={"players.json","world-chat.json","save-format.json","bot-world.json"};
    private static final Set<String> HELD=new HashSet<>();
    private static final Pattern PENDING_DELETE=Pattern.compile("([a-f0-9]{32})\\.deleting\\.([a-f0-9]{32}|empty|keep)");
    private static final class BusyException extends IOException {BusyException(){super("游戏或存档操作正在运行，请正常退出游戏或等待操作完成后再管理存档。日志仍可查看与分享。");}}
    private final File files,external,root;
    public SharedWorkspace(File files,File external){this.files=files;this.external=external;root=new File(files,"EmbeddedServer/Saves");}
    public static final class Lease implements AutoCloseable {
        private final RandomAccessFile file;private final FileLock lock;private final String path;private boolean closed;
        private Lease(RandomAccessFile file,FileLock lock,String path){this.file=file;this.lock=lock;this.path=path;}
        public void close() throws IOException {synchronized(HELD){if(closed)return;closed=true;try{lock.release();}finally{file.close();HELD.remove(path);}}}
    }
    public Lease acquire() throws IOException {
        Lease lease=acquireLock();
        try {
            recoverDeletes();
            return lease;
        }catch(Exception error){
            try{lease.close();}catch(IOException closing){error.addSuppressed(closing);}
            if(error instanceof IOException)throw (IOException)error;
            throw new IOException("无法完成上次存档删除："+error.getMessage(),error);
        }
    }
    private Lease acquireLock()throws IOException {
        File parent=new File(files,"EmbeddedServer");mkdir(parent);
        String path=new File(parent,".management.lock").getCanonicalPath();
        synchronized(HELD){
            // Do not open/close a second descriptor for a JVM-held fcntl lock.
            if(HELD.contains(path))throw new BusyException();
            RandomAccessFile file=new RandomAccessFile(path,"rw");
            try {FileLock lock=file.getChannel().tryLock();if(lock==null)throw new BusyException();HELD.add(path);return new Lease(file,lock,path);}
            catch(OverlappingFileLockException e){file.close();throw new BusyException();}
            catch(IOException e){file.close();throw e;}
        }
    }
    public static String utc(long millis){SimpleDateFormat f=new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",Locale.US);f.setTimeZone(TimeZone.getTimeZone("UTC"));return f.format(new Date(millis));}
    private static String id() {return UUID.randomUUID().toString().replace("-","");}
    private static void mkdir(File dir)throws IOException {if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("无法创建目录，请检查剩余空间");}
    private static byte[] read(File file,int max)throws IOException {
        try(InputStream in=new FileInputStream(file);ByteArrayOutputStream out=new ByteArrayOutputStream()){
            byte[] b=new byte[16384];int n,total=0;while((n=in.read(b))!=-1){if((total+=n)>max)throw new IOException("文件超过支持的大小");out.write(b,0,n);}return out.toByteArray();
        }
    }
    private static JSONObject json(File file)throws Exception{return new JSONObject(new String(read(file,64*1024*1024),StandardCharsets.UTF_8));}
    private static void atomic(File file,String text)throws Exception {
        mkdir(file.getParentFile());File temp=new File(file.getParentFile(),file.getName()+"."+id()+".tmp");
        try {try(FileOutputStream out=new FileOutputStream(temp)){out.write(text.getBytes(StandardCharsets.UTF_8));out.getFD().sync();}
            Files.move(temp.toPath(),file.toPath(),StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);
        }finally{temp.delete();}
    }
    private File profile(String id)throws Exception {
        if(id==null||!id.matches("[a-f0-9]{32}"))throw new IOException("存档编号无效");
        File dir=new File(root,"profiles/"+id);if(!json(new File(dir,"profile.json")).getString("Id").equals(id))throw new IOException("存档目录与编号不一致");return dir;
    }
    private String active()throws Exception {
        File index=new File(root,"workspace.json");if(!index.exists())return "";
        JSONObject value=json(index);if(value.getInt("FormatVersion")!=1)throw new IOException("存档格式较新，请更新游戏和管理器");
        String id=value.getString("ActiveProfileId");profile(id);return id;
    }
    private void select(String id)throws Exception {atomic(new File(root,"workspace.json"),new JSONObject().put("FormatVersion",1).put("ActiveProfileId",id).toString(2));}
    private static long bytes(File saves){long total=0;for(String name:DATA)total+=new File(saves,name).length();return total;}
    public JSONObject list()throws Exception {
        try(Lease ignored=acquire()){return list(false);}
        catch(BusyException error){return list(true);}
    }
    private JSONObject list(boolean busy)throws Exception {
        String active=active();JSONArray rows=new JSONArray();File[] dirs=new File(root,"profiles").listFiles();
        if(dirs!=null){Arrays.sort(dirs,Comparator.comparing(File::getName));for(File dir:dirs){
            if(!dir.isDirectory()||!dir.getName().matches("[a-f0-9]{32}"))continue;
            JSONObject row=json(new File(profile(dir.getName()),"profile.json"));File saves=new File(dir,"saves");long modified=new File(dir,"profile.json").lastModified();
            for(String name:DATA)modified=Math.max(modified,new File(saves,name).lastModified());
            rows.put(new JSONObject().put("id",row.getString("Id")).put("name",row.getString("Name")).put("createdUtc",row.getString("CreatedUtc"))
                .put("modifiedUtc",utc(modified)).put("bytes",bytes(saves)).put("active",dir.getName().equals(active)));
        }}
        return new JSONObject().put("profiles",rows).put("activeProfileId",active).put("gameRunning",busy).put("logs",logs()).put("exports",exports());
    }
    private static String name(String text)throws IOException {
        String value=text==null?"":text.trim();if(value.length()<1||value.length()>40)throw new IOException("存档名称需为1至40个字符");
        for(char c:value.toCharArray())if(Character.isISOControl(c))throw new IOException("存档名称不能包含控制字符");return value;
    }
    private String create(String text,Map<String,byte[]> data)throws Exception {
        return create(text,data,id(),null);
    }
    private String create(String text,Map<String,byte[]> data,String id,String importHash)throws Exception {
        String name=name(text);File dir=new File(root,"profiles/"+id),temp=new File(root,"profiles/"+id+".creating");
        if(temp.exists())throw new IOException("发现未完成的存档处理，请重新选择文件再导入");mkdir(new File(temp,"saves"));
        try {
            if(data==null){data=new LinkedHashMap<>();data.put("players.json","[]".getBytes(StandardCharsets.UTF_8));
                data.put("save-format.json",new JSONObject().put("Product","BnsmLocalGame").put("SchemaVersion",1).put("MinimumReaderVersion",1).put("LastWriterBuild","20261031-BotWorld").toString().getBytes(StandardCharsets.UTF_8));}
            for(Map.Entry<String,byte[]> e:data.entrySet())try(FileOutputStream out=new FileOutputStream(new File(temp,"saves/"+e.getKey()))){out.write(e.getValue());out.getFD().sync();}
            validate(new File(temp,"saves"));
            JSONObject metadata=new JSONObject().put("Id",id).put("Name",name).put("CreatedUtc",utc(System.currentTimeMillis()));
            if(importHash!=null)metadata.put("ImportSha256",importHash).put("ImportedUtc",utc(System.currentTimeMillis()));
            atomic(new File(temp,"profile.json"),metadata.toString(2));
            Files.move(temp.toPath(),dir.toPath(),StandardCopyOption.ATOMIC_MOVE);return id;
        }finally{if(temp.exists())eraseCreating(temp);}
    }
    public JSONObject importProfile(File source,String text,String request)throws Exception {
        if(request==null||!request.matches("[a-f0-9]{32}"))throw new IOException("导入请求编号无效");
        String title=name(text);
        try(Lease ignored=acquire()){
            String current=active(),fingerprint;
            if(source.length()>128L*1024*1024)throw new IOException("存档 ZIP 超过128 MiB");
            fingerprint=SaveArchiveReader.hash(source);
            File dir=new File(root,"profiles/"+request);
            if(dir.exists()){
                JSONObject prior=json(new File(profile(request),"profile.json"));
                if(!fingerprint.equals(prior.optString("ImportSha256"))||!title.equals(prior.getString("Name")))throw new IOException("此请求已用于其他存档，请刷新列表确认");
                if(current.isEmpty())select(request);
                return new JSONObject().put("id",request).put("selected",current.isEmpty()||current.equals(request)).put("repeated",true);
            }
            if(current.isEmpty()){
                File[] existing=new File(root,"profiles").listFiles();
                if(existing!=null)for(File prior:existing)if(prior.isDirectory()&&prior.getName().matches("[a-f0-9]{32}"))throw new IOException("已有存档但索引缺失，请在列表选择切换；原数据已保留");
            }
            Map<String,byte[]> data=SaveArchiveReader.read(source);create(title,data,request,fingerprint);
            if(current.isEmpty())select(request);
            return new JSONObject().put("id",request).put("selected",current.isEmpty()).put("repeated",false);
        }
    }
    private static void eraseCreating(File dir)throws IOException {
        // Only a fresh, random, local staging directory is eligible for deletion.
        if(!dir.getName().matches("[a-f0-9]{32}\\.creating"))throw new IOException("Invalid staging directory");
        File[] files=dir.listFiles();if(files!=null)for(File file:files){if(file.getName().equals("saves")&&file.isDirectory()){File[] children=file.listFiles();if(children!=null)for(File child:children)child.delete();}file.delete();}dir.delete();
    }
    private static void validate(File saves)throws Exception {
        File format=new File(saves,"save-format.json");if(format.exists()){JSONObject v=json(format);
            if(!v.getString("Product").equals("BnsmLocalGame")||v.getInt("SchemaVersion")!=1||v.getInt("MinimumReaderVersion")>1)throw new IOException("存档版本不兼容，原存档未覆盖");}
        JSONArray players=new JSONArray(new String(read(new File(saves,"players.json"),64*1024*1024),StandardCharsets.UTF_8));
        HashSet<Long> uids=new HashSet<>();HashSet<String> accounts=new HashSet<>();
        for(int i=0;i<players.length();i++){JSONObject p=players.getJSONObject(i);long uid=p.getLong("Uid");String account=p.getString("Account");
            if(uid<=0||uid>0xffffffffL||!uids.add(uid)||account.trim().isEmpty()||!accounts.add(account))throw new IOException("玩家存档结构无效");p.getJSONArray("CardIds");}
        File bots=new File(saves,"bot-world.json");if(bots.exists()){JSONObject world=json(bots);if(world.getInt("Schema")!=1)throw new IOException("人机存档版本不兼容");world.getJSONArray("Bots");}
        File chat=new File(saves,"world-chat.json");if(chat.exists())new JSONTokener(new String(read(chat,64*1024*1024),StandardCharsets.UTF_8)).nextValue();
    }
    public JSONObject change(String command,String selected,String text)throws Exception {
        try(Lease ignored=acquire()){
            if(command.equals("new")){if(!new File(root,"workspace.json").exists()){File[] dirs=profileDirs();if(dirs!=null&&dirs.length>0)throw new IOException("已有存档但索引缺失，请先保留目录并联系维护者");}
                else active();String created=create(text,null);select(created);return new JSONObject().put("id",created);}
            File dir=profile(selected);active();
            if(command.equals("switch")){validate(new File(dir,"saves"));select(selected);}
            else if(command.equals("rename")){JSONObject row=json(new File(dir,"profile.json"));row.put("Name",name(text));atomic(new File(dir,"profile.json"),row.toString(2));}
            else throw new IOException("未知存档操作");
            return new JSONObject().put("id",selected);
        }
    }
    private File[] profileDirs() {
        return new File(root,"profiles").listFiles(file->file.getName().matches("[a-f0-9]{32}")&&file.isDirectory());
    }

    public JSONObject deleteProfile(String selected,boolean wasActive)throws Exception {
        try(Lease ignored=acquire()){
            File profiles=checkedProfiles();
            File target=profile(selected);
            if(!target.getCanonicalFile().equals(new File(profiles,selected))||
                !Files.isDirectory(target.toPath(),LinkOption.NOFOLLOW_LINKS))
                throw new IOException("存档目录无效，已停止删除");
            String current=active();
            if(wasActive!=selected.equals(current))throw new IOException("当前存档已切换，请刷新后重新确认删除");
            String replacement="keep";
            if(wasActive){
                File[] candidates=profileDirs();
                if(candidates==null)throw new IOException("无法读取存档列表");
                Arrays.sort(candidates,Comparator.comparing(File::getName));
                replacement="empty";
                for(File other:candidates){
                    if(other.getName().equals(selected))continue;
                    try{
                        File candidate=profile(other.getName());
                        if(!candidate.getCanonicalFile().equals(new File(profiles,other.getName())))continue;
                        validate(new File(candidate,"saves"));
                        replacement=other.getName();
                        break;
                    }catch(IOException|JSONException unavailable){/* Try another valid profile. */}
                }
                if(replacement.equals("empty")&&candidates.length>1)
                    throw new IOException("其余存档无法读取，请先新建或导入一份可用存档");
            }
            File pending=new File(profiles,selected+".deleting."+replacement);
            // The same-volume rename commits removal from the profile list.
            // The hidden directory retains the intended index change across process exit.
            Files.move(target.toPath(),pending.toPath(),StandardCopyOption.ATOMIC_MOVE);
            boolean cleared=finishDelete(pending,selected,replacement);
            return new JSONObject().put("id",selected).put("cleanupPending",!cleared).put("snapshot",list(false));
        }
    }

    private File checkedProfiles()throws IOException {
        File path=new File(files.getCanonicalFile(),"EmbeddedServer/Saves/profiles");
        if(!path.equals(path.getCanonicalFile()))throw new IOException("存档目录无效");
        return path;
    }

    private void recoverDeletes()throws Exception {
        File profiles=new File(root,"profiles");
        if(!profiles.exists())return;
        File[] pending=checkedProfiles().listFiles(file->PENDING_DELETE.matcher(file.getName()).matches());
        if(pending==null)throw new IOException("无法读取存档目录");
        for(File file:pending){
            Matcher deletion=PENDING_DELETE.matcher(file.getName());
            if(deletion.matches())finishDelete(file,deletion.group(1),deletion.group(2));
        }
    }

    private boolean finishDelete(File pending,String removed,String replacement)throws Exception {
        File index=new File(root,"workspace.json");
        if(!replacement.equals("keep")&&index.exists()&&json(index).getString("ActiveProfileId").equals(removed)){
            if(replacement.equals("empty"))Files.delete(index.toPath());
            else {profile(replacement);select(replacement);}
        }
        try{
            Files.walkFileTree(pending.toPath(),new SimpleFileVisitor<Path>(){
                @Override public FileVisitResult visitFile(Path file,BasicFileAttributes attrs)throws IOException {
                    Files.delete(file);
                    return FileVisitResult.CONTINUE;
                }
                @Override public FileVisitResult postVisitDirectory(Path dir,IOException error)throws IOException {
                    if(error!=null)throw error;
                    Files.delete(dir);
                    return FileVisitResult.CONTINUE;
                }
            });
            return true;
        }catch(IOException error){
            // The selection already points to a usable profile. Retry cleanup
            // at the next lease acquisition without preventing game startup.
            return false;
        }
    }
    private static final List<String> BOT_TIERS=Arrays.asList("casual","regular","expert","legend");
    private static final List<String> BOT_STYLES=Arrays.asList("balanced","burst","control","sustain","counter");
    private JSONObject botWorld(File file)throws Exception {
        JSONObject world=file.exists()?json(file):new JSONObject().put("Schema",1).put("NextUid",910001).put("Bots",new JSONArray());
        if(world.getInt("Schema")!=1||world.getJSONArray("Bots").length()>100)throw new IOException("人机存档结构不兼容，请先保留存档");
        return world;
    }
    public JSONObject bots()throws Exception {
        JSONObject listing=list();String id=listing.getString("activeProfileId");
        if(id.isEmpty())throw new IOException("请先在存档页新建或选择存档");
        File file=new File(profile(id),"saves/bot-world.json");JSONObject world=botWorld(file);JSONArray rows=new JSONArray(),raw=world.getJSONArray("Bots");
        for(int i=0;i<raw.length();i++){JSONObject bot=raw.getJSONObject(i),p=bot.getJSONObject("Profile");
            rows.put(new JSONObject().put("uid",bot.getLong("Uid")).put("name",p.getString("Nickname")).put("level",p.getLong("Level"))
                .put("strength",bot.getString("Strength")).put("style",bot.getString("Style")).put("aiStrength",bot.optString("AiStrength",bot.getString("Strength"))).put("revision",bot.optInt("Revision",1)));}
        return new JSONObject().put("profileId",id).put("profileName",json(new File(profile(id),"profile.json")).getString("Name"))
            .put("gameRunning",listing.getBoolean("gameRunning")).put("bots",rows)
            .put("requests",world.optJSONArray("ManagementRequests")==null?new JSONArray():world.getJSONArray("ManagementRequests"))
            .put("results",world.optJSONArray("ManagementResults")==null?new JSONArray():world.getJSONArray("ManagementResults"))
            .put("modifiedUtc",file.exists()?utc(file.lastModified()):"");
    }
    public JSONObject queueBots(String method,String argument)throws Exception {
        if(argument==null||argument.length()>8192)throw new IOException("人机设置内容无效");
        try(Lease ignored=acquire()){
            JSONObject input=new JSONObject(argument);String id=input.getString("profileId");
            if(!id.equals(active()))throw new IOException("当前存档已切换，请刷新后重新设置人机");
            File file=new File(profile(id),"saves/bot-world.json");JSONObject world=botWorld(file);JSONArray bots=world.getJSONArray("Bots");
            JSONArray queue=world.optJSONArray("ManagementRequests");if(queue==null)queue=new JSONArray();
            String tier=input.getString("strength");JSONObject request=new JSONObject().put("Id",id()).put("Strength",tier).put("CreatedUtc",utc(System.currentTimeMillis()));
            if(method.equals("botGenerate")){
                if(!tier.equals("mixed")&&!BOT_TIERS.contains(tier))throw new IOException("新增人机强度无效");
                int projected=bots.length()==0?12:bots.length();
                for(int i=0;i<queue.length();i++)if(queue.getJSONObject(i).optString("Action").equals("generate"))projected+=queue.getJSONObject(i).getInt("Count");
                if(projected+4>100)throw new IOException("包括待新增的人机在内，总数最多100名");
                request.put("Action","generate").put("Count",4);
            }else if(method.equals("botConfigure")){
                String style=input.getString("style"),ai=input.getString("aiStrength");long uid=input.getLong("botUid");int revision=input.getInt("revision");
                if(!BOT_TIERS.contains(tier)||!BOT_TIERS.contains(ai)||!BOT_STYLES.contains(style))throw new IOException("人机强度或配装选项无效");
                JSONObject found=null;for(int i=0;i<bots.length();i++)if(bots.getJSONObject(i).getLong("Uid")==uid)found=bots.getJSONObject(i);
                if(found==null||found.optInt("Revision",1)!=revision)throw new IOException("该人机配置已变化，请刷新后重试");
                JSONArray keep=new JSONArray();for(int i=0;i<queue.length();i++){JSONObject q=queue.getJSONObject(i);if(!q.optString("Action").equals("configure")||q.optLong("BotUid")!=uid)keep.put(q);}queue=keep;
                request.put("Action","configure").put("BotUid",uid).put("ExpectedRevision",revision).put("Style",style).put("AiStrength",ai);
            }else throw new IOException("未知人机设置操作");
            if(queue.length()>=128)throw new IOException("待应用的人机设置过多，请先启动游戏应用设置");
            queue.put(request);
            // JSONObject/JSONTokener on Android rounds UInt64.MaxValue to a
            // Double. Residents are opaque: write only the command property.
            String original=file.exists()?new String(read(file,64*1024*1024),StandardCharsets.UTF_8):"{\"Schema\":1,\"NextUid\":910001,\"Bots\":[]}";
            atomic(file,JsonDocumentPatch.replace(original,"ManagementRequests",queue.toString()));
        }
        return bots();
    }
    private static String hash(byte[] data)throws Exception {StringBuilder s=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(data))s.append(String.format(Locale.US,"%02X",b&255));return s.toString();}
    private File exportFile(String prefix,String suffix)throws IOException {File dir=new File(files,"SupportExports");mkdir(dir);return new File(dir,prefix+"-"+new SimpleDateFormat("yyyyMMdd-HHmmss",Locale.US).format(new Date())+"-"+id().substring(0,6)+suffix);}
    private static void put(ZipOutputStream out,String name,byte[] data,long time)throws IOException {ZipEntry e=new ZipEntry(name);e.setTime(time);out.putNextEntry(e);out.write(data);out.closeEntry();}
    private static long elapsedMillis(long start){return (System.nanoTime()-start)/1000000;}
    private static String digestText(byte[] digest){StringBuilder text=new StringBuilder(64);for(byte b:digest)text.append(String.format(Locale.US,"%02x",b&255));return text.toString();}
    private static String putStoredSnapshot(ZipOutputStream out,File file)throws Exception {
        // The inner save is already compressed. Stream it unchanged instead
        // of allocating another whole-file buffer and deflating it again.
        long length=file.length(),count=0;if(length<=0||length>128L*1024*1024)throw new IOException("存档快照大小无效");
        CRC32 crc=new CRC32();MessageDigest digest=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[65536];
        try(InputStream in=new FileInputStream(file)){int n;while((n=in.read(buffer))!=-1){if((count+=n)>length)throw new IOException("存档快照发生变化，请重新导出");crc.update(buffer,0,n);digest.update(buffer,0,n);}}
        if(count!=length)throw new IOException("存档快照读取不完整，请重新导出");String expected=digestText(digest.digest());
        ZipEntry entry=new ZipEntry("save.bnsm-save.zip");entry.setTime(file.lastModified());entry.setMethod(ZipEntry.STORED);entry.setSize(length);entry.setCompressedSize(length);entry.setCrc(crc.getValue());out.putNextEntry(entry);
        count=0;digest.reset();try(InputStream in=new FileInputStream(file)){int n;while((n=in.read(buffer))!=-1){if((count+=n)>length)throw new IOException("存档快照发生变化，请重新导出");out.write(buffer,0,n);digest.update(buffer,0,n);}}
        out.closeEntry();if(count!=length||!expected.equals(digestText(digest.digest())))throw new IOException("存档快照校验失败，请重新导出");return expected;
    }
    private static long putLog(ZipOutputStream out,String name,File file)throws IOException {
        // Bounded streaming avoids one multi-megabyte allocation per log.
        try(InputStream in=new FileInputStream(file)){
            ZipEntry entry=new ZipEntry(name);entry.setTime(file.lastModified());out.putNextEntry(entry);
            byte[] buffer=new byte[65536];int n;long total=0;while((n=in.read(buffer))!=-1){if((total+=n)>3L*1024*1024)throw new IOException("日志超过支持的大小");out.write(buffer,0,n);}out.closeEntry();return total;
        }
    }
    public File exportProfile(String selected)throws Exception {
        try(Lease ignored=acquire()){
            File dir=profile(selected),saves=new File(dir,"saves");validate(saves);File result=exportFile("bnsm-save",".bnsm-save.zip"),partial=new File(result.getPath()+".partial");
            try(ZipOutputStream zip=new ZipOutputStream(new FileOutputStream(partial))){JSONObject hashes=new JSONObject();
                for(String name:DATA){File file=new File(saves,name);if(file.exists()){byte[] data=read(file,64*1024*1024);hashes.put(name,hash(data));put(zip,name,data,file.lastModified());}}
                JSONObject manifest=new JSONObject().put("Product","BnsmLocalGame").put("ArchiveVersion",1).put("Name",json(new File(dir,"profile.json")).getString("Name")).put("CreatedUtc",utc(System.currentTimeMillis())).put("Files",hashes);
                put(zip,"archive.json",manifest.toString(2).getBytes(StandardCharsets.UTF_8),System.currentTimeMillis());
            }catch(Exception e){partial.delete();throw e;}promote(partial,result);return result;
        }
    }
    public JSONArray logs()throws Exception {
        JSONArray result=new JSONArray();for(String group:new String[]{"client","server"}){File dir=logDir(group);if(dir==null)continue;
            File[] list=dir.listFiles();if(list!=null){Arrays.sort(list,Comparator.comparingLong(File::lastModified).reversed());for(File file:list)
                if(file.isFile()&&file.getName().matches("(?:client|server)(?:\\.[1-3])?\\.log"))result.put(new JSONObject().put("id",group+"/"+file.getName()).put("name",(group.equals("client")?"客户端 · ":"服务端 · ")+file.getName()).put("modifiedUtc",utc(file.lastModified())).put("bytes",file.length()));}}
        return result;
    }
    private File logDir(String group){return group.equals("server")?new File(files,"EmbeddedServer/Diagnostics"):external==null?new File(files,"Diagnostics"):new File(external,"Diagnostics");}
    private File log(String id)throws IOException {
        if(id==null||!id.matches("(?:client/client|server/server)(?:\\.[1-3])?\\.log"))throw new IOException("日志编号无效");
        String[] parts=id.split("/");File dir=logDir(parts[0]);if(dir==null)throw new IOException("日志尚未创建");File file=new File(dir,parts[1]);if(!file.isFile())throw new IOException("日志已轮换，请刷新列表");return file;
    }
    public String viewLog(String id)throws Exception {File f=log(id);try(RandomAccessFile in=new RandomAccessFile(f,"r")){long length=in.length(),start=Math.max(0,length-128*1024);in.seek(start);byte[] bytes=new byte[(int)(length-start)];in.readFully(bytes);return (start>0?"（仅显示末尾128 KB）\n":"")+new String(bytes,StandardCharsets.UTF_8);}}
    private static void promote(File partial,File target)throws IOException {try{Files.move(partial.toPath(),target.toPath(),StandardCopyOption.ATOMIC_MOVE);}finally{partial.delete();}}
    public File exportLog(String id)throws Exception {File source=log(id),target=exportFile(id.startsWith("client/")?"client":"server",".log"),partial=new File(target.getPath()+".partial");try{Files.copy(source.toPath(),partial.toPath());promote(partial,target);return target;}finally{partial.delete();}}
    public File bundle(File save,String details)throws Exception {
        return bundle(save,details,save==null?"本次选择导出日志，未请求存档快照":"");
    }
    public File bundle(File save,String details,String omission)throws Exception {
        return bundle(save,details,omission,-1);
    }
    public File bundle(File save,String details,String omission,long snapshotElapsedMs)throws Exception {
        // A supplied snapshot must be a complete checksummed save. Never turn
        // a missing/damaged snapshot into a successful logs-only backup.
        long started=System.nanoTime();Set<String> saveFiles=save==null?null:new LinkedHashSet<>(SaveArchiveReader.read(save).keySet());
        long validationMs=elapsedMillis(started),writing=System.nanoTime(),logBytes=0;
        File result=exportFile(save==null?"bnsm-logs":"bnsm-save-and-logs",".zip"),partial=new File(result.getPath()+".partial");
        try(ZipOutputStream zip=new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(partial),65536))){
            zip.setLevel(Deflater.BEST_SPEED);String saveHash=save==null?null:putStoredSnapshot(zip,save);
            JSONArray rows=logs();for(int i=0;i<rows.length();i++){String id=rows.getJSONObject(i).getString("id");try{logBytes+=putLog(zip,"logs/"+id,log(id));}catch(FileNotFoundException ignored){}}
            JSONObject metadata=new JSONObject().put("product","BnsmLocalGame").put("exportedUtc",utc(System.currentTimeMillis())).put("saveFilesIncluded",save!=null).put("runtimeDetails",details==null?"":details);
            if(save!=null)metadata.put("saveArchive","save.bnsm-save.zip").put("saveBytes",save.length()).put("saveSha256",saveHash).put("saveFiles",new JSONArray(saveFiles));
            else metadata.put("saveOmissionReason",omission==null||omission.trim().isEmpty()?"未取得存档快照":omission);
            File clientDir=logDir("client");File state=clientDir==null?null:new File(clientDir,"embedded-state.json");
            if(state!=null&&state.isFile())try{put(zip,"host-state.json",read(state,256*1024),state.lastModified());metadata.put("hostStateIncluded",true);}
                catch(IOException e){metadata.put("hostStateIncluded",false).put("hostStateReadError",e.getClass().getSimpleName());}
            metadata.put("exportPerformance",new JSONObject().put("snapshotElapsedMs",snapshotElapsedMs).put("saveValidationMs",validationMs).put("bundleWriteMs",elapsedMillis(writing)).put("bundleElapsedMs",elapsedMillis(started)).put("logBytes",logBytes).put("nestedSaveCompression",save==null?"none":"stored"));
            put(zip,"diagnostics.json",metadata.toString(2).getBytes(StandardCharsets.UTF_8),System.currentTimeMillis());
            put(zip,"说明.txt",("生成时间（UTC）："+metadata.getString("exportedUtc")+"\n"+(save!=null?"已包含存档：save.bnsm-save.zip（内层为玩家进度等数据）。可直接在存档管家导入此总包；未结算战斗不会成为可恢复的中途检查点。":"仅包含本机日志，不包含存档。原因："+metadata.getString("saveOmissionReason"))+"\n移植与维护：QingMo").getBytes(StandardCharsets.UTF_8),System.currentTimeMillis());
        }catch(Exception e){partial.delete();throw e;}promote(partial,result);return result;
    }
    public static JSONObject bundleInfo(File file)throws Exception {
        try(ZipFile zip=new ZipFile(file)){
            ZipEntry manifest=zip.getEntry("diagnostics.json"),save=zip.getEntry("save.bnsm-save.zip");
            if(manifest==null)throw new IOException("备份说明缺失，请重新导出");
            JSONObject info=new JSONObject(SaveArchiveReader.json(readZip(zip,manifest,1024*1024)));
            if(info.getBoolean("saveFilesIncluded")!=(save!=null)||(save!=null&&save.getSize()<=0))throw new IOException("备份内容与说明不一致，请重新导出");
            return info;
        }
    }
    private static byte[] readZip(ZipFile zip,ZipEntry entry,int maximum)throws IOException {
        try(InputStream in=zip.getInputStream(entry);ByteArrayOutputStream out=new ByteArrayOutputStream()){
            byte[] b=new byte[16384];int n,total=0;while((n=in.read(b))!=-1){if((total+=n)>maximum)throw new IOException("备份说明过大");out.write(b,0,n);}return out.toByteArray();
        }
    }
    public JSONArray exports()throws Exception {JSONArray rows=new JSONArray();File[] list=new File(files,"SupportExports").listFiles();if(list!=null){Arrays.sort(list,Comparator.comparingLong(File::lastModified).reversed());for(File f:list)if(f.isFile()&&f.getName().matches("[a-zA-Z0-9._-]+\\.(?:zip|log)"))rows.put(new JSONObject().put("name",f.getName()).put("modifiedUtc",utc(f.lastModified())).put("bytes",f.length()));}return rows;}
    public File exported(String name)throws IOException {if(name==null||!name.matches("[a-zA-Z0-9._-]+\\.(?:zip|log)")||name.contains(".."))throw new IOException("导出文件编号无效");File f=new File(files,"SupportExports/"+name);if(!f.isFile())throw new FileNotFoundException("导出文件不存在");return f;}
}
