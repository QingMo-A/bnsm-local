package local.bnsm.bridge;

import android.content.*;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.*;
import java.io.*;
import org.json.*;

/** Runs in :management; listing archives does not load Unity or boot the server. */
public final class SaveBridge extends ContentProvider {
    private static SharedWorkspace.Lease gameLease;
    public interface GameCallbacks {void requestExit();}
    private static GameCallbacks gameCallbacks;
    private static Context gameContext;
    private static boolean exiting;
    public static synchronized void registerGameCallbacks(Context context,GameCallbacks callbacks){gameContext=context.getApplicationContext();gameCallbacks=callbacks;}
    public static synchronized void taskRemoved(Context context){
        if(exiting)return;exiting=true;gameContext=context.getApplicationContext();
        if(gameCallbacks!=null)gameCallbacks.requestExit();else finishTaskExit();
    }
    public static synchronized void finishTaskExit(){
        if(!exiting)return;
        releaseGame();if(gameContext!=null)gameContext.stopService(new Intent(gameContext,EmbeddedService.class));
        // The user removed the game task. All host writes have completed before
        // this callback; end only this game process, never the manager process.
        android.os.Process.killProcess(android.os.Process.myPid());
    }
    public static synchronized void taskExitFailed(String reason){exiting=false;android.util.Log.e("BnsmLocalHost","Save before task exit failed: "+reason);}
    public static synchronized boolean acquireGame(Context context){
        if(gameLease!=null)return true;
        try{gameLease=new SharedWorkspace(context.getFilesDir(),context.getExternalFilesDir(null)).acquire();return true;}
        catch(IOException e){return false;}
    }
    public static synchronized void releaseGame(){if(gameLease!=null){try{gameLease.close();}catch(IOException ignored){}gameLease=null;}}
    private SharedWorkspace files(){return new SharedWorkspace(getContext().getFilesDir(),getContext().getExternalFilesDir(null));}
    private void authorize(){
        if(Binder.getCallingUid()==android.os.Process.myUid())return;
        String caller=getCallingPackage();
        if(!"local.bnsm.savemanager".equals(caller)||getContext().getPackageManager().checkSignatures(caller,getContext().getPackageName())!=PackageManager.SIGNATURE_MATCH)
            throw new SecurityException("仅允许同签名的剑灵存档管家访问");
        getContext().enforceCallingPermission(getContext().getPackageName()+".permission.MANAGE_SAVES","Management permission required");
    }
    public boolean onCreate(){return true;}
    public synchronized Bundle call(String method,String argument,Bundle extras){authorize();Bundle result=new Bundle();
        try{JSONObject value=ManagementActions.execute(getContext(),method,argument,extras==null?null:extras.getString("name"));
            result.putString("json",value.toString());result.putBoolean("ok",true);
        }catch(Exception e){result.putBoolean("ok",false);result.putString("error",e.getMessage());}return result;
    }
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException {authorize();
        if(!"r".equals(mode)||uri.getPathSegments().size()!=2||!"exports".equals(uri.getPathSegments().get(0)))throw new FileNotFoundException("Read-only export URI required");
        try{return ParcelFileDescriptor.open(files().exported(uri.getLastPathSegment()),ParcelFileDescriptor.MODE_READ_ONLY);}catch(IOException e){throw new FileNotFoundException(e.getMessage());}
    }
    public String getType(Uri uri){authorize();return uri.toString().endsWith(".log")?"text/plain":"application/zip";}
    public Cursor query(Uri u,String[] p,String s,String[] a,String o){authorize();throw new UnsupportedOperationException();}
    public Uri insert(Uri u,ContentValues v){authorize();throw new UnsupportedOperationException();}
    public int update(Uri u,ContentValues v,String s,String[] a){authorize();throw new UnsupportedOperationException();}
    public int delete(Uri u,String s,String[] a){authorize();throw new UnsupportedOperationException();}
}
