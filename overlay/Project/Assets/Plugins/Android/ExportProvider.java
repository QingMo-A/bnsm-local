package local.bnsm.bridge;

import android.content.*;
import android.database.*;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;

/** Read-only, explicitly granted files only. Shared by the game and small manager APK. */
public final class ExportProvider extends ContentProvider {
    public boolean onCreate(){return true;}
    private File file(Uri uri)throws FileNotFoundException {
        String name=uri.getLastPathSegment();
        if(uri.getPathSegments().size()!=1||name==null||!name.matches("[a-zA-Z0-9._-]+\\.(?:zip|log)")||name.contains(".."))throw new FileNotFoundException("Invalid export URI");
        File a=new File(getContext().getFilesDir(),"SupportExports/"+name),b=new File(getContext().getCacheDir(),"SharedExports/"+name);
        File selected=getContext().getPackageName().equals("local.bnsm.savemanager")?b:a;
        if(!selected.isFile())throw new FileNotFoundException("Export not found");return selected;
    }
    public String getType(Uri uri){return uri.toString().endsWith(".log")?"text/plain":"application/zip";}
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException {if(!mode.equals("r"))throw new FileNotFoundException("Read only");return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY);}
    public Cursor query(Uri uri,String[] projection,String selection,String[] args,String order){
        try{File f=file(uri);String[] columns=projection==null?new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE}:projection;
            MatrixCursor cursor=new MatrixCursor(columns);Object[] row=new Object[columns.length];for(int i=0;i<columns.length;i++)row[i]=columns[i].equals(OpenableColumns.DISPLAY_NAME)?f.getName():columns[i].equals(OpenableColumns.SIZE)?f.length():null;cursor.addRow(row);return cursor;
        }catch(FileNotFoundException e){return null;}
    }
    public Uri insert(Uri u,ContentValues v){throw new UnsupportedOperationException();}
    public int update(Uri u,ContentValues v,String s,String[] a){throw new UnsupportedOperationException();}
    public int delete(Uri u,String s,String[] a){throw new UnsupportedOperationException();}
    public static void share(android.app.Activity activity,File file){
        Uri uri=new Uri.Builder().scheme("content").authority(activity.getPackageName()+".exports").appendPath(file.getName()).build();
        Intent intent=new Intent(Intent.ACTION_SEND).setType(file.getName().endsWith(".log")?"text/plain":"application/zip").putExtra(Intent.EXTRA_STREAM,uri)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        intent.setClipData(ClipData.newRawUri(file.getName(),uri));
        activity.startActivity(Intent.createChooser(intent,"分享至 QQ 或其他应用"));
    }
}
