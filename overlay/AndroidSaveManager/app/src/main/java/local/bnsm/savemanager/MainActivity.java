package local.bnsm.savemanager;

import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.pm.PackageInfo;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.text.*;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.security.MessageDigest;
import org.json.*;
import local.bnsm.bridge.ExportProvider;

/** Small native launcher/manager. It never boots Unity to manage files. */
public final class MainActivity extends Activity {
    private static final int DELETE_GAME_VERSION=53;
    private static final String GAME="local.bnsm.integratedtest";
    private static final String GATEWAY="local.bnsm.bridge.SaveManagerGateway";
    private final ExecutorService work=Executors.newSingleThreadExecutor();
    private LinearLayout body;private TextView status;private JSONObject snapshot;private volatile boolean busy;private String tab="saves";
    private File pending;
    private boolean resumed;
    private boolean skipResumeRefresh;
    private volatile CompletableFuture<Intent> exchange;
    private volatile String activeRequest;
    private JSONObject botSnapshot;
    private long selectedBot;
    private long gameVersion;
    private static final String[] BOT_TIERS={"casual","regular","expert","legend"},BOT_TIER_LABELS={"普通","精英","高手","宗师"};
    private static final String[] BOT_STYLES={"balanced","burst","control","sustain","counter"},BOT_STYLE_LABELS={"均衡","爆发","控制","续航","反击"};
    private int dp(float v){return Math.round(v*getResources().getDisplayMetrics().density);}
    private TextView text(String value,int size,int color){TextView v=new TextView(this);v.setText(value);v.setTextSize(size);v.setTextColor(color);v.setPadding(0,dp(3),0,dp(3));return v;}
    private LinearLayout stack(){LinearLayout v=new LinearLayout(this);v.setOrientation(LinearLayout.VERTICAL);return v;}
    private Button button(LinearLayout box,String title,Runnable action){Button b=new Button(this);b.setText(title);b.setTextSize(14);b.setAllCaps(false);b.setOnClickListener(v->action.run());box.addView(b,new LinearLayout.LayoutParams(-1,dp(48)));return b;}
    private void pair(LinearLayout box,String a,Runnable first,String b,Runnable second,boolean enabled){LinearLayout row=new LinearLayout(this);row.setOrientation(LinearLayout.HORIZONTAL);box.addView(row);
        for(int i=0;i<2;i++){Button v=new Button(this);v.setAllCaps(false);v.setText(i==0?a:b);v.setTextSize(13);v.setEnabled(enabled);final Runnable run=i==0?first:second;v.setOnClickListener(w->run.run());row.addView(v,new LinearLayout.LayoutParams(0,dp(46),1));}}
    private LinearLayout card(){LinearLayout c=stack();c.setPadding(dp(16),dp(12),dp(16),dp(12));GradientDrawable bg=new GradientDrawable();bg.setColor(Color.WHITE);bg.setCornerRadius(dp(16));c.setBackground(bg);LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.bottomMargin=dp(12);body.addView(c,p);return c;}
    protected void onCreate(Bundle saved){super.onCreate(saved);if(saved!=null){tab=saved.getString("tab","saves");String file=saved.getString("pending");if(file!=null)pending=new File(getCacheDir(),"SharedExports/"+file);}
        LinearLayout root=stack();root.setBackgroundColor(0xfff1f5fa);root.setPadding(dp(16),dp(18),dp(16),dp(12));setContentView(root);
        root.setOnApplyWindowInsetsListener((v,i)->{v.setPadding(dp(16)+i.getSystemWindowInsetLeft(),dp(14)+i.getSystemWindowInsetTop(),dp(16)+i.getSystemWindowInsetRight(),dp(10)+i.getSystemWindowInsetBottom());return i;});root.requestApplyInsets();
        TextView title=text("剑灵存档管家",26,0xff172c40);title.setTypeface(null,Typeface.BOLD);root.addView(title);
        root.addView(text("启动游戏前整理进度，随时查看本机日志",13,0xff65778a));
        status=text("正在连接游戏的存档空间…",13,0xff3d6486);root.addView(status);
        pair(root,"刷新",()->refresh(),"启动游戏",()->launch(),true);
        LinearLayout nav=new LinearLayout(this);root.addView(nav);for(String key:new String[]{"saves","backups","bots","logs","exports"}){Button b=new Button(this);b.setText(key.equals("saves")?"存档":key.equals("backups")?"备份":key.equals("bots")?"人机":key.equals("logs")?"日志":"导出记录");b.setTextSize(13);b.setPadding(0,0,0,0);nav.addView(b,new LinearLayout.LayoutParams(0,dp(48),1));b.setOnClickListener(v->{tab=key;if(key.equals("bots")||(!key.equals("backups")&&snapshot==null))refresh();else render();});}
        ScrollView scroll=new ScrollView(this);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));body=stack();body.setPadding(0,dp(10),0,0);scroll.addView(body);
        TextView credit=text("移植与维护：QingMo  ·  时间按本机时区显示",11,0xff808890);credit.setGravity(Gravity.CENTER);root.addView(credit);
        refresh();
    }
    private JSONObject call(String method,String arg,String name)throws Exception {
        return call(method,arg,name,null);
    }
    private JSONObject call(String method,String arg,String name,File backup)throws Exception {
        PackageInfo installed;
        try{installed=getPackageManager().getPackageInfo(GAME,0);}catch(PackageManager.NameNotFoundException e){throw new IOException("尚未安装“内置服体验”游戏，请安装配套 v33 或更新的游戏 APK");}
        long version=Build.VERSION.SDK_INT>=28?installed.getLongVersionCode():installed.versionCode;
        gameVersion=version;
        if(method.equals("import")&&version<36)throw new IOException("导入功能需要配套游戏 v36 或更新版本，请先覆盖更新游戏 APK");
        if(method.equals("delete")&&version<DELETE_GAME_VERSION)
            throw new IOException("删除存档需要配套游戏 v"+DELETE_GAME_VERSION+" 或更新版本，请先覆盖更新游戏 APK");
        if(version<33)throw new IOException("已安装游戏 v"+version+"；此版存档和人机管理需配套 v33 或更新版本，请覆盖更新游戏");
        if(getPackageManager().checkSignatures(GAME,getPackageName())!=PackageManager.SIGNATURE_MATCH)throw new IOException("游戏与管家签名不一致，请安装同一套配套 APK");
        ComponentName gateway=new ComponentName(GAME,GATEWAY);
        try{getPackageManager().getActivityInfo(gateway,0);}catch(PackageManager.NameNotFoundException e){throw new IOException("游戏 v"+version+" 缺少独立存档入口，请重新安装配套游戏 APK");}
        String request=UUID.randomUUID().toString().replace("-","");boolean transfer=Arrays.asList("export","exportLog","exportLogs","file").contains(method),importing=method.equals("import");
        Uri destination=Uri.parse("content://"+getPackageName()+".incoming/"+request);
        CompletableFuture<Intent> response=new CompletableFuture<>();activeRequest=request;exchange=response;
        Intent intent=new Intent().setComponent(gateway).putExtra("request",request).putExtra("method",method).putExtra("argument",arg).putExtra("name",name);
        File importGrant=null;String importChecksum=null;long importBytes=0;
        File incoming=new File(getCacheDir(),"Incoming/"+request+".partial");
        try{
        if(importing){
            File source=backup==null?sharedFile(arg):backup;importBytes=source.length();if(importBytes==0||importBytes>SaveBackups.MAX_BYTES)throw new IOException("请选择不超过128 MiB的存档 ZIP");
            File folder=new File(getCacheDir(),"SharedExports");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("无法创建导入缓存");
            importGrant=new File(folder,"import-"+request+".zip");
            java.nio.file.Files.copy(source.toPath(),importGrant.toPath());importChecksum=hash(importGrant);
            destination=new Uri.Builder().scheme("content").authority(getPackageName()+".exports").appendPath(importGrant.getName()).build();
            intent.setData(destination).setClipData(ClipData.newRawUri("存档导入",destination));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            intent.putExtra("transferBytes",importBytes).putExtra("transferSha256",importChecksum);
        }
        if(transfer){getSharedPreferences("transfers",MODE_PRIVATE).edit().putString("pending",request).commit();intent.setData(destination);intent.setClipData(ClipData.newRawUri("存档传输",destination));intent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);}
            ui(()->{skipResumeRefresh=true;try{startActivityForResult(intent,7201);}catch(Exception e){response.completeExceptionally(e);}});
            Intent result;
            try{result=response.get(120,TimeUnit.SECONDS);}catch(TimeoutException e){throw new IOException("存档处理尚未返回结果，请刷新确认；不要重复提交同一操作");}
            catch(ExecutionException e){throw new IOException("独立存档入口启动失败："+e.getCause().getMessage(),e.getCause());}
            if(!result.getBooleanExtra("ok",false))throw new IOException(result.getStringExtra("error"));
            JSONObject value=new JSONObject(result.getStringExtra("json"));if(value.optInt("bridgeProtocol")!=1)throw new IOException("存档接口协议不兼容，请更新配套安装包");
            if(importing&&(value.getLong("importBytes")!=importBytes||!value.getString("importSha256").equals(importChecksum)))throw new IOException("导入结果校验不一致，请刷新列表确认");
            if(transfer){String file=value.getString("file");if(!file.matches("[a-zA-Z0-9._-]+\\.(?:zip|log)")||file.contains(".."))throw new IOException("收到的文件名无效");
                if(!incoming.isFile()||incoming.length()!=value.getLong("transferBytes"))throw new IOException("文件传输未完成，请重新导出");
                MessageDigest hash=MessageDigest.getInstance("SHA-256");try(InputStream in=new FileInputStream(incoming)){byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)hash.update(b,0,n);}
                StringBuilder digest=new StringBuilder();for(byte b:hash.digest())digest.append(String.format(Locale.US,"%02x",b&255));
                if(!digest.toString().equals(value.getString("transferSha256")))throw new IOException("文件传输校验不一致，未使用不完整文件");
                File folder=new File(getCacheDir(),"SharedExports");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("无法创建分享缓存");
                java.nio.file.Files.move(incoming.toPath(),new File(folder,file).toPath(),java.nio.file.StandardCopyOption.ATOMIC_MOVE,java.nio.file.StandardCopyOption.REPLACE_EXISTING);
            }
            return value;
        }finally{
            if(transfer){revokeUriPermission(destination,Intent.FLAG_GRANT_WRITE_URI_PERMISSION);getSharedPreferences("transfers",MODE_PRIVATE).edit().remove("pending").apply();}
            if(importing){revokeUriPermission(destination,Intent.FLAG_GRANT_READ_URI_PERMISSION);if(importGrant!=null)importGrant.delete();}
            incoming.delete();if(exchange==response){exchange=null;activeRequest=null;}
        }
    }
    private static String hash(File file)throws Exception {
        MessageDigest digest=MessageDigest.getInstance("SHA-256");try(InputStream in=new FileInputStream(file)){byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)digest.update(b,0,n);}
        StringBuilder out=new StringBuilder();for(byte b:digest.digest())out.append(String.format(Locale.US,"%02x",b&255));return out.toString();
    }
    private interface Job{void run()throws Exception;}
    private void perform(String title,Job job){if(busy){Toast.makeText(this,"正在处理，请稍候",Toast.LENGTH_SHORT).show();return;}busy=true;status.setText(title);
        work.execute(()->{try{job.run();}catch(Exception e){ui(()->status.setText("操作失败："+(e.getMessage()==null?e.getClass().getSimpleName():e.getMessage())));}finally{busy=false;}});}
    private void ui(Runnable r){runOnUiThread(()->{if(!isFinishing()&&!isDestroyed())r.run();});}
    private void refresh(){if(tab.equals("backups")){status.setText("备份保存在存档管家内。");render();return;}perform("正在连接独立存档入口…",()->{if(tab.equals("bots")){botSnapshot=call("bots",null,null);snapshot=botSnapshot.getJSONObject("snapshot");}else snapshot=call("list",null,null);ui(()->{render();status.setText(snapshot.optBoolean("gameRunning")?"游戏进程仍在运行（后台也会占用）。退出游戏后可修改存档和人机；日志仍可查看与分享。":"存档空间已就绪，无需启动游戏即可管理；人机设置在下次启动时应用。");});});}
    private String time(String value){try{return DateFormat.getDateTimeInstance(DateFormat.MEDIUM,DateFormat.MEDIUM).format(Date.from(Instant.parse(value)));}catch(Exception e){return value.isEmpty()?"尚无记录":value;}}
    private String size(long n){return n<1024?n+" B":n<1024*1024?String.format(Locale.getDefault(),"%.1f KB",n/1024f):String.format(Locale.getDefault(),"%.1f MB",n/(1024f*1024));}
    private void render(){body.removeAllViews();
        try{if(tab.equals("backups")){backups();return;}if(snapshot==null){body.addView(text("尚未连接存档空间，请点击“刷新”查看具体原因。无需先打开游戏。",15,0xff65778a));return;}
            if(tab.equals("saves"))saves();else if(tab.equals("bots"))bots();else if(tab.equals("logs"))logs();else exports();}catch(Exception e){status.setText(e.getMessage());}}
    private Spinner choices(LinearLayout box,String title,String[] labels){box.addView(text(title,14,0xff3d6486));Spinner spinner=new Spinner(this);ArrayAdapter<String> adapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_item,labels);adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);spinner.setAdapter(adapter);box.addView(spinner,new LinearLayout.LayoutParams(-1,dp(48)));return spinner;}
    private static int option(String[] keys,String value){return Math.max(0,Arrays.asList(keys).indexOf(value));}
    private void bots()throws Exception {
        if(botSnapshot==null){body.addView(text("点击刷新读取当前存档的人机。",15,0xff65778a));return;}
        final JSONObject data=botSnapshot;final String profile=data.getString("profileId");boolean idle=!data.getBoolean("gameRunning");
        JSONArray rows=data.getJSONArray("bots"),requests=data.getJSONArray("requests"),results=data.getJSONArray("results");
        LinearLayout summary=card();summary.addView(text("人机管理 · "+data.getString("profileName"),19,0xff183b60));
        summary.addView(text("现有人机 "+rows.length()+" 名 · 待应用 "+requests.length()+" 项\n更新时间："+time(data.getString("modifiedUtc")),13,0xff65778a));
        summary.addView(text("关闭游戏后保存设置，下次启动游戏时应用。设置随此存档备份保留。",13,0xff65778a));
        LinearLayout add=card();Spinner tier=choices(add,"新增人机强度",new String[]{"混合强度","普通","精英","高手","宗师"});tier.setEnabled(idle);
        button(add,"新增4名人机",()->confirm("新增人机","将在下次启动游戏时，按所选强度新增4名人机；总数最多100名。",()->{try{queueBot("botGenerate",new JSONObject().put("profileId",profile).put("strength",tier.getSelectedItemPosition()==0?"mixed":BOT_TIERS[tier.getSelectedItemPosition()-1]));}catch(Exception e){status.setText(e.getMessage());}})).setEnabled(idle);
        if(rows.length()==0)add.addView(text("首次启动游戏会生成基础居民；待新增的人机在此基础上增加。",13,0xff65778a));
        else{
            LinearLayout edit=card();String[] names=new String[rows.length()];int selected=0;
            for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);names[i]=row.getString("name")+" · Lv."+row.getLong("level");if(row.getLong("uid")==selectedBot)selected=i;}
            Spinner residents=choices(edit,"管理现有人机",names),growth=choices(edit,"养成强度",BOT_TIER_LABELS),style=choices(edit,"阵容和配装",BOT_STYLE_LABELS),ai=choices(edit,"AI策略档位（预留）",BOT_TIER_LABELS);
            for(Spinner control:new Spinner[]{growth,style,ai})control.setEnabled(idle);
            residents.setSelection(selected);
            residents.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int position,long id){try{
                JSONObject row=rows.getJSONObject(position);selectedBot=row.getLong("uid");String strength=row.getString("strength"),loadout=row.getString("style"),policy=row.getString("aiStrength");
                for(int i=0;i<requests.length();i++){JSONObject q=requests.getJSONObject(i);if(q.optString("Action").equals("configure")&&q.optLong("BotUid")==selectedBot){strength=q.getString("Strength");loadout=q.getString("Style");policy=q.getString("AiStrength");}}
                growth.setSelection(option(BOT_TIERS,strength));style.setSelection(option(BOT_STYLES,loadout));ai.setSelection(option(BOT_TIERS,policy));
            }catch(Exception e){status.setText(e.getMessage());}}});
            edit.addView(text("养成和配装影响实际卡组。AI策略档位沿用原服务端的预留设置，不代表新增手动战斗AI。",12,0xff65778a));
            button(edit,"保存该人机设置",()->{try{JSONObject row=rows.getJSONObject(residents.getSelectedItemPosition());queueBot("botConfigure",new JSONObject().put("profileId",profile).put("botUid",row.getLong("uid")).put("revision",row.getInt("revision"))
                .put("strength",BOT_TIERS[growth.getSelectedItemPosition()]).put("style",BOT_STYLES[style.getSelectedItemPosition()]).put("aiStrength",BOT_TIERS[ai.getSelectedItemPosition()]));}catch(Exception e){status.setText(e.getMessage());}}).setEnabled(idle);
        }
        if(requests.length()>0){LinearLayout pending=card();pending.addView(text("下次启动时应用",17,0xff183b60));for(int i=0;i<requests.length();i++){JSONObject q=requests.getJSONObject(i);pending.addView(text((q.getString("Action").equals("generate")?"新增"+q.getInt("Count")+"名人机":"更新人机 #"+q.getLong("BotUid"))+" · "+time(q.optString("CreatedUtc")),12,0xff65778a));}}
        if(results.length()>0){LinearLayout history=card();history.addView(text("最近应用结果",17,0xff183b60));for(int i=results.length()-1;i>=Math.max(0,results.length()-5);i--){JSONObject r=results.getJSONObject(i);history.addView(text((r.getBoolean("Applied")?"已应用 · ":"未应用 · ")+r.getString("Message")+"\n"+time(r.getString("CompletedUtc")),12,0xff65778a));}}
    }
    private void queueBot(String method,JSONObject input){perform("正在保存人机设置…",()->{botSnapshot=call(method,input.toString(),null);snapshot=botSnapshot.getJSONObject("snapshot");ui(()->{render();status.setText("人机设置已保存，将在下次启动游戏时应用。");});});}
    private void saves()throws Exception {
        boolean idle=!snapshot.optBoolean("gameRunning");button(body,"＋ 新建独立存档",()->ask("新建并切换存档","新的冒险",name->mutate("new",null,name))).setEnabled(idle);
        button(body,"导入存档",()->pickImport()).setEnabled(idle&&gameVersion>=36);
        body.addView(text(gameVersion<36?"导入功能需配套游戏 v36 或更新版本。":"支持 .bnsm-save.zip 和“存档＋日志”总包。导入为独立存档，已有进度保留；游戏运行时不能导入。",12,0xff65778a));
        JSONArray rows=snapshot.getJSONArray("profiles");if(rows.length()==0)body.addView(text("还没有存档。新建后直接启动游戏即可。",15,0xff65778a));
        if(rows.length()>0&&gameVersion<DELETE_GAME_VERSION)
            body.addView(text("删除存档需更新到配套游戏 v"+DELETE_GAME_VERSION+"。",12,0xff65778a));
        for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);String id=row.getString("id"),name=row.getString("name");boolean active=row.getBoolean("active");LinearLayout c=card();
            TextView title=text((active?"当前使用 · ":"")+name,18,0xff183b60);title.setTypeface(null,Typeface.BOLD);c.addView(title);
            c.addView(text("创建："+time(row.getString("createdUtc"))+"\n更新："+time(row.getString("modifiedUtc"))+"\n数据大小："+size(row.getLong("bytes")),12,0xff65778a));
            pair(c,active?"正在使用":"切换到此存档",()->confirm("切换存档","下次启动游戏将使用“"+name+"”。现有存档保留。",()->mutate("switch",id,null)),"重命名",()->ask("重命名存档",name,n->mutate("rename",id,n)),idle);
            pair(c,"导出到文件夹",()->export("export",id,false),"分享存档",()->export("export",id,true),idle);
            button(c,"创建备份",()->createBackup(id,name)).setEnabled(idle);
            button(c,"删除存档",()->confirmDelete(id,name,active)).setEnabled(idle&&gameVersion>=DELETE_GAME_VERSION);
        }
    }
    private SaveBackups backupStore(){return new SaveBackups(new File(getFilesDir(),"SaveBackups"));}
    private void createBackup(String id,String name){
        perform("正在备份存档…",()->{
            JSONObject result=call("export",id,null);snapshot=result.getJSONObject("snapshot");
            SaveBackups.Entry entry=backupStore().create(sharedFile(result.getString("file")),name,Instant.now());
            ui(()->{tab="backups";render();status.setText("已创建新备份 · "+time(entry.created.toString()));});
        });
    }
    private void backups()throws IOException {
        body.addView(text("每次创建都保留一份独立备份。导入会添加到存档列表，原备份保留。请退出游戏后创建或导入。",13,0xff65778a));
        button(body,"前往存档列表创建备份",()->{tab="saves";refresh();});
        List<SaveBackups.Entry> entries=backupStore().list();
        if(entries.isEmpty())body.addView(text("还没有备份。可以在任一存档下点击“创建备份”。",15,0xff65778a));
        for(SaveBackups.Entry entry:entries){
            LinearLayout c=card();TextView title=text(entry.name,18,0xff183b60);title.setTypeface(null,Typeface.BOLD);c.addView(title);
            c.addView(text("备份时间："+time(entry.created.toString())+"\n备份大小："+size(entry.archive.length()),12,0xff65778a));
            button(c,"导入到存档列表",()->ask("导入为独立存档",entry.name,n->restoreBackup(entry,n)));
            button(c,"分享备份",()->perform("正在准备备份分享…",()->{
                File file=backupStore().shareCopy(entry,new File(getCacheDir(),"SharedExports"));
                ui(()->{status.setText("请选择接收备份的应用");deliver(file,true);});
            }));
        }
    }
    private void restoreBackup(SaveBackups.Entry entry,String name){
        perform("正在校验并导入备份…",()->{
            JSONObject result=call("import",null,name,entry.archive);snapshot=result.getJSONObject("snapshot");
            ui(()->{tab="saves";render();status.setText("已导入为独立存档，原备份仍在。“备份”页可再次使用。"+(result.optBoolean("selected")?"已选中导入的存档。":"当前使用的存档未切换。"));});
        });
    }
    private void confirmDelete(String id,String name,boolean active){
        String message="将永久删除「"+name+"」的游戏进度。已经导出的备份和日志会保留。";
        if(active)message+="\n\n删除后将切换到另一份可用存档。若这是最后一份，下次可新建或导入存档。";
        new AlertDialog.Builder(this).setTitle("删除存档").setMessage(message)
            .setNegativeButton("取消",null).setPositiveButton("永久删除",(dialog,which)->perform("正在删除存档…",()->{
                JSONObject result=call("delete",id,active?"active":"inactive");
                if(!id.equals(result.getString("id")))throw new IOException("删除结果不一致，请刷新确认");
                snapshot=result.getJSONObject("snapshot");
                botSnapshot=null;
                selectedBot=0;
                ui(()->{render();status.setText(result.optBoolean("cleanupPending")
                    ?"存档已移出列表，文件清理将在下次管理存档时重试。"
                    :"已删除「"+name+"」 · "+DateFormat.getDateTimeInstance().format(new Date()));});
            })).show();
    }
    private interface Named{void accept(String name);}
    private void pickImport(){
        if(busy)return;skipResumeRefresh=true;
        try{startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"),7102);}
        catch(Exception e){status.setText("无法打开文件选择器："+e.getMessage());}
    }
    private void receiveSelectedImport(Uri uri){
        perform("正在读取所选存档…",()->{
            File folder=new File(getCacheDir(),"SharedExports");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("无法创建导入缓存");
            File selected=new File(folder,"selected-import-"+UUID.randomUUID().toString().replace("-","")+".zip");boolean copied=false;
            try{
                try(InputStream in=getContentResolver().openInputStream(uri);FileOutputStream out=new FileOutputStream(selected)){
                    if(in==null)throw new IOException("所选文件不可读");byte[] b=new byte[32768];int n;long total=0;
                    while((n=in.read(b))!=-1){if((total+=n)>128L*1024*1024)throw new IOException("文件超过128 MiB，请选择存档 ZIP");out.write(b,0,n);}out.getFD().sync();
                }
                copied=true;
                ui(()->{
                    EditText input=new EditText(this);input.setSingleLine(true);input.setText("导入的进度");input.setSelectAllOnFocus(true);
                    new AlertDialog.Builder(this).setTitle("导入为独立存档").setMessage("已有存档保留，导入后可在列表选择切换；首次使用会自动选中导入的存档。").setView(input)
                        .setNegativeButton("取消",(d,w)->selected.delete()).setOnCancelListener(d->selected.delete())
                        .setPositiveButton("导入",(d,w)->perform("正在校验并导入存档…",()->{
                            try{JSONObject value=call("import",selected.getName(),input.getText().toString());snapshot=value.getJSONObject("snapshot");
                                ui(()->{render();status.setText("存档导入成功 · "+DateFormat.getDateTimeInstance().format(new Date())+(value.optBoolean("selected")?"\n已选中，启动游戏即可继续。":"\n当前使用的存档未切换，可在列表中选择新导入的进度。"));});
                            }finally{selected.delete();}
                        })).show();
                });
            }finally{if(!copied)selected.delete();}
        });
    }
    private void ask(String title,String value,Named done){EditText input=new EditText(this);input.setSingleLine(true);input.setText(value);input.setSelectAllOnFocus(true);
        new AlertDialog.Builder(this).setTitle(title).setView(input).setNegativeButton("取消",null).setPositiveButton("确定",(d,w)->done.accept(input.getText().toString())).show();}
    private void confirm(String title,String text,Runnable done){new AlertDialog.Builder(this).setTitle(title).setMessage(text).setNegativeButton("取消",null).setPositiveButton("确定",(d,w)->done.run()).show();}
    private void mutate(String command,String id,String name){perform("正在处理存档…",()->{snapshot=call(command,id,name).getJSONObject("snapshot");ui(()->{render();status.setText("存档操作完成 · "+DateFormat.getDateTimeInstance().format(new Date()));});});}
    private void logs()throws Exception {
        button(body,"打包全部日志并分享",()->export("exportLogs",null,true));JSONArray rows=snapshot.getJSONArray("logs");
        if(rows.length()==0)body.addView(text("尚无日志，首次启动游戏后会自动记录。",15,0xff65778a));
        for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);String id=row.getString("id");LinearLayout c=card();c.addView(text(row.getString("name"),17,0xff183b60));
            c.addView(text("更新："+time(row.getString("modifiedUtc"))+" · "+size(row.getLong("bytes")),12,0xff65778a));
            pair(c,"查看日志",()->perform("正在读取日志…",()->{String contents=call("log",id,null).getString("text");ui(()->showLog(contents));}),"分享日志",()->export("exportLog",id,true),true);
        }
    }
    private void showLog(String value){TextView text=text(value,12,0xff223344);text.setTypeface(Typeface.MONOSPACE);text.setTextIsSelectable(true);text.setPadding(dp(14),dp(10),dp(14),dp(10));ScrollView scroll=new ScrollView(this);scroll.addView(text);
        new AlertDialog.Builder(this).setTitle("日志内容 · 最新记录在底部").setView(scroll).setPositiveButton("关闭",null).show();}
    private void exports()throws Exception {JSONArray rows=snapshot.getJSONArray("exports");if(rows.length()==0)body.addView(text("导出的备份与日志会保留在这里，显示生成时间。",15,0xff65778a));
        for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);String name=row.getString("name");LinearLayout c=card();c.addView(text(name,15,0xff183b60));c.addView(text("生成："+time(row.getString("modifiedUtc"))+" · "+size(row.getLong("bytes")),12,0xff65778a));
            pair(c,"保存到文件夹",()->existing(name,false),"分享",()->existing(name,true),true);}}
    private File download(String name)throws Exception {
        return sharedFile(call("file",name,null).getString("file"));
    }
    private File sharedFile(String name)throws IOException {if(name==null||!name.matches("[a-zA-Z0-9._-]+\\.(?:zip|log)")||name.contains(".."))throw new IOException("导出文件名无效");File f=new File(getCacheDir(),"SharedExports/"+name);if(!f.isFile())throw new IOException("文件尚未接收完成");return f;}
    private void deliver(File file,boolean share){pending=file;try{if(share)ExportProvider.share(this,file);else startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType(file.getName().endsWith(".log")?"text/plain":"application/zip").putExtra(Intent.EXTRA_TITLE,file.getName()),7101);}catch(Exception e){status.setText("无法打开系统菜单："+e.getMessage());}}
    private void export(String method,String id,boolean share){perform("正在生成带时间的导出文件…",()->{JSONObject value=call(method,id,null);File file=sharedFile(value.getString("file"));snapshot=value.getJSONObject("snapshot");ui(()->{render();status.setText("已生成 · "+file.getName());deliver(file,share);});});}
    private void existing(String name,boolean share){perform("正在准备文件…",()->{File file=download(name);ui(()->deliver(file,share));});}
    private void launch(){try{Intent intent=getPackageManager().getLaunchIntentForPackage(GAME);if(intent==null)throw new IOException("请先安装配套“内置服体验”游戏 APK");startActivity(intent);}catch(Exception e){status.setText(e.getMessage());}}
    protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);
        if(request==7201){skipResumeRefresh=true;CompletableFuture<Intent> current=exchange;if(current!=null){if(result!=RESULT_OK||data==null)current.completeExceptionally(new IOException("独立存档入口未返回结果，请刷新确认"));else if(Objects.equals(activeRequest,data.getStringExtra("request")))current.complete(data);}return;}
        if(request==7102){skipResumeRefresh=true;if(result==RESULT_OK&&data!=null&&data.getData()!=null)receiveSelectedImport(data.getData());return;}
        if(request!=7101||result!=RESULT_OK||data==null||data.getData()==null||pending==null)return;
        final File source=pending;final Uri uri=data.getData();perform("正在保存文件…",()->{try(InputStream in=new FileInputStream(source);OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("所选位置不可写");byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)out.write(b,0,n);}ui(()->status.setText("文件已保存 · "+DateFormat.getDateTimeInstance().format(new Date())));});}
    protected void onSaveInstanceState(Bundle state){state.putString("tab",tab);if(pending!=null)state.putString("pending",pending.getName());super.onSaveInstanceState(state);}
    protected void onResume(){super.onResume();if(skipResumeRefresh)skipResumeRefresh=false;else if(resumed&&!busy)refresh();resumed=true;}
    protected void onDestroy(){CompletableFuture<Intent> current=exchange;if(current!=null)current.completeExceptionally(new IOException("管家界面已关闭，请重新打开后刷新确认"));work.shutdown();super.onDestroy();}
}
