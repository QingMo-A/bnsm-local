package local.bnsm.bridge;

import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.widget.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

/** Explicit foreground IPC in :management. Never touches Unity or starts the host. */
public final class SaveManagerGateway extends Activity {
    private final ExecutorService work=Executors.newSingleThreadExecutor();
    private String request;
    private TextView status;
    private boolean finished;
    protected void onCreate(Bundle saved){
        super.onCreate(saved);
        LinearLayout box=new LinearLayout(this);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(36,28,36,28);
        ProgressBar progress=new ProgressBar(this);box.addView(progress);status=new TextView(this);status.setText("正在处理存档…");status.setTextSize(16);box.addView(status);setContentView(box);
        setTitle("剑灵存档管家");setFinishOnTouchOutside(false);
        try {
            String caller=getCallingPackage();
            if(!"local.bnsm.savemanager".equals(caller)||getPackageManager().checkSignatures(caller,getPackageName())!=PackageManager.SIGNATURE_MATCH)throw new SecurityException("仅允许同签名存档管家访问");
            request=getIntent().getStringExtra("request");if(request==null||!request.matches("[a-f0-9]{32}"))throw new IOException("存档请求编号无效");
            if(saved!=null)throw new IOException("存档处理界面已重建，请刷新确认结果；操作不会自动重复提交");
            final String method=getIntent().getStringExtra("method"),arg=getIntent().getStringExtra("argument"),name=getIntent().getStringExtra("name");
            if(method==null)throw new IOException("存档操作缺失");
            final boolean transfer=Arrays.asList("export","exportLog","exportLogs","file").contains(method);
            final boolean importing=method.equals("import");
            final Uri destination=getIntent().getData();
            if(transfer&&(destination==null||!"content".equals(destination.getScheme())||!"local.bnsm.savemanager.incoming".equals(destination.getAuthority())||destination.getPathSegments().size()!=1||!request.equals(destination.getLastPathSegment())))throw new IOException("存档接收位置无效");
            if(importing&&(destination==null||!"content".equals(destination.getScheme())||!"local.bnsm.savemanager.exports".equals(destination.getAuthority())||destination.getPathSegments().size()!=1||!("import-"+request+".zip").equals(destination.getLastPathSegment())))throw new IOException("存档导入来源无效");
            work.execute(()->{try {
                JSONObject result=importing?receiveImport(destination,name):ManagementActions.execute(this,method,arg,name);
                if(transfer)copy(result,destination);
                if(!result.has("snapshot")&&!method.equals("list")&&!method.equals("log"))result.put("snapshot",new SharedWorkspace(getFilesDir(),getExternalFilesDir(null)).list());
                result.put("bridgeProtocol",1);
                finishResult(new Intent().putExtra("ok",true).putExtra("json",result.toString()));
            }catch(Exception e){failure(e);}});
        }catch(Exception e){failure(e);}
    }
    private JSONObject receiveImport(Uri source,String name)throws Exception {
        long size=getIntent().getLongExtra("transferBytes",-1);String checksum=getIntent().getStringExtra("transferSha256");
        File dir=new File(getCacheDir(),"ManagerImports");if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("无法创建导入缓存");
        File input=File.createTempFile("import-"+request+"-",".zip",dir);
        try{
            SaveArchiveReader.copyVerified(getContentResolver().openInputStream(source),input,size,checksum);
            return new SharedWorkspace(getFilesDir(),getExternalFilesDir(null)).importProfile(input,name,request)
                .put("importBytes",size).put("importSha256",checksum);
        }finally{input.delete();}
    }
    private void copy(JSONObject result,Uri destination)throws Exception {
        File source=new SharedWorkspace(getFilesDir(),getExternalFilesDir(null)).exported(result.getString("file"));
        MessageDigest hash=MessageDigest.getInstance("SHA-256");long total=0;
        try(InputStream input=new FileInputStream(source);OutputStream output=getContentResolver().openOutputStream(destination,"wt")){
            if(output==null)throw new IOException("管家接收文件不可写");byte[] b=new byte[32768];int n;
            while((n=input.read(b))!=-1){if((total+=n)>256L*1024*1024)throw new IOException("导出文件超过256 MB");hash.update(b,0,n);output.write(b,0,n);}output.flush();
        }
        StringBuilder digest=new StringBuilder();for(byte b:hash.digest())digest.append(String.format(Locale.US,"%02x",b&255));
        result.put("transferBytes",total).put("transferSha256",digest.toString());
    }
    private void failure(Exception e){finishResult(new Intent().putExtra("ok",false).putExtra("error",e.getClass().getSimpleName()+"："+e.getMessage()));}
    private void finishResult(Intent result){runOnUiThread(()->{if(finished||isDestroyed())return;finished=true;result.putExtra("request",request);setResult(RESULT_OK,result);finish();});}
    public void onBackPressed(){status.setText("正在完成存档操作，请稍候…");}
    protected void onDestroy(){work.shutdown();super.onDestroy();}
}
