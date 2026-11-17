package local.bnsm.savemanager;

import android.content.*;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.*;
import java.io.*;
import java.util.Arrays;

/** One request-scoped write grant, never a general file/directory bridge. */
public final class IncomingProvider extends ContentProvider {
    public boolean onCreate(){return true;}
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException {
        Context c=getContext();String[] packages=c.getPackageManager().getPackagesForUid(Binder.getCallingUid());
        if(packages==null||!Arrays.asList(packages).contains("local.bnsm.integratedtest")||c.getPackageManager().checkSignatures("local.bnsm.integratedtest",c.getPackageName())!=PackageManager.SIGNATURE_MATCH)throw new SecurityException("Only the paired game can write this transfer");
        String request=uri.getLastPathSegment();
        if(!"wt".equals(mode)||uri.getPathSegments().size()!=1||request==null||!request.matches("[a-f0-9]{32}")||!request.equals(c.getSharedPreferences("transfers",Context.MODE_PRIVATE).getString("pending","")))throw new FileNotFoundException("Invalid transfer");
        File dir=new File(c.getCacheDir(),"Incoming");if(!dir.isDirectory()&&!dir.mkdirs())throw new FileNotFoundException("Cannot create transfer directory");
        return ParcelFileDescriptor.open(new File(dir,request+".partial"),ParcelFileDescriptor.MODE_CREATE|ParcelFileDescriptor.MODE_TRUNCATE|ParcelFileDescriptor.MODE_WRITE_ONLY);
    }
    public String getType(Uri u){return "application/octet-stream";}
    public Cursor query(Uri u,String[] p,String s,String[] a,String o){throw new UnsupportedOperationException();}
    public Uri insert(Uri u,ContentValues v){throw new UnsupportedOperationException();}
    public int update(Uri u,ContentValues v,String s,String[] a){throw new UnsupportedOperationException();}
    public int delete(Uri u,String s,String[] a){throw new UnsupportedOperationException();}
}
